using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.Help;

/// <summary>
/// Zentraler Service für das AAIA-Hilfezentrum.
///
/// Lädt docs/help/index.json und die zugehörigen Markdown-Dateien.
/// Arbeitet vollständig offline — keine externe Abhängigkeit.
/// </summary>
public sealed class HelpCenterService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    private readonly string     _helpRoot;
    private          HelpIndex? _index;
    private          bool       _loaded;

    /// <summary>
    /// Erstellt den Service.
    /// </summary>
    /// <param name="helpRoot">
    ///   Pfad zum docs/help-Ordner.
    ///   Wenn null, wird automatisch relativ zur ausführenden Assembly ermittelt.
    /// </param>
    public HelpCenterService(string? helpRoot = null)
    {
        _helpRoot = helpRoot ?? ResolveDefaultHelpRoot();
    }

    // ── Laden ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lädt den Index (einmalig, danach gecacht).
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        var indexPath = Path.Combine(_helpRoot, "index.json");
        if (!File.Exists(indexPath))
        {
            _index  = new HelpIndex();
            _loaded = true;
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexPath);
            _index = JsonSerializer.Deserialize<HelpIndex>(json, JsonOpts) ?? new HelpIndex();
        }
        catch
        {
            _index = new HelpIndex();
        }

        _loaded = true;
    }

    // ── Abfragen ──────────────────────────────────────────────────────────────

    /// <summary>Alle Artikel aus dem Index.</summary>
    public IReadOnlyList<HelpArticle> AllArticles
        => _index?.Articles ?? [];

    /// <summary>Alle Kategorien (dedupliziert, sortiert).</summary>
    public IReadOnlyList<string> Categories
        => AllArticles
            .Select(a => a.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    /// <summary>Alle Artikel einer Kategorie.</summary>
    public IReadOnlyList<HelpArticle> GetByCategory(string category)
        => AllArticles
            .Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Artikel per ID finden. Null wenn nicht vorhanden.</summary>
    public HelpArticle? GetById(string id)
        => AllArticles.FirstOrDefault(a =>
            a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Artikel per Fehlercode oder HelpArticleId finden.
    /// Nützlich für direkte Fehler→Hilfe-Verlinkung.
    /// </summary>
    public HelpArticle? GetByErrorCode(string errorCode)
    {
        // Erst direkte ID-Suche
        var direct = GetById(errorCode);
        if (direct is not null) return direct;

        // Dann Fehlercode-Suche
        return AllArticles.FirstOrDefault(a =>
            a.ErrorCodes.Any(e =>
                e.Equals(errorCode, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Verwandte Artikel für einen gegebenen Artikel.</summary>
    public IReadOnlyList<HelpArticle> GetRelated(HelpArticle article)
        => article.RelatedArticleIds
            .Select(GetById)
            .Where(a => a is not null)
            .Cast<HelpArticle>()
            .ToList();

    // ── Markdown laden ────────────────────────────────────────────────────────

    /// <summary>
    /// Lädt den Markdown-Inhalt eines Artikels (gecacht im Artikel-Objekt).
    /// </summary>
    public async Task<string> LoadMarkdownAsync(HelpArticle article)
    {
        if (article.MarkdownContent is not null)
            return article.MarkdownContent;

        var path = Path.Combine(_helpRoot, article.MarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            article.MarkdownContent = $"# {article.Title}\n\n_{article.Summary}_\n\n*(Artikel nicht gefunden: `{path}`)*";
            return article.MarkdownContent;
        }

        try
        {
            article.MarkdownContent = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            article.MarkdownContent = $"# {article.Title}\n\n*Fehler beim Laden: {ex.Message}*";
        }

        return article.MarkdownContent;
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static string ResolveDefaultHelpRoot()
    {
        // Versuche Pfad relativ zur ausführenden Assembly
        var assembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var dir      = Path.GetDirectoryName(assembly) ?? AppContext.BaseDirectory;

        // Typische Pfade: neben der EXE, oder im Repo für Entwicklung
        string[] candidates =
        [
            Path.Combine(dir, "docs", "help"),
            Path.Combine(dir, "..", "..", "..", "..", "..", "docs", "help"),
            Path.Combine(AppContext.BaseDirectory, "docs", "help"),
        ];

        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
                return Path.GetFullPath(c);
        }

        // Fallback: neben der EXE, wird ggf. beim ersten Zugriff fehlen
        return Path.Combine(dir, "docs", "help");
    }
}
