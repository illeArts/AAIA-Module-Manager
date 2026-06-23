namespace AAIA.Air.Contracts;

/// <summary>Ein Runtime-Ereignis mit der auslösenden Session/Identität.</summary>
public sealed class AiRuntimeEvent
{
    public required AiRuntimeEventType Type { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string? SessionId { get; init; }
    public string? ClientName { get; init; }
    public string? Project { get; init; }
    public string? Tool { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, object?>? Data { get; init; }
}
