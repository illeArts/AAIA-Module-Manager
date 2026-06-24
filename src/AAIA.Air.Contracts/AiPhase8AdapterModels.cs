namespace AAIA.Air.Contracts;

/// <summary>Stabile Fehlercodes der Phase-8-Adapteroberfläche.</summary>
public static class AiPhase8ErrorCodes
{
    public const string BadInput = "bad_input";
    public const string NotFound = "not_found";
    public const string NotOwner = "not_owner";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string MessageRejected = "message_rejected";
    public const string ExecutionRejected = "execution_rejected";
}

/// <summary>Für externe Leser redigierte Sicht auf ein Ressourcenprofil.</summary>
public sealed class AiResourcePublicSnapshot
{
    public required string ResourceId { get; init; }
    public AiResourceKind Kind { get; init; }
    public bool Enabled { get; init; }
    public AiResourceLocality Locality { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public required AiResourceCapacity Capacity { get; init; }
    public bool TelemetryAvailable { get; init; }
    public bool Healthy { get; init; }
    public bool Throttled { get; init; }
    public int ActiveReservations { get; init; }
}

/// <summary>Aggregierter Ressourcenstatus ohne Provider- oder Kostendetails.</summary>
public sealed class AiResourcePublicStatus
{
    public int ResourceCount { get; init; }
    public int EnabledCount { get; init; }
    public int HealthyCount { get; init; }
    public int ActiveReservations { get; init; }
    public int BudgetCount { get; init; }
    public int ExhaustedBudgetCount { get; init; }
}
