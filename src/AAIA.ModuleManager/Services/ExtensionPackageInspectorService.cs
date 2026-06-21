using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AAIA.ModuleManager.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Einzelner Eintrag im .aaiaext-Paket.
/// </summary>
public sealed class PackageFileEntry
{
    public string Path      { get; set; } = "";
    public string Extension { get; set; } = "";
    public long   SizeBytes { get; set; }

    /// <summary>Manifest | Assembly | Dependency | Documentation | Asset | Config | NativeBinary | Suspicious | Other</summary>
    public string Category  { get; set; } = "Other";

    /// <summary>"" | "Warning" | "Blocker"</summary>
    public string RiskHint  { get; set; } = "";

    public bool IsBlocker => RiskHint == "Blocker";
    public bool IsWarning => RiskHint == "Warning";

    public string SizeLabel => SizeBytes switch
    {
        < 1024        => $"{SizeBytes} B",
        < 1048576     => $"{SizeBytes / 1024.0:F1} KB",
        _             => $"{SizeBytes / 1048576.0:F2} MB"
    };
}

/// <summary>
/// Gesamtergebnis der Paket-Inspektion.
/// </summary>
public sealed class PackageInspectionResult
{
    public string              PackagePath      { get; set; } = "";
    public long                PackageSizeBytes { get; set; }
    public int                 FileCount        => Files.Count;
    public List<PackageFileEntry>    Files      { get; set; } = [];
    public List<ValidationIssue>     Issues     { get; set; } = [];

    public bool HasBlockers  => Issues.Exists(i => i.IsError);
    public bool IsClean      => !HasBlockers && Issues.All(i => !i.IsWarning);

    public int BlockerCount  => Files.Count(f => f.IsBlocker);
    public int WarningCount  => Files.Count(f => f.IsWarning);

    public string PackageSizeLabel => PackageSizeBytes switch
    {
        < 1024        => $"{PackageSizeBytes} B",
        < 1048576     => $"{PackageSizeBytes / 1024.0:F1} KB",
        _             => $"{PackageSizeBytes / 1048576.0:F2} MB"
    };
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Öffnet ein .aaiaext-Paket (ZIP), liest Metadaten und klassifiziert Inhalte.
/// Führt KEINE Ausführung durch — nur Lesen.
/// </summary>
public static class ExtensionPackageInspectorService
{
    private const long MaxFileSizeBytes    = 50L  * 1024 * 1024;  // 50 MB
    private const long MaxPackageSizeBytes = 100L * 1024 * 1024;  // 100 MB

    private static readonly HashSet<string> HardBlockedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".pem", ".pfx", ".key", ".p12", ".cer", ".der", ".crt" };

    private static readonly HashSet<string> HardBlockedNames =
        new(StringComparer.OrdinalIgnoreCase)
        { ".env", ".env.local", ".env.development", "appsettings.development.json" };

    private static readonly HashSet<string> SuspiciousConfigWords =
        new(StringComparer.OrdinalIgnoreCase)
        { "secret", "password", "credential", "apikey", "api_key", "connectionstring" };

    // ── Haupt-Methode ─────────────────────────────────────────────────────────

    public static PackageInspectionResult Inspect(string packagePath)
    {
        var result = new PackageInspectionResult
        {
            PackagePath      = packagePath,
            PackageSizeBytes = new FileInfo(packagePath).Length
        };

        // Paket-Gesamt-Größe
        if (result.PackageSizeBytes > MaxPackageSizeBytes)
            result.Issues.Add(Warning("Größe", "Paket sehr groß",
                $"Das Paket ist {result.PackageSizeLabel} groß. Empfohlen: unter 100 MB.\n" +
                "Prüfe ob unnötige Dateien enthalten sind."));

        using var zip = ZipFile.OpenRead(packagePath);

        int  manifestCount = 0;
        bool hasReadme     = false;
        bool hasLicense    = false;

        foreach (var entry in zip.Entries)
        {
            // Verzeichnis-Einträge überspringen
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var path      = entry.FullName.Replace('\\', '/');
            var name      = entry.Name;
            var nameLower = name.ToLowerInvariant();
            var ext       = System.IO.Path.GetExtension(name);
            var sizeBytes = entry.Length;

            // Manifest zählen
            if (nameLower == "aaia-manifest.json") manifestCount++;

            // Struktur-Tracking
            if (nameLower.StartsWith("readme")) hasReadme  = true;
            if (nameLower.StartsWith("license") || nameLower.StartsWith("licence"))
                hasLicense = true;

            var (category, riskHint, riskMessage) =
                ClassifyEntry(path, name, nameLower, ext, sizeBytes);

            result.Files.Add(new PackageFileEntry
            {
                Path      = path,
                Extension = ext,
                SizeBytes = sizeBytes,
                Category  = category,
                RiskHint  = riskHint
            });

            // Issues für Blocker/Warnungen erzeugen
            if (riskHint == "Blocker" && riskMessage is not null)
                result.Issues.Add(Error("Sicherheit",
                    $"Verbotene Datei: {name}", riskMessage));
            else if (riskHint == "Warning" && riskMessage is not null)
                result.Issues.Add(Warning("Inhalt",
                    $"Prüfen: {name}", riskMessage));

            // Größen-Blocker
            if (sizeBytes > MaxFileSizeBytes)
                result.Issues.Add(Error("Größe",
                    $"Datei zu groß: {name}",
                    $"'{path}' ist {new PackageFileEntry { SizeBytes = sizeBytes }.SizeLabel} " +
                    $"(Grenzwert: 50 MB). Große Binärdateien gehören nicht ins Paket."));
        }

        // ── Post-Scan-Checks ──────────────────────────────────────────────────

        if (manifestCount == 0)
            result.Issues.Insert(0, Error("Manifest", "Manifest fehlt im Paket",
                "aaia-manifest.json wurde nicht im Paket gefunden.\n" +
                "AAIAS kann dieses Paket nicht laden."));
        else if (manifestCount > 1)
            result.Issues.Insert(0, Error("Manifest",
                $"{manifestCount}× aaia-manifest.json gefunden",
                "Im Paket darf nur genau ein Manifest vorhanden sein."));

        if (!hasReadme)
            result.Issues.Add(Warning("Struktur", "README fehlt im Paket",
                "Ein README sollte im Paket enthalten sein (Marketplace-Anforderung)."));

        if (!hasLicense)
            result.Issues.Add(Warning("Struktur", "Lizenz fehlt im Paket",
                "Eine Lizenzdatei muss für Marketplace-Uploads enthalten sein."));

        // Dateien sortieren: Blocker → Warning → Normal
        result.Files.Sort((a, b) =>
            RiskOrder(a.RiskHint).CompareTo(RiskOrder(b.RiskHint)));

        return result;
    }

    // ── Klassifizierung ───────────────────────────────────────────────────────

    private static (string category, string riskHint, string? riskMessage)
        ClassifyEntry(string path, string name, string nameLower, string ext, long sizeBytes)
    {
        var extLower  = ext.ToLowerInvariant();
        var pathLower = path.ToLowerInvariant();

        // ── Harte Blocker ──────────────────────────────────────────────────────

        if (extLower == ".exe")
            return ("Suspicious", "Blocker",
                $"Ausführbare Datei '{path}' darf nicht im AAIA-Paket enthalten sein.");

        if (HardBlockedExtensions.Contains(extLower))
            return ("Suspicious", "Blocker",
                $"'{path}' ({extLower}) ist ein Zertifikat oder Schlüssel.\n" +
                "Solche Dateien dürfen nie in ein Paket gepackt werden.");

        if (HardBlockedNames.Contains(nameLower))
            return ("Suspicious", "Blocker",
                $"'{path}' ist eine verbotene Konfigurationsdatei.\n" +
                "Sie könnte Geheimnisse oder lokale Einstellungen enthalten.");

        // ── Warnungen ──────────────────────────────────────────────────────────

        if (extLower is ".so" or ".dylib")
            return ("NativeBinary", "Warning",
                $"Native Bibliothek '{path}' — plattformspezifisch.\n" +
                "Stelle sicher dass die Zielplattform bekannt ist.");

        // DLL außerhalb des lib/-Verzeichnisses
        if (extLower == ".dll" && !pathLower.StartsWith("lib/"))
            return ("Assembly", "Warning",
                $"'{path}' liegt außerhalb von lib/ — ungewöhnliche Stelle.\n" +
                "Assemblies sollten unter lib/ abgelegt sein.");

        // Config mit verdächtigem Namen
        if (extLower is ".json" or ".xml" or ".yaml" or ".yml" or ".config" or ".ini")
        {
            foreach (var word in SuspiciousConfigWords)
                if (nameLower.Contains(word))
                    return ("Config", "Warning",
                        $"'{path}' enthält '{word}' im Namen.\n" +
                        "Prüfe ob die Datei Geheimnisse enthält.");
        }

        // ── Normale Klassifizierung ────────────────────────────────────────────

        return (ClassifyNormal(pathLower, nameLower, extLower), "", null);
    }

    private static string ClassifyNormal(string pathLower, string nameLower, string extLower)
    {
        if (nameLower == "aaia-manifest.json") return "Manifest";

        if (nameLower.StartsWith("readme")) return "Documentation";
        if (nameLower.StartsWith("license") || nameLower.StartsWith("licence"))
            return "Documentation";

        return extLower switch
        {
            ".dll"                          => pathLower.StartsWith("lib/") ? "Assembly" : "Assembly",
            ".json" or ".xml"
                or ".yaml" or ".yml"
                or ".config" or ".ini"      => "Config",
            ".md" or ".txt"                 => "Documentation",
            ".png" or ".svg" or ".jpg"
                or ".jpeg" or ".ico"
                or ".gif" or ".webp"        => "Asset",
            ".so" or ".dylib"               => "NativeBinary",
            _                               => "Other"
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int RiskOrder(string risk) => risk switch
    {
        "Blocker" => 0,
        "Warning" => 1,
        _         => 2
    };

    private static ValidationIssue Error(string cat, string title, string msg) =>
        new() { Severity = "Error", Category = cat, Title = title, Message = msg };

    private static ValidationIssue Warning(string cat, string title, string msg) =>
        new() { Severity = "Warning", Category = cat, Title = title, Message = msg };
}
