using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime.Memory;

public enum AiMemoryKind { Decision, Rationale, Approval, Note, Change }

/// <summary>
/// Ein Eintrag im Projekt-Gedächtnis. KEIN Chat-Memory, sondern Projekt-Memory:
/// Warum wurde etwas geändert? Welche Designentscheidung? Wer hat zugestimmt? Welche KI?
/// Wann? Das wird über die Lebenszeit eines Projekts unschätzbar wertvoll.
/// </summary>
public sealed class AiMemoryEntry
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string Project { get; init; }
    public required string Topic { get; init; }
    public AiMemoryKind Kind { get; init; } = AiMemoryKind.Decision;
    public required string Content { get; init; }

    /// <summary>Welche KI/Session hat es festgehalten.</summary>
    public string? AuthorClient { get; init; }
    public string? AuthorSessionId { get; init; }

    /// <summary>Wer hat zugestimmt (z. B. der Nutzer André oder eine Reviewer-KI).</summary>
    public string? ApprovedBy { get; init; }

    public IReadOnlyList<string> RelatedFiles { get; init; } = Array.Empty<string>();
    public DateTime TimestampUtc { get; } = DateTime.UtcNow;
}

/// <summary>
/// Persistentes Projekt-Gedächtnis über alle KIs hinweg. Hält Designentscheidungen,
/// Begründungen und Zustimmungen fest, damit spätere KIs (und der Nutzer) nachvollziehen
/// können, warum etwas so ist, wie es ist.
/// </summary>
public sealed class AiProjectMemory
{
    private readonly ConcurrentBag<AiMemoryEntry> _entries = new();

    public event Action<AiMemoryEntry>? EntryRecorded;

    public AiMemoryEntry Record(AiSession session, string project, string topic,
                                string content, AiMemoryKind kind = AiMemoryKind.Decision,
                                string? approvedBy = null, IReadOnlyList<string>? relatedFiles = null)
    {
        var entry = new AiMemoryEntry
        {
            Project = project,
            Topic = topic,
            Kind = kind,
            Content = content,
            AuthorClient = session.ClientName,
            AuthorSessionId = session.SessionId,
            ApprovedBy = approvedBy,
            RelatedFiles = relatedFiles ?? Array.Empty<string>()
        };
        _entries.Add(entry);
        EntryRecorded?.Invoke(entry);
        return entry;
    }

    public IReadOnlyList<AiMemoryEntry> Query(string project, string? topic = null)
        => _entries
            .Where(e => string.Equals(e.Project, project, StringComparison.OrdinalIgnoreCase))
            .Where(e => topic is null || e.Topic.Contains(topic, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.TimestampUtc)
            .ToList();
}
