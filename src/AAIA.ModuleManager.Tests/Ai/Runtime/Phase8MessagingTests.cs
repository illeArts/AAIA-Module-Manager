using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Messaging;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase8MessagingTests
{
    [Fact]
    public void DirectMessage_IsCreatedByBus_AndDelivered()
    {
        var (sessions, events, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");
        var published = new List<AiRuntimeEvent>();
        events.EventPublished += published.Add;

        var sent = bus.TrySend(sender.SessionId, receiver.SessionId, "Review", "Bitte prüfen",
            AiMessagePriority.High, "task-1", out var message, out var error);

        Assert.True(sent, error);
        Assert.NotNull(message);
        Assert.Equal(sender.SessionId, message!.Sender);
        Assert.Equal(receiver.SessionId, message.Receiver);
        Assert.True(bus.TryReadInbox(receiver.SessionId, false, out var inbox));
        var delivery = Assert.Single(inbox);
        Assert.Same(message, delivery.Message);
        Assert.Contains(published, e => e.Type == AiRuntimeEventType.MessageSent);
        Assert.Contains(published, e => e.Type == AiRuntimeEventType.MessageDelivered);
    }

    [Fact]
    public void Broadcast_DeliversToAllOtherActiveSessions()
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var reviewer = CreateSession(sessions, "reviewer");
        var tester = CreateSession(sessions, "tester");

        Assert.True(bus.TrySend(sender.SessionId, AiMessageBus.BroadcastReceiver, "Status", "Build grün",
            AiMessagePriority.Normal, null, out _, out var error), error);

        Assert.True(bus.TryReadInbox(reviewer.SessionId, false, out var reviewerInbox));
        Assert.True(bus.TryReadInbox(tester.SessionId, false, out var testerInbox));
        Assert.True(bus.TryReadInbox(sender.SessionId, false, out var senderInbox));
        Assert.Single(reviewerInbox);
        Assert.Single(testerInbox);
        Assert.Empty(senderInbox);
    }

    [Fact]
    public void UnknownSender_CannotSpoofMessage()
    {
        var (sessions, _, bus) = CreateBus();
        var receiver = CreateSession(sessions, "reviewer");

        var sent = bus.TrySend("spoofed-session", receiver.SessionId, "Fake", "Payload",
            AiMessagePriority.Urgent, null, out var message, out var error);

        Assert.False(sent);
        Assert.Null(message);
        Assert.Contains("Sender-Session", error);
    }

    [Fact]
    public void Acknowledge_UpdatesRecipientDelivery_AndUnreadFilter()
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");
        Assert.True(bus.TrySend(sender.SessionId, receiver.SessionId, "Review", "Payload",
            AiMessagePriority.Normal, null, out var message, out _));

        Assert.True(bus.TryAcknowledge(receiver.SessionId, message!.Id, out var error), error);
        Assert.True(bus.TryReadInbox(receiver.SessionId, true, out var unread));
        Assert.Empty(unread);
        Assert.True(bus.TryReadInbox(receiver.SessionId, false, out var all));
        Assert.True(Assert.Single(all).IsAcknowledged);
    }

    [Fact]
    public void InboxLimit_RemovesOldestAcknowledgedMessageFirst()
    {
        var (sessions, events, _) = CreateBus();
        var bus = new AiMessageBus(sessions, events, inboxLimit: 2);
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");

        Assert.True(bus.TrySend(sender.SessionId, receiver.SessionId, "One", "1",
            AiMessagePriority.Normal, null, out var first, out _));
        Assert.True(bus.TrySend(sender.SessionId, receiver.SessionId, "Two", "2",
            AiMessagePriority.Normal, null, out var second, out _));
        Assert.True(bus.TryAcknowledge(receiver.SessionId, first!.Id, out _));
        Assert.True(bus.TrySend(sender.SessionId, receiver.SessionId, "Three", "3",
            AiMessagePriority.Normal, null, out var third, out _));

        Assert.True(bus.TryReadInbox(receiver.SessionId, false, out var inbox));
        Assert.Equal(new[] { second!.Id, third!.Id }, inbox.Select(x => x.Message.Id));
    }

    [Fact]
    public void PurgeUnknownInboxes_RemovesDisconnectedRecipients()
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");
        Assert.True(bus.TrySend(sender.SessionId, receiver.SessionId, "Review", "Payload",
            AiMessagePriority.Normal, null, out _, out _));
        Assert.True(sessions.Remove(receiver.SessionId));

        Assert.Equal(1, bus.PurgeUnknownInboxes());
        Assert.Equal(0, bus.InboxCount);
    }

    [Fact]
    public void OversizedPayload_IsRejectedBeforeDelivery()
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");

        var sent = bus.TrySend(sender.SessionId, receiver.SessionId, "Payload",
            new string('x', AiMessageBus.MaxPayloadLength + 1), AiMessagePriority.Normal,
            null, out var message, out var error);

        Assert.False(sent);
        Assert.Null(message);
        Assert.Contains("Payload", error);
    }

    [Fact]
    public void ConcurrentSend_PreservesEveryDelivery()
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");
        var failures = 0;

        Parallel.For(0, 100, i =>
        {
            if (!bus.TrySend(sender.SessionId, receiver.SessionId, $"Message {i}", i.ToString(),
                    AiMessagePriority.Normal, null, out _, out _))
                Interlocked.Increment(ref failures);
        });

        Assert.Equal(0, failures);
        Assert.True(bus.TryReadInbox(receiver.SessionId, false, out var inbox));
        Assert.Equal(100, inbox.Count);
        Assert.Equal(100, inbox.Select(x => x.Message.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("api_key=abcdefgh12345678")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    public void SensitivePayload_IsRejected(string payload)
    {
        var (sessions, _, bus) = CreateBus();
        var sender = CreateSession(sessions, "architect");
        var receiver = CreateSession(sessions, "reviewer");

        var sent = bus.TrySend(sender.SessionId, receiver.SessionId, "Secret", payload,
            AiMessagePriority.Normal, null, out var message, out var error);

        Assert.False(sent);
        Assert.Null(message);
        Assert.Contains("sensible Zugangsdaten", error);
    }

    private static (AiSessionManager Sessions, AiRuntimeEventBus Events, AiMessageBus Bus) CreateBus()
    {
        var sessions = new AiSessionManager();
        var events = new AiRuntimeEventBus();
        return (sessions, events, new AiMessageBus(sessions, events));
    }

    private static AiSession CreateSession(AiSessionManager sessions, string name)
        => sessions.Create(new AiClientIdentity { Name = name, Fingerprint = name });
}
