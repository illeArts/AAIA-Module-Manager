using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Verwaltet gleichzeitig aktive Sessions. Weiß jederzeit "wer arbeitet woran".
/// Mehrere KIs parallel sind Designziel ab Phase 7.0.
/// </summary>
public sealed class AiSessionManager
{
    private readonly ConcurrentDictionary<string, AiSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Erzeugt eine neue Session mit ausgehandelten Capabilities und Default-Permissions.</summary>
    public AiSession Create(AiClientIdentity identity,
                            IEnumerable<string>? capabilities = null,
                            AiPermission grantedPermissions = AiPermission.Read)
    {
        var session = new AiSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ClientId  = string.IsNullOrEmpty(identity.Fingerprint) ? identity.Name : identity.Fingerprint,
            Identity  = identity,
            GrantedPermissions = grantedPermissions
        };

        if (capabilities is not null)
            foreach (var c in capabilities) session.Capabilities.Add(c);

        _sessions[session.SessionId] = session;
        return session;
    }

    public AiSession? Get(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s : null;

    public bool TryGet(string sessionId, out AiSession session)
    {
        var ok = _sessions.TryGetValue(sessionId, out var s);
        session = s!;
        return ok;
    }

    public void Touch(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
            s.LastActivity = DateTime.UtcNow;
    }

    public bool Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public IReadOnlyList<AiSession> Active => _sessions.Values.ToList();

    public int Count => _sessions.Count;

    /// <summary>Entfernt Sessions, die länger als <paramref name="idle"/> inaktiv sind.</summary>
    public void PurgeIdle(TimeSpan idle)
    {
        var cutoff = DateTime.UtcNow - idle;
        foreach (var kv in _sessions)
            if (kv.Value.LastActivity < cutoff)
                _sessions.TryRemove(kv.Key, out _);
    }
}
