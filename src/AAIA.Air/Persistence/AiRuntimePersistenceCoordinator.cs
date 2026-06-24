namespace AAIA.Air.Persistence;

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
    private AiRuntimeStateManifest? _manifest;
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
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null) return Status;
            Status = AiRuntimeRecoveryStatus.Recovering;
            _session = await _store.OpenAsync(
                AiStateStoreOpenMode.ReadWrite, _runtimeInstanceId, ct).ConfigureAwait(false);
            if (_session.IsQuarantined)
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.Quarantined,
                    "State Store ist quarantänisiert.");

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
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
            var result = await operation(ct).ConfigureAwait(false);
            _session = await _store.OpenAsync(
                AiStateStoreOpenMode.ReadWrite, _runtimeInstanceId, ct).ConfigureAwait(false);
            _manifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
            var snapshot = await _session.LoadSnapshotAsync(ct).ConfigureAwait(false);
            _sequence = Math.Max(_manifest?.LastSequence ?? 0, snapshot?.Sequence ?? 0);
            await PersistCheckpointLockedAsync("runtime.maintenance", ct).ConfigureAwait(false);
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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_runtime.Resources.DurableMutationRequired, _resourceMutationCallback))
                _runtime.Resources.DurableMutationRequired = null;
            if (ReferenceEquals(_runtime.Scheduler.DurableTransitionRequired, _schedulerTransitionCallback))
                _runtime.Scheduler.DurableTransitionRequired = null;
            if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
