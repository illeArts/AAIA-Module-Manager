namespace AAIA.Air.Contracts;

/// <summary>Zustellungszustand einer Nachricht in einer konkreten Session-Inbox.</summary>
public sealed class AiMessageDelivery
{
    public required AiMessage Message { get; init; }
    public required string RecipientSessionId { get; init; }
    public DateTime DeliveredAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }
    public bool IsAcknowledged => AcknowledgedAtUtc.HasValue;
}
