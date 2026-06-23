using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AAIA.ModuleManager.Services.Help;

// ── Artikel ───────────────────────────────────────────────────────────────────

/// <summary>
/// Ein einzelner Hilfeartikel aus dem AAIA-Hilfezentrum.
/// Metadaten kommen aus docs/help/index.json.
/// Inhalt kommt aus der jeweiligen Markdown-Datei.
/// </summary>
public sealed class HelpArticle
{
    /// <summary>Eindeutige ID, z.B. "getting-started.overview"</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Anzeige-Titel</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Rubrik-Name, z.B. "Erste Schritte"</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    /// <summary>Unterrubrik (optional), z.B. "Signatur"</summary>
    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }

    /// <summary>Kurze laienverständliche Beschreibung (1–2 Sätze)</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>Relativer Pfad zur Markdown-Datei innerhalb von docs/help/</summary>
    [JsonPropertyName("markdownPath")]
    public string MarkdownPath { get; set; } = "";

    /// <summary>Suchbare Schlagwörter</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>IDs verwandter Artikel</summary>
    [JsonPropertyName("relatedArticleIds")]
    public List<string> RelatedArticleIds { get; set; } = [];

    /// <summary>Fehlercodes oder HelpArticleIds die diesen Artikel direkt öffnen</summary>
    [JsonPropertyName("errorCodes")]
    public List<string> ErrorCodes { get; set; } = [];

    /// <summary>Pipeline-Schritte zu denen dieser Artikel passt</summary>
    [JsonPropertyName("pipelineSteps")]
    public List<string> PipelineSteps { get; set; } = [];

    /// <summary>Für Einsteiger geeignet</summary>
    [JsonPropertyName("isForBeginners")]
    public bool IsForBeginners { get; set; } = true;

    /// <summary>Für ETW-Entwickler relevant</summary>
    [JsonPropertyName("isForDevelopers")]
    public bool IsForDevelopers { get; set; }

    /// <summary>Für Admins/Owner relevant</summary>
    [JsonPropertyName("isForAdmins")]
    public bool IsForAdmins { get; set; }

    /// <summary>Darf in KI-Kontext-Exports einbezogen werden</summary>
    [JsonPropertyName("isAiReadable")]
    public bool IsAiReadable { get; set; } = true;

    // ── Laufzeit-Feld (nicht serialisiert) ───────────────────────────────────

    /// <summary>Geladener Markdown-Inhalt (wird beim ersten Zugriff befüllt)</summary>
    [JsonIgnore]
    public string? MarkdownContent { get; set; }
}

// ── Index ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Root-Objekt von docs/help/index.json.
/// </summary>
public sealed class HelpIndex
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("articles")]
    public List<HelpArticle> Articles { get; set; } = [];
}

// ── Suchergebnis ──────────────────────────────────────────────────────────────

/// <summary>
/// Ein Treffer aus der lokalen Hilfe-Suche.
/// </summary>
public sealed class HelpSearchResult
{
    public HelpArticle Article    { get; init; } = null!;
    public double      Score      { get; init; }   // höher = relevanter
    public string      Excerpt    { get; init; } = "";
    public bool        TitleMatch { get; init; }
    public bool        TagMatch   { get; init; }
    public bool        ErrorMatch { get; init; }
}
