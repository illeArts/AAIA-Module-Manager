using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Erzwingt Sicherheitsregeln bevor Inhalte an externe KIs gesendet werden.
///
/// UNVERÄNDERLICHE REGELN:
///   1. Private Keys (PEM, Hex-Strings ab 40 Zeichen) kommen NIEMALS in einen Prompt.
///   2. API-Keys und Tokens kommen NIEMALS in einen Prompt.
///   3. Marketplace-Tokens kommen NIEMALS in einen Prompt.
///   4. Quelltexte werden NICHT automatisch einbezogen — nur explizit erlaubte Inhalte.
///   5. ETW-Signatur darf NIEMALS durch externe KI ausgelöst werden.
/// </summary>
public static class AiSafetyPolicy
{
    // ── Bekannte Gefahrenmuster ───────────────────────────────────────────────

    private static readonly Regex[] DangerPatterns =
    [
        // PEM Private Key Block
        new(@"-----BEGIN (RSA |EC |PRIVATE KEY|OPENSSH PRIVATE).*?-----", RegexOptions.IgnoreCase),
        // Lange Hex-Strings (potentielle Private Keys, ab 40 Zeichen)
        new(@"\b[0-9a-fA-F]{40,}\b"),
        // Bearer / API Token Pattern
        new(@"Bearer\s+[A-Za-z0-9\-_\.]{20,}", RegexOptions.IgnoreCase),
        // sk-... OpenAI Key Pattern
        new(@"\bsk-[A-Za-z0-9]{20,}\b"),
        // Anthropic Key Pattern
        new(@"\bsk-ant-[A-Za-z0-9\-]{20,}\b"),
        // AIza... Google Key Pattern
        new(@"\bAIza[A-Za-z0-9\-_]{20,}\b"),
        // JWT Token (drei Punkte-getrennte Base64-Segmente)
        new(@"\beyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\b"),
        // Password-ähnliche Felder
        new(@"""(password|secret|token|apikey|api_key|private_key)""\s*:\s*""[^""]{8,}""", RegexOptions.IgnoreCase),
    ];

    // ── Öffentliche API ───────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob ein Prompt sicher ist. Gibt Warnungen zurück wenn verdächtige Inhalte
    /// gefunden werden. Bei kritischen Verstößen wird <paramref name="isCritical"/> = true.
    /// </summary>
    public static List<string> Validate(string prompt, out bool isCritical)
    {
        var warnings = new List<string>();
        isCritical = false;

        if (string.IsNullOrWhiteSpace(prompt))
            return warnings;

        foreach (var pattern in DangerPatterns)
        {
            if (pattern.IsMatch(prompt))
            {
                isCritical = true;
                warnings.Add($"Sicherheitsregel verletzt: Verdächtiger Inhalt gefunden (Pattern: {pattern}).");
            }
        }

        // Bekannte Dateipfade die Private Keys enthalten könnten
        if (prompt.Contains("-private.pem", StringComparison.OrdinalIgnoreCase))
        {
            isCritical = true;
            warnings.Add("Sicherheitsregel verletzt: Verweis auf Private-Key-Datei gefunden.");
        }

        // Allgemeine Längen-Warnung für UserNote
        if (prompt.Length > 50_000)
            warnings.Add($"Warnung: Prompt ist sehr lang ({prompt.Length:N0} Zeichen). Manche KIs kürzen ab.");

        return warnings;
    }

    /// <summary>
    /// Bereinigt einen User-Note-String von offensichtlich sensiblen Inhalten.
    /// Gibt bereinigten String zurück — ersetzt gefundene Muster durch [ENTFERNT].
    /// </summary>
    public static string SanitizeUserNote(string? userNote)
    {
        if (string.IsNullOrWhiteSpace(userNote)) return "";

        var result = userNote;
        foreach (var pattern in DangerPatterns)
            result = pattern.Replace(result, "[ENTFERNT]");

        return result;
    }

    /// <summary>
    /// Prüft ob eine bestimmte Aktion durch externe KI ausgelöst werden darf.
    /// Gibt false zurück für alle sicherheitskritischen Aktionen.
    /// </summary>
    public static bool IsActionAllowed(ExternalAiAction action) => action switch
    {
        // Lese-Aktionen: immer erlaubt
        ExternalAiAction.ReadProjectSummary    => true,
        ExternalAiAction.ReadManifest          => true,
        ExternalAiAction.ReadBuildErrors       => true,
        ExternalAiAction.ReadValidationReport  => true,
        ExternalAiAction.ReadSelectedFiles     => true,

        // Schreib-Aktionen: nur mit User-Approval (Phase 6.2)
        ExternalAiAction.ProposePatch          => true,

        // Kritische Aktionen: NIEMALS durch externe KI
        ExternalAiAction.ApplyPatch            => false,
        ExternalAiAction.RunBuild              => false,
        ExternalAiAction.RunValidation         => false,
        ExternalAiAction.TriggerEtwSignature   => false,
        ExternalAiAction.PrepareRelease        => false,
        ExternalAiAction.UploadToMarketplace   => false,

        _ => false
    };
}

/// <summary>Aktionen die externe KIs anfordern könnten.</summary>
public enum ExternalAiAction
{
    ReadProjectSummary,
    ReadManifest,
    ReadBuildErrors,
    ReadValidationReport,
    ReadSelectedFiles,
    ProposePatch,
    ApplyPatch,
    RunBuild,
    RunValidation,
    TriggerEtwSignature,
    PrepareRelease,
    UploadToMarketplace
}
