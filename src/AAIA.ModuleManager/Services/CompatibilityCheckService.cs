using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Prüft ob die .csproj(s) eines Projekts mit AAIAS/AAIAC kompatibel sind.
/// </summary>
public static class CompatibilityCheckService
{
    // AAIAS erfordert mindestens net8.0
    private const int MinMajor = 8;
    private const int MinMinor = 0;

    private static readonly Regex TfmPattern = new(
        @"<TargetFramework[s]?>([^<]+)</TargetFramework[s]?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NullablePattern = new(
        @"<Nullable>\s*enable\s*</Nullable>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ValidationIssue> Validate(string projectDir, NewProjectType projectType)
    {
        var issues = new List<ValidationIssue>();

        var csprojFiles = GetCsprojFiles(projectDir, projectType);

        if (csprojFiles.Count == 0)
            return issues; // ExtensionStructureValidator meldet bereits fehlende .csproj

        foreach (var csproj in csprojFiles)
        {
            string content;
            try { content = File.ReadAllText(csproj); }
            catch { continue; }

            var relativeName = Path.GetFileName(csproj);

            // ── TargetFramework ───────────────────────────────────────────────

            var tfmMatch = TfmPattern.Match(content);
            if (!tfmMatch.Success)
            {
                issues.Add(Warning("Kompatibilitaet",
                    $"TargetFramework nicht gefunden ({relativeName})",
                    "Das TargetFramework konnte in der .csproj nicht gelesen werden.\n" +
                    "AAIAS erfordert mindestens net8.0.",
                    "open-csproj"));
            }
            else
            {
                // Mehrere TFMs möglich (z. B. net8.0;net9.0) — erstes auswerten
                var tfm   = tfmMatch.Groups[1].Value.Split(';')[0].Trim().ToLower();
                var major = ParseNetMajor(tfm);
                var minor = ParseNetMinor(tfm);

                if (major < 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = "Info",
                        Category = "Kompatibilitaet",
                        Title    = $"Unbekanntes TargetFramework '{tfm}'",
                        Message  = "Das angegebene TargetFramework ist kein bekanntes .NET-TFM.\n" +
                                   "AAIAS erfordert net8.0 oder höher.",
                        Actions  = [new() { Label = ".csproj öffnen", ActionId = "open-csproj", IsAutomatic = true }]
                    });
                }
                else if (major < MinMajor || (major == MinMajor && minor < MinMinor))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = "Error",
                        Category = "Kompatibilitaet",
                        Title    = $"TargetFramework zu alt: {tfm}",
                        Message  = $"AAIAS erfordert mindestens net{MinMajor}.{MinMinor}.\n" +
                                   $"Dieses Projekt zielt auf {tfm} — das ist nicht kompatibel.\n" +
                                   "Ändere <TargetFramework> in der .csproj auf net8.0.",
                        Actions  = [new() { Label = ".csproj öffnen", ActionId = "open-csproj", IsAutomatic = true }]
                    });
                }
            }

            // ── Nullable ──────────────────────────────────────────────────────

            if (!NullablePattern.IsMatch(content))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = "Info",
                    Category = "Kompatibilitaet",
                    Title    = $"Nullable nicht aktiviert ({relativeName})",
                    Message  = "AAIA SDK-Contracts nutzen Nullable Reference Types.\n" +
                               "Empfehlung: <Nullable>enable</Nullable> in die .csproj eintragen.",
                    Actions  = [new() { Label = ".csproj öffnen", ActionId = "open-csproj", IsAutomatic = true }]
                });
            }
        }

        return issues;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> GetCsprojFiles(string projectDir, NewProjectType type)
    {
        if (type == NewProjectType.HybridModule)
        {
            var result = new List<string>();
            foreach (var sub in new[] { "Server", "Client" })
            {
                var dir   = Path.Combine(projectDir, sub);
                if (Directory.Exists(dir))
                    result.AddRange(Directory.GetFiles(dir, "*.csproj"));
            }
            return result;
        }

        if (type == NewProjectType.LanguagePack)
            return []; // kein C# Projekt

        return [.. Directory.GetFiles(projectDir, "*.csproj")];
    }

    private static int ParseNetMajor(string tfm)
    {
        // net8.0 → 8, net9.0-windows → 9
        var m = Regex.Match(tfm, @"^net(\d+)\.(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    private static int ParseNetMinor(string tfm)
    {
        var m = Regex.Match(tfm, @"^net\d+\.(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static ValidationIssue Warning(
        string category, string title, string message, string? actionId = null) =>
        new()
        {
            Severity = "Warning",
            Category = category,
            Title    = title,
            Message  = message,
            Actions  = actionId is null ? [] :
                       [new() { Label = "Öffnen", ActionId = actionId, IsAutomatic = true }]
        };
}
