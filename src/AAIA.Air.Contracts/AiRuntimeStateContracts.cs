namespace AAIA.Air.Contracts;

public static class AiRuntimeStateSchema
{
    public const int CurrentVersion = 1;
    public const int MinimumSupportedVersion = 1;

    public static bool IsSupported(int version)
        => version is >= MinimumSupportedVersion and <= CurrentVersion;
}

public static class AiRuntimeStateReasonCodes
{
    public const string StoreLocked = "state_store_locked";
    public const string SchemaUnsupported = "state_schema_unsupported";
    public const string SnapshotCorrupt = "state_snapshot_corrupt";
    public const string JournalGap = "state_journal_gap";
    public const string JournalCorrupt = "state_journal_corrupt";
    public const string JournalChecksumFailed = "state_journal_checksum_failed";
    public const string JournalEventUnknown = "state_journal_event_unknown";
    public const string ProtectorUnavailable = "state_protector_unavailable";
    public const string PayloadRejected = "state_payload_rejected";
    public const string RecoveryRequired = "state_recovery_required";
    public const string RecoveryForbidden = "state_recovery_forbidden";
    public const string QuotaExceeded = "state_quota_exceeded";
    public const string Disabled = "state_store_disabled";
    public const string ReadOnly = "state_store_read_only";
    public const string Quarantined = "state_store_quarantined";
    public const string BackupMissing = "state_backup_missing";
    public const string RepairNotRequired = "state_repair_not_required";
}

public enum AiStateStoreOpenMode
{
    ReadOnly,
    ReadWrite
}

public enum AiRuntimeRecoveryStatus
{
    Disabled,
    Ready,
    Recovering,
    RecoveryRequired,
    RecoveryFailed,
    Quarantined
}

public sealed class AiRuntimeStateDiagnostics
{
    public required string StoreId { get; init; }
    public AiRuntimeRecoveryStatus Status { get; init; }
    public int? SchemaVersion { get; init; }
    public long LastSequence { get; init; }
    public long SnapshotSequence { get; init; }
    public long StoreSizeBytes { get; init; }
    public DateTime? LastUpdatedAtUtc { get; init; }
    public string? ReasonCode { get; init; }
    public string? RedactedMessage { get; init; }
}

public sealed class AiStateStoreBackupResult
{
    public required string BackupId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public long ThroughSequence { get; init; }
}

public sealed class AiStateStoreRepairResult
{
    public bool Repaired { get; init; }
    public required string BackupId { get; init; }
    public string? ReasonCode { get; init; }
}

public sealed class AiStateMaintenanceOperationResult
{
    public required string Action { get; init; }
    public bool Success { get; init; }
    public string? BackupId { get; init; }
    public string? ReasonCode { get; init; }
}

public sealed class AiStateStoreException : InvalidOperationException
{
    public string ReasonCode { get; }

    public AiStateStoreException(string reasonCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ReasonCode = reasonCode;
    }

    public AiStateStoreException(string reasonCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentNullException.ThrowIfNull(innerException);
        ReasonCode = reasonCode;
    }
}

public sealed class AiRuntimePersistenceOptions
{
    public bool Enabled { get; set; }
    public long MaxStoreBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxProtectedPayloadBytes { get; set; } = 1024 * 1024;
    public int SnapshotJournalEntryThreshold { get; set; } = 1000;
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan IdempotencyTtl { get; set; } = TimeSpan.FromHours(24);
    public int MaxIdempotencyEntries { get; set; } = 10_000;
}

public sealed class AiRuntimeStateManifest
{
    public int SchemaVersion { get; init; } = AiRuntimeStateSchema.CurrentVersion;
    public required string StoreId { get; init; }
    public required string RuntimeInstanceId { get; init; }
    public long LastSequence { get; init; }
    public long SnapshotSequence { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string? SnapshotChecksumSha256 { get; init; }
    public IReadOnlyDictionary<string, bool> FeatureFlags { get; init; }
        = new Dictionary<string, bool>(StringComparer.Ordinal);
}

public sealed class AiRuntimeStateSnapshot
{
    public int SchemaVersion { get; init; } = AiRuntimeStateSchema.CurrentVersion;
    public long Sequence { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public required byte[] Payload { get; init; }
    public required string ChecksumSha256 { get; init; }
}

public sealed class AiRuntimeJournalEntry
{
    public int SchemaVersion { get; init; } = AiRuntimeStateSchema.CurrentVersion;
    public long Sequence { get; init; }
    public required string OperationId { get; init; }
    public required string EventType { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public bool IsProtected { get; init; }
    public required byte[] Payload { get; init; }
    public required string ChecksumSha256 { get; init; }
}

public sealed class AiStateProtectionContext
{
    public required string StoreId { get; init; }
    public required string RecordType { get; init; }
    public required string RecordId { get; init; }
    public int SchemaVersion { get; init; } = AiRuntimeStateSchema.CurrentVersion;
}

public sealed class AiProtectedStatePayload
{
    public required string ProtectorId { get; init; }
    public required byte[] Ciphertext { get; init; }
}

public interface IAiStateProtector
{
    string ProtectorId { get; }

    ValueTask<AiProtectedStatePayload> ProtectAsync(
        ReadOnlyMemory<byte> plaintext,
        AiStateProtectionContext context,
        CancellationToken ct = default);

    ValueTask<byte[]> UnprotectAsync(
        AiProtectedStatePayload protectedPayload,
        AiStateProtectionContext context,
        CancellationToken ct = default);
}

public interface IAiRuntimeStateStore
{
    string StoreId { get; }

    ValueTask<IAiRuntimeStateStoreSession> OpenAsync(
        AiStateStoreOpenMode mode,
        string runtimeInstanceId,
        CancellationToken ct = default);
}

/// <summary>Optionale Host-Grenze für lokale, explizit autorisierte Wartung.</summary>
public interface IAiRuntimeStateMaintenanceStore
{
    ValueTask<AiRuntimeStateDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default);
    ValueTask<AiStateStoreBackupResult> CreateBackupAsync(
        string runtimeInstanceId,
        CancellationToken ct = default);
    ValueTask<AiStateStoreRepairResult> RepairAsync(
        string runtimeInstanceId,
        string backupId,
        CancellationToken ct = default);
}

public interface IAiStateMaintenanceAuthorizer
{
    bool IsAuthorized(string actorId, string action, bool confirmed, out string? denialReason);
}

public interface IAiRuntimeStateStoreSession : IAsyncDisposable
{
    string StoreId { get; }
    string RuntimeInstanceId { get; }
    AiStateStoreOpenMode Mode { get; }
    bool IsQuarantined { get; }
    string? QuarantineReason { get; }

    ValueTask<AiRuntimeStateManifest?> LoadManifestAsync(CancellationToken ct = default);
    ValueTask WriteManifestAsync(AiRuntimeStateManifest manifest, CancellationToken ct = default);

    ValueTask<AiRuntimeStateSnapshot?> LoadSnapshotAsync(CancellationToken ct = default);
    ValueTask WriteSnapshotAsync(AiRuntimeStateSnapshot snapshot, CancellationToken ct = default);

    IAsyncEnumerable<AiRuntimeJournalEntry> ReadJournalAsync(
        long afterSequence,
        CancellationToken ct = default);

    ValueTask AppendJournalAsync(AiRuntimeJournalEntry entry, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
    ValueTask CompactAsync(long throughSequence, CancellationToken ct = default);
    ValueTask QuarantineAsync(string reason, CancellationToken ct = default);
}
