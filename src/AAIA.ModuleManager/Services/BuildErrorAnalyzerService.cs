using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Parst dotnet-Build-Output und übersetzt Fehlercodes in ETW-gerechte Nachrichten.
/// Funktioniert vollständig offline (regelbasiert).
/// Optional: AI-Anreicherung wenn Provider vorhanden.
/// </summary>
public static class BuildErrorAnalyzerService
{
    // ── Regex-Muster ────────────────────────────────────────────────────────────

    // Beispiel: src/Foo.cs(10,5): error CS0246: ...
    private static readonly Regex CsErrorPattern = new(
        @"(?<file>.+)\((?<line>\d+),(?<col>\d+)\):\s+(?<severity>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Beispiel:   error NU1101: Unable to find package ...
    private static readonly Regex NuErrorPattern = new(
        @"\s*(?:error|warning)\s+(?<code>NU\d+|MSB\d+|NETSDK\d+|SDK\d*):\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // project.assets.json fehlt
    private static readonly Regex AssetsPattern = new(
        @"project\.assets\.json",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Öffentliche API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Analysiert den Roh-Output eines dotnet-Build-Laufs und gibt übersetzte Issues zurück.
    /// Immer verfügbar, auch ohne KI.
    /// </summary>
    public static List<BuildIssue> Analyze(string rawOutput)
    {
        var issues = new List<BuildIssue>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // project.assets.json fehlt → restore erforderlich (hohe Priorität)
        if (AssetsPattern.IsMatch(rawOutput) &&
            rawOutput.Contains("run a NuGet package restore", StringComparison.OrdinalIgnoreCase))
        {
            var key = "RESTORE_NEEDED";
            if (seen.Add(key))
                issues.Add(MakeRestoreNeeded());
        }

        foreach (var line in rawOutput.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // CS / Roslyn Fehler (mit Datei + Zeile)
            var m = CsErrorPattern.Match(trimmed);
            if (m.Success)
            {
                var code     = m.Groups["code"].Value.ToUpperInvariant();
                var severity = m.Groups["severity"].Value.ToLower() == "warning" ? "Warning" : "Error";
                var file     = m.Groups["file"].Value.Trim();
                var lineNum  = int.TryParse(m.Groups["line"].Value, out var l) ? l : (int?)null;
                var rawMsg   = m.Groups["msg"].Value.Trim();
                var dedupeKey = $"{code}:{file}:{lineNum}";

                if (!seen.Add(dedupeKey)) continue;

                var issue = TranslateCode(code, rawMsg, severity);
                issue.FilePath        = file;
                issue.Line            = lineNum;
                issue.TechnicalDetails = trimmed;
                issues.Add(issue);
                continue;
            }

            // NU / MSB / NETSDK Fehler (ohne Datei)
            var m2 = NuErrorPattern.Match(trimmed);
            if (m2.Success)
            {
                var code   = m2.Groups["code"].Value.ToUpperInvariant();
                var rawMsg = m2.Groups["msg"].Value.Trim();
                if (!seen.Add(code)) continue;

                var issue = TranslateCode(code, rawMsg, "Error");
                issue.TechnicalDetails = trimmed;
                issues.Add(issue);
            }
        }

        return issues;
    }

    /// <summary>
    /// Optionale KI-Anreicherung: Fügt detailliertere Erklärungen hinzu wenn Provider verfügbar.
    /// Schlägt silent fehl — regelbasierte Übersetzungen bleiben immer erhalten.
    /// </summary>
    public static async Task EnrichWithAiAsync(
        BuildResult        result,
        IAiProviderService provider,
        string?            projectName,
        CancellationToken  ct = default)
    {
        if (result.Success || result.Issues.Count == 0) return;

        try
        {
            // Maximal die ersten 3 Fehler an KI senden
            var sb = new StringBuilder();
            sb.AppendLine("Du bist AAIA Build-Assistent. Analysiere diese Buildfehler eines ETW-Projekts.");
            sb.AppendLine("Antworte auf Deutsch, klar und knapp. Maximal 3 Sätze pro Fehler.");
            sb.AppendLine("Formatiere nicht mit Markdown. Kein Präambel.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(projectName))
                sb.AppendLine($"Projektname: {projectName}");
            sb.AppendLine();
            sb.AppendLine("Fehler:");
            var count = 0;
            foreach (var issue in result.Issues)
            {
                if (issue.IsWarning) continue;
                if (++count > 3) break;
                sb.AppendLine($"- [{issue.Code}] {issue.TechnicalDetails}");
            }
            sb.AppendLine();
            sb.AppendLine("Erkläre die Ursache jedes Fehlers und was der ETW konkret tun soll.");

            var resp = await provider.SendAsync(
                new AiRequest([new ChatMessage("user", sb.ToString())],
                    "Du bist ein hilfreicher AAIA Build-Assistent.", 800), ct);

            if (resp.Success && !string.IsNullOrWhiteSpace(resp.Text))
            {
                // KI-Antwort als zusätzliche Erklärung auf den ersten Fehler setzen
                var firstError = result.Issues.Find(i => i.IsError);
                if (firstError is not null)
                    firstError.HumanMessage =
                        firstError.HumanMessage + "\n\n🤖 KI-Hinweis:\n" + resp.Text.Trim();

                result.WasAiEnriched = true;
            }
        }
        catch
        {
            // KI-Fehler still schlucken — regelbasierte Übersetzungen bleiben
        }
    }

    // ── Übersetzungstabelle ──────────────────────────────────────────────────────

    private static BuildIssue TranslateCode(string code, string rawMsg, string severity)
    {
        var issue = new BuildIssue { Code = code, Severity = severity };

        switch (code)
        {
            // ── C# Kompilerfehler ────────────────────────────────────────────────

            case "CS0246":
                issue.Title        = "Typ oder Namespace nicht gefunden";
                issue.HumanMessage =
                    "Das Projekt verweist auf einen Typ oder Namespace der nicht existiert.\n\n" +
                    "Wahrscheinliche Ursachen:\n" +
                    "• Ein NuGet-Paket oder SDK-Verweis fehlt in der .csproj\n" +
                    "• dotnet restore wurde noch nicht ausgeführt\n" +
                    "• Ein using-Statement fehlt in der Datei";
                issue.SuggestedActions =
                [
                    new() { Label = "Restore ausführen", ActionId = "restore",   IsAutomatic = true },
                    new() { Label = "SDK prüfen",         ActionId = "sdk-info",  IsAutomatic = true },
                    new() { Label = ".csproj öffnen",     ActionId = "open-csproj" }
                ];
                break;

            case "CS0103":
                issue.Title        = "Unbekannter Bezeichner";
                issue.HumanMessage =
                    "Ein Name (Variable, Klasse, Methode) wird verwendet, ist aber im aktuellen Scope nicht bekannt.\n\n" +
                    "Prüfe ob:\n" +
                    "• Das richtige using-Statement vorhanden ist\n" +
                    "• Der Name korrekt geschrieben ist (Groß-/Kleinschreibung!)";
                break;

            case "CS0101":
                issue.Title        = "Typ doppelt definiert";
                issue.HumanMessage =
                    "Eine Klasse oder ein Struct ist mehrfach mit gleichem Namen im selben Namespace vorhanden.\n\n" +
                    "Entweder einen der Namen umbenennen oder die Dateien zusammenführen.";
                break;

            case "CS0111":
                issue.Title        = "Methode doppelt definiert";
                issue.HumanMessage =
                    "Eine Methode existiert mehrfach mit identischer Signatur in derselben Klasse.\n\n" +
                    "Entweder die Parameterliste ändern (Overloading) oder eine der Methoden entfernen.";
                break;

            case "CS1061":
                issue.Title        = "Methode oder Eigenschaft nicht vorhanden";
                issue.HumanMessage =
                    "Das Objekt kennt die aufgerufene Methode oder Eigenschaft nicht.\n\n" +
                    "Mögliche Ursachen:\n" +
                    "• Tippfehler im Methodennamen\n" +
                    "• Veraltetes NuGet-Paket (Restore ausführen)\n" +
                    "• Methode existiert in der genutzten SDK-Version nicht";
                issue.SuggestedActions =
                [
                    new() { Label = "Restore ausführen", ActionId = "restore", IsAutomatic = true }
                ];
                break;

            case "CS0234":
                issue.Title        = "Typ im Namespace nicht gefunden";
                issue.HumanMessage =
                    "Der Namespace existiert, aber der erwartete Typ ist darin nicht vorhanden.\n\n" +
                    "Oft ein Versions- oder Restore-Problem. Prüfe die Paketversion in der .csproj.";
                issue.SuggestedActions =
                [
                    new() { Label = "Restore ausführen", ActionId = "restore",   IsAutomatic = true },
                    new() { Label = ".csproj öffnen",    ActionId = "open-csproj" }
                ];
                break;

            case "CS0029":
                issue.Title        = "Typkonvertierung nicht möglich";
                issue.HumanMessage =
                    "Ein Wert kann nicht automatisch in den erwarteten Typ konvertiert werden.\n\n" +
                    "Explizite Konvertierung (cast) oder Anpassung der Typen erforderlich.";
                break;

            case "CS0117":
            case "CS0122":
                issue.Title        = "Mitglied nicht zugänglich oder nicht vorhanden";
                issue.HumanMessage =
                    "Ein Feld, eine Eigenschaft oder Methode ist entweder nicht vorhanden " +
                    "oder durch einen Zugriffsmodifier (private/internal) geschützt.";
                break;

            case "CS1002":
                issue.Title        = "Semikolon erwartet";
                issue.HumanMessage =
                    "An dieser Stelle erwartet der Compiler ein Semikolon.\n\n" +
                    "Häufig: vergessenes ';' am Ende einer Anweisung.";
                break;

            case "CS1513":
                issue.Title        = "Geschweifte Klammer erwartet";
                issue.HumanMessage =
                    "Eine schließende geschweifte Klammer '}' fehlt.\n\n" +
                    "Prüfe ob alle '{' ... '}' Blöcke korrekt geschlossen sind.";
                break;

            // ── NuGet ────────────────────────────────────────────────────────────

            case "NU1101":
                issue.Title        = "NuGet-Paket nicht gefunden";
                issue.HumanMessage =
                    "Ein in der .csproj referenziertes NuGet-Paket existiert nicht in den konfigurierten Quellen.\n\n" +
                    "Prüfe:\n" +
                    "• Paketname und Version in der .csproj\n" +
                    "• Internetverbindung (für nuget.org)\n" +
                    "• Lokale NuGet-Quellen falls du AAIA.Shared.Contracts lokal nutzt";
                issue.SuggestedActions =
                [
                    new() { Label = "Restore ausführen", ActionId = "restore",    IsAutomatic = true },
                    new() { Label = "NuGet prüfen",      ActionId = "open-nuget" },
                    new() { Label = ".csproj öffnen",    ActionId = "open-csproj" }
                ];
                break;

            case "NU1102":
                issue.Title        = "NuGet-Paket in dieser Version nicht gefunden";
                issue.HumanMessage =
                    "Das Paket existiert, aber die angegebene Version ist nicht verfügbar.\n\n" +
                    "Passe die Version in der .csproj an oder prüfe ob die Quelle die Version enthält.";
                issue.SuggestedActions =
                [
                    new() { Label = ".csproj öffnen", ActionId = "open-csproj" }
                ];
                break;

            // ── .NET SDK ─────────────────────────────────────────────────────────

            case "NETSDK1045":
                issue.Title        = "Falsche .NET SDK Version";
                issue.HumanMessage =
                    "Das Projekt erfordert eine .NET SDK Version die auf diesem Rechner nicht installiert ist.\n\n" +
                    "Prüfe die installierte SDK-Version und vergleiche sie mit dem TargetFramework in der .csproj.";
                issue.SuggestedActions =
                [
                    new() { Label = "SDK-Info anzeigen", ActionId = "sdk-info", IsAutomatic = true },
                    new() { Label = ".csproj öffnen",    ActionId = "open-csproj" }
                ];
                break;

            case "NETSDK1004":
                issue.Title        = "project.assets.json fehlt — Restore erforderlich";
                issue.HumanMessage =
                    "Die NuGet-Restore-Datei fehlt. dotnet restore muss zuerst ausgeführt werden.";
                issue.SuggestedActions =
                [
                    new() { Label = "Restore ausführen", ActionId = "restore", IsAutomatic = true }
                ];
                break;

            // ── MSBuild ──────────────────────────────────────────────────────────

            case "MSB1009":
                issue.Title        = "Projektdatei nicht gefunden";
                issue.HumanMessage =
                    "Die .csproj-Datei wurde nicht am erwarteten Pfad gefunden.\n\n" +
                    "Prüfe ob das Projekt korrekt erstellt wurde und der Pfad stimmt.";
                break;

            case "MSB3644":
                issue.Title        = "Referenz-Assemblies fehlen";
                issue.HumanMessage =
                    "Die .NET Referenz-Assemblies für das Zielframework fehlen.\n\n" +
                    "Prüfe ob die passende .NET SDK und Workloads installiert sind.";
                issue.SuggestedActions =
                [
                    new() { Label = "SDK-Info anzeigen", ActionId = "sdk-info", IsAutomatic = true }
                ];
                break;

            // ── Fallback ─────────────────────────────────────────────────────────

            default:
                issue.Title        = code.StartsWith("CS") ? "Kompilerfehler"
                                   : code.StartsWith("NU") ? "NuGet-Fehler"
                                   : code.StartsWith("NETSDK") ? "SDK-Problem"
                                   : code.StartsWith("MSB") ? "Build-Systemfehler"
                                   : "Unbekannter Fehler";
                issue.HumanMessage =
                    $"Fehlercode {code}: {rawMsg}\n\n" +
                    "Dieser Fehlercode ist nicht in der bekannten Übersetzungsliste. " +
                    "Prüfe die technischen Details oder frage den KI-Assistenten.";
                break;
        }

        return issue;
    }

    private static BuildIssue MakeRestoreNeeded() => new()
    {
        Code           = "RESTORE",
        Severity       = "Error",
        Title          = "NuGet Restore erforderlich",
        HumanMessage   = "Das Projekt wurde noch nicht restored. NuGet-Pakete fehlen.\n\n" +
                         "Das passiert beim ersten Build nach dem Erstellen — einfach Restore ausführen.",
        TechnicalDetails = "project.assets.json not found",
        SuggestedActions =
        [
            new() { Label = "Restore ausführen", ActionId = "restore", IsAutomatic = true }
        ]
    };
}
