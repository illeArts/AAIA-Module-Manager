namespace AAIA.Air.Contracts;

public enum AiExecutionPriority { Low, Normal, High, Critical }

public enum AiExecutionState
{
    Queued,
    Leased,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Unveränderliche Anforderung an die AIR Execution Queue.</summary>
public sealed class AiExecutionRequest
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string TaskId { get; init; }
    public AiExecutionPriority Priority { get; init; } = AiExecutionPriority.Normal;
    public AiRole? RequiredRole { get; init; }
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public DateTime EnqueuedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? NotBeforeUtc { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public AiResourceRequirements? ResourceRequirements { get; init; }
}

/// <summary>Zeitlich begrenzte Reservierung einer Execution für eine Session.</summary>
public sealed class AiExecutionLease
{
    public required string RequestId { get; init; }
    public required string TaskId { get; init; }
    public required string SessionId { get; init; }
    public int Attempt { get; init; }
    public DateTime LeasedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
}

/// <summary>Read-only-Sicht auf den aktuellen Scheduler-Zustand.</summary>
public sealed class AiExecutionSnapshot
{
    public required AiExecutionRequest Request { get; init; }
    public AiExecutionState State { get; init; }
    public int AttemptCount { get; init; }
    public AiExecutionLease? Lease { get; init; }
    public string? LastError { get; init; }
    public string? ResourceId { get; init; }
    public string? ResourceReservationId { get; init; }
    public int ResourceDeferralCount { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
