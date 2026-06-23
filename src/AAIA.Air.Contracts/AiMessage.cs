namespace AAIA.Air.Contracts;

public enum AiMessagePriority { Low, Normal, High, Urgent }

/// <summary>
/// Nachricht zwischen Teilnehmern der AIR (KI ↔ KI ↔ Mensch). Der Messaging-Bus
/// selbst ist Bestandteil einer späteren Runtime-Phase; dies ist nur der Contract.
/// </summary>
public sealed class AiMessage
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string Sender { get; init; }
    public required string Receiver { get; init; }
    public string Subject { get; init; } = "";
    public string Payload { get; init; } = "";
    public AiMessagePriority Priority { get; init; } = AiMessagePriority.Normal;
    public string? CorrelationId { get; init; }
    public DateTime TimestampUtc { get; } = DateTime.UtcNow;
}
