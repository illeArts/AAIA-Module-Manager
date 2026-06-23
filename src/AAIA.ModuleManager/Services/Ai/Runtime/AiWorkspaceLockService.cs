using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>Ein gehaltener Workspace-Lock.</summary>
public sealed class AiWorkspaceLock
{
    public required string LockId { get; init; }
    public required AiLockScope Scope { get; init; }
    public required string NormalizedPath { get; init; }
    public required string OwnerSessionId { get; init; }
    public DateTime AcquiredAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Timeout { get; init; }
    public bool IsExpired => DateTime.UtcNow - AcquiredAt > Timeout;
}

/// <summary>
/// Verhindert Kollisionen, wenn mehrere KIs gleichzeitig dieselbe Datei/Projekt ändern.
/// Schreibende Tools erwerben vor Ausführung den passenden Lock; ist er von einer
/// anderen Session gehalten, wird der Aufruf abgelehnt. Locks laufen per Timeout ab.
/// </summary>
public sealed class AiWorkspaceLockService
{
    private readonly ConcurrentDictionary<string, AiWorkspaceLock> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .ToLowerInvariant();

    /// <summary>
    /// Versucht, einen Lock zu erwerben. Kollidiert der angeforderte Scope mit einem
    /// bestehenden Lock einer ANDEREN Session, schlägt es fehl.
    /// </summary>
    public bool TryAcquire(AiSession session, AiLockScope scope, string path,
                           out AiWorkspaceLock? acquired, out string? conflict)
    {
        acquired = null;
        conflict = null;
        var norm = Normalize(path);

        lock (_gate)
        {
            PurgeExpiredNoLock();

            foreach (var existing in _locks.Values)
            {
                if (existing.OwnerSessionId == session.SessionId) continue;
                if (PathsConflict(norm, scope, existing.NormalizedPath, existing.Scope))
                {
                    conflict = $"Gesperrt durch Session {existing.OwnerSessionId} " +
                               $"({existing.Scope} {existing.NormalizedPath}).";
                    return false;
                }
            }

            var lk = new AiWorkspaceLock
            {
                LockId = Guid.NewGuid().ToString("N")[..12],
                Scope = scope,
                NormalizedPath = norm,
                OwnerSessionId = session.SessionId,
                Timeout = DefaultTimeout
            };
            _locks[lk.LockId] = lk;
            session.ActiveLocks.Add(lk.LockId);
            acquired = lk;
            return true;
        }
    }

    public bool Release(AiSession session, string lockId)
    {
        lock (_gate)
        {
            if (_locks.TryGetValue(lockId, out var lk) && lk.OwnerSessionId == session.SessionId)
            {
                _locks.TryRemove(lockId, out _);
                session.ActiveLocks.Remove(lockId);
                return true;
            }
            return false;
        }
    }

    public void ReleaseAll(AiSession session)
    {
        lock (_gate)
        {
            foreach (var id in session.ActiveLocks.ToList())
                if (_locks.TryGetValue(id, out var lk) && lk.OwnerSessionId == session.SessionId)
                    _locks.TryRemove(id, out _);
            session.ActiveLocks.Clear();
        }
    }

    public IReadOnlyList<AiWorkspaceLock> Active
    {
        get { lock (_gate) { PurgeExpiredNoLock(); return _locks.Values.ToList(); } }
    }

    private void PurgeExpiredNoLock()
    {
        foreach (var kv in _locks.Where(kv => kv.Value.IsExpired).ToList())
            _locks.TryRemove(kv.Key, out _);
    }

    /// <summary>
    /// Zwei Locks kollidieren, wenn sich ihre Pfade überschneiden (Projekt umfasst Datei,
    /// gleiche Datei, Ordner umfasst Datei …).
    /// </summary>
    private static bool PathsConflict(string pathA, AiLockScope scopeA, string pathB, AiLockScope scopeB)
    {
        if (pathA == pathB) return true;
        // Ein breiterer Lock (Project/Folder) umschließt einen engeren Pfad.
        bool aContainsB = pathB.StartsWith(pathA + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        bool bContainsA = pathA.StartsWith(pathB + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (scopeA is AiLockScope.Project or AiLockScope.Folder && aContainsB) return true;
        if (scopeB is AiLockScope.Project or AiLockScope.Folder && bContainsA) return true;
        return false;
    }
}
