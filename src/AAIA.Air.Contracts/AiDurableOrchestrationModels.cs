namespace AAIA.Air.Contracts;

/// <summary>Geschützter, sessionfreier Snapshot eines Task-Schritts.</summary>
public sealed class AiDurableTaskStepSnapshot
{
    public required string ToolName { get; init; }
    public AiTaskStepStatus Status { get; init; }
    public required AiProtectedStatePayload ProtectedInput { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>Persistierbarer Task ohne Owner-Session, Handler oder Ergebnis-Payload.</summary>
public sealed class AiDurableTaskSnapshot
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string? Project { get; init; }
    public AiTaskStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyList<AiDurableTaskStepSnapshot> Steps { get; init; }
        = Array.Empty<AiDurableTaskStepSnapshot>();
}

/// <summary>Persistierbare Execution ohne Lease, Session oder Cancellation Token.</summary>
public sealed class AiDurableExecutionSnapshot
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public string? SubmittedByClientId { get; init; }
    public AiExecutionPriority Priority { get; init; }
    public AiRole? RequiredRole { get; init; }
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public DateTime EnqueuedAtUtc { get; init; }
    public DateTime? NotBeforeUtc { get; init; }
    public int MaxAttempts { get; init; }
    public AiResourceRequirements? ResourceRequirements { get; init; }
    public AiExecutionState State { get; init; }
    public int AttemptCount { get; init; }
    public string? LastErrorCode { get; init; }
    public string? ResourceId { get; init; }
    public string? ResourceReservationId { get; init; }
    public int ResourceDeferralCount { get; init; }
    public DateTime? ResourceDeferredUntilUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

/// <summary>Persistierbarer Budgetstand. Werte werden beim Restore aus der Historie verifiziert.</summary>
public sealed class AiDurableBudgetSnapshot
{
    public required AiResourceBudget Budget { get; init; }
    public decimal Spent { get; init; }
    public decimal Reserved { get; init; }
}

/// <summary>Sessionfreie Reservationshistorie ohne Live-Profil oder Telemetrie.</summary>
public sealed class AiDurableReservationSnapshot
{
    public required string Id { get; init; }
    public required string ResourceId { get; init; }
    public required string ExecutionRequestId { get; init; }
    public required string TaskId { get; init; }
    public AiReservationState State { get; init; }
    public required string CostUnit { get; init; }
    public decimal EstimatedCost { get; init; }
    public decimal? ActualCost { get; init; }
    public DateTime ReservedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
    public string? SettlementReasonCode { get; init; }
    public IReadOnlyList<string> BudgetIds { get; init; } = Array.Empty<string>();
}

/// <summary>Begrenzter Idempotenzdatensatz ohne Request- oder Result-Payload.</summary>
public sealed class AiDurableIdempotencyRecord
{
    public required string ClientFingerprint { get; init; }
    public required string Operation { get; init; }
    public required string IdempotencyId { get; init; }
    public required string InputFingerprint { get; init; }
    public required string ResultId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
}

/// <summary>Versionierte Nutzlast eines AIR-Orchestrierungs-Snapshots.</summary>
public sealed class AiDurableOrchestrationSnapshot
{
    public int SchemaVersion { get; init; } = AiRuntimeStateSchema.CurrentVersion;
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<AiDurableTaskSnapshot> Tasks { get; init; }
        = Array.Empty<AiDurableTaskSnapshot>();
    public IReadOnlyList<AiDurableExecutionSnapshot> Executions { get; init; }
        = Array.Empty<AiDurableExecutionSnapshot>();
    public IReadOnlyList<AiDurableBudgetSnapshot> Budgets { get; init; }
        = Array.Empty<AiDurableBudgetSnapshot>();
    public IReadOnlyList<AiDurableReservationSnapshot> Reservations { get; init; }
        = Array.Empty<AiDurableReservationSnapshot>();
    public IReadOnlyList<AiDurableIdempotencyRecord> IdempotencyRecords { get; init; }
        = Array.Empty<AiDurableIdempotencyRecord>();
}

public sealed class AiOrchestrationRestoreReport
{
    public int TaskCount { get; init; }
    public int ExecutionCount { get; init; }
    public int ReleasedClaims { get; init; }
    public int ReleasedLeases { get; init; }
    public int RecoveryRequiredCount { get; init; }
    public int BudgetCount { get; init; }
    public int ReservationCount { get; init; }
    public int RecoveryReleasedReservations { get; init; }
    public int IdempotencyRecordCount { get; init; }
}

/// <summary>Host-Grenze für lokale Owner/Admin-Entscheidungen nach einem Crash.</summary>
public interface IAiRecoveryAuthorizer
{
    bool IsAuthorized(string actorId, string action, out string? denialReason);
}
