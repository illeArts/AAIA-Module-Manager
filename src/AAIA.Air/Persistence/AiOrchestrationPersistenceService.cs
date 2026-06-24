using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AAIA.Air.Messaging;
using AAIA.Air.Scheduling;
using AAIA.Air.Tasks;

namespace AAIA.Air.Persistence;

/// <summary>
/// Exportiert und importiert Task-/Execution-Zustand ohne Sessions, Locks oder Handler.
/// Schritt-Inputs werden ausschließlich über einen expliziten State Protector gespeichert.
/// </summary>
public sealed partial class AiOrchestrationPersistenceService
{
    private const int MaxTasks = 10_000;
    private const int MaxExecutions = 10_000;
    private const int MaxStepsPerTask = 1_000;
    private const int MaxBudgets = 10_000;
    private const int MaxReservations = 100_000;
    private const int MaxIdempotencyRecords = 10_000;
    private const int MaxMetadataLength = 4_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 64
    };

    private readonly AiRuntimeService _runtime;
    private readonly IAiStateProtector _protector;
    private readonly string _storeId;
    private readonly object _recoveryGate = new();
    private readonly IAiRecoveryAuthorizer? _recoveryAuthorizer;
    private readonly TimeProvider _time;

    public AiOrchestrationPersistenceService(
        AiRuntimeService runtime,
        IAiStateProtector protector,
        string storeId,
        IAiRecoveryAuthorizer? recoveryAuthorizer = null,
        TimeProvider? timeProvider = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _protector = protector ?? throw new AiStateStoreException(
            AiRuntimeStateReasonCodes.ProtectorUnavailable,
            "Task-Payloads benötigen einen State Protector.");
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        _storeId = storeId;
        _recoveryAuthorizer = recoveryAuthorizer;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<AiRuntimeStateSnapshot> CaptureAsync(
        long sequence,
        DateTime createdAtUtc,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes,
        CancellationToken ct = default)
    {
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot-Zeitpunkt muss UTC sein.", nameof(createdAtUtc));

        var tasks = _runtime.Tasks.List().OrderBy(task => task.Id, StringComparer.Ordinal).ToArray();
        var executions = _runtime.Scheduler.List()
            .OrderBy(execution => execution.Request.Id, StringComparer.Ordinal).ToArray();
        if (tasks.Length > MaxTasks || executions.Length > MaxExecutions)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Orchestrierungs-Snapshot überschreitet die Objektgrenze.");

        var durableTasks = new List<AiDurableTaskSnapshot>(tasks.Length);
        foreach (var task in tasks)
        {
            ct.ThrowIfCancellationRequested();
            EnsureSafeMetadata(task.Title, nameof(task.Title));
            EnsureSafeMetadata(task.Description, nameof(task.Description));
            EnsureSafeMetadata(task.Project, nameof(task.Project));
            if (task.Steps.Count > MaxStepsPerTask)
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                    $"Task '{task.Id}' besitzt zu viele Schritte.");

            var steps = new List<AiDurableTaskStepSnapshot>(task.Steps.Count);
            for (var index = 0; index < task.Steps.Count; index++)
            {
                var step = task.Steps[index];
                EnsureSafeMetadata(step.ToolName, nameof(step.ToolName));
                var inputJson = step.Input.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : step.Input.GetRawText();
                if (AiMessageSafetyPolicy.ContainsSensitiveContent(inputJson))
                    throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                        $"Task-Payload '{task.Id}/{index}' enthält potenziell sensible Daten.");
                var plaintext = Encoding.UTF8.GetBytes(inputJson);
                var context = Context("task-step", $"{task.Id}:{index}");
                var protectedInput = await _protector.ProtectAsync(plaintext, context, ct)
                    .ConfigureAwait(false);
                ValidateProtectedPayload(protectedInput);
                steps.Add(new AiDurableTaskStepSnapshot
                {
                    ToolName = step.ToolName,
                    Status = step.Status,
                    ProtectedInput = Clone(protectedInput),
                    ErrorCode = StableErrorCode(step.Error)
                });
            }

            durableTasks.Add(new AiDurableTaskSnapshot
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Project = task.Project,
                Status = task.Status,
                CreatedAtUtc = EnsureUtc(task.CreatedAt, "Task.CreatedAt"),
                UpdatedAtUtc = EnsureUtc(task.UpdatedAt, "Task.UpdatedAt"),
                Steps = steps
            });
        }

        var durableExecutions = executions.Select(execution =>
        {
            EnsureSafeMetadata(execution.Request.SubmittedByClientId, "Execution.SubmittedByClientId");
            EnsureSafeMetadata(execution.ResourceId, "Execution.ResourceId");
            EnsureSafeMetadata(execution.ResourceReservationId, "Execution.ResourceReservationId");
            foreach (var capability in execution.Request.RequiredCapabilities)
                EnsureSafeMetadata(capability, "Execution.RequiredCapability");
            ValidateResourceMetadata(execution.Request.ResourceRequirements);
            return new AiDurableExecutionSnapshot
            {
                Id = execution.Request.Id,
                TaskId = execution.Request.TaskId,
                SubmittedByClientId = execution.Request.SubmittedByClientId,
                Priority = execution.Request.Priority,
                RequiredRole = execution.Request.RequiredRole,
                RequiredCapabilities = execution.Request.RequiredCapabilities
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                EnqueuedAtUtc = EnsureUtc(execution.Request.EnqueuedAtUtc, "Execution.EnqueuedAtUtc"),
                NotBeforeUtc = EnsureOptionalUtc(execution.Request.NotBeforeUtc, "Execution.NotBeforeUtc"),
                MaxAttempts = execution.Request.MaxAttempts,
                ResourceRequirements = Clone(execution.Request.ResourceRequirements),
                State = execution.State,
                AttemptCount = execution.AttemptCount,
                LastErrorCode = StableErrorCode(execution.LastError),
                ResourceId = execution.ResourceId,
                ResourceReservationId = execution.ResourceReservationId,
                ResourceDeferralCount = execution.ResourceDeferralCount,
                ResourceDeferredUntilUtc = EnsureOptionalUtc(
                    execution.ResourceDeferredUntilUtc, "Execution.ResourceDeferredUntilUtc"),
                UpdatedAtUtc = EnsureUtc(execution.UpdatedAtUtc, "Execution.UpdatedAtUtc")
            };
        }).ToArray();

        var resourceState = _runtime.Resources.CaptureDurableState();
        foreach (var budget in resourceState.Budgets) ValidateBudgetMetadata(budget);
        foreach (var reservation in resourceState.Reservations) ValidateReservationMetadata(reservation);
        var idempotencyRecords = _runtime.Idempotency.CaptureDurableRecords(createdAtUtc);
        foreach (var record in idempotencyRecords) ValidateIdempotencyMetadata(record);
        if (resourceState.Budgets.Count > MaxBudgets ||
            resourceState.Reservations.Count > MaxReservations ||
            idempotencyRecords.Count > MaxIdempotencyRecords)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Durabler Ressourcen- oder Idempotenzzustand überschreitet die Objektgrenze.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new AiDurableOrchestrationSnapshot
        {
            CreatedAtUtc = createdAtUtc,
            Tasks = durableTasks,
            Executions = durableExecutions,
            Budgets = resourceState.Budgets,
            Reservations = resourceState.Reservations,
            IdempotencyRecords = idempotencyRecords
        }, JsonOptions);
        return AiRuntimeStateCodec.CreateSnapshot(
            sequence, createdAtUtc, payload,
            AiRuntimeStateSchema.CurrentVersion, maxPayloadBytes);
    }

    public async ValueTask<AiOrchestrationRestoreReport> RestoreAsync(
        AiRuntimeStateSnapshot stateSnapshot,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stateSnapshot);
        AiRuntimeStateCodec.VerifySnapshot(stateSnapshot, maxPayloadBytes);
        if (_runtime.Tasks.Count != 0 || _runtime.Scheduler.List().Count != 0 ||
            _runtime.Resources.ListBudgets().Count != 0 || _runtime.Resources.ListReservations().Count != 0 ||
            _runtime.Idempotency.Count != 0)
            throw new InvalidOperationException("Restore benötigt eine leere Runtime.");

        AiDurableOrchestrationSnapshot durable;
        try
        {
            durable = JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(
                stateSnapshot.Payload, JsonOptions)
                ?? throw new JsonException("Orchestrierungs-Snapshot ist leer.");
        }
        catch (JsonException ex)
        {
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "Orchestrierungs-Snapshot ist ungültig.", ex);
        }
        ValidateRoot(durable);

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        var restoredTasks = new List<AiTask>(durable.Tasks.Count);
        var releasedClaims = 0;
        var taskRecoveryRequired = 0;
        foreach (var persisted in durable.Tasks.OrderBy(task => task.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            ValidateTask(persisted, taskIds);
            var status = persisted.Status;
            if (status == AiTaskStatus.Claimed)
            {
                status = AiTaskStatus.Pending;
                releasedClaims++;
            }
            else if (status == AiTaskStatus.InProgress)
            {
                status = AiTaskStatus.RecoveryRequired;
                taskRecoveryRequired++;
            }
            else if (status == AiTaskStatus.RecoveryRequired)
            {
                taskRecoveryRequired++;
            }

            var task = new AiTask
            {
                Id = persisted.Id,
                Title = persisted.Title,
                Description = persisted.Description,
                Project = persisted.Project,
                Status = status,
                CreatedAt = persisted.CreatedAtUtc,
                UpdatedAt = persisted.UpdatedAtUtc,
                OwnerSessionId = null,
                OwnerClientName = null
            };
            for (var index = 0; index < persisted.Steps.Count; index++)
            {
                var step = persisted.Steps[index];
                ValidateProtectedPayload(step.ProtectedInput);
                var plaintext = await _protector.UnprotectAsync(
                    Clone(step.ProtectedInput), Context("task-step", $"{persisted.Id}:{index}"), ct)
                    .ConfigureAwait(false);
                if (plaintext.Length > maxPayloadBytes)
                    throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                        "Entschützte Task-Payload überschreitet das Limit.");
                var inputJson = StrictUtf8(plaintext);
                if (AiMessageSafetyPolicy.ContainsSensitiveContent(inputJson))
                    throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                        "Entschützte Task-Payload enthält sensible Daten.");
                JsonElement input;
                try { input = JsonDocument.Parse(inputJson).RootElement.Clone(); }
                catch (JsonException ex)
                {
                    throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                        "Task-Payload ist kein gültiges JSON.", ex);
                }
                task.Steps.Add(new AiTaskStep
                {
                    ToolName = step.ToolName,
                    Input = input,
                    Status = step.Status,
                    Error = step.ErrorCode,
                    ResultJson = null
                });
            }
            restoredTasks.Add(task);
        }

        var executionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var execution in durable.Executions)
            ValidateExecution(execution, executionIds, taskIds);
        foreach (var budget in durable.Budgets) ValidateBudgetMetadata(budget);
        foreach (var reservation in durable.Reservations) ValidateReservationMetadata(reservation);
        foreach (var record in durable.IdempotencyRecords) ValidateIdempotencyMetadata(record);

        // Eine laufende Execution macht auch den zugehörigen Task recovery-pflichtig.
        foreach (var execution in durable.Executions.Where(execution =>
                     execution.State is AiExecutionState.Running or AiExecutionState.Cancelling))
        {
            var task = restoredTasks.Single(item => item.Id == execution.TaskId);
            if (task.Status is AiTaskStatus.Completed or AiTaskStatus.Failed or AiTaskStatus.Cancelled)
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                    "Laufende Execution verweist auf terminalen Task.");
            if (task.Status != AiTaskStatus.RecoveryRequired)
            {
                task.Status = AiTaskStatus.RecoveryRequired;
                taskRecoveryRequired++;
            }
        }

        _runtime.Tasks.RestoreDurableTasks(restoredTasks);
        try
        {
            var executionReport = _runtime.Scheduler.RestoreDurableExecutions(durable.Executions);
            int releasedReservations;
            int idempotencyCount;
            try
            {
                var recoveredAtUtc = _time.GetUtcNow().UtcDateTime;
                releasedReservations = _runtime.Resources.RestoreDurableState(
                    durable.Budgets, durable.Reservations, recoveredAtUtc);
                idempotencyCount = _runtime.Idempotency.RestoreDurableRecords(
                    durable.IdempotencyRecords, recoveredAtUtc);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                    "Ressourcen- oder Idempotenzzustand ist inkonsistent.", ex);
            }
            var totalRecovery = taskRecoveryRequired + executionReport.RecoveryRequired;
            foreach (var execution in _runtime.Scheduler.List()
                         .Where(item => item.State == AiExecutionState.RecoveryRequired))
            {
                _runtime.Events.Publish(new AiRuntimeEvent
                {
                    Type = AiRuntimeEventType.ExecutionRecoveryRequired,
                    Message = execution.Request.Id,
                    Data = new Dictionary<string, object?> { ["taskId"] = execution.Request.TaskId }
                });
            }
            return new AiOrchestrationRestoreReport
            {
                TaskCount = restoredTasks.Count,
                ExecutionCount = durable.Executions.Count,
                ReleasedClaims = releasedClaims,
                ReleasedLeases = executionReport.ReleasedLeases,
                RecoveryRequiredCount = totalRecovery,
                BudgetCount = durable.Budgets.Count,
                ReservationCount = durable.Reservations.Count,
                RecoveryReleasedReservations = releasedReservations,
                IdempotencyRecordCount = idempotencyCount
            };
        }
        catch
        {
            _runtime.Idempotency.ClearDurableRestore();
            _runtime.Resources.ClearDurableRestore();
            _runtime.Scheduler.ClearDurableRestore();
            _runtime.Tasks.ClearDurableRestore();
            throw;
        }
    }

    /// <summary>Core-Primitive; Autorisierung/Bestätigung bleibt Aufgabe der lokalen UI.</summary>
    public bool ResolveRecoveryRequiredAsFailed(string executionId, string actorId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        AuthorizeRecovery(actorId, "fail");
        EnsureSafeMetadata(reason, nameof(reason));
        lock (_recoveryGate)
        {
            var execution = _runtime.Scheduler.Get(executionId);
            if (execution is null || execution.State != AiExecutionState.RecoveryRequired) return false;
            if (_runtime.Tasks.Get(execution.Request.TaskId)?.Status != AiTaskStatus.RecoveryRequired) return false;
            if (!_runtime.Tasks.FailRecoveryRequired(execution.Request.TaskId, reason)) return false;
            if (!_runtime.Scheduler.FailRecoveryRequired(executionId, reason)) return false;
            PublishResolved(executionId, execution.Request.TaskId, "failed", actorId: actorId);
            return true;
        }
    }

    /// <summary>Erzeugt neue IDs; die alte Execution wird als fehlgeschlagen abgeschlossen.</summary>
    public AiExecutionSnapshot CreateRecoveryRetry(string executionId, string actorId)
    {
        AuthorizeRecovery(actorId, "retry");
        lock (_recoveryGate)
        {
            var source = _runtime.Scheduler.Get(executionId)
                ?? throw new InvalidOperationException("Execution nicht gefunden.");
            if (source.State != AiExecutionState.RecoveryRequired ||
                _runtime.Tasks.Get(source.Request.TaskId)?.Status != AiTaskStatus.RecoveryRequired)
                throw new InvalidOperationException("Execution ist nicht recovery-pflichtig.");

            var retryTask = _runtime.Tasks.CreateRecoveryRetry(source.Request.TaskId);
            AiExecutionSnapshot? retry = null;
            try
            {
                retry = _runtime.Scheduler.Enqueue(
                    retryTask.Id,
                    source.Request.Priority,
                    source.Request.RequiredRole,
                    source.Request.RequiredCapabilities,
                    notBeforeUtc: null,
                    maxAttempts: source.Request.MaxAttempts,
                    resourceRequirements: Clone(source.Request.ResourceRequirements),
                    submittedBySessionId: null,
                    submittedByClientId: source.Request.SubmittedByClientId);
                const string reason = "runtime_recovery_retry_created";
                if (!_runtime.Tasks.FailRecoveryRequired(source.Request.TaskId, reason) ||
                    !_runtime.Scheduler.FailRecoveryRequired(executionId, reason))
                    throw new InvalidOperationException("Recovery-Ausgangszustand hat sich geändert.");
            }
            catch
            {
                if (retry is not null) _runtime.Scheduler.RemoveDurableRetry(retry.Request.Id);
                _runtime.Tasks.RemoveDurableRetry(retryTask.Id);
                throw;
            }
            PublishResolved(executionId, source.Request.TaskId, "retry", retry!.Request.Id, actorId);
            return retry;
        }
    }

    private void PublishResolved(
        string executionId,
        string taskId,
        string resolution,
        string? retryId = null,
        string? actorId = null)
        => _runtime.Events.Publish(new AiRuntimeEvent
        {
            Type = AiRuntimeEventType.ExecutionRecoveryResolved,
            Message = executionId,
            Data = new Dictionary<string, object?>
            {
                ["taskId"] = taskId,
                ["resolution"] = resolution,
                ["retryExecutionId"] = retryId,
                ["actorId"] = actorId
            }
        });

    private void ValidateRoot(AiDurableOrchestrationSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != AiRuntimeStateSchema.CurrentVersion)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.SchemaUnsupported,
                "Orchestrierungs-Schema wird nicht unterstützt.");
        EnsureUtc(snapshot.CreatedAtUtc, "Snapshot.CreatedAtUtc");
        if (snapshot.Tasks is null || snapshot.Executions is null || snapshot.Budgets is null ||
            snapshot.Reservations is null || snapshot.IdempotencyRecords is null ||
            snapshot.Tasks.Count > MaxTasks || snapshot.Executions.Count > MaxExecutions ||
            snapshot.Budgets.Count > MaxBudgets || snapshot.Reservations.Count > MaxReservations ||
            snapshot.IdempotencyRecords.Count > MaxIdempotencyRecords)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Orchestrierungs-Snapshot überschreitet die Objektgrenze.");
    }

    private static void ValidateTask(AiDurableTaskSnapshot task, HashSet<string> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task.Id);
        if (!ids.Add(task.Id)) throw Corrupt($"Doppelte Task-ID: {task.Id}");
        EnsureSafeMetadata(task.Title, nameof(task.Title));
        EnsureSafeMetadata(task.Description, nameof(task.Description));
        EnsureSafeMetadata(task.Project, nameof(task.Project));
        if (!Enum.IsDefined(task.Status)) throw Corrupt("Ungültiger Task-Status.");
        EnsureUtc(task.CreatedAtUtc, "Task.CreatedAtUtc");
        EnsureUtc(task.UpdatedAtUtc, "Task.UpdatedAtUtc");
        if (task.Steps is null || task.Steps.Count > MaxStepsPerTask)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Task besitzt zu viele Schritte.");
        foreach (var step in task.Steps)
        {
            EnsureSafeMetadata(step.ToolName, nameof(step.ToolName));
            if (!Enum.IsDefined(step.Status)) throw Corrupt("Ungültiger Task-Step-Status.");
        }
    }

    private static void ValidateExecution(
        AiDurableExecutionSnapshot execution,
        HashSet<string> executionIds,
        HashSet<string> taskIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.TaskId);
        if (!executionIds.Add(execution.Id)) throw Corrupt($"Doppelte Execution-ID: {execution.Id}");
        if (!taskIds.Contains(execution.TaskId)) throw Corrupt($"Task fehlt: {execution.TaskId}");
        if (!Enum.IsDefined(execution.State) || !Enum.IsDefined(execution.Priority) ||
            (execution.RequiredRole.HasValue && !Enum.IsDefined(execution.RequiredRole.Value)))
            throw Corrupt("Ungültiger Execution-Enum-Wert.");
        if (execution.MaxAttempts <= 0 || execution.AttemptCount < 0 ||
            execution.ResourceDeferralCount < 0)
            throw Corrupt("Ungültiger Execution-Zähler.");
        EnsureUtc(execution.EnqueuedAtUtc, "Execution.EnqueuedAtUtc");
        EnsureUtc(execution.UpdatedAtUtc, "Execution.UpdatedAtUtc");
        EnsureOptionalUtc(execution.NotBeforeUtc, "Execution.NotBeforeUtc");
        EnsureOptionalUtc(execution.ResourceDeferredUntilUtc, "Execution.ResourceDeferredUntilUtc");
        if (execution.RequiredCapabilities is null || execution.RequiredCapabilities.Count > 100 ||
            execution.RequiredCapabilities.Any(string.IsNullOrWhiteSpace))
            throw Corrupt("Ungültige Execution-Capabilities.");
        EnsureSafeMetadata(execution.SubmittedByClientId, "Execution.SubmittedByClientId");
        EnsureSafeMetadata(execution.ResourceId, "Execution.ResourceId");
        EnsureSafeMetadata(execution.ResourceReservationId, "Execution.ResourceReservationId");
        foreach (var capability in execution.RequiredCapabilities)
            EnsureSafeMetadata(capability, "Execution.RequiredCapability");
        ValidateResourceMetadata(execution.ResourceRequirements);
    }

    private AiStateProtectionContext Context(string recordType, string recordId) => new()
    {
        StoreId = _storeId,
        RecordType = recordType,
        RecordId = recordId
    };

    private static void ValidateProtectedPayload(AiProtectedStatePayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.ProtectorId) || payload.Ciphertext is null)
            throw Corrupt("Geschützte Payload ist ungültig.");
    }

    private static AiProtectedStatePayload Clone(AiProtectedStatePayload payload) => new()
    {
        ProtectorId = payload.ProtectorId,
        Ciphertext = payload.Ciphertext.ToArray()
    };

    private static AiResourceRequirements? Clone(AiResourceRequirements? value) => value is null ? null : new()
    {
        Kind = value.Kind,
        RequiredCapabilities = value.RequiredCapabilities
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
        MinimumContextTokens = value.MinimumContextTokens,
        MinimumMemoryMiB = value.MinimumMemoryMiB,
        MinimumWorkUnitsPerMinute = value.MinimumWorkUnitsPerMinute,
        EstimatedInputUnits = value.EstimatedInputUnits,
        EstimatedOutputUnits = value.EstimatedOutputUnits,
        EstimatedWorkUnits = value.EstimatedWorkUnits,
        CostUnit = value.CostUnit,
        PinnedResourceId = value.PinnedResourceId,
        ReservationDuration = value.ReservationDuration
    };

    private static string StrictUtf8(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException ex)
        {
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "Task-Payload ist kein gültiges UTF-8.", ex);
        }
    }

    private static void EnsureSafeMetadata(string? value, string field)
    {
        if (value is null) return;
        if (value.Length > MaxMetadataLength)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                $"Metadatenfeld '{field}' überschreitet das Limit.");
        if (AiMessageSafetyPolicy.ContainsSensitiveContent(value))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                $"Metadatenfeld '{field}' enthält sensible Daten.");
    }

    private static string? StableErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (AiMessageSafetyPolicy.ContainsSensitiveContent(value)) return "runtime_error";
        return StableCodeRegex().IsMatch(value) ? value : "runtime_error";
    }

    private static void ValidateResourceMetadata(AiResourceRequirements? value)
    {
        if (value is null) return;
        EnsureSafeMetadata(value.CostUnit, "Resource.CostUnit");
        EnsureSafeMetadata(value.PinnedResourceId, "Resource.PinnedResourceId");
        if (value.RequiredCapabilities is null || value.RequiredCapabilities.Count > 100)
            throw Corrupt("Ungültige Resource-Capabilities.");
        foreach (var capability in value.RequiredCapabilities)
            EnsureSafeMetadata(capability, "Resource.RequiredCapability");
    }

    private static void ValidateBudgetMetadata(AiDurableBudgetSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Budget is null) throw Corrupt("Budget fehlt.");
        EnsureSafeMetadata(snapshot.Budget.Id, "Budget.Id");
        EnsureSafeMetadata(snapshot.Budget.ScopeId, "Budget.ScopeId");
        EnsureSafeMetadata(snapshot.Budget.CostUnit, "Budget.CostUnit");
        EnsureUtc(snapshot.Budget.WindowStartsAtUtc, "Budget.WindowStartsAtUtc");
        EnsureUtc(snapshot.Budget.WindowEndsAtUtc, "Budget.WindowEndsAtUtc");
    }

    private static void ValidateReservationMetadata(AiDurableReservationSnapshot snapshot)
    {
        if (snapshot is null) throw Corrupt("Reservation fehlt.");
        EnsureSafeMetadata(snapshot.Id, "Reservation.Id");
        EnsureSafeMetadata(snapshot.ResourceId, "Reservation.ResourceId");
        EnsureSafeMetadata(snapshot.ExecutionRequestId, "Reservation.ExecutionRequestId");
        EnsureSafeMetadata(snapshot.TaskId, "Reservation.TaskId");
        EnsureSafeMetadata(snapshot.CostUnit, "Reservation.CostUnit");
        EnsureSafeMetadata(snapshot.SettlementReasonCode, "Reservation.SettlementReasonCode");
        if (snapshot.BudgetIds is null || snapshot.BudgetIds.Count > MaxBudgets)
            throw Corrupt("Reservation besitzt eine ungültige Budgetliste.");
        foreach (var budgetId in snapshot.BudgetIds)
            EnsureSafeMetadata(budgetId, "Reservation.BudgetId");
        EnsureUtc(snapshot.ReservedAtUtc, "Reservation.ReservedAtUtc");
        EnsureUtc(snapshot.ExpiresAtUtc, "Reservation.ExpiresAtUtc");
        EnsureOptionalUtc(snapshot.SettledAtUtc, "Reservation.SettledAtUtc");
    }

    private static void ValidateIdempotencyMetadata(AiDurableIdempotencyRecord record)
    {
        if (record is null) throw Corrupt("Idempotenzdatensatz fehlt.");
        EnsureSafeMetadata(record.ClientFingerprint, "Idempotency.ClientFingerprint");
        EnsureSafeMetadata(record.Operation, "Idempotency.Operation");
        EnsureSafeMetadata(record.IdempotencyId, "Idempotency.Id");
        EnsureSafeMetadata(record.InputFingerprint, "Idempotency.InputFingerprint");
        EnsureSafeMetadata(record.ResultId, "Idempotency.ResultId");
        EnsureUtc(record.CreatedAtUtc, "Idempotency.CreatedAtUtc");
        EnsureUtc(record.ExpiresAtUtc, "Idempotency.ExpiresAtUtc");
    }

    private void AuthorizeRecovery(string actorId, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        EnsureSafeMetadata(actorId, nameof(actorId));
        string? denialReason = null;
        if (_recoveryAuthorizer is null ||
            !_recoveryAuthorizer.IsAuthorized(actorId, action, out denialReason))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.RecoveryForbidden,
                string.IsNullOrWhiteSpace(denialReason)
                    ? "Recovery-Entscheidung ist nicht autorisiert."
                    : denialReason);
    }

    private static DateTime EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc) throw Corrupt($"{field} ist nicht UTC.");
        return value;
    }

    private static DateTime? EnsureOptionalUtc(DateTime? value, string field)
    {
        if (value.HasValue) EnsureUtc(value.Value, field);
        return value;
    }

    private static AiStateStoreException Corrupt(string message)
        => new(AiRuntimeStateReasonCodes.SnapshotCorrupt, message);

    [GeneratedRegex("^[a-zA-Z0-9_.:-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableCodeRegex();
}
