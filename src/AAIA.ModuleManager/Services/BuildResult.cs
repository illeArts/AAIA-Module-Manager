using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Ergebnis eines dotnet restore + build Laufs.
/// Enthält sowohl den Roh-Output als auch die übersetzten Issues.
/// </summary>
public sealed class BuildResult
{
    /// <summary>True wenn der Build erfolgreich war (ExitCode 0 und keine Fehler).</summary>
    public bool Success { get; set; }

    /// <summary>Vollständiger stdout+stderr Output (für "Details anzeigen").</summary>
    public string RawOutput { get; set; } = "";

    /// <summary>Übersetzte Build-Probleme. Leer bei Erfolg.</summary>
    public List<BuildIssue> Issues { get; set; } = [];

    /// <summary>Wurde der Output zusätzlich von der KI angereichert?</summary>
    public bool WasAiEnriched { get; set; }

    public int ErrorCount   => Issues.Count(i => i.IsError);
    public int WarningCount => Issues.Count(i => i.IsWarning);
    public bool HasIssues   => Issues.Count > 0;

    /// <summary>Zusammenfassung für den ETW.</summary>
    public string SummaryLabel => Success
        ? WarningCount > 0
            ? $"Erfolgreich — {WarningCount} Hinweis(e)"
            : "Erfolgreich kompiliert."
        : ErrorCount == 1
            ? "1 Fehler gefunden."
            : $"{ErrorCount} Fehler gefunden.";
}
