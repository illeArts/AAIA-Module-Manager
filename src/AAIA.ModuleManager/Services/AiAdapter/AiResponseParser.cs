using System;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Normalisiert und analysiert KI-Antworten unabhängig vom Anbieter.
/// Erkennt Struktur wie Code-Blöcke, Patch-Vorschläge und Fehler.
/// </summary>
public static class AiResponseParser
{
    // ── Basis-Parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalisiert eine rohe API-Antwort.
    /// Entfernt übermäßige Leerzeilen, trimmt, behält Code-Blöcke intakt.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // Mehr als 2 aufeinanderfolgende Leerzeilen auf 2 reduzieren
        var normalized = Regex.Replace(raw.Trim(), @"\n{3,}", "\n\n");
        return normalized;
    }

    /// <summary>
    /// Extrahiert alle Code-Blöcke (```...```) aus einer Antwort.
    /// </summary>
    public static ParsedCodeBlock[] ExtractCodeBlocks(string response)
    {
        var matches = Regex.Matches(
            response,
            @"```(\w*)\n([\s\S]*?)```",
            RegexOptions.Multiline);

        var result = new ParsedCodeBlock[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            result[i] = new ParsedCodeBlock(
                Language: matches[i].Groups[1].Value.Trim(),
                Code:     matches[i].Groups[2].Value.Trim());
        }
        return result;
    }

    /// <summary>
    /// Erkennt ob die Antwort einen Patch-Vorschlag enthält
    /// (diff-Format oder explizite Datei-Änderungen).
    /// </summary>
    public static bool ContainsPatchProposal(string response)
    {
        return response.Contains("```diff", StringComparison.OrdinalIgnoreCase)
            || response.Contains("--- a/", StringComparison.Ordinal)
            || response.Contains("+++ b/", StringComparison.Ordinal)
            || Regex.IsMatch(response, @"@@\s+-\d+,\d+\s+\+\d+,\d+\s+@@");
    }

    /// <summary>
    /// Gibt true zurück wenn die Antwort erkennbar eine Fehlermeldung der KI-API ist
    /// (kein inhaltlicher Fehler, sondern API-Fehler).
    /// </summary>
    public static bool IsApiError(string response)
    {
        return response.StartsWith("Fehler", StringComparison.OrdinalIgnoreCase)
            || response.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            || response.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || response.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Schätzt die Qualität einer Antwort (für UI-Darstellung).
    /// Rein heuristisch — kein Anspruch auf Korrektheit.
    /// </summary>
    public static AiResponseQuality EstimateQuality(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length < 50)
            return AiResponseQuality.Empty;

        if (IsApiError(response))
            return AiResponseQuality.ApiError;

        var codeBlocks = ExtractCodeBlocks(response);
        if (codeBlocks.Length > 0 && response.Length > 200)
            return AiResponseQuality.WithCode;

        if (response.Length > 500)
            return AiResponseQuality.Detailed;

        return AiResponseQuality.Brief;
    }
}

// ── Hilfsdatentypen ───────────────────────────────────────────────────────────

public sealed record ParsedCodeBlock(string Language, string Code);

public enum AiResponseQuality
{
    Empty,
    ApiError,
    Brief,
    Detailed,
    WithCode
}
