using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.Air;

/// <summary>Ein Audit-Eintrag: wer hat was wann getan, mit welchem Ergebnis.</summary>
public sealed class AiAuditEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public required string ClientIdentity { get; init; }
    public required string SessionId { get; init; }
    public required string Tool { get; init; }
    public string ToolVersion { get; init; } = "";
    public string? Project { get; init; }
    public required bool Success { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Auditiert jede Aktion mit AiClientIdentity + SessionId. Grundlage dafür, dass
/// mehrere KIs gleichzeitig nachvollziehbar arbeiten. Fehlertexte werden maskiert.
/// </summary>
public sealed class AiAuditService
{
    private readonly ConcurrentQueue<AiAuditEntry> _entries = new();
    private const int MaxEntries = 5000;

    public event Action<AiAuditEntry>? EntryRecorded;

    public void Record(AiSession session, AiToolDefinition tool, bool success, string? detail = null)
    {
        var entry = new AiAuditEntry
        {
            ClientIdentity = session.Identity.ToString(),
            SessionId = session.SessionId,
            Tool = tool.Name,
            ToolVersion = tool.Version,
            Project = session.CurrentProject,
            Success = success,
            Detail = Mask(detail)
        };
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
        EntryRecorded?.Invoke(entry);
    }

    /// <summary>Auditiert eine ausdrücklich bestätigte lokale Verwaltungsaktion.</summary>
    public void RecordAdministrative(string actor, string action, bool success, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var entry = new AiAuditEntry
        {
            ClientIdentity = actor,
            SessionId = "local-admin",
            Tool = action,
            ToolVersion = "8.4.0",
            Success = success,
            Detail = Mask(detail)
        };
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
        EntryRecorded?.Invoke(entry);
    }

    public IReadOnlyList<AiAuditEntry> Recent(int count = 100)
        => _entries.Reverse().Take(count).ToList();

    /// <summary>Maskiert offensichtliche Secrets in Detailtexten (defensiv).</summary>
    private static string? Mask(string? detail)
    {
        if (string.IsNullOrEmpty(detail)) return detail;
        var masked = System.Text.RegularExpressions.Regex.Replace(
            detail,
            @"(?i)(token|secret|key|password|bearer)\s*[:=]\s*\S+",
            "$1=***");
        return masked;
    }
}
