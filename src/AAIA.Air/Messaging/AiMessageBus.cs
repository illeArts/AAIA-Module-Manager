using System.Collections.Concurrent;

namespace AAIA.Air.Messaging;

/// <summary>
/// Sessiongebundener In-Memory-Bus. Der Bus konstruiert Nachrichten selbst, damit
/// Sender-ID, Message-ID und Zeitstempel nicht von einem Client gefälscht werden können.
/// </summary>
public sealed class AiMessageBus
{
    public const string BroadcastReceiver = "broadcast";
    public const int DefaultInboxLimit = 500;
    public const int MaxSubjectLength = 200;
    public const int MaxPayloadLength = 64 * 1024;

    private sealed class DeliveryState
    {
        public required AiMessage Message { get; init; }
        public required string RecipientSessionId { get; init; }
        public required DateTime DeliveredAtUtc { get; init; }
        public DateTime? AcknowledgedAtUtc { get; set; }
    }

    private sealed class Inbox
    {
        public object Gate { get; } = new();
        public List<DeliveryState> Items { get; } = new();
    }

    private readonly AiSessionManager _sessions;
    private readonly AiRuntimeEventBus _events;
    private readonly ConcurrentDictionary<string, Inbox> _inboxes = new(StringComparer.Ordinal);

    public int InboxLimit { get; }
    public int InboxCount => _inboxes.Count;

    public AiMessageBus(
        AiSessionManager sessions,
        AiRuntimeEventBus events,
        int inboxLimit = DefaultInboxLimit)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(events);
        if (inboxLimit <= 0) throw new ArgumentOutOfRangeException(nameof(inboxLimit));

        _sessions = sessions;
        _events = events;
        InboxLimit = inboxLimit;
    }

    public bool TrySend(
        string senderSessionId,
        string receiver,
        string subject,
        string payload,
        AiMessagePriority priority,
        string? correlationId,
        out AiMessage? message,
        out string? error)
    {
        message = null;
        error = null;

        if (!_sessions.TryGet(senderSessionId, out var sender))
            return Fail("Ungültige oder abgelaufene Sender-Session.", out error);
        if (string.IsNullOrWhiteSpace(receiver))
            return Fail("Empfänger fehlt.", out error);
        subject ??= "";
        payload ??= "";
        if (AiMessageSafetyPolicy.ContainsSensitiveContent(subject) ||
            AiMessageSafetyPolicy.ContainsSensitiveContent(payload))
            return Fail("Nachricht enthält potenziell sensible Zugangsdaten.", out error);
        if (subject.Length > MaxSubjectLength)
            return Fail($"Betreff überschreitet {MaxSubjectLength} Zeichen.", out error);
        if (payload.Length > MaxPayloadLength)
            return Fail($"Payload überschreitet {MaxPayloadLength} Zeichen.", out error);

        string[] recipients;
        if (string.Equals(receiver, BroadcastReceiver, StringComparison.OrdinalIgnoreCase))
        {
            recipients = _sessions.Active
                .Where(s => !string.Equals(s.SessionId, senderSessionId, StringComparison.Ordinal))
                .Select(s => s.SessionId)
                .ToArray();

            if (recipients.Length == 0)
                return Fail("Keine aktiven Broadcast-Empfänger vorhanden.", out error);
            receiver = BroadcastReceiver;
        }
        else
        {
            if (!_sessions.TryGet(receiver, out _))
                return Fail("Empfänger-Session ist nicht aktiv.", out error);
            recipients = new[] { receiver };
        }

        message = new AiMessage
        {
            Sender = senderSessionId,
            Receiver = receiver,
            Subject = subject,
            Payload = payload,
            Priority = priority,
            CorrelationId = correlationId
        };

        _sessions.Touch(senderSessionId);
        _events.Publish(AiRuntimeEventType.MessageSent, sender, message: message.Id,
            data: new Dictionary<string, object?> { ["receiver"] = receiver });

        foreach (var recipient in recipients)
        {
            Enqueue(recipient, message);
            _events.Publish(new AiRuntimeEvent
            {
                Type = AiRuntimeEventType.MessageDelivered,
                SessionId = recipient,
                Project = sender.CurrentProject,
                Message = message.Id,
                Data = new Dictionary<string, object?> { ["sender"] = senderSessionId }
            });
        }

        return true;
    }

    public bool TryReadInbox(
        string sessionId,
        bool unacknowledgedOnly,
        out IReadOnlyList<AiMessageDelivery> messages)
    {
        messages = Array.Empty<AiMessageDelivery>();
        if (!_sessions.TryGet(sessionId, out _)) return false;
        if (!_inboxes.TryGetValue(sessionId, out var inbox)) return true;

        lock (inbox.Gate)
        {
            messages = inbox.Items
                .Where(item => !unacknowledgedOnly || !item.AcknowledgedAtUtc.HasValue)
                .Select(ToDelivery)
                .ToArray();
        }

        _sessions.Touch(sessionId);
        return true;
    }

    public bool TryAcknowledge(string sessionId, string messageId, out string? error)
    {
        error = null;
        if (!_sessions.TryGet(sessionId, out var session))
            return Fail("Ungültige oder abgelaufene Empfänger-Session.", out error);
        if (!_inboxes.TryGetValue(sessionId, out var inbox))
            return Fail("Nachricht wurde in dieser Inbox nicht gefunden.", out error);

        lock (inbox.Gate)
        {
            var item = inbox.Items.FirstOrDefault(x =>
                string.Equals(x.Message.Id, messageId, StringComparison.Ordinal));
            if (item is null)
                return Fail("Nachricht wurde in dieser Inbox nicht gefunden.", out error);

            item.AcknowledgedAtUtc ??= DateTime.UtcNow;
        }

        _sessions.Touch(sessionId);
        _events.Publish(AiRuntimeEventType.MessageAcknowledged, session, message: messageId);
        return true;
    }

    public int PurgeUnknownInboxes()
    {
        var active = _sessions.Active.Select(s => s.SessionId).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var sessionId in _inboxes.Keys)
            if (!active.Contains(sessionId) && _inboxes.TryRemove(sessionId, out _)) removed++;
        return removed;
    }

    private void Enqueue(string recipientSessionId, AiMessage message)
    {
        var inbox = _inboxes.GetOrAdd(recipientSessionId, _ => new Inbox());
        lock (inbox.Gate)
        {
            inbox.Items.Add(new DeliveryState
            {
                Message = message,
                RecipientSessionId = recipientSessionId,
                DeliveredAtUtc = DateTime.UtcNow
            });

            while (inbox.Items.Count > InboxLimit)
            {
                var acknowledgedIndex = inbox.Items.FindIndex(x => x.AcknowledgedAtUtc.HasValue);
                inbox.Items.RemoveAt(acknowledgedIndex >= 0 ? acknowledgedIndex : 0);
            }
        }
    }

    private static AiMessageDelivery ToDelivery(DeliveryState item) => new()
    {
        Message = item.Message,
        RecipientSessionId = item.RecipientSessionId,
        DeliveredAtUtc = item.DeliveredAtUtc,
        AcknowledgedAtUtc = item.AcknowledgedAtUtc
    };

    private static bool Fail(string reason, out string? error)
    {
        error = reason;
        return false;
    }
}
