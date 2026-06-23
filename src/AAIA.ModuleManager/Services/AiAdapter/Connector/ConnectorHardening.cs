using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

/// <summary>
/// Sicherheits-Härtung für den lokalen Connector-Server.
///
/// Schutzziele:
///   1. Nur localhost — Requests von externen IPs werden sofort abgelehnt.
///   2. Rate-Limiting — maximal N Requests/Minute pro Connector-ID.
///   3. Body-Size-Limit — Schutz vor Memory-Exhaustion durch riesige Payloads.
///   4. Patch-Target-Validation — Dateipfade dürfen nicht aus dem Projekt-Root
///      ausbrechen und keine sensiblen Pfade (Keys, .env, …) referenzieren.
/// </summary>
public static class ConnectorHardening
{
    // ── Konstanten ────────────────────────────────────────────────────────────

    /// <summary>Maximale Request-Body-Größe in Bytes (512 KB).</summary>
    public const int MaxBodyBytes = 512 * 1024;

    /// <summary>Maximale Requests pro Minute pro Connector-ID.</summary>
    public const int MaxRequestsPerMinute = 60;

    /// <summary>Timeout für Patch-Approval durch den User (Minuten).</summary>
    public const int PatchApprovalTimeoutMinutes = 10;

    // ── Localhost-Check ───────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob die Verbindung ausschließlich von localhost kommt.
    /// Akzeptiert: 127.0.0.1, ::1 und andere loopback-Adressen.
    /// </summary>
    public static bool IsLocalhost(IPAddress? remoteAddress)
    {
        if (remoteAddress is null) return false;
        // IPv4-mapped IPv6 normalisieren (::ffff:127.0.0.1)
        if (remoteAddress.IsIPv4MappedToIPv6)
            remoteAddress = remoteAddress.MapToIPv4();
        return IPAddress.IsLoopback(remoteAddress);
    }

    // ── Rate-Limiter ──────────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, RateWindow> _rateWindows = new();

    /// <summary>
    /// Gibt zurück ob der Connector das Raten-Limit überschritten hat.
    /// Sliding-Window: zählt Requests der letzten 60 Sekunden.
    /// </summary>
    public static bool IsRateLimited(string connectorId)
    {
        var now    = DateTime.UtcNow;
        var window = _rateWindows.GetOrAdd(connectorId, _ => new RateWindow());

        lock (window)
        {
            // Alte Einträge entfernen (älter als 1 Minute)
            window.Timestamps.RemoveAll(t => (now - t).TotalSeconds > 60);

            if (window.Timestamps.Count >= MaxRequestsPerMinute)
                return true;

            window.Timestamps.Add(now);
            return false;
        }
    }

    /// <summary>Setzt den Rate-Counter für einen Connector zurück (für Tests).</summary>
    public static void ResetRateWindow(string connectorId)
        => _rateWindows.TryRemove(connectorId, out _);

    private sealed class RateWindow
    {
        public List<DateTime> Timestamps { get; } = new(64);
    }

    // ── Body-Size-Check ───────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob der Content-Length-Header die erlaubte Größe überschreitet.
    /// Gibt true zurück wenn der Body zu groß ist.
    /// </summary>
    public static bool IsBodyTooLarge(long contentLength64)
        => contentLength64 > MaxBodyBytes;

    // ── Patch-Target-Validation ───────────────────────────────────────────────

    /// <summary>
    /// Sensible Pfadmuster die ein Patch niemals referenzieren darf.
    /// Groß-/Kleinschreibung irrelevant.
    /// </summary>
    private static readonly string[] ForbiddenPathPatterns =
    [
        ".env",
        ".pem",
        ".pfx",
        ".p12",
        ".key",
        "private",
        "secret",
        "password",
        "credentials",
        "token",
        "apikey",
        "api_key",
        "appsettings.production",
        "appsettings.secrets",
    ];

    /// <summary>
    /// Validiert einen Patch-Ziel-Pfad.
    /// Gibt eine Fehlermeldung zurück, oder null wenn der Pfad sicher ist.
    /// </summary>
    public static string? ValidatePatchTarget(string? targetFile, string? projectRoot = null)
    {
        if (string.IsNullOrWhiteSpace(targetFile))
            return "Zieldatei darf nicht leer sein.";

        // Path-Traversal verhindern
        if (targetFile.Contains(".."))
            return $"Ungültiger Pfad: Path-Traversal nicht erlaubt ('{targetFile}').";

        // Absolute Pfade ablehnen
        if (System.IO.Path.IsPathRooted(targetFile))
            return $"Ungültiger Pfad: Absolute Pfade nicht erlaubt ('{targetFile}').";

        // Sensible Dateinamen prüfen
        var lower = targetFile.ToLowerInvariant().Replace('\\', '/');
        foreach (var pattern in ForbiddenPathPatterns)
        {
            if (lower.Contains(pattern))
                return $"Ungültiger Pfad: Referenziert potenziell sensible Datei ('{pattern}').";
        }

        // Optional: Pfad muss innerhalb des Projekt-Root liegen
        if (!string.IsNullOrEmpty(projectRoot))
        {
            var root     = System.IO.Path.GetFullPath(projectRoot);
            var absolute = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(root, targetFile));
            if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return $"Ungültiger Pfad: Außerhalb des Projekt-Root ('{targetFile}').";
        }

        return null; // OK
    }

    /// <summary>
    /// Validiert alle Patches einer Anfrage.
    /// Gibt eine Liste von Fehlern zurück (leer = alles OK).
    /// </summary>
    public static List<string> ValidatePatchRequest(AiPatchRequest request, string? projectRoot = null)
    {
        var errors = new List<string>();

        if (request.Patches.Count == 0)
        {
            errors.Add("Patch-Anfrage enthält keine Patches.");
            return errors;
        }

        if (request.Patches.Count > 20)
        {
            errors.Add($"Zu viele Patches ({request.Patches.Count}). Maximum: 20.");
        }

        for (int i = 0; i < request.Patches.Count; i++)
        {
            var patch = request.Patches[i];
            var error = ValidatePatchTarget(patch.TargetFile, projectRoot);
            if (error is not null)
                errors.Add($"Patch[{i}]: {error}");

            if (patch.Content.Length > MaxBodyBytes)
                errors.Add($"Patch[{i}]: Inhalt zu groß ({patch.Content.Length} Bytes, max {MaxBodyBytes}).");

            // Kind muss bekannt sein
            if (patch.Kind is not ("FullFileReplacement" or "UnifiedDiff" or "CodeSnippet"))
                errors.Add($"Patch[{i}]: Unbekannter Kind-Wert '{patch.Kind}'.");
        }

        return errors;
    }
}
