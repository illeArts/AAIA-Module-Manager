using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Scannt Projektdateien auf versehentlich eingecheckte Geheimnisse und
/// blockierte Dateien, die nie in ein .aaiaext-Paket dürfen.
///
/// Sicherheitsregel: Kein Paket darf Geheimnisse enthalten.
/// </summary>
public static class SecretScanService
{
    // ── Dateien die kategorisch ausgeschlossen werden müssen ──────────────────

    private static readonly string[] BlockedFilePatterns =
    [
        "appsettings.Development.json",
        "appsettings.Local.json",
        ".env",
        ".env.local",
        ".env.development",
        "*.user",
        "*.suo",
        "*.pfx",
        "*.pem",
        "*.key",
        "*.p12",
        "*.cer",
        "*.der"
    ];

    // ── Secret-Muster in Quellcode/JSON ──────────────────────────────────────

    private static readonly (Regex Pattern, string Label)[] SecretPatterns =
    [
        (new Regex(@"apiKey\s*[=:]\s*[""'][^""']{8,}[""']",         RegexOptions.IgnoreCase | RegexOptions.Compiled), "API-Schlüssel"),
        (new Regex(@"apiSecret\s*[=:]\s*[""'][^""']{8,}[""']",      RegexOptions.IgnoreCase | RegexOptions.Compiled), "API-Secret"),
        (new Regex(@"secret\s*[=:]\s*[""'][^""']{8,}[""']",         RegexOptions.IgnoreCase | RegexOptions.Compiled), "Secret"),
        (new Regex(@"password\s*[=:]\s*[""'][^""']{4,}[""']",       RegexOptions.IgnoreCase | RegexOptions.Compiled), "Passwort"),
        (new Regex(@"\btoken\s*[=:]\s*[""'][^""']{8,}[""']",        RegexOptions.IgnoreCase | RegexOptions.Compiled), "Token"),
        (new Regex(@"bearer\s+[A-Za-z0-9\-._~+/]{20,}",             RegexOptions.IgnoreCase | RegexOptions.Compiled), "Bearer-Token"),
        (new Regex(@"private.?key\s*[=:]\s*[""'][^""']{8,}[""']",   RegexOptions.IgnoreCase | RegexOptions.Compiled), "Private Key"),
        (new Regex(@"connectionString\s*[=:]\s*[""'][^""']{10,}[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Connection-String"),
        // Klassische JWT-Struktur (eyJ...)
        (new Regex(@"eyJ[A-Za-z0-9\-_]{20,}\.[A-Za-z0-9\-_]{20,}",  RegexOptions.Compiled),                          "JWT-Token")
    ];

    // Dateitypen die auf Secrets gescannt werden
    private static readonly HashSet<string> ScannableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".json", ".xml", ".yaml", ".yml", ".config", ".ini", ".env", ".txt", ".md"
    };

    // Dateien/Ordner die beim Scan übersprungen werden (Build-Artefakte etc.)
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules"
    };

    // ── Haupt-Scan ────────────────────────────────────────────────────────────

    public static List<ValidationIssue> Scan(string projectDir)
    {
        var issues = new List<ValidationIssue>();

        if (!Directory.Exists(projectDir)) return issues;

        // 1. Blockierte Dateien prüfen
        ScanBlockedFiles(projectDir, issues);

        // 2. Source-Code auf Secret-Muster prüfen
        ScanForSecrets(projectDir, issues);

        return issues;
    }

    // ── Blockierte Dateien ────────────────────────────────────────────────────

    private static void ScanBlockedFiles(string projectDir, List<ValidationIssue> issues)
    {
        foreach (var file in GetAllFiles(projectDir))
        {
            var fileName = Path.GetFileName(file);
            var ext      = Path.GetExtension(file);

            foreach (var pattern in BlockedFilePatterns)
            {
                if (MatchesPattern(fileName, pattern))
                {
                    var relative = Path.GetRelativePath(projectDir, file);
                    issues.Add(new ValidationIssue
                    {
                        Severity = "Error",
                        Category = "Sicherheit",
                        Title    = $"Verbotene Datei: {fileName}",
                        Message  =
                            $"Die Datei '{relative}' darf nie in ein AAIA-Paket gelangen.\n" +
                            "Sie könnte Zertifikate, Schlüssel oder lokale Konfigurationen enthalten, die nicht öffentlich werden dürfen.\n" +
                            "Füge sie zu .gitignore und .aaiaignore hinzu und entferne sie aus dem Projektordner.",
                        Actions  =
                        [
                            new() { Label = "Ordner öffnen", ActionId = "open-folder", IsAutomatic = true }
                        ]
                    });
                    break;
                }
            }
        }
    }

    // ── Secret-Muster-Scan ────────────────────────────────────────────────────

    private static void ScanForSecrets(string projectDir, List<ValidationIssue> issues)
    {
        foreach (var file in GetAllFiles(projectDir))
        {
            var ext = Path.GetExtension(file);
            if (!ScannableExtensions.Contains(ext)) continue;

            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }

            var relative = Path.GetRelativePath(projectDir, file);

            foreach (var (pattern, label) in SecretPatterns)
            {
                var match = pattern.Match(content);
                if (!match.Success) continue;

                // Kontext: Zeile des Treffers
                var lineNum = CountLines(content, match.Index);

                issues.Add(new ValidationIssue
                {
                    Severity = "Error",
                    Category = "Sicherheit",
                    Title    = $"Mögliches {label} im Code",
                    Message  =
                        $"In '{relative}' (Zeile ~{lineNum}) wurde ein Muster gefunden, das einem {label} ähnelt.\n" +
                        "Hartcodierte Geheimnisse dürfen nicht veröffentlicht werden.\n" +
                        "Nutze AAIA RequiredSecrets oder appsettings.json (außerhalb des Pakets) stattdessen.",
                    Actions  =
                    [
                        new() { Label = "Datei öffnen", ActionId = $"open-file:{file}", IsAutomatic = true }
                    ]
                });
                break; // Pro Datei + Muster nur ein Issue
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<string> GetAllFiles(string dir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if (Directory.Exists(entry))
            {
                var name = Path.GetFileName(entry);
                if (SkipDirs.Contains(name)) continue;
                foreach (var sub in GetAllFiles(entry))
                    yield return sub;
            }
            else
            {
                yield return entry;
            }
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);

        // Einfaches Glob: *.pfx → endet mit .pfx
        var suffix = pattern.TrimStart('*');
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountLines(string text, int charIndex)
    {
        var line = 1;
        for (var i = 0; i < charIndex && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }
}
