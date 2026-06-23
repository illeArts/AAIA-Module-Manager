using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.Help;

/// <summary>
/// Erzeugt einen sicheren, maschinenlesbaren KI-Kontext aus Hilfeartikeln.
///
/// SICHERHEITSREGELN (unveränderlich):
///   - Keine API-Keys, Tokens, Passwörter, Private Keys
///   - Keine lokalen vollständigen Dateipfade
///   - Keine Quellcode-Dateien ohne explizite Auswahl
///   - Keine personenbezogenen Käufer- oder Zahlungsdaten
///   - Nur isAiReadable=true Artikel werden einbezogen
/// </summary>
public sealed class AiHelpContextService
{
    private readonly HelpCenterService _center;
    private readonly HelpSearchService _search;

    public AiHelpContextService(HelpCenterService center, HelpSearchService search)
    {
        _center = center;
        _search = search;
    }

    // ── Öffentliche API ───────────────────────────────────────────────────────

    /// <summary>
    /// Erzeugt KI-Kontext für einen spezifischen Artikel.
    /// </summary>
    public async Task<string> BuildForArticleAsync(string articleId)
    {
        var article = _center.GetById(articleId);
        if (article is null || !article.IsAiReadable)
            return BuildHeader() + "\n\n*(Kein Artikel gefunden.)*";

        await _center.LoadMarkdownAsync(article);

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        sb.AppendLine();
        sb.AppendLine("## Kontext-Typ: Hilfe-Artikel");
        sb.AppendLine();
        AppendArticle(sb, article);
        AppendRelated(sb, article);
        sb.AppendLine(BuildFooter());
        return sb.ToString();
    }

    /// <summary>
    /// Erzeugt KI-Kontext für einen Fehler (per Fehlercode oder HelpArticleId).
    /// Enthält den Fehlerartikel + verwandte Artikel.
    /// </summary>
    public async Task<string> BuildForErrorAsync(string errorCodeOrArticleId, string? currentErrorMessage = null)
    {
        var article = _center.GetByErrorCode(errorCodeOrArticleId);

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        sb.AppendLine();
        sb.AppendLine("## Kontext-Typ: Fehlerbehebung");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(currentErrorMessage))
        {
            sb.AppendLine("## Aktueller Fehler");
            sb.AppendLine();
            // Fehlertext sanitieren — keine Pfade, keine Keys
            var safeMessage = SanitizeErrorMessage(currentErrorMessage);
            sb.AppendLine($"> {safeMessage}");
            sb.AppendLine();
        }

        if (article is not null && article.IsAiReadable)
        {
            await _center.LoadMarkdownAsync(article);
            sb.AppendLine("## Relevanter Hilfe-Artikel");
            sb.AppendLine();
            AppendArticle(sb, article);
            AppendRelated(sb, article);
        }
        else
        {
            sb.AppendLine($"*(Kein spezifischer Hilfe-Artikel für `{errorCodeOrArticleId}` vorhanden.)*");
            sb.AppendLine();
        }

        sb.AppendLine(BuildFooter());
        return sb.ToString();
    }

    /// <summary>
    /// Erzeugt KI-Kontext für eine Suche (bis zu maxArticles Artikel).
    /// </summary>
    public async Task<string> BuildForSearchAsync(string query, int maxArticles = 5)
    {
        var results = _search.Search(query, maxArticles);

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        sb.AppendLine();
        sb.AppendLine($"## Kontext-Typ: Suchergebnisse für „{SanitizeQuery(query)}“");
        sb.AppendLine();

        if (results.Count == 0)
        {
            sb.AppendLine("*(Keine Treffer gefunden.)*");
        }
        else
        {
            foreach (var result in results.Where(r => r.Article.IsAiReadable))
            {
                await _center.LoadMarkdownAsync(result.Article);
                AppendArticle(sb, result.Article, compact: false);
            }
        }

        sb.AppendLine(BuildFooter());
        return sb.ToString();
    }

    /// <summary>
    /// Erzeugt KI-Kontext für einen Pipeline-Schritt.
    /// </summary>
    public async Task<string> BuildForPipelineStepAsync(string stepName)
    {
        var articles = _search.GetForPipelineStep(stepName)
            .Where(a => a.IsAiReadable)
            .Take(4)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        sb.AppendLine();
        sb.AppendLine($"## Kontext-Typ: Pipeline-Schritt „{stepName}“");
        sb.AppendLine();

        foreach (var article in articles)
        {
            await _center.LoadMarkdownAsync(article);
            AppendArticle(sb, article, compact: true);
        }

        if (articles.Count == 0)
            sb.AppendLine("*(Keine spezifischen Artikel für diesen Schritt verfügbar.)*");

        sb.AppendLine(BuildFooter());
        return sb.ToString();
    }

    // ── Privat ────────────────────────────────────────────────────────────────

    private static string BuildHeader() =>
        """
        # AAIA Hilfe-Kontext

        Dieses Dokument wurde vom AAIA Module Manager erzeugt.
        Es enthält Hilfeinformationen aus der lokalen AAIA-Wissensbasis.

        ## Erlaubte Unterstützung

        Du darfst:
        - Das Problem erklären und einordnen.
        - Sichere Lösungsschritte vorschlagen.
        - Konfigurationsfehler im Projekt identifizieren.
        - Auf die enthaltenen Hilfeartikel verweisen.

        ## Einschränkungen

        Du darfst NICHT:
        - Nach Private Keys, API-Keys, Tokens oder Passwörtern fragen.
        - Vorschlagen, Signatur-, Trust-, Lizenz- oder Marketplace-Gates zu umgehen.
        - Dateisystem-Pfade oder Systeminformationen anfordern.
        - Automatisch Code oder Konfigurationsdateien verändern.

        ---
        """;

    private static string BuildFooter() =>
        """
        ---

        *Dieser Kontext wurde automatisch aus der AAIA-Wissensbasis zusammengestellt.*
        *Keine sensiblen Daten (Keys, Tokens, Pfade) sind enthalten.*
        """;

    private static void AppendArticle(StringBuilder sb, HelpArticle article, bool compact = false)
    {
        sb.AppendLine($"### {article.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Kategorie:** {article.Category}");
        if (!string.IsNullOrEmpty(article.Subcategory))
            sb.AppendLine($"**Unterrubrik:** {article.Subcategory}");
        sb.AppendLine();
        sb.AppendLine($"**Kurz erklärt:** {article.Summary}");
        sb.AppendLine();

        if (article.Tags.Count > 0)
            sb.AppendLine($"**Tags:** {string.Join(", ", article.Tags)}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(article.MarkdownContent))
        {
            if (compact)
            {
                // Nur erste 800 Zeichen des Markdown-Inhalts
                var content = article.MarkdownContent;
                if (content.Length > 800)
                    content = content[..800] + "\n\n*(…Inhalt gekürzt)*";
                sb.AppendLine(content);
            }
            else
            {
                sb.AppendLine(article.MarkdownContent);
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private void AppendRelated(StringBuilder sb, HelpArticle article)
    {
        var related = _center.GetRelated(article);
        if (related.Count == 0) return;

        sb.AppendLine("**Verwandte Themen:**");
        foreach (var r in related)
            sb.AppendLine($"- {r.Title} (`{r.Id}`)");
        sb.AppendLine();
    }

    private static string SanitizeErrorMessage(string message)
    {
        // Einfache Sanitierung: lange Tokens und Pfade entfernen
        var result = message;

        // Absolute Windows-Pfade maskieren
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"[A-Za-z]:\\[^\s""'<>|]*", "[PFAD]");

        // Lange Hex-Strings (Private Keys, Tokens) maskieren
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"[0-9a-fA-F]{40,}", "[TOKEN/KEY]");

        // Bearer / Authorization Header
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"(Bearer|Authorization:?)\s+\S+", "$1 [TOKEN]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return result.Length > 500 ? result[..500] + "…" : result;
    }

    private static string SanitizeQuery(string query)
        => query.Length > 100 ? query[..100] : query;
}
