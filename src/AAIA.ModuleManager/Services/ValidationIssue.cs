using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Art der Aktion die bei einem ValidationIssue vorgeschlagen wird.
/// </summary>
public sealed class ValidationAction
{
    /// <summary>Anzeige-Label auf dem Button.</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Identifier — wird im ViewModel ausgewertet.
    /// Bekannte Werte: "create-manifest", "add-license-mit", "add-readme",
    ///                 "open-manifest", "open-csproj", "open-folder"
    /// </summary>
    public string ActionId { get; set; } = "";

    /// <summary>True wenn direkt ausführbar (Datei erzeugen, Öffnen).</summary>
    public bool IsAutomatic { get; set; }
}

/// <summary>
/// Einzelner Prüfbefund.
/// Severity: "Error" (Blocker) | "Warning" (Empfehlung) | "Info" (Hinweis)
/// </summary>
public sealed class ValidationIssue
{
    /// <summary>"Error" | "Warning" | "Info"</summary>
    public string Severity { get; set; } = "Info";

    /// <summary>Kurzer Titel in ETW-Sprache.</summary>
    public string Title { get; set; } = "";

    /// <summary>Ausführliche Erklärung und warum das wichtig ist.</summary>
    public string Message { get; set; } = "";

    /// <summary>Kategorie: "Manifest" | "Struktur" | "Kompatibilitaet" | "Risiko"</summary>
    public string Category { get; set; } = "";

    /// <summary>Aktionen die dem ETW angeboten werden.</summary>
    public List<ValidationAction> Actions { get; set; } = [];

    public bool CanAutoFix    => Actions.Exists(a => a.IsAutomatic);
    public bool IsError       => Severity == "Error";
    public bool IsWarning     => Severity == "Warning";
    public bool IsInfo        => Severity == "Info";

    public string SeverityIcon => Severity switch
    {
        "Error"   => "✖",
        "Warning" => "⚠",
        _         => "ℹ"
    };
}

/// <summary>
/// Gesamtergebnis aller Prüfungen für ein AAIA-Projekt.
/// </summary>
public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; set; } = [];

    public int  ErrorCount   => Issues.Count(i => i.IsError);
    public int  WarningCount => Issues.Count(i => i.IsWarning);
    public int  InfoCount    => Issues.Count(i => i.IsInfo);
    public bool HasBlockers    => ErrorCount > 0;
    public bool IsClean        => ErrorCount == 0 && WarningCount == 0;
    public bool WasAiEnriched  { get; set; }

    /// <summary>Kurztext für Header-Badge.</summary>
    public string OverallStatus => HasBlockers
        ? $"{ErrorCount} Blocker — nicht paketierbar"
        : WarningCount > 0
            ? $"{WarningCount} Empfehlung(en)"
            : "Alle Prüfungen bestanden";

    /// <summary>Emoji für den Status-Badge.</summary>
    public string OverallIcon => HasBlockers ? "🔴" : WarningCount > 0 ? "🟡" : "🟢";
}
