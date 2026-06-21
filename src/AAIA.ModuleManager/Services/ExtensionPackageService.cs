using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Ergebnis einer Paket-Erstellung.
/// </summary>
public sealed class PackageResult
{
    public bool              Success      { get; set; }
    public string?           PackagePath  { get; set; }
    public long              SizeBytes    { get; set; }
    public string            PackageHash  { get; set; } = "";
    public string            ManifestHash { get; set; } = "";
    public List<string>      IncludedFiles { get; set; } = [];
    public List<ValidationIssue> Issues   { get; set; } = [];

    public string SizeLabel => SizeBytes switch
    {
        < 1024            => $"{SizeBytes} B",
        < 1024 * 1024     => $"{SizeBytes / 1024.0:F1} KB",
        _                 => $"{SizeBytes / (1024.0 * 1024):F2} MB"
    };
}

/// <summary>
/// Erstellt aus dem Build-Output eines AAIA-Projekts ein .aaiaext-Paket (ZIP).
/// Schließt Secrets, Build-Artefakt-Quellen und verbotene Dateien aus.
/// Paket wird nur erstellt wenn keine Blocker vorhanden sind.
/// </summary>
public static class ExtensionPackageService
{
    // Dateien/Ordner die NIE ins Paket dürfen (Quell-Ordner)
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "obj", "node_modules", ".github"
    };

    // Dateiendungen die nie ins Paket dürfen
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",   // Debug-Symbole
        ".user",  ".suo",           // IDE-Einstellungen
        ".pfx",   ".pem", ".key",   // Zertifikate/Schlüssel
        ".p12",   ".cer", ".der",
        ".env",
        ".log"
    };

    // Dateien die nie ins Paket dürfen (Dateiname exakt)
    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.Development.json",
        "appsettings.Local.json",
        ".env",
        ".env.local",
        ".env.development",
        "secrets.json",
        "usersecrets.json"
    };

    /// <summary>
    /// Erstellt ein .aaiaext-Paket aus dem Build-Output.
    /// packageOutputDir: Zielordner für das Paket (z. B. ein /packages-Unterordner)
    /// </summary>
    public static async Task<PackageResult> CreatePackageAsync(
        string            projectDir,
        string?           packageOutputDir = null,
        CancellationToken ct               = default)
    {
        var result = new PackageResult();
        packageOutputDir ??= Path.Combine(projectDir, "packages");

        // ── Pre-Checks ────────────────────────────────────────────────────────

        var blockers = RunPreChecks(projectDir);
        if (blockers.Count > 0)
        {
            result.Success = false;
            result.Issues  = blockers;
            return result;
        }

        // ── Manifest lesen für Dateinamen ─────────────────────────────────────

        var manifestPath = Path.Combine(projectDir, "aaia-manifest.json");
        var (moduleId, version) = ReadManifestIdVersion(manifestPath);
        var safeName    = SanitizeFileName(moduleId);
        var safeVersion = SanitizeFileName(version);
        var packageName = $"{safeName}-{safeVersion}.aaiaext";

        // ── Ausgabe-Ordner sicherstellen ──────────────────────────────────────

        Directory.CreateDirectory(packageOutputDir);
        var packagePath = Path.Combine(packageOutputDir, packageName);

        if (File.Exists(packagePath)) File.Delete(packagePath);

        // ── Build-Output suchen ───────────────────────────────────────────────
        // Bevorzugt: bin/Release/net8.0/ oder bin/Debug/net8.0/

        var buildOutputDir = FindBuildOutput(projectDir);
        if (buildOutputDir is null)
        {
            result.Success = false;
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Paket",
                Title    = "Kein Build-Output gefunden",
                Message  = "Es wurden keine Build-Artefakte in bin/ gefunden.\n" +
                           "Führe zuerst 'dotnet build' aus.",
                Actions  = [new() { Label = "Projekt bauen", ActionId = "build-project", IsAutomatic = true }]
            });
            return result;
        }

        // ── Paket erstellen ───────────────────────────────────────────────────

        try
        {
            using var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create);

            // 1. aaia-manifest.json immer aus Projektroot
            zip.CreateEntryFromFile(manifestPath, "aaia-manifest.json");
            result.IncludedFiles.Add("aaia-manifest.json");

            // 2. README + LICENSE aus Projektroot (optional)
            foreach (var candidate in new[] { "README.md", "README.txt", "LICENSE", "LICENSE.md" })
            {
                var path = Path.Combine(projectDir, candidate);
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, candidate);
                    result.IncludedFiles.Add(candidate);
                }
            }

            // 3. Icon aus Projektroot
            foreach (var icon in new[] { "icon.png", "icon.svg", "icon.jpg", "Icon.png" })
            {
                var path = Path.Combine(projectDir, icon);
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, icon);
                    result.IncludedFiles.Add(icon);
                    break;
                }
            }

            // 4. Alle Dateien aus dem Build-Output
            foreach (var file in GetPackageableFiles(buildOutputDir))
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(buildOutputDir, file);
                var entryName    = "lib/" + relativePath.Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName);
                result.IncludedFiles.Add(entryName);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Paket",
                Title    = "Paket-Erstellung fehlgeschlagen",
                Message  = ex.Message
            });
            if (File.Exists(packagePath)) File.Delete(packagePath);
            return result;
        }

        // ── Hashes berechnen ──────────────────────────────────────────────────

        result.PackagePath  = packagePath;
        result.SizeBytes    = new FileInfo(packagePath).Length;
        result.PackageHash  = await ComputeSha256Async(packagePath, ct);
        result.ManifestHash = await ComputeSha256Async(manifestPath, ct);
        result.Success      = true;

        return result;
    }

    // ── Pre-Checks ─────────────────────────────────────────────────────────────

    private static List<ValidationIssue> RunPreChecks(string projectDir)
    {
        var issues = new List<ValidationIssue>();

        if (!File.Exists(Path.Combine(projectDir, "aaia-manifest.json")))
            issues.Add(new ValidationIssue
            {
                Severity = "Error", Category = "Paket",
                Title    = "Manifest fehlt",
                Message  = "aaia-manifest.json muss vorhanden sein bevor ein Paket erstellt werden kann.",
                Actions  = [new() { Label = "Manifest erzeugen", ActionId = "create-manifest", IsAutomatic = true }]
            });

        // Secret-Scan
        issues.AddRange(SecretScanService.Scan(projectDir));

        return issues;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindBuildOutput(string projectDir)
    {
        // Suche: bin/Release zuerst, dann bin/Debug
        foreach (var config in new[] { "Release", "Debug" })
        {
            var binDir = Path.Combine(projectDir, "bin", config);
            if (!Directory.Exists(binDir)) continue;

            // Ersten net*-Unterordner finden
            var tfmDir = Directory.EnumerateDirectories(binDir)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("net", StringComparison.OrdinalIgnoreCase));

            if (tfmDir is not null && Directory.GetFiles(tfmDir, "*.dll").Length > 0)
                return tfmDir;
        }
        return null;
    }

    private static IEnumerable<string> GetPackageableFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ext      = Path.GetExtension(file);
            var fileName = Path.GetFileName(file);

            if (ExcludedExtensions.Contains(ext))       continue;
            if (ExcludedFileNames.Contains(fileName))   continue;

            yield return file;
        }
    }

    private static (string id, string version) ReadManifestIdVersion(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            var id   = root?["id"]?.GetValue<string>()      ?? "aaia.module.unknown";
            var ver  = root?["version"]?.GetValue<string>() ?? "0.1.0";
            return (id, ver);
        }
        catch { return ("aaia.module.unknown", "0.1.0"); }
    }

    private static string SanitizeFileName(string name)
        => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha  = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLower();
    }
}
