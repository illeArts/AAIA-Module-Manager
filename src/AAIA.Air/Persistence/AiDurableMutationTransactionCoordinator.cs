using System.Text.Json;

namespace AAIA.Air.Persistence;

public sealed class AiDurableMutationCommitResult
{
    public long Sequence { get; init; }
    public bool Applied { get; init; }
    public bool SnapshotCreated { get; init; }
    public bool SnapshotDeferred { get; init; }
}

/// <summary>
/// Phase-10.1.2-Referenztransaktion auf einer bereits exklusiv geöffneten Store-Session.
/// Sie schaltet den produktiven Phase-9-Writer ausdrücklich noch nicht um.
/// </summary>
public sealed class AiDurableMutationTransactionCoordinator : IAsyncDisposable
{
    public const string Phase9CheckpointEventType = "orchestration.checkpoint";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64
    };

    private readonly IAiRuntimeStateStoreSession _session;
    private readonly AiRuntimePersistenceOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly string _runtimeInstanceId;
    private AiDurableMutationReducer _reducer;
    private long _snapshotSequence;
    private DateTime _lastSnapshotAtUtc;
    private bool _disposed;

    public AiRuntimeRecoveryStatus Status { get; private set; }
    public string? FailureReasonCode { get; private set; }
    public string? SnapshotFailureReasonCode { get; private set; }
    public long LastSequence
    {
        get { lock (_stateGate) return _reducer.LastSequence; }
    }

    public long SnapshotSequence
    {
        get { lock (_stateGate) return _snapshotSequence; }
    }

    private AiDurableMutationTransactionCoordinator(
        IAiRuntimeStateStoreSession session,
        AiRuntimePersistenceOptions options,
        TimeProvider time,
        AiDurableMutationReducer reducer,
        long snapshotSequence,
        DateTime lastSnapshotAtUtc)
    {
        _session = session;
        _options = options;
        _time = time;
        _reducer = reducer;
        _snapshotSequence = snapshotSequence;
        _lastSnapshotAtUtc = lastSnapshotAtUtc;
        _runtimeInstanceId = session.RuntimeInstanceId;
        Status = AiRuntimeRecoveryStatus.Ready;
    }

    public static async ValueTask<AiDurableMutationTransactionCoordinator> RecoverAsync(
        IAiRuntimeStateStoreSession session,
        AiRuntimePersistenceOptions options,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        if (session.Mode != AiStateStoreOpenMode.ReadWrite)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.ReadOnly,
                "Durable Mutation Transactions benötigen eine Writer-Session.");
        ValidateOptions(options);
        var time = timeProvider ?? TimeProvider.System;
        var now = time.GetUtcNow().UtcDateTime;
        var stateSnapshot = await session.LoadSnapshotAsync(ct).ConfigureAwait(false);
        var durable = stateSnapshot is null
            ? new AiDurableOrchestrationSnapshot { CreatedAtUtc = DateTime.UnixEpoch }
            : DeserializeSnapshot(stateSnapshot, options.MaxProtectedPayloadBytes);
        var reducer = new AiDurableMutationReducer(durable, stateSnapshot?.Sequence ?? 0);
        var snapshotSequence = stateSnapshot?.Sequence ?? 0;
        var lastSnapshotAtUtc = stateSnapshot?.CreatedAtUtc ?? now;

        await foreach (var entry in session.ReadJournalAsync(snapshotSequence, ct).ConfigureAwait(false))
        {
            if (entry.Sequence != reducer.LastSequence + 1)
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalGap,
                    $"Journal-Sequenz {entry.Sequence} ist ungültig; erwartet wird {reducer.LastSequence + 1}.");

            if (string.Equals(entry.EventType, Phase9CheckpointEventType, StringComparison.Ordinal))
            {
                AiRuntimeStateCodec.VerifyJournalEntry(entry, options.MaxProtectedPayloadBytes);
                var checkpoint = DeserializePayload(entry.Payload);
                reducer = new AiDurableMutationReducer(checkpoint, entry.Sequence);
                continue;
            }

            reducer.Apply(AiDurableMutationCodec.FromJournalEntry(
                entry, options.MaxProtectedPayloadBytes));
        }

        return new AiDurableMutationTransactionCoordinator(
            session, options, time, reducer, snapshotSequence, lastSnapshotAtUtc);
    }

    public async ValueTask<AiDurableMutationCommitResult> CommitAsync(
        string operationId,
        AiDurableMutationType mutationType,
        object payload,
        string? actorFingerprint = null,
        string? inputFingerprint = null,
        CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            var now = _time.GetUtcNow().UtcDateTime;
            var currentSequence = LastSequence;
            var envelope = AiDurableMutationCodec.CreateEnvelope(
                checked(currentSequence + 1), operationId, mutationType, now, payload,
                actorFingerprint, inputFingerprint, _options.MaxProtectedPayloadBytes);

            // Vollständige semantische Prüfung vor dem ersten Store-Write.
            AiDurableOrchestrationSnapshot current;
            lock (_stateGate) current = _reducer.CreateSnapshot(now);
            var validation = new AiDurableMutationReducer(current, currentSequence);
            if (!validation.Apply(envelope))
                return new AiDurableMutationCommitResult
                {
                    Sequence = currentSequence,
                    Applied = false
                };

            var entry = AiDurableMutationCodec.ToJournalEntry(
                envelope, _options.MaxProtectedPayloadBytes);
            try
            {
                await _session.AppendJournalAsync(entry, ct).ConfigureAwait(false);
                await _session.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                Status = AiRuntimeRecoveryStatus.RecoveryRequired;
                FailureReasonCode = AiRuntimeStateReasonCodes.PersistenceFailed;
                throw;
            }

            try
            {
                lock (_stateGate) _reducer.Apply(envelope);
            }
            catch (Exception ex)
            {
                Status = AiRuntimeRecoveryStatus.RecoveryRequired;
                FailureReasonCode = AiRuntimeStateReasonCodes.RecoveryRequired;
                throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryRequired,
                    "Delta wurde dauerhaft bestätigt, konnte aber nicht im Speicher angewendet werden. " +
                    "Ein Recovery-Neustart ist erforderlich.", ex);
            }

            var snapshotCreated = false;
            var snapshotDeferred = false;
            if (IsSnapshotDue(now))
            {
                try
                {
                    await WriteVerifiedSnapshotAndCompactLockedAsync(now, ct).ConfigureAwait(false);
                    snapshotCreated = true;
                }
                catch (Exception ex)
                {
                    // Die Mutation ist bereits durable und angewendet. Der alte Snapshot plus
                    // Journal bleibt der Recovery-Punkt; Compact wurde noch nicht ausgeführt.
                    SnapshotFailureReasonCode = ex is AiStateStoreException stateError
                        ? stateError.ReasonCode
                        : AiRuntimeStateReasonCodes.PersistenceFailed;
                    snapshotDeferred = true;
                }
            }

            return new AiDurableMutationCommitResult
            {
                Sequence = envelope.Sequence,
                Applied = true,
                SnapshotCreated = snapshotCreated,
                SnapshotDeferred = snapshotDeferred
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> SnapshotIfDueAsync(CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            var now = _time.GetUtcNow().UtcDateTime;
            if (!IsSnapshotDue(now)) return false;
            await WriteVerifiedSnapshotAndCompactLockedAsync(now, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CreateShutdownSnapshotAsync(CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (LastSequence > SnapshotSequence)
                await WriteVerifiedSnapshotAndCompactLockedAsync(
                    _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public AiDurableOrchestrationSnapshot CreateCurrentSnapshot(DateTime createdAtUtc)
    {
        lock (_stateGate) return _reducer.CreateSnapshot(createdAtUtc);
    }

    private bool IsSnapshotDue(DateTime now)
    {
        lock (_stateGate)
            return _reducer.LastSequence > _snapshotSequence &&
                   (_reducer.LastSequence - _snapshotSequence >= _options.SnapshotJournalEntryThreshold ||
                    now - _lastSnapshotAtUtc >= _options.SnapshotInterval);
    }

    private async ValueTask WriteVerifiedSnapshotAndCompactLockedAsync(
        DateTime now,
        CancellationToken ct)
    {
        AiDurableOrchestrationSnapshot durable;
        long sequence;
        lock (_stateGate)
        {
            sequence = _reducer.LastSequence;
            if (sequence <= _snapshotSequence) return;
            durable = _reducer.CreateSnapshot(now);
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(durable, JsonOptions);
        var snapshot = AiRuntimeStateCodec.CreateSnapshot(
            sequence, now, payload, AiRuntimeStateSchema.CurrentVersion,
            _options.MaxProtectedPayloadBytes);

        await _session.WriteSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        await _session.FlushAsync(ct).ConfigureAwait(false);
        var reloaded = await _session.LoadSnapshotAsync(ct).ConfigureAwait(false)
            ?? throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "Der gerade geschriebene Snapshot fehlt beim Verify.");
        AiRuntimeStateCodec.VerifySnapshot(reloaded, _options.MaxProtectedPayloadBytes);
        if (reloaded.Sequence != sequence ||
            !string.Equals(reloaded.ChecksumSha256, snapshot.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "Der gerade geschriebene Snapshot stimmt nicht mit dem Verify-Read überein.");
        _ = DeserializeSnapshot(reloaded, _options.MaxProtectedPayloadBytes);

        var previousManifest = await _session.LoadManifestAsync(ct).ConfigureAwait(false);
        var manifest = new AiRuntimeStateManifest
        {
            StoreId = _session.StoreId,
            RuntimeInstanceId = _runtimeInstanceId,
            LastSequence = sequence,
            SnapshotSequence = sequence,
            CreatedAtUtc = previousManifest?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            SnapshotChecksumSha256 = reloaded.ChecksumSha256,
            FeatureFlags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["orchestration"] = true,
                ["typedDeltaJournal"] = true
            }
        };
        await _session.WriteManifestAsync(manifest, ct).ConfigureAwait(false);
        await _session.FlushAsync(ct).ConfigureAwait(false);

        // Erst Snapshot-Verify und Manifest-Flush, dann Compact.
        await _session.CompactAsync(sequence, ct).ConfigureAwait(false);
        await _session.FlushAsync(ct).ConfigureAwait(false);
        lock (_stateGate)
        {
            _snapshotSequence = sequence;
            _lastSnapshotAtUtc = now;
        }
        SnapshotFailureReasonCode = null;
    }

    private static AiDurableOrchestrationSnapshot DeserializeSnapshot(
        AiRuntimeStateSnapshot snapshot,
        int maxPayloadBytes)
    {
        AiRuntimeStateCodec.VerifySnapshot(snapshot, maxPayloadBytes);
        return DeserializePayload(snapshot.Payload);
    }

    private static AiDurableOrchestrationSnapshot DeserializePayload(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(payload, JsonOptions)
                ?? throw new JsonException("Durable Snapshot-Payload ist leer.");
        }
        catch (JsonException ex)
        {
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "Durable Snapshot-Payload ist ungültig.", ex);
        }
    }

    private static void ValidateOptions(AiRuntimePersistenceOptions options)
    {
        if (options.MaxProtectedPayloadBytes <= 0 || options.SnapshotJournalEntryThreshold <= 0 ||
            options.SnapshotInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Payload-Limit und Snapshot-Grenzen müssen positiv sein.");
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Status != AiRuntimeRecoveryStatus.Ready)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryRequired,
                "Durable Mutation Transactions sind bis zum Recovery gesperrt.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
