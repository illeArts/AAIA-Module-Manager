namespace AAIA.Air.Persistence;

public sealed class AiRuntimeReadinessLease : IDisposable
{
    private readonly AiRuntimeLifecycle _owner;
    private readonly Guid _runtimeGeneration;
    private bool _disposed;

    internal AiRuntimeReadinessLease(AiRuntimeLifecycle owner, Guid runtimeGeneration)
    {
        _owner = owner;
        _runtimeGeneration = runtimeGeneration;
    }

    public bool IsValid => !_disposed && _owner.IsLeaseValid(_runtimeGeneration);

    public void ThrowIfExpired()
    {
        if (!IsValid)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ReadinessExpired,
                "Runtime-Readiness-Lease ist abgelaufen.");
    }

    public void Dispose() => _disposed = true;
}

public sealed class AiRuntimeLifecycleOptions
{
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// App-neutraler Lifecycle für AIR-Hosts. Er öffnet höchstens einen Writer, gibt
/// Adapter erst nach Ready frei und invalidiert Readiness-Leases vor Shutdown/Recovery.
/// </summary>
public sealed class AiRuntimeLifecycle : IAsyncDisposable
{
    private readonly AiRuntimeService _runtime;
    private readonly AiRuntimePersistenceCoordinator? _persistence;
    private readonly IAiRuntimeStateMaintenanceStore? _maintenanceStore;
    private readonly Func<CancellationToken, ValueTask>? _adapterStart;
    private readonly Func<CancellationToken, ValueTask>? _adapterStop;
    private readonly AiRuntimeLifecycleOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid _generation = Guid.NewGuid();
    private bool _disposed;

    public AiRuntimeRecoveryStatus Status { get; private set; } = AiRuntimeRecoveryStatus.Stopped;
    public string? FailureReasonCode { get; private set; }

    public AiRuntimeLifecycle(
        AiRuntimeService runtime,
        AiRuntimePersistenceCoordinator? persistence = null,
        IAiRuntimeStateMaintenanceStore? maintenanceStore = null,
        Func<CancellationToken, ValueTask>? adapterStart = null,
        Func<CancellationToken, ValueTask>? adapterStop = null,
        AiRuntimeLifecycleOptions? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _persistence = persistence;
        _maintenanceStore = maintenanceStore;
        _adapterStart = adapterStart;
        _adapterStop = adapterStop;
        _options = options ?? new AiRuntimeLifecycleOptions();
        if (_options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        _runtime.AttachReadinessGate(() =>
            Status is AiRuntimeRecoveryStatus.Ready or AiRuntimeRecoveryStatus.Disabled);
    }

    public async ValueTask<AiRuntimeRecoveryStatus> InitializeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Status == AiRuntimeRecoveryStatus.Ready) return Status;
            if (Status is AiRuntimeRecoveryStatus.Stopping)
                throw InvalidTransition("Lifecycle stoppt bereits.");
            _generation = Guid.NewGuid();
            Status = _persistence is null
                ? AiRuntimeRecoveryStatus.Disabled
                : AiRuntimeRecoveryStatus.OpeningStore;
            FailureReasonCode = null;

            var recovered = AiRuntimeRecoveryStatus.Disabled;
            if (_persistence is not null)
            {
                Status = AiRuntimeRecoveryStatus.Recovering;
                recovered = await _persistence.InitializeAsync(ct).ConfigureAwait(false);
                Status = recovered;
                if (recovered == AiRuntimeRecoveryStatus.RecoveryRequired)
                    return Status;
                if (recovered is not (AiRuntimeRecoveryStatus.Ready or AiRuntimeRecoveryStatus.Disabled))
                    throw new AiStateStoreException(
                        _persistence.FailureReasonCode ?? AiRuntimeStateReasonCodes.PersistenceFailed,
                        "Runtime-Recovery wurde nicht erfolgreich abgeschlossen.");
            }

            if (_adapterStart is not null)
                await _adapterStart(ct).ConfigureAwait(false);
            Status = recovered == AiRuntimeRecoveryStatus.Disabled
                ? AiRuntimeRecoveryStatus.Disabled
                : AiRuntimeRecoveryStatus.Ready;
            return Status;
        }
        catch (Exception ex)
        {
            FailureReasonCode = ex is AiStateStoreException stateError
                ? stateError.ReasonCode
                : AiRuntimeStateReasonCodes.PersistenceFailed;
            Status = ex is AiStateStoreException { ReasonCode: AiRuntimeStateReasonCodes.Quarantined }
                ? AiRuntimeRecoveryStatus.Quarantined
                : AiRuntimeRecoveryStatus.RecoveryFailed;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> CompleteRecoveryAsync(CancellationToken ct = default)
    {
        if (_persistence is null) return Status is AiRuntimeRecoveryStatus.Disabled or AiRuntimeRecoveryStatus.Ready;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var ready = await _persistence.CompleteRecoveryAsync(ct).ConfigureAwait(false);
            Status = ready ? AiRuntimeRecoveryStatus.Ready : AiRuntimeRecoveryStatus.RecoveryRequired;
            return ready;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<AiRuntimeStateDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        if (_maintenanceStore is null)
            return new AiRuntimeStateDiagnostics
            {
                StoreId = "unconfigured",
                Status = Status,
                ReasonCode = FailureReasonCode
            };
        var diagnostics = await _maintenanceStore.GetDiagnosticsAsync(ct).ConfigureAwait(false);
        return new AiRuntimeStateDiagnostics
        {
            StoreId = diagnostics.StoreId,
            Status = Status is AiRuntimeRecoveryStatus.Stopped or AiRuntimeRecoveryStatus.Disabled
                ? diagnostics.Status
                : Status,
            SchemaVersion = diagnostics.SchemaVersion,
            LastSequence = diagnostics.LastSequence,
            SnapshotSequence = diagnostics.SnapshotSequence,
            StoreSizeBytes = diagnostics.StoreSizeBytes,
            LastUpdatedAtUtc = diagnostics.LastUpdatedAtUtc,
            ReasonCode = FailureReasonCode ?? diagnostics.ReasonCode,
            RedactedMessage = diagnostics.RedactedMessage
        };
    }

    public AiRuntimeReadinessLease CreateReadinessLease()
    {
        if (Status is not (AiRuntimeRecoveryStatus.Ready or AiRuntimeRecoveryStatus.Disabled))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ReadinessExpired,
                "Runtime ist nicht Ready.");
        return new AiRuntimeReadinessLease(this, _generation);
    }

    public async ValueTask StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Status is AiRuntimeRecoveryStatus.Stopped) return;
            Status = AiRuntimeRecoveryStatus.Stopping;
            _generation = Guid.NewGuid();
            using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                if (_persistence is not null)
                    await _persistence.StopAsync(linked.Token).ConfigureAwait(false);
                if (_adapterStop is not null)
                    await _adapterStop(linked.Token).ConfigureAwait(false);
                Status = AiRuntimeRecoveryStatus.Stopped;
            }
            catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                FailureReasonCode = AiRuntimeStateReasonCodes.ShutdownIncomplete;
                Status = AiRuntimeRecoveryStatus.RecoveryFailed;
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ShutdownIncomplete,
                    "Runtime-Shutdown wurde nicht vollständig bestätigt.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal bool IsLeaseValid(Guid generation)
        => Status is (AiRuntimeRecoveryStatus.Ready or AiRuntimeRecoveryStatus.Disabled) &&
           generation == _generation;

    private static AiStateStoreException InvalidTransition(string message)
        => new(AiRuntimeStateReasonCodes.LifecycleInvalidTransition, message);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Dispose ist idempotenter Fallback; explizite StopAsync-Aufrufer erhalten Fehler.
        }
        _runtime.AttachReadinessGate(null);
        _gate.Dispose();
        _disposed = true;
    }
}
