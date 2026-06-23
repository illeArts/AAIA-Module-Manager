using System;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services.Help;

/// <summary>
/// Lokale Volltext-Suche über den Hilfe-Index.
///
/// Ranking (absteigend):
///   10 — exakter Titel-Match
///    8 — Titel enthält Suchbegriff
///    6 — Tag-Match (exakt)
///    5 — Fehlercode-Match
///    4 — Summary enthält Suchbegriff
///    2 — Markdown-Inhalt enthält Suchbegriff
///    1 — Pipeline-Step-Match
/// </summary>
public sealed class HelpSearchService
{
    private readonly HelpCenterService _center;

    public HelpSearchService(HelpCenterService center)
    {
        _center = center;
    }

    /// <summary>
    /// Sucht in allen geladenen Artikeln.
    /// Gibt Treffer sortiert nach Relevanz zurück.
    /// </summary>
    public IReadOnlyList<HelpSearchResult> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var terms   = query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<HelpSearchResult>();

        foreach (var article in _center.AllArticles)
        {
            var score      = Score(article, terms);
            if (score <= 0) continue;

            var titleMatch = MatchesAny(article.Title.ToLowerInvariant(), terms);
            var tagMatch   = terms.Any(t => article.Tags.Any(tag =>
                tag.Contains(t, StringComparison.OrdinalIgnoreCase)));
            var errorMatch = terms.Any(t => article.ErrorCodes.Any(e =>
                e.Contains(t, StringComparison.OrdinalIgnoreCase)));

            results.Add(new HelpSearchResult
            {
                Article    = article,
                Score      = score,
                Excerpt    = ExtractExcerpt(article, terms),
                TitleMatch = titleMatch,
                TagMatch   = tagMatch,
                ErrorMatch = errorMatch,
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Findet Artikel die zu einem bestimmten Pipeline-Schritt passen.
    /// </summary>
    public IReadOnlyList<HelpArticle> GetForPipelineStep(string step)
        => _center.AllArticles
            .Where(a => a.PipelineSteps.Any(s =>
                s.Equals(step, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    // ── Privat ────────────────────────────────────────────────────────────────

    private static double Score(HelpArticle article, string[] terms)
    {
        double total = 0;
        var   title  = article.Title.ToLowerInvariant();
        var   summary = article.Summary.ToLowerInvariant();
        var   content = (article.MarkdownContent ?? "").ToLowerInvariant();

        foreach (var term in terms)
        {
            if (title.Equals(term))               total += 10;
            else if (title.StartsWith(term))       total += 9;
            else if (title.Contains(term))         total += 8;

            if (article.Tags.Any(t => t.Equals(term, StringComparison.OrdinalIgnoreCase)))
                total += 6;

            if (article.ErrorCodes.Any(e => e.Contains(term, StringComparison.OrdinalIgnoreCase)))
                total += 5;

            if (summary.Contains(term))            total += 4;

            if (content.Contains(term))            total += 2;

            if (article.PipelineSteps.Any(s => s.Contains(term, StringComparison.OrdinalIgnoreCase)))
                total += 1;
        }

        return total;
    }

    private static bool MatchesAny(string text, string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string ExtractExcerpt(HelpArticle article, string[] terms)
    {
        // Kurzen Ausschnitt aus Markdown-Inhalt extrahieren
        var content = article.MarkdownContent ?? article.Summary;
        if (string.IsNullOrEmpty(content)) return article.Summary;

        var lower = content.ToLowerInvariant();
        foreach (var term in terms)
        {
            var idx = lower.IndexOf(term, StringComparison.Ordinal);
            if (idx < 0) continue;

            var start   = Math.Max(0, idx - 60);
            var length  = Math.Min(160, content.Length - start);
            var excerpt = content.Substring(start, length).Replace('\n', ' ').Trim();
            return (start > 0 ? "…" : "") + excerpt + (start + length < content.Length ? "…" : "");
        }

        // Kein Treffer im Inhalt — Summary zurückgeben
        return article.Summary.Length > 160
            ? article.Summary[..160] + "…"
            : article.Summary;
    }
}
