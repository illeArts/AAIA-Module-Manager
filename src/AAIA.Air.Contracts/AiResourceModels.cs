namespace AAIA.Air.Contracts;

public enum AiResourceKind { Inference, Compute, ToolHost }
public enum AiResourceLocality { Local, PrivateNetwork, Remote }
public enum AiBudgetScope { Runtime, Project, Session, Task }
public enum AiBudgetWindow { Execution, Hour, Day, Month }
public enum AiReservationState { Reserved, Committed, Released, Expired }
public enum AiResourceDecisionStatus { Selected, Denied }

public static class AiResourceReasonCodes
{
    public const string NoMatchingResource = "no_matching_resource";
    public const string CapacityUnavailable = "capacity_unavailable";
    public const string TelemetryStale = "telemetry_stale";
    public const string BudgetExceeded = "budget_exceeded";
    public const string CostUnitMismatch = "cost_unit_mismatch";
    public const string PinnedResourceUnavailable = "pinned_resource_unavailable";
    public const string ResourceUnhealthy = "resource_unhealthy";
}

public sealed class AiResourceCapacity
{
    public int? MaxConcurrentExecutions { get; init; }
    public int? ContextWindowTokens { get; init; }
    public int? RequestsPerMinute { get; init; }
    public int? TokensPerMinute { get; init; }
    public decimal? WorkUnitsPerMinute { get; init; }
    public int? MemoryMiB { get; init; }
}

public sealed class AiResourceCostRate
{
    public required string CostUnit { get; init; }
    public decimal FixedPerExecution { get; init; }
    public decimal PerThousandInputUnits { get; init; }
    public decimal PerThousandOutputUnits { get; init; }
    public decimal PerWorkUnit { get; init; }
}

public sealed class AiResourceProfile
{
    public required string ResourceId { get; init; }
    public required string ProviderId { get; init; }
    public string DisplayName { get; init; } = "";
    public AiResourceKind Kind { get; init; }
    public bool Enabled { get; init; } = true;
    public AiResourceLocality Locality { get; init; } = AiResourceLocality.Remote;
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public required AiResourceCapacity Capacity { get; init; }
    public AiResourceCostRate? CostRate { get; init; }
}

public sealed class AiResourceTelemetry
{
    public required string ResourceId { get; init; }
    public DateTime ObservedAtUtc { get; init; }
    public bool Healthy { get; init; } = true;
    public bool Throttled { get; init; }
    public int ExternalRunningExecutions { get; init; }
    public int RequestsInCurrentMinute { get; init; }
    public int TokensInCurrentMinute { get; init; }
    public decimal WorkUnitsInCurrentMinute { get; init; }
    public double? QueueLatencyMs { get; init; }
    public double? P95ExecutionLatencyMs { get; init; }
    public double? FailureRate { get; init; }
}

/// <summary>Anforderungen ohne Execution-Kontext; kann am Scheduler-Eintrag hängen.</summary>
public sealed class AiResourceRequirements
{
    public AiResourceKind Kind { get; init; }
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public int? MinimumContextTokens { get; init; }
    public int? MinimumMemoryMiB { get; init; }
    public decimal? MinimumWorkUnitsPerMinute { get; init; }
    public int EstimatedInputUnits { get; init; }
    public int EstimatedOutputUnits { get; init; }
    public decimal EstimatedWorkUnits { get; init; }
    public string? CostUnit { get; init; }
    public string? PinnedResourceId { get; init; }
    public TimeSpan ReservationDuration { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class AiResourceRequest
{
    public required string ExecutionRequestId { get; init; }
    public required string TaskId { get; init; }
    public string? ProjectId { get; init; }
    public required string SessionId { get; init; }
    public required AiResourceRequirements Requirements { get; init; }
}

public sealed class AiResourceBudget
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public AiBudgetScope Scope { get; init; }
    public string? ScopeId { get; init; }
    public required string CostUnit { get; init; }
    public AiBudgetWindow Window { get; init; }
    public decimal HardLimit { get; init; }
    public decimal? WarningThreshold { get; init; }
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
}

public sealed class AiResourceBudgetSnapshot
{
    public required AiResourceBudget Budget { get; init; }
    public decimal Spent { get; init; }
    public decimal Reserved { get; init; }
}

public sealed class AiResourceRejection
{
    public required string ResourceId { get; init; }
    public required string ReasonCode { get; init; }
    public string Message { get; init; } = "";
    public bool Retryable { get; init; }
    public DateTime? RetryAfterUtc { get; init; }
}

public sealed class AiResourceScoreBreakdown
{
    public double Capacity { get; init; }
    public double Cost { get; init; }
    public double Reliability { get; init; }
    public double Latency { get; init; }
    public double Locality { get; init; }
    public double Total { get; init; }
}

public sealed class AiResourceReservation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public required string ResourceId { get; init; }
    public required string ExecutionRequestId { get; init; }
    public required string TaskId { get; init; }
    public required string SessionId { get; init; }
    public AiReservationState State { get; init; }
    public required string CostUnit { get; init; }
    public decimal EstimatedCost { get; init; }
    public decimal? ActualCost { get; init; }
    public DateTime ReservedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
}

public sealed class AiResourceDecision
{
    public AiResourceDecisionStatus Status { get; init; }
    public string? ReasonCode { get; init; }
    public bool Retryable { get; init; }
    public DateTime? RetryAfterUtc { get; init; }
    public string? SelectedResourceId { get; init; }
    public AiResourceReservation? Reservation { get; init; }
    public AiResourceScoreBreakdown? Score { get; init; }
    public IReadOnlyList<AiResourceRejection> Rejections { get; init; } = Array.Empty<AiResourceRejection>();
}
