using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AAIA.Air;

/// <summary>Ein Audit-Eintrag: wer hat was wann getan, mit welchem Ergebnis.</summary>
public sealed class AiAuditEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public required string ClientIdentity { get; init; }
    public string? SessionId { get; init; }
    public required string Tool { get; init; }
    public string ToolVersion { get; init; } = "";
    public string? Project { get; init; }
    public required bool Success { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Begrenztes Audit mit defensiver Redaction. Dauerhafte Exporte enthalten keine
/// Session-ID und höchstens 30 Tage beziehungsweise 50.000 Einträge.
/// </summary>
public sealed partial class AiAuditService
{
    private const int MaxEntries = 50_000;
    private const int MaxDetailLength = 2_000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly ConcurrentQueue<AiAuditEntry> _entries = new();

    public event Action<AiAuditEntry>? EntryRecorded;

    public int Count => _entries.Count;

    public void Record(AiSession session, AiToolDefinition tool, bool success, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tool);
        Add(new AiAuditEntry
        {
            ClientIdentity = Redact(session.Identity.ToString()) ?? "redacted",
            SessionId = session.SessionId,
            Tool = Redact(tool.Name) ?? "redacted",
            ToolVersion = Redact(tool.Version) ?? "",
            Project = Redact(session.CurrentProject),
            Success = success,
            Detail = Redact(detail)
        });
    }

    /// <summary>Auditiert eine ausdrücklich bestätigte lokale Verwaltungsaktion.</summary>
    public void RecordAdministrative(string actor, string action, bool success, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        Add(new AiAuditEntry
        {
            ClientIdentity = Redact(actor) ?? "redacted",
            SessionId = "local-admin",
            Tool = Redact(action) ?? "redacted",
            ToolVersion = "9.4.0",
            Success = success,
            Detail = Redact(detail)
        });
    }

    public IReadOnlyList<AiAuditEntry> Recent(int count = 100)
        => _entries.Reverse().Take(Math.Clamp(count, 0, MaxEntries)).ToArray();

    internal IReadOnlyList<AiDurableAuditEntry> CaptureDurableEntries(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        var cutoff = nowUtc - Retention;
        return _entries
            .Where(entry => entry.TimestampUtc >= cutoff && entry.TimestampUtc <= nowUtc)
            .OrderBy(entry => entry.TimestampUtc)
            .TakeLast(MaxEntries)
            .Select(entry => new AiDurableAuditEntry
            {
                TimestampUtc = entry.TimestampUtc,
                Actor = Redact(entry.ClientIdentity) ?? "redacted",
                Action = Redact(entry.Tool) ?? "redacted",
                Version = Redact(entry.ToolVersion) ?? "",
                Project = Redact(entry.Project),
                Success = entry.Success,
                Detail = Redact(entry.Detail)
            }).ToArray();
    }

    internal int RestoreDurableEntries(IEnumerable<AiDurableAuditEntry> entries, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        EnsureUtc(nowUtc);
        var cutoff = nowUtc - Retention;
        var restored = entries.Select(entry =>
        {
            ArgumentNullException.ThrowIfNull(entry);
            EnsureUtc(entry.TimestampUtc);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Actor);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);
            return new AiAuditEntry
            {
                TimestampUtc = entry.TimestampUtc,
                ClientIdentity = Redact(entry.Actor) ?? "redacted",
                SessionId = null,
                Tool = Redact(entry.Action) ?? "redacted",
                ToolVersion = Redact(entry.Version) ?? "",
                Project = Redact(entry.Project),
                Success = entry.Success,
                Detail = Redact(entry.Detail)
            };
        }).Where(entry => entry.TimestampUtc >= cutoff && entry.TimestampUtc <= nowUtc)
          .OrderBy(entry => entry.TimestampUtc)
          .TakeLast(MaxEntries)
          .ToArray();

        if (!_entries.IsEmpty)
            throw new InvalidOperationException("Audit kann nur in einen leeren Dienst wiederhergestellt werden.");
        foreach (var entry in restored) _entries.Enqueue(entry);
        return restored.Length;
    }

    internal void ClearDurableRestore()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    private void Add(AiAuditEntry entry)
    {
        _entries.Enqueue(entry);
        var cutoff = DateTime.UtcNow - Retention;
        while (_entries.TryPeek(out var oldest) &&
               (_entries.Count > MaxEntries || oldest.TimestampUtc < cutoff))
            _entries.TryDequeue(out _);
        if (EntryRecorded is not { } handlers) return;
        foreach (Action<AiAuditEntry> handler in handlers.GetInvocationList())
        {
            try { handler(entry); }
            catch { /* Beobachter dürfen Audit und Laufzeitaktion nicht brechen. */ }
        }
    }

    public static string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = SecretAssignmentRegex().Replace(value, "$1=***");
        redacted = BearerRegex().Replace(redacted, "Bearer ***");
        return redacted.Length <= MaxDetailLength ? redacted : redacted[..MaxDetailLength];
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Audit-Zeitpunkt muss UTC sein.");
    }

    [GeneratedRegex(@"(?i)\b(token|secret|api[-_]?key|password|private[-_]?key)\s*[:=]\s*[^\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();
}
