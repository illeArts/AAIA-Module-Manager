using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

/// <summary>
/// Parst eine KI-Antwort (Markdown-Text) und extrahiert strukturierte Patch-Vorschläge.
///
/// Erkannte Muster:
///   • Fenced Code Blocks (```csharp ... ```)
///   • Unified Diff-Blöcke (```diff ... ```)
///   • Datei-Header-Kommentare (// File: src/... oder // Datei: src/...)
///   • Explizite Dateiangaben ("Ändere src/X.cs:", "Replace in X.cs:")
///
/// Ergebnis: Liste von PatchProposal — Basis für Phase 6.2 User-Approval-Diff.
/// </summary>
public static class AiResponseImporter
{
    // ── Öffentliche API ───────────────────────────────────────────────────────

    /// <summary>
    /// Parst eine KI-Antwort und gibt alle erkannten Patch-Vorschläge zurück.
    /// Gibt eine leere Liste zurück wenn keine Code-Blöcke gefunden wurden.
    /// </summary>
    public static List<PatchProposal> ParseResponse(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
            return [];

        var proposals = new List<PatchProposal>();

        // 1. Diff-Blöcke (höchste Priorität — eindeutiges Format)
        ExtractDiffBlocks(aiResponse, proposals);

        // 2. Code-Blöcke mit Dateinamen-Hinweis
        ExtractCodeBlocksWithFilename(aiResponse, proposals);

        // 3. Alle weiteren Code-Blöcke ohne expliziten Dateinamen
        ExtractAnonymousCodeBlocks(aiResponse, proposals);

        return proposals;
    }

    /// <summary>
    /// Schnellprüfung: enthält die Antwort überhaupt verwertbare Vorschläge?
    /// </summary>
    public static bool HasPatches(string aiResponse) =>
        ParseResponse(aiResponse).Count > 0;

    // ── Extraktion ────────────────────────────────────────────────────────────

    private static void ExtractDiffBlocks(string text, List<PatchProposal> proposals)
    {
        var diffPattern = new Regex(
            @"```diff\s*\n([\s\S]*?)```",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (Match m in diffPattern.Matches(text))
        {
            var diffContent = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(diffContent)) continue;

            // Versuche Zieldatei aus Diff-Header zu lesen (+++ b/path)
            var filePath = ExtractFileFromDiffHeader(diffContent);

            proposals.Add(new PatchProposal
            {
                Kind          = PatchKind.UnifiedDiff,
                Content       = diffContent,
                SuggestedFile = filePath,
                LineCount     = diffContent.Split('\n').Length,
                RawBlock      = m.Value
            });
        }
    }

    private static void ExtractCodeBlocksWithFilename(string text, List<PatchProposal> proposals)
    {
        // Muster: "File: src/X.cs" oder "// Datei: X.cs" direkt vor oder im Code-Block
        var fileHintPattern = new Regex(
            @"(?:(?:^|\n)(?:[\w\s]*[Ff]ile[:\s]+|//\s*(?:Datei|File)[:\s]+)([^\n`]+)\n\s*)?```(\w*)\s*\n([\s\S]*?)```",
            RegexOptions.Multiline);

        foreach (Match m in fileHintPattern.Matches(text))
        {
            var hintFile = m.Groups[1].Value.Trim();
            var lang     = m.Groups[2].Value.Trim();
            var code     = m.Groups[3].Value.Trim();

            if (string.IsNullOrWhiteSpace(code)) continue;

            // Diff-Blöcke wurden schon erfasst
            if (lang.Equals("diff", StringComparison.OrdinalIgnoreCase)) continue;

            // Dateiname aus Code-Header-Kommentar extrahieren falls kein Hint
            if (string.IsNullOrEmpty(hintFile))
                hintFile = ExtractFileFromCodeComment(code);

            if (string.IsNullOrEmpty(hintFile)) continue; // ohne Dateiname → anonymer Block

            proposals.Add(new PatchProposal
            {
                Kind          = PatchKind.FullFileReplacement,
                Content       = code,
                SuggestedFile = NormalizePath(hintFile),
                Language      = lang,
                LineCount     = code.Split('\n').Length,
                RawBlock      = m.Value
            });
        }
    }

    private static void ExtractAnonymousCodeBlocks(string text, List<PatchProposal> proposals)
    {
        var codePattern = new Regex(
            @"```(\w+)\s*\n([\s\S]*?)```",
            RegexOptions.Multiline);

        foreach (Match m in codePattern.Matches(text))
        {
            var lang = m.Groups[1].Value.Trim();
            var code = m.Groups[2].Value.Trim();

            if (string.IsNullOrWhiteSpace(code)) continue;
            if (lang.Equals("diff", StringComparison.OrdinalIgnoreCase)) continue;

            // Prüfe ob dieser Block schon als "mit Dateiname" erfasst wurde
            if (proposals.Exists(p => p.RawBlock == m.Value)) continue;

            // Dateiname aus erstem Kommentar versuchen
            var suggestedFile = ExtractFileFromCodeComment(code);

            proposals.Add(new PatchProposal
            {
                Kind          = PatchKind.CodeSnippet,
                Content       = code,
                SuggestedFile = suggestedFile,
                Language      = lang,
                LineCount     = code.Split('\n').Length,
                RawBlock      = m.Value
            });
        }
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static string? ExtractFileFromDiffHeader(string diff)
    {
        var m = Regex.Match(diff, @"^\+\+\+\s+b?/?(.+)$", RegexOptions.Multiline);
        return m.Success ? NormalizePath(m.Groups[1].Value.Trim()) : null;
    }

    private static string? ExtractFileFromCodeComment(string code)
    {
        // Erste Zeile: // File: path, // Datei: path, # File: path
        var m = Regex.Match(
            code,
            @"^(?://|#)\s*(?:[Ff]ile|[Dd]atei)[:\s]+([^\n]+)",
            RegexOptions.Multiline);
        return m.Success ? NormalizePath(m.Groups[1].Value.Trim()) : null;
    }

    private static string NormalizePath(string raw) =>
        raw.TrimStart('/', '\\')
           .Replace('\\', '/')
           .Trim();
}

// ── Datenmodelle ──────────────────────────────────────────────────────────────

/// <summary>Welche Art von Vorschlag die KI gemacht hat.</summary>
public enum PatchKind
{
    /// <summary>Unified Diff — enthält +/- Zeilen.</summary>
    UnifiedDiff,
    /// <summary>Vollständige Datei-Ersetzung — komplette neue Version einer Datei.</summary>
    FullFileReplacement,
    /// <summary>Code-Schnipsel ohne klare Datei-Zuordnung.</summary>
    CodeSnippet
}

/// <summary>
/// Ein einzelner Patch-Vorschlag aus einer KI-Antwort.
/// Basis für Phase 6.2 User-Approval-Diff.
/// </summary>
public sealed class PatchProposal
{
    /// <summary>Art des Vorschlags.</summary>
    public PatchKind Kind { get; init; }
    /// <summary>Inhalt (Diff-Text oder vollständiger Datei-Inhalt).</summary>
    public string Content { get; init; } = "";
    /// <summary>Zieldatei — null wenn KI keinen Dateinamen angegeben hat.</summary>
    public string? SuggestedFile { get; init; }
    /// <summary>Programmiersprache (cs, axaml, json, …).</summary>
    public string Language { get; init; } = "";
    /// <summary>Anzahl Codezeilen.</summary>
    public int LineCount { get; init; }
    /// <summary>Roher Markdown-Block (für Debugging).</summary>
    public string RawBlock { get; init; } = "";

    /// <summary>True wenn eine Zieldatei bekannt ist.</summary>
    public bool HasTargetFile => !string.IsNullOrEmpty(SuggestedFile);

    /// <summary>Kurzbezeichnung für UI-Darstellung.</summary>
    public string DisplayLabel => Kind switch
    {
        PatchKind.UnifiedDiff        => $"Diff  {SuggestedFile ?? "(Datei unbekannt)"}",
        PatchKind.FullFileReplacement => $"Datei  {SuggestedFile}",
        PatchKind.CodeSnippet        => $"Snippet ({Language}, {LineCount} Zeilen)",
        _                            => Kind.ToString()
    };
}
