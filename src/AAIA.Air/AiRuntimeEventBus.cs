using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AAIA.Air.Contracts;

namespace AAIA.Air;

/// <summary>
/// Push statt Polling: Clients mit der Events-Capability abonnieren Ereignisse
/// (subscribe.pipeline). Mehrere gleichzeitig arbeitende KIs sehen, wer was auslöst.
/// </summary>
public sealed class AiRuntimeEventBus
{
    // subscriptionId → callback
    private readonly ConcurrentDictionary<string, Action<AiRuntimeEvent>> _subscribers = new(StringComparer.Ordinal);

    /// <summary>Globales Ereignis (z. B. für UI/Logging im Connector Tab).</summary>
    public event Action<AiRuntimeEvent>? EventPublished;

    public string Subscribe(Action<AiRuntimeEvent> handler)
    {
        var id = Guid.NewGuid().ToString("N");
        _subscribers[id] = handler;
        return id;
    }

    public bool Unsubscribe(string subscriptionId) => _subscribers.TryRemove(subscriptionId, out _);

    public void Publish(AiRuntimeEvent evt)
    {
        EventPublished?.Invoke(evt);
        foreach (var sub in _subscribers.Values)
        {
            try { sub(evt); } catch { /* ein Subscriber-Fehler stoppt den Bus nicht */ }
        }
    }

    public void Publish(AiRuntimeEventType type, AiSession? session, string? tool = null,
                        string? message = null, IReadOnlyDictionary<string, object?>? data = null)
        => Publish(new AiRuntimeEvent
        {
            Type = type,
            SessionId = session?.SessionId,
            ClientName = session?.ClientName,
            Project = session?.CurrentProject,
            Tool = tool,
            Message = message,
            Data = data
        });

    public int SubscriberCount => _subscribers.Count;
}
