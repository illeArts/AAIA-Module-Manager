using System.Collections.Generic;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Einzelner, bereits übersetzter Build-Fehler oder -Hinweis.
/// "Code" und "TechnicalDetails" sind der Roh-dotnet-Output.
/// "HumanMessage" ist die ETW-gerechte Erklärung.
/// </summary>
public sealed class BuildIssue
{
    /// <summary>Fehlercode, z. B. "CS0246", "NU1101", "NETSDK1045".</summary>
    public string Code { get; set; } = "";

    /// <summary>Kurzer Titel in ETW-Sprache, z. B. "Typ oder Namespace nicht gefunden".</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Verständliche Erklärung für den ETW.
    /// Kann durch KI-Anreicherung erweitert werden.
    /// </summary>
    public string HumanMessage { get; set; } = "";

    /// <summary>Originale Zeile aus dem dotnet-Output.</summary>
    public string TechnicalDetails { get; set; } = "";

    /// <summary>"Error" | "Warning"</summary>
    public string Severity { get; set; } = "Error";

    /// <summary>Datei in der der Fehler auftrat (kann leer sein).</summary>
    public string? FilePath { get; set; }

    /// <summary>Zeilennummer in der Datei (null wenn unbekannt).</summary>
    public int? Line { get; set; }

    /// <summary>
    /// Aktionen die dem ETW vorgeschlagen werden, z. B.
    /// "NuGet Restore ausführen", "SDK prüfen".
    /// </summary>
    public List<BuildAction> SuggestedActions { get; set; } = [];

    /// <summary>True wenn eine dieser Aktionen automatisch ausgeführt werden kann.</summary>
    public bool CanAutoFix => SuggestedActions.Exists(a => a.IsAutomatic);

    /// <summary>
    /// ID des passenden Hilfeartikels (docs/help/index.json).
    /// Wird von ErrorHelpMappingService gesetzt. Null = kein Artikel bekannt.
    /// </summary>
    public string? HelpArticleId { get; set; }

    public bool IsError   => Severity == "Error";
    public bool IsWarning => Severity == "Warning";

    public string SeverityIcon => Severity == "Warning" ? "⚠" : "✖";
    public string SeverityColor => Severity == "Warning" ? "#c9a227" : "#e05252";
}

/// <summary>
/// Eine mögliche Aktion als Reaktion auf einen BuildIssue.
/// </summary>
public sealed class BuildAction
{
    /// <summary>Anzeige-Label auf dem Button.</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Identifier für die Aktion — wird im ViewModel per Command-Parameter ausgewertet.
    /// Bekannte Werte: "restore", "sdk-info", "open-csproj", "open-nuget"
    /// </summary>
    public string ActionId { get; set; } = "";

    /// <summary>True → kann direkt ausgeführt werden (Restore, SDK-Info). False → nur Hinweis.</summary>
    public bool IsAutomatic { get; set; }
}
