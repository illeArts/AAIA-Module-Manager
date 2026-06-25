namespace AAIA.Air.Persistence;

using System.Text.Json;

public interface IAiRuntimeMutationPersistence
{
    AiRuntimeRecoveryStatus Status { get; }
    bool IsDurableMutation(AiToolDefinition tool);
    ValueTask PersistMutationAsync(string operationType, CancellationToken ct = default);
}

/// <summary>
/// Hält den Single-Writer, stellt vor Freigabe der Runtime den letzten konsistenten
/// Checkpoint wieder her und bestätigt durable Mutationen erst nach Journal-Flush.
/// </summary>
public sealed class AiRuntimePersistenceCoordinator : IAiRuntimeMutationPersistence, IAsyncDisposable
{
    public const string CheckpointEventType = "orchestration.checkpoint";

    private readonly AiRuntimeService _runtime;
    private readonly IAiRuntimeStateStore _store;
    private readonly AiOrchestrationPersistenceService _orchestration;
    private readonly AiRuntimePersistenceOptions _options;
    private readonly string _runtimeInstanceId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string> _resourceMutationCallback;
    private readonly Action<string> _schedulerTransitionCallback;
    private IAiRuntimeStateStoreSession? _session;
    private AiDurableMutationTransactionCoordinator? _typedWriter;
    private AiRuntimeStateManifest? _manifest;
    private AiDurableOrchestrationSnapshot? _lastDurableSnapshot;
    private long _sequence;

    public AiRuntimeRecoveryStatus Status { get; private set; }
    public AiOrchestrationRestoreReport? LastRestoreReport { get; private set; }
    public string? FailureReasonCode { get; private set; }

    public AiRuntimePersistenceCoordinator(
        AiRuntimeService runtime,
        IAiRuntimeStateStore store,
        IAiStateProtector protector,
        AiRuntimePersistenceOptions options,
        string runtimeInstanceId,
        IAiRecoveryAuthorizer? recoveryAuthorizer = null,
        TimeProvider? timeProvider = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        _runtimeInstanceId = runtimeInstanceId;
        _orchestration = new AiOrchestrationPersistenceService(
            runtime, protector, store.StoreId, recoveryAuthorizer, timeProvider);
        Status = options.Enabled ? AiRuntimeRecoveryStatus.Recovering : AiRuntimeRecoveryStatus.Disabled;
        _resourceMutationCallback = PersistSynchronousResourceMutation;
        _schedulerTransitionCallback = PersistSynchronousRuntimeTransition;
        runtime.Resources.DurableMutationRequired = _resourceMutationCallback;
        runtime.Scheduler.DurableTransitionRequired = _schedulerTransitionCallback;
        runtime.AttachMutationPersistence(this);
    }

    public async ValueTask<AiRuntimeRecoveryStatus> InitializeAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return Status = AiRuntimeRecoveryStatus.Disabled;
        await WaitForWriterAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null) return Status;
            Status = AiRuntimeRecoveryStatus.Recovering;
            if (UseTypedDeltaWriter && _options.BackupPhase9CheckpointBeforeMigration &&
                _store is IAiRuntimeStateMaintenanceStore maintenanceStore)
            {
                await maintenanceStore.CreateBackupAsync(_runtimeInstanceId, ct).ConfigureAwait(false);
            }
            _session = await _store.OpenAsync(
                AiStateStoreOpenMode.ReadWrite, _runtimeInstanceId, ct).ConfigureAwait(false);
            if (_session.IsQuarantined)
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.Quarantined,
                    "State Store ist quarantänisiert.");

            if (UseTypedDeltaWriter)
                return await InitializeTypedDeltaWriterLockedAsync(ct).ConfigureAwait(false);

            _manifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
            var snapshot = await _session.LoadSnapshotAsync(ct).ConfigureAwait(false);
            _sequence = Math.Max(_manifest?.LastSequence ?? 0, snapshot?.Sequence ?? 0);
            await foreach (var entry in _session.ReadJournalAsync(snapshot?.Sequence ?? 0, ct)
                               .ConfigureAwait(false))
            {
                if (!string.Equals(entry.EventType, CheckpointEventType, StringComparison.Ordinal))
                    throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalEventUnknown,
                        "Journal enthält einen unbekannten Event-Typ.");
                snapshot = AiRuntimeStateCodec.CreateSnapshot(
                    entry.Sequence, entry.OccurredAtUtc, entry.Payload,
                    entry.SchemaVersion, _options.MaxProtectedPayloadBytes);
                _sequence = entry.Sequence;
            }

            if (snapshot is not null)
            {
                LastRestoreReport = await _orchestration.RestoreAsync(
                    snapshot, _options.MaxProtectedPayloadBytes, ct).ConfigureAwait(false);
                Status = LastRestoreReport.RecoveryRequiredCount > 0
                    ? AiRuntimeRecoveryStatus.RecoveryRequired
                    : AiRuntimeRecoveryStatus.Ready;
                await PersistCheckpointLockedAsync("runtime.recovered", ct).ConfigureAwait(false);
            }
            else
            {
                Status = AiRuntimeRecoveryStatus.Ready;
            }
            return Status;
        }
        catch (Exception ex)
        {
            FailureReasonCode = ex is AiStateStoreException stateError
                ? stateError.ReasonCode
                : AiRuntimeStateReasonCodes.SnapshotCorrupt;
            Status = ex is AiStateStoreException { ReasonCode: AiRuntimeStateReasonCodes.Quarantined }
                ? AiRuntimeRecoveryStatus.Quarantined
                : AiRuntimeRecoveryStatus.RecoveryFailed;
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsDurableMutation(AiToolDefinition tool)
        => tool.Name is "aaia.message.send" or
            "aaia.task.create" or "aaia.task.claim" or "aaia.task.run" or
            "aaia.execution.enqueue" or "aaia.execution.cancel";

    public async ValueTask PersistMutationAsync(string operationType, CancellationToken ct = default)
    {
        if (Status != AiRuntimeRecoveryStatus.Ready)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryRequired,
                "Runtime ist nicht für durable Mutationen freigegeben.");
        await WaitForWriterAsync(ct).ConfigureAwait(false);
        try
        {
            if (UseTypedDeltaWriter)
                await PersistTypedDeltaLockedAsync(operationType, ct).ConfigureAwait(false);
            else
                await PersistCheckpointLockedAsync(operationType, ct).ConfigureAwait(false);
        }
        catch
        {
            Status = AiRuntimeRecoveryStatus.RecoveryFailed;
            FailureReasonCode = AiRuntimeStateReasonCodes.PersistenceFailed;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> CompleteRecoveryAsync(CancellationToken ct = default)
    {
        if (Status != AiRuntimeRecoveryStatus.RecoveryRequired) return Status == AiRuntimeRecoveryStatus.Ready;
        if (_runtime.Scheduler.List().Any(item => item.State == AiExecutionState.RecoveryRequired)) return false;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Status = AiRuntimeRecoveryStatus.Ready;
            if (UseTypedDeltaWriter)
                await PersistTypedDeltaLockedAsync("runtime.recovery-resolved", ct).ConfigureAwait(false);
            else
                await PersistCheckpointLockedAsync("runtime.recovery-resolved", ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            Status = AiRuntimeRecoveryStatus.RecoveryFailed;
            FailureReasonCode = AiRuntimeStateReasonCodes.PersistenceFailed;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<T> RunExclusiveMaintenanceAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Status == AiRuntimeRecoveryStatus.Disabled || _session is null)
            return await operation(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var previousStatus = Status;
        try
        {
            Status = AiRuntimeRecoveryStatus.Recovering;
            if (_typedWriter is not null)
            {
                await _typedWriter.DisposeAsync().ConfigureAwait(false);
                _typedWriter = null;
            }
            else
            {
                await _session.DisposeAsync().ConfigureAwait(false);
            }
            _session = null;
            var result = await operation(ct).ConfigureAwait(false);
            _session = await _store.OpenAsync(
                AiStateStoreOpenMode.ReadWrite, _runtimeInstanceId, ct).ConfigureAwait(false);
            _manifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
            var snapshot = await _session.LoadSnapshotAsync(ct).ConfigureAwait(false);
            _sequence = Math.Max(_manifest?.LastSequence ?? 0, snapshot?.Sequence ?? 0);
            if (UseTypedDeltaWriter)
            {
                _typedWriter = await AiDurableMutationTransactionCoordinator.RecoverAsync(
                    _session, _options, timeProvider: null, ct).ConfigureAwait(false);
                _lastDurableSnapshot = _typedWriter.CreateCurrentSnapshot(DateTime.UtcNow);
                _sequence = _typedWriter.LastSequence;
            }
            else
            {
                await PersistCheckpointLockedAsync("runtime.maintenance", ct).ConfigureAwait(false);
            }
            Status = previousStatus;
            return result;
        }
        catch
        {
            Status = AiRuntimeRecoveryStatus.RecoveryFailed;
            FailureReasonCode = AiRuntimeStateReasonCodes.PersistenceFailed;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Status is AiRuntimeRecoveryStatus.Disabled or AiRuntimeRecoveryStatus.Stopped)
            {
                Status = _options.Enabled ? AiRuntimeRecoveryStatus.Stopped : AiRuntimeRecoveryStatus.Disabled;
                return;
            }
            Status = AiRuntimeRecoveryStatus.Stopping;
            try
            {
                if (_typedWriter is not null && _typedWriter.Status == AiRuntimeRecoveryStatus.Ready)
                    await _typedWriter.CreateShutdownSnapshotAsync(ct).ConfigureAwait(false);
                if (_typedWriter is not null)
                {
                    await _typedWriter.DisposeAsync().ConfigureAwait(false);
                    _typedWriter = null;
                    _session = null;
                }
                if (_session is not null)
                {
                    await _session.FlushAsync(ct).ConfigureAwait(false);
                    await _session.DisposeAsync().ConfigureAwait(false);
                    _session = null;
                }
                Status = AiRuntimeRecoveryStatus.Stopped;
            }
            catch
            {
                Status = AiRuntimeRecoveryStatus.RecoveryFailed;
                FailureReasonCode = AiRuntimeStateReasonCodes.ShutdownIncomplete;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask PersistCheckpointLockedAsync(string operationType, CancellationToken ct)
    {
        if (_session is null)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.Disabled,
                "Persistenzkoordinator ist nicht initialisiert.");
        var now = DateTime.UtcNow;
        var sequence = checked(_sequence + 1);
        var snapshot = await _orchestration.CaptureAsync(
            sequence, now, _options.MaxProtectedPayloadBytes, ct).ConfigureAwait(false);
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            sequence,
            Guid.NewGuid().ToString("N"),
            CheckpointEventType,
            now,
            isProtected: false,
            snapshot.Payload,
            AiRuntimeStateSchema.CurrentVersion,
            _options.MaxProtectedPayloadBytes);
        await _session.AppendJournalAsync(entry, ct).ConfigureAwait(false);
        await _session.FlushAsync(ct).ConfigureAwait(false);
        await _session.WriteSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        var createdAt = _manifest?.CreatedAtUtc ?? now;
        _manifest = new AiRuntimeStateManifest
        {
            StoreId = _store.StoreId,
            RuntimeInstanceId = _runtimeInstanceId,
            LastSequence = sequence,
            SnapshotSequence = sequence,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = now,
            SnapshotChecksumSha256 = snapshot.ChecksumSha256,
            FeatureFlags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["orchestration"] = true,
                [operationType] = true
            }
        };
        await _session.WriteManifestAsync(_manifest, ct).ConfigureAwait(false);
        await _session.FlushAsync(ct).ConfigureAwait(false);
        _sequence = sequence;
    }

    private async ValueTask WaitForWriterAsync(CancellationToken ct)
    {
        if (_options.WriterBackpressureTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(_options.WriterBackpressureTimeout));
        var acquired = _options.WriterBackpressureTimeout == Timeout.InfiniteTimeSpan
            ? await WaitIndefinitelyAsync(ct).ConfigureAwait(false)
            : await _gate.WaitAsync(_options.WriterBackpressureTimeout, ct).ConfigureAwait(false);
        if (!acquired)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.Backpressure,
                "AIR-State-Writer ist ausgelastet.");
    }

    private async ValueTask<bool> WaitIndefinitelyAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private bool UseTypedDeltaWriter
        => _options.UseTypedDeltaWriter && !_options.RollbackToPhase9CheckpointWriter;

    private async ValueTask<AiRuntimeRecoveryStatus> InitializeTypedDeltaWriterLockedAsync(CancellationToken ct)
    {
        if (_session is null)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.Disabled,
                "Persistenzkoordinator ist nicht initialisiert.");

        _manifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
        var hadTypedDelta = _manifest?.FeatureFlags.TryGetValue("typedDeltaJournal", out var enabled) == true && enabled;
        _typedWriter = await AiDurableMutationTransactionCoordinator.RecoverAsync(
            _session, _options, timeProvider: null, ct).ConfigureAwait(false);
        _sequence = _typedWriter.LastSequence;
        _lastDurableSnapshot = _typedWriter.CreateCurrentSnapshot(DateTime.UtcNow);

        if (_sequence > 0)
        {
            var restoreSnapshot = DurableSnapshotToRuntimeSnapshot(
                _sequence, DateTime.UtcNow, _lastDurableSnapshot, _options.MaxProtectedPayloadBytes);
            LastRestoreReport = await _orchestration.RestoreAsync(
                restoreSnapshot, _options.MaxProtectedPayloadBytes, ct).ConfigureAwait(false);
            Status = LastRestoreReport.RecoveryRequiredCount > 0
                ? AiRuntimeRecoveryStatus.RecoveryRequired
                : AiRuntimeRecoveryStatus.Ready;
        }
        else
        {
            Status = AiRuntimeRecoveryStatus.Ready;
        }

        if (Status == AiRuntimeRecoveryStatus.Ready && !hadTypedDelta)
            await _typedWriter.EnsureVerifiedSnapshotAsync(ct).ConfigureAwait(false);

        _sequence = _typedWriter.LastSequence;
        _manifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
        return Status;
    }

    private async ValueTask PersistTypedDeltaLockedAsync(string operationType, CancellationToken ct)
    {
        if (_typedWriter is null || _lastDurableSnapshot is null)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.Disabled,
                "Typed-Delta-Writer ist nicht initialisiert.");

        var now = DateTime.UtcNow;
        var captured = await _orchestration.CaptureAsync(
            _typedWriter.LastSequence + 1, now, _options.MaxProtectedPayloadBytes, ct).ConfigureAwait(false);
        var next = DeserializeDurableSnapshot(captured.Payload);
        var mutations = DiffSnapshots(_lastDurableSnapshot, next, operationType).ToArray();
        if (mutations.Length == 0)
            return;

        foreach (var mutation in mutations)
        {
            await _typedWriter.CommitAsync(
                mutation.OperationId, mutation.Type, mutation.Payload, ct: ct).ConfigureAwait(false);
        }

        _lastDurableSnapshot = next;
        _sequence = _typedWriter.LastSequence;
        _manifest = await _session!.LoadManifestAsync(ct).ConfigureAwait(false);
    }

    private static AiRuntimeStateSnapshot DurableSnapshotToRuntimeSnapshot(
        long sequence,
        DateTime createdAtUtc,
        AiDurableOrchestrationSnapshot durable,
        int maxPayloadBytes)
        => AiRuntimeStateCodec.CreateSnapshot(
            sequence,
            DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
            JsonSerializer.SerializeToUtf8Bytes(durable),
            AiRuntimeStateSchema.CurrentVersion,
            maxPayloadBytes);

    private static AiDurableOrchestrationSnapshot DeserializeDurableSnapshot(byte[] payload)
        => JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(payload)
           ?? throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
               "Durable Snapshot-Payload ist leer.");

    private sealed record PendingTypedMutation(
        string OperationId,
        AiDurableMutationType Type,
        object Payload);

    private static IEnumerable<PendingTypedMutation> DiffSnapshots(
        AiDurableOrchestrationSnapshot previous,
        AiDurableOrchestrationSnapshot next,
        string operationType)
    {
        var batch = $"{operationType}:{Guid.NewGuid():N}";
        var index = 0;
        foreach (var task in DiffById(previous.Tasks, next.Tasks, item => item.Id))
            yield return Mutation(batch, ref index, task.Added
                ? AiDurableMutationType.TaskCreated
                : TaskMutationType(task.Value), new AiTaskMutationPayload { Task = task.Value });

        foreach (var execution in DiffById(previous.Executions, next.Executions, item => item.Id))
            yield return Mutation(batch, ref index, execution.Added
                ? AiDurableMutationType.ExecutionQueued
                : ExecutionMutationType(execution.Value), new AiExecutionMutationPayload { Execution = execution.Value });

        foreach (var budget in DiffById(previous.Budgets, next.Budgets, item => item.Budget.Id))
        {
            if (budget.Added)
                yield return Mutation(batch, ref index, AiDurableMutationType.BudgetCreated,
                    new AiBudgetMutationPayload { Budget = budget.Value });
        }

        foreach (var reservation in DiffById(previous.Reservations, next.Reservations, item => item.Id))
            yield return Mutation(batch, ref index, reservation.Added
                ? AiDurableMutationType.ReservationCreated
                : ReservationMutationType(reservation.Value), new AiReservationMutationPayload { Reservation = reservation.Value });

        foreach (var record in DiffById(previous.IdempotencyRecords, next.IdempotencyRecords, IdempotencyKey))
        {
            if (record.Added)
                yield return Mutation(batch, ref index, AiDurableMutationType.IdempotencyStored,
                    new AiIdempotencyStoredPayload { Record = record.Value });
        }

        foreach (var removed in RemovedById(previous.IdempotencyRecords, next.IdempotencyRecords, IdempotencyKey))
            yield return Mutation(batch, ref index, AiDurableMutationType.IdempotencyEvicted,
                new AiIdempotencyEvictedPayload
                {
                    ClientFingerprint = removed.ClientFingerprint,
                    Operation = removed.Operation,
                    IdempotencyId = removed.IdempotencyId
                });

        foreach (var entry in DiffById(previous.AuditEntries, next.AuditEntries, AuditKey))
            if (entry.Added)
                yield return Mutation(batch, ref index, AiDurableMutationType.AuditRecorded,
                    new AiAuditMutationPayload { Entry = entry.Value });

        if (previous.Tasks.Count != next.Tasks.Count ||
            previous.Executions.Count != next.Executions.Count ||
            previous.Reservations.Count != next.Reservations.Count)
            yield return Mutation(batch, ref index, AiDurableMutationType.RuntimeRecoveryCheckpoint,
                new AiRecoveryCheckpointPayload());
    }

    private static PendingTypedMutation Mutation(
        string batch,
        ref int index,
        AiDurableMutationType type,
        object payload)
        => new($"{batch}:{++index:000}", type, payload);

    private sealed record DiffItem<T>(bool Added, T Value);

    private static IEnumerable<DiffItem<T>> DiffById<T>(
        IReadOnlyList<T> previous,
        IReadOnlyList<T> next,
        Func<T, string> key)
    {
        var old = previous.ToDictionary(key, item => JsonSerializer.Serialize(item), StringComparer.Ordinal);
        foreach (var item in next.OrderBy(key, StringComparer.Ordinal))
        {
            var id = key(item);
            var json = JsonSerializer.Serialize(item);
            if (!old.TryGetValue(id, out var previousJson))
                yield return new DiffItem<T>(true, item);
            else if (!string.Equals(previousJson, json, StringComparison.Ordinal))
                yield return new DiffItem<T>(false, item);
        }
    }

    private static IEnumerable<T> RemovedById<T>(
        IReadOnlyList<T> previous,
        IReadOnlyList<T> next,
        Func<T, string> key)
    {
        var current = next.Select(key).ToHashSet(StringComparer.Ordinal);
        return previous.Where(item => !current.Contains(key(item))).OrderBy(key, StringComparer.Ordinal);
    }

    private static AiDurableMutationType TaskMutationType(AiDurableTaskSnapshot task)
        => task.Status switch
        {
            AiTaskStatus.Claimed => AiDurableMutationType.TaskClaimed,
            AiTaskStatus.Pending => AiDurableMutationType.TaskClaimReleased,
            AiTaskStatus.Completed or AiTaskStatus.Failed or AiTaskStatus.Cancelled => AiDurableMutationType.TaskSettled,
            _ => AiDurableMutationType.TaskStepChanged
        };

    private static AiDurableMutationType ExecutionMutationType(AiDurableExecutionSnapshot execution)
        => execution.State switch
        {
            AiExecutionState.Leased => AiDurableMutationType.ExecutionLeased,
            AiExecutionState.RecoveryRequired => AiDurableMutationType.ExecutionRecoveryResolved,
            _ => AiDurableMutationType.ExecutionStateChanged
        };

    private static AiDurableMutationType ReservationMutationType(AiDurableReservationSnapshot reservation)
        => reservation.State switch
        {
            AiReservationState.Committed => AiDurableMutationType.ReservationCommitted,
            AiReservationState.Expired => AiDurableMutationType.ReservationExpired,
            _ => AiDurableMutationType.ReservationReleased
        };

    private static string IdempotencyKey(AiDurableIdempotencyRecord record)
        => $"{record.ClientFingerprint.Length}:{record.ClientFingerprint}{record.Operation.Length}:{record.Operation}{record.IdempotencyId.Length}:{record.IdempotencyId}";

    private static string AuditKey(AiDurableAuditEntry entry)
        => $"{entry.TimestampUtc.Ticks}:{entry.Actor}:{entry.Action}:{entry.Success}:{entry.Detail}";

    private void PersistSynchronousResourceMutation(string operationType)
    {
        if (Status == AiRuntimeRecoveryStatus.RecoveryRequired)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryRequired,
                "Runtime ist nicht für Ressourcenmutationen freigegeben.");
        PersistSynchronousRuntimeTransition(operationType);
    }

    private void PersistSynchronousRuntimeTransition(string operationType)
    {
        if (Status == AiRuntimeRecoveryStatus.Disabled) return;
        if (Status == AiRuntimeRecoveryStatus.RecoveryRequired) return;
        if (Status != AiRuntimeRecoveryStatus.Ready)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryRequired,
                "Runtime ist nicht für Ressourcenmutationen freigegeben.");
        PersistMutationAsync(operationType).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(_runtime.Resources.DurableMutationRequired, _resourceMutationCallback))
            _runtime.Resources.DurableMutationRequired = null;
        if (ReferenceEquals(_runtime.Scheduler.DurableTransitionRequired, _schedulerTransitionCallback))
            _runtime.Scheduler.DurableTransitionRequired = null;
        await StopAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
