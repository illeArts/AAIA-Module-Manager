namespace AAIA.Air.Persistence;

/// <summary>
/// Deterministischer In-Memory-Reducer für Phase-10-Delta-Events. Er führt keine Tools,
/// Hosts oder Sessions aus und verändert erst nach vollständiger Eventvalidierung Zustand.
/// </summary>
public sealed class AiDurableMutationReducer
{
    private const int MaxAppliedOperations = 10_000;
    private const int MaxAuditEntries = 50_000;

    private readonly Dictionary<string, AiDurableTaskSnapshot> _tasks;
    private readonly Dictionary<string, AiDurableExecutionSnapshot> _executions;
    private readonly Dictionary<string, AiDurableBudgetSnapshot> _budgets;
    private readonly Dictionary<string, AiDurableReservationSnapshot> _reservations;
    private readonly Dictionary<string, AiDurableIdempotencyRecord> _idempotency;
    private readonly List<AiDurableAuditEntry> _audit;
    private readonly Dictionary<string, AiDurableAppliedOperation> _operations;
    private long _lastSequence;

    public long LastSequence => _lastSequence;

    public AiDurableMutationReducer(
        AiDurableOrchestrationSnapshot? snapshot = null,
        long snapshotSequence = 0)
    {
        if (snapshotSequence < 0) throw new ArgumentOutOfRangeException(nameof(snapshotSequence));
        snapshot ??= new AiDurableOrchestrationSnapshot { CreatedAtUtc = DateTime.UnixEpoch };
        ValidateCollections(snapshot);
        _tasks = Unique(snapshot.Tasks, item => item.Id, "Task");
        _executions = Unique(snapshot.Executions, item => item.Id, "Execution");
        _budgets = Unique(snapshot.Budgets, item => item.Budget.Id, "Budget");
        _reservations = Unique(snapshot.Reservations, item => item.Id, "Reservation");
        _idempotency = Unique(snapshot.IdempotencyRecords, IdempotencyKey, "Idempotenz");
        _audit = snapshot.AuditEntries.OrderBy(item => item.TimestampUtc).TakeLast(MaxAuditEntries).ToList();
        _operations = Unique(snapshot.AppliedOperations, item => item.OperationId, "Operation");
        foreach (var operation in _operations.Values) ValidateAppliedOperation(operation);
        TrimOperations();
        _lastSequence = snapshotSequence;
    }

    /// <returns>True, wenn der Fachzustand verändert wurde; false bei identischem Operation-ID-Replay.</returns>
    public bool Apply(AiDurableMutationEnvelope envelope)
    {
        AiDurableMutationCodec.ValidateEnvelope(envelope);
        if (envelope.Sequence != _lastSequence + 1)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalGap,
                $"Mutation-Sequenz {envelope.Sequence} ist ungültig; erwartet wird {_lastSequence + 1}.");

        if (_operations.TryGetValue(envelope.OperationId, out var applied))
        {
            if (applied.MutationType != envelope.MutationType ||
                !string.Equals(applied.PayloadChecksumSha256, envelope.PayloadChecksumSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.OperationConflict,
                    "Operation-ID wurde mit einer anderen Mutation oder Payload wiederverwendet.");
            _lastSequence = envelope.Sequence;
            return false;
        }

        ApplyPayload(envelope);
        _operations.Add(envelope.OperationId, new AiDurableAppliedOperation
        {
            OperationId = envelope.OperationId,
            MutationType = envelope.MutationType,
            PayloadChecksumSha256 = envelope.PayloadChecksumSha256,
            AppliedAtUtc = envelope.OccurredAtUtc
        });
        TrimOperations();
        _lastSequence = envelope.Sequence;
        return true;
    }

    public AiDurableOrchestrationSnapshot CreateSnapshot(DateTime createdAtUtc)
    {
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot-Zeitpunkt muss UTC sein.", nameof(createdAtUtc));
        return new AiDurableOrchestrationSnapshot
        {
            CreatedAtUtc = createdAtUtc,
            Tasks = _tasks.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Executions = _executions.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Budgets = _budgets.Values.OrderBy(item => item.Budget.Id, StringComparer.Ordinal).ToArray(),
            Reservations = _reservations.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            IdempotencyRecords = _idempotency.Values
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(IdempotencyKey, StringComparer.Ordinal)
                .ToArray(),
            AuditEntries = _audit.OrderBy(item => item.TimestampUtc).ToArray(),
            AppliedOperations = _operations.Values
                .OrderBy(item => item.AppliedAtUtc)
                .ThenBy(item => item.OperationId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private void ApplyPayload(AiDurableMutationEnvelope envelope)
    {
        switch (envelope.MutationType)
        {
            case AiDurableMutationType.TaskCreated:
                AddTask(Read<AiTaskMutationPayload>(envelope).Task);
                break;
            case AiDurableMutationType.TaskClaimed:
            case AiDurableMutationType.TaskClaimReleased:
            case AiDurableMutationType.TaskStepChanged:
            case AiDurableMutationType.TaskSettled:
                ReplaceTask(Read<AiTaskMutationPayload>(envelope).Task);
                break;
            case AiDurableMutationType.ExecutionQueued:
                AddExecution(Read<AiExecutionMutationPayload>(envelope).Execution);
                break;
            case AiDurableMutationType.ExecutionLeased:
            case AiDurableMutationType.ExecutionStateChanged:
            case AiDurableMutationType.ExecutionRecoveryResolved:
                ReplaceExecution(Read<AiExecutionMutationPayload>(envelope).Execution);
                break;
            case AiDurableMutationType.BudgetCreated:
                AddBudget(Read<AiBudgetMutationPayload>(envelope).Budget);
                break;
            case AiDurableMutationType.ReservationCreated:
                AddReservation(Read<AiReservationMutationPayload>(envelope).Reservation);
                break;
            case AiDurableMutationType.ReservationCommitted:
            case AiDurableMutationType.ReservationReleased:
            case AiDurableMutationType.ReservationExpired:
                ReplaceReservation(Read<AiReservationMutationPayload>(envelope).Reservation);
                break;
            case AiDurableMutationType.IdempotencyStored:
                StoreIdempotency(Read<AiIdempotencyStoredPayload>(envelope).Record);
                break;
            case AiDurableMutationType.IdempotencyEvicted:
                EvictIdempotency(Read<AiIdempotencyEvictedPayload>(envelope));
                break;
            case AiDurableMutationType.AuditRecorded:
                RecordAudit(Read<AiAuditMutationPayload>(envelope).Entry);
                break;
            case AiDurableMutationType.RuntimeRecoveryCheckpoint:
                _ = Read<AiRecoveryCheckpointPayload>(envelope);
                break;
            default:
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalEventUnknown,
                    "Mutationstyp ist nicht registriert.");
        }
    }

    private static T Read<T>(AiDurableMutationEnvelope envelope)
        => AiDurableMutationCodec.DeserializePayload<T>(envelope);

    private void AddTask(AiDurableTaskSnapshot task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ValidateId(task.Id, "Task");
        if (!_tasks.TryAdd(task.Id, task)) throw Conflict("Task existiert bereits.");
    }

    private void ReplaceTask(AiDurableTaskSnapshot task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ValidateId(task.Id, "Task");
        if (!_tasks.ContainsKey(task.Id)) throw Corrupt("Task für Mutation fehlt.");
        _tasks[task.Id] = task;
    }

    private void AddExecution(AiDurableExecutionSnapshot execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ValidateId(execution.Id, "Execution");
        ValidateId(execution.TaskId, "Execution.Task");
        if (!_tasks.ContainsKey(execution.TaskId)) throw Corrupt("Task der Execution fehlt.");
        if (!_executions.TryAdd(execution.Id, execution)) throw Conflict("Execution existiert bereits.");
    }

    private void ReplaceExecution(AiDurableExecutionSnapshot execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ValidateId(execution.Id, "Execution");
        if (!_executions.TryGetValue(execution.Id, out var existing))
            throw Corrupt("Execution für Mutation fehlt.");
        if (!string.Equals(existing.TaskId, execution.TaskId, StringComparison.Ordinal))
            throw Conflict("Task-ID einer Execution darf nicht wechseln.");
        _executions[execution.Id] = execution;
    }

    private void AddBudget(AiDurableBudgetSnapshot budget)
    {
        if (budget?.Budget is null) throw Corrupt("Budget fehlt.");
        ValidateId(budget.Budget.Id, "Budget");
        if (!_budgets.TryAdd(budget.Budget.Id, budget)) throw Conflict("Budget existiert bereits.");
    }

    private void AddReservation(AiDurableReservationSnapshot reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateId(reservation.Id, "Reservation");
        if (!_reservations.TryAdd(reservation.Id, reservation))
            throw Conflict("Reservation existiert bereits.");
    }

    private void ReplaceReservation(AiDurableReservationSnapshot reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateId(reservation.Id, "Reservation");
        if (!_reservations.TryGetValue(reservation.Id, out var existing))
            throw Corrupt("Reservation für Mutation fehlt.");
        if (!string.Equals(existing.ResourceId, reservation.ResourceId, StringComparison.Ordinal) ||
            !string.Equals(existing.ExecutionRequestId, reservation.ExecutionRequestId, StringComparison.Ordinal))
            throw Conflict("Identität einer Reservation darf nicht wechseln.");
        _reservations[reservation.Id] = reservation;
    }

    private void StoreIdempotency(AiDurableIdempotencyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = IdempotencyKey(record);
        if (_idempotency.TryGetValue(key, out var existing))
        {
            if (existing.InputFingerprint != record.InputFingerprint || existing.ResultId != record.ResultId)
                throw Conflict("Idempotenz-ID wurde mit anderem Input oder Resultat wiederverwendet.");
            return;
        }
        _idempotency.Add(key, record);
    }

    private void EvictIdempotency(AiIdempotencyEvictedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _idempotency.Remove(IdempotencyKey(
            payload.ClientFingerprint, payload.Operation, payload.IdempotencyId));
    }

    private void RecordAudit(AiDurableAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.TimestampUtc.Kind != DateTimeKind.Utc) throw Corrupt("Audit-Zeitpunkt ist nicht UTC.");
        _audit.Add(entry);
        if (_audit.Count > MaxAuditEntries)
            _audit.RemoveRange(0, _audit.Count - MaxAuditEntries);
    }

    private void TrimOperations()
    {
        if (_operations.Count <= MaxAppliedOperations) return;
        foreach (var operation in _operations.Values
                     .OrderBy(item => item.AppliedAtUtc)
                     .ThenBy(item => item.OperationId, StringComparer.Ordinal)
                     .Take(_operations.Count - MaxAppliedOperations).ToArray())
            _operations.Remove(operation.OperationId);
    }

    private static Dictionary<string, T> Unique<T>(
        IEnumerable<T> values,
        Func<T, string> key,
        string label)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            var id = key(value);
            ValidateId(id, label);
            if (!result.TryAdd(id, value)) throw Corrupt($"Doppelte {label}-ID im Snapshot.");
        }
        return result;
    }

    private static void ValidateCollections(AiDurableOrchestrationSnapshot snapshot)
    {
        if (snapshot.Tasks is null || snapshot.Executions is null || snapshot.Budgets is null ||
            snapshot.Reservations is null || snapshot.IdempotencyRecords is null ||
            snapshot.AuditEntries is null || snapshot.AppliedOperations is null)
            throw Corrupt("Snapshot enthält fehlende Collections.");
    }

    private static void ValidateAppliedOperation(AiDurableAppliedOperation operation)
    {
        if (operation.AppliedAtUtc.Kind != DateTimeKind.Utc)
            throw Corrupt("Operation-Zeitpunkt ist nicht UTC.");
        try
        {
            AiDurableMutationRegistry.Get(operation.MutationType);
        }
        catch (AiStateStoreException)
        {
            throw Corrupt("Operation enthält einen unbekannten Mutationstyp.");
        }
        if (string.IsNullOrEmpty(operation.PayloadChecksumSha256) ||
            operation.PayloadChecksumSha256.Length != 64 ||
            !operation.PayloadChecksumSha256.All(Uri.IsHexDigit))
            throw Corrupt("Operation enthält eine ungültige Payload-Prüfsumme.");
    }

    private static void ValidateId(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Corrupt($"{label}-ID fehlt.");
    }

    private static string IdempotencyKey(AiDurableIdempotencyRecord record)
        => IdempotencyKey(record.ClientFingerprint, record.Operation, record.IdempotencyId);

    private static string IdempotencyKey(string client, string operation, string id)
        => $"{client.Length}:{client}{operation.Length}:{operation}{id.Length}:{id}";

    private static AiStateStoreException Corrupt(string message)
        => new(AiRuntimeStateReasonCodes.JournalCorrupt, message);

    private static AiStateStoreException Conflict(string message)
        => new(AiRuntimeStateReasonCodes.OperationConflict, message);
}
