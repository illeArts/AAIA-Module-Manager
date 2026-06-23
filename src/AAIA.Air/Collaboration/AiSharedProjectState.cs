using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.Air.Collaboration;

public enum AiWorkItemStatus { NotStarted, InProgress, Blocked, Done }

public enum AiPriority { Low, Normal, High, Critical }

/// <summary>
/// Ein Eintrag auf dem Blackboard — ein Arbeitsbereich/Thema eines Projekts.
/// </summary>
public sealed class AiBlackboardEntry
{
    public required string Project { get; init; }
    public required string Topic { get; init; }
    public AiWorkItemStatus Status { get; set; } = AiWorkItemStatus.NotStarted;
    public string? OwnerSessionId { get; set; }
    public string? OwnerClientName { get; set; }
    public string? Notes { get; set; }
    public AiPriority Priority { get; set; } = AiPriority.Normal;
    /// <summary>Fortschritt 0–100.</summary>
    public int Progress { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Das Blackboard — der klassische Multi-Agenten-Begriff: ein gemeinsamer Speicher, in
/// den alle KIs schreiben und aus dem alle lesen. Trägt Aufgabe, Status, Owner, Notes,
/// Priority und Progress. Beispiel: Claude setzt "Login = InProgress, Owner: Claude";
/// ChatGPT sieht sofort "nicht bearbeiten". Ergänzt Sessions/Locks/Audit/Events um
/// geteilte Aufgaben-Awareness.
///
/// Hinweis: Datei heißt aus Kompatibilitätsgründen noch AiSharedProjectState.cs; die
/// öffentliche Klasse ist <see cref="AiBlackboard"/>.
/// </summary>
public sealed class AiBlackboard
{
    // Schlüssel: project|topic (case-insensitive)
    private readonly ConcurrentDictionary<string, AiBlackboardEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public event Action<AiBlackboardEntry>? EntryChanged;

    private static string Key(string project, string topic) => $"{project}|{topic}".ToLowerInvariant();

    /// <summary>
    /// Schreibt/aktualisiert einen Eintrag. Gehört der Bereich bereits einer ANDEREN
    /// aktiven Session und ist InProgress, wird die Änderung als Konflikt abgelehnt.
    /// </summary>
    public bool Write(AiSession session, string project, string topic, AiWorkItemStatus status,
                      string? notes, out AiBlackboardEntry entry, out string? conflict,
                      AiPriority? priority = null, int? progress = null)
    {
        conflict = null;
        var key = Key(project, topic);
        var existing = _entries.TryGetValue(key, out var found) ? found : null;

        if (existing is { Status: AiWorkItemStatus.InProgress } &&
            existing.OwnerSessionId is not null &&
            existing.OwnerSessionId != session.SessionId)
        {
            conflict = $"'{topic}' wird bereits von {existing.OwnerClientName} bearbeitet (InProgress).";
            entry = existing;
            return false;
        }

        var updated = existing ?? new AiBlackboardEntry { Project = project, Topic = topic };
        updated.Status = status;
        if (notes is not null) updated.Notes = notes;
        if (priority.HasValue) updated.Priority = priority.Value;
        if (progress.HasValue) updated.Progress = Math.Clamp(progress.Value, 0, 100);
        updated.UpdatedAt = DateTime.UtcNow;

        if (status == AiWorkItemStatus.InProgress)
        {
            updated.OwnerSessionId = session.SessionId;
            updated.OwnerClientName = session.ClientName;
        }
        else if (status is AiWorkItemStatus.Done or AiWorkItemStatus.NotStarted)
        {
            updated.OwnerSessionId = null;
            updated.OwnerClientName = null;
            if (status == AiWorkItemStatus.Done) updated.Progress = 100;
        }

        _entries[key] = updated;
        entry = updated;
        EntryChanged?.Invoke(updated);
        return true;
    }

    public AiBlackboardEntry? Get(string project, string topic)
        => _entries.TryGetValue(Key(project, topic), out var i) ? i : null;

    public IReadOnlyList<AiBlackboardEntry> List(string project)
        => _entries.Values
            .Where(i => string.Equals(i.Project, project, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Topic)
            .ToList();

    /// <summary>True, wenn der Bereich von einer anderen Session aktiv bearbeitet wird.</summary>
    public bool IsOwnedByOther(AiSession session, string project, string topic)
    {
        var i = Get(project, topic);
        return i is { Status: AiWorkItemStatus.InProgress }
               && i.OwnerSessionId is not null
               && i.OwnerSessionId != session.SessionId;
    }
}
