namespace AAIA.Air.Contracts;

public static class AiDurableMutationSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>Geschlossene Menge persistierbarer Phase-10-Mutationen.</summary>
public enum AiDurableMutationType
{
    TaskCreated,
    TaskClaimed,
    TaskClaimReleased,
    TaskStepChanged,
    TaskSettled,
    ExecutionQueued,
    ExecutionLeased,
    ExecutionStateChanged,
    ExecutionRecoveryResolved,
    BudgetCreated,
    ReservationCreated,
    ReservationCommitted,
    ReservationReleased,
    ReservationExpired,
    IdempotencyStored,
    IdempotencyEvicted,
    AuditRecorded,
    RuntimeRecoveryCheckpoint
}

/// <summary>Kanonischer Envelope; Payload wird ausschließlich über die geschlossene Registry gelesen.</summary>
public sealed class AiDurableMutationEnvelope
{
    public int SchemaVersion { get; init; } = AiDurableMutationSchema.CurrentVersion;
    public long Sequence { get; init; }
    public required string OperationId { get; init; }
    public AiDurableMutationType MutationType { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string? ActorFingerprint { get; init; }
    public string? InputFingerprint { get; init; }
    public required byte[] Payload { get; init; }
    public required string PayloadChecksumSha256 { get; init; }
}

/// <summary>Begrenzter Deduplizierungsnachweis über Snapshot-/Compact-Grenzen hinweg.</summary>
public sealed class AiDurableAppliedOperation
{
    public required string OperationId { get; init; }
    public AiDurableMutationType MutationType { get; init; }
    public required string PayloadChecksumSha256 { get; init; }
    public DateTime AppliedAtUtc { get; init; }
}

public sealed class AiTaskMutationPayload
{
    public required AiDurableTaskSnapshot Task { get; init; }
}

public sealed class AiExecutionMutationPayload
{
    public required AiDurableExecutionSnapshot Execution { get; init; }
}

public sealed class AiBudgetMutationPayload
{
    public required AiDurableBudgetSnapshot Budget { get; init; }
}

public sealed class AiReservationMutationPayload
{
    public required AiDurableReservationSnapshot Reservation { get; init; }
}

public sealed class AiIdempotencyStoredPayload
{
    public required AiDurableIdempotencyRecord Record { get; init; }
}

public sealed class AiIdempotencyEvictedPayload
{
    public required string ClientFingerprint { get; init; }
    public required string Operation { get; init; }
    public required string IdempotencyId { get; init; }
}

public sealed class AiAuditMutationPayload
{
    public required AiDurableAuditEntry Entry { get; init; }
}

public sealed class AiRecoveryCheckpointPayload
{
    public int ReleasedClaims { get; init; }
    public int ReleasedLeases { get; init; }
    public int ReleasedReservations { get; init; }
    public int RecoveryRequiredCount { get; init; }
}
