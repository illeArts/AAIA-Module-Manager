using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Ergebnis einer Paket-Erstellung.
/// </summary>
public sealed class PackageResult
{
    public bool              Success       { get; set; }
    public string?           PackagePath   { get; set; }
    public string            PackageName   { get; set; } = "";
    public long              SizeBytes     { get; set; }
    public string            PackageHash   { get; set; } = "";
    public string            ManifestHash  { get; set; } = "";
    public List<string>      IncludedFiles { get; set; } = [];
    public List<ValidationIssue> Issues    { get; set; } = [];

    public string SizeLabel => SizeBytes switch
    {
        < 1024            => $"{SizeBytes} B",
        < 1024 * 1024     => $"{SizeBytes / 1024.0:F1} KB",
        _                 => $"{SizeBytes / (1024.0 * 1024):F2} MB"
    };
}

/// <summary>
/// Erstellt aus dem Build-Output eines AAIA-Projekts ein .aaiaext-Paket (ZIP).
///
/// Finale ZIP-Struktur:
///   aaia-manifest.json
///   README.md / LICENSE / icon.png   (Root)
///   lib/net8.0/*.dll                 (Assemblies aus Build-Output)
///   package/package-info.json        (Paket-Metadaten, signaturbereit)
///
/// Schließt Secrets, IDE-Reste und verbotene Dateien aus.
/// Paket wird nur erstellt wenn keine Blocker vorhanden sind.
/// </summary>
public static class ExtensionPackageService
{
    // Verzeichnisse die NIE ins Paket kommen
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "obj", "node_modules", ".github", "packages", "test", "tests"
    };

    // Erweiterungen die nie ins Paket dürfen
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",                        // Debug-Symbole
        ".exe",                        // Ausführbare Dateien — BLOCKER
        ".user", ".suo",               // IDE-Einstellungen
        ".pfx", ".pem", ".key",        // Zertifikate/Schlüssel
        ".p12", ".cer", ".der", ".crt",
        ".env", ".log"
    };

    // Dateinamen die nie ins Paket dürfen
    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.Development.json",
        "appsettings.Local.json",
        ".env", ".env.local", ".env.development",
        "secrets.json", "usersecrets.json"
    };

    // ── Haupt-Methode ─────────────────────────────────────────────────────────

    /// <summary>
    /// Erstellt ein .aaiaext-Paket aus dem Build-Output.
    /// </summary>
    public static async Task<PackageResult> CreatePackageAsync(
        string            projectDir,
        NewProjectType    projectType      = NewProjectType.ServerModule,
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

        // ── Manifest lesen ────────────────────────────────────────────────────

        var manifestPath = Path.Combine(projectDir, "aaia-manifest.json");
        var (moduleId, version) = ReadManifestIdVersion(manifestPath);

        // Paketname: <id>.<version>.aaiaext  (z.B. aaia.fritzbox.module.1.0.0.aaiaext)
        var safeName    = SanitizeFileName(moduleId);
        var safeVersion = SanitizeFileName(version);
        var packageName = $"{safeName}.{safeVersion}.aaiaext";

        // ── Ausgabe-Ordner sicherstellen, alte Pakete für diese ID bereinigen ─

        Directory.CreateDirectory(packageOutputDir);
        CleanOldPackages(packageOutputDir, safeName);

        var packagePath = Path.Combine(packageOutputDir, packageName);

        // ── Build-Output suchen ───────────────────────────────────────────────
        // Reihenfolge: publish/ → bin/Release → bin/Debug

        var buildOutputDir = FindBuildOutput(projectDir, projectType);
        if (buildOutputDir is null)
        {
            result.Success = false;
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Paket",
                Title    = "Kein Build-Output gefunden",
                Message  = "Keine Build-Artefakte in bin/ oder publish/ gefunden.\n" +
                           "Führe zuerst 'dotnet build' aus.",
                Actions  = [new() { Label = "Projekt bauen", ActionId = "build-project", IsAutomatic = true }]
            });
            return result;
        }

        // ── Verbotene Dateien im Build-Output vorab prüfen ────────────────────

        var exeFiles = Directory.EnumerateFiles(buildOutputDir, "*.exe", SearchOption.AllDirectories).ToList();
        if (exeFiles.Count > 0)
        {
            result.Success = false;
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Sicherheit",
                Title    = $"{exeFiles.Count} ausführbare Datei(en) im Build-Output",
                Message  = ".exe-Dateien dürfen nicht in einem AAIA-Paket enthalten sein.\n" +
                           "Prüfe das Projektziel — AAIA-Erweiterungen sind Class Library, keine Konsolenanwendungen."
            });
            return result;
        }

        // ── Paket erstellen ───────────────────────────────────────────────────

        try
        {
            using var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create);

            // 1. aaia-manifest.json (immer zuerst)
            zip.CreateEntryFromFile(manifestPath, "aaia-manifest.json");
            result.IncludedFiles.Add("aaia-manifest.json");

            // 2. Dokumentation aus Projektroot
            foreach (var candidate in new[] { "README.md", "README.txt", "CHANGELOG.md", "LICENSE", "LICENSE.md", "LICENSE.txt" })
            {
                var path = Path.Combine(projectDir, candidate);
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, candidate);
                    result.IncludedFiles.Add(candidate);
                }
            }

            // 3. Icon aus Projektroot (maximal eines)
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

            // 4. Assemblies aus Build-Output → lib/<tfm>/
            var tfm = Path.GetFileName(buildOutputDir);  // z.B. "net8.0"
            foreach (var file in GetPackageableFiles(buildOutputDir))
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(buildOutputDir, file);
                var entryName    = $"lib/{tfm}/{relativePath.Replace('\\', '/')}";
                zip.CreateEntryFromFile(file, entryName);
                result.IncludedFiles.Add(entryName);
            }

            // 5. package/package-info.json (Paket-Metadaten für Phase 4)
            var packageInfo = BuildPackageInfo(moduleId, version, projectType);
            var infoEntry   = zip.CreateEntry("package/package-info.json",
                                              CompressionLevel.Optimal);
            using (var w = new StreamWriter(infoEntry.Open(), Encoding.UTF8))
                await w.WriteAsync(packageInfo);
            result.IncludedFiles.Add("package/package-info.json");
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
        result.PackageName  = packageName;
        result.SizeBytes    = new FileInfo(packagePath).Length;
        result.PackageHash  = await ComputeSha256Async(packagePath, ct);
        result.ManifestHash = await ComputeSha256Async(manifestPath, ct);
        result.Success      = true;

        return result;
    }

    // ── package-info.json ─────────────────────────────────────────────────────

    private static string BuildPackageInfo(string id, string version, NewProjectType projectType)
    {
        var info = new
        {
            packageFormat        = "aaiaext",
            packageFormatVersion = "1.0",
            extensionId          = id,
            extensionVersion     = version,
            sourceProjectType    = projectType.ToString(),
            createdAtUtc         = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            createdBy            = "AAIA Module Manager",
            moduleManagerVersion = GitHubUpdateService.CurrentVersion,
            isSigned             = false,
            signaturePhase       = "Phase4Pending",
            note                 = "Signatur wird in Phase 4 hinzugefügt. Nicht für Marketplace-Upload verwenden."
        };

        return JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
    }

    // ── Pre-Checks ────────────────────────────────────────────────────────────

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

    // ── Build-Output suchen ───────────────────────────────────────────────────

    private static string? FindBuildOutput(string projectDir, NewProjectType projectType)
    {
        // Bei HybridModule: Server-Projekt bevorzugen
        var searchRoot = projectType == NewProjectType.HybridModule
            ? Path.Combine(projectDir, "Server")
            : projectDir;

        if (!Directory.Exists(searchRoot)) searchRoot = projectDir;

        // 1. publish/-Output bevorzugen (dotnet publish)
        var publishDir = Path.Combine(searchRoot, "publish");
        if (Directory.Exists(publishDir) &&
            Directory.GetFiles(publishDir, "*.dll").Length > 0)
            return publishDir;

        // 2. bin/Release/<tfm>/publish/
        var releasePublish = FindInBin(searchRoot, "Release", subDir: "publish");
        if (releasePublish is not null) return releasePublish;

        // 3. bin/Release/<tfm>/
        var release = FindInBin(searchRoot, "Release");
        if (release is not null) return release;

        // 4. bin/Debug/<tfm>/
        return FindInBin(searchRoot, "Debug");
    }

    private static string? FindInBin(string root, string config, string? subDir = null)
    {
        var binDir = Path.Combine(root, "bin", config);
        if (!Directory.Exists(binDir)) return null;

        foreach (var tfmDir in Directory.EnumerateDirectories(binDir)
                     .Where(d => Path.GetFileName(d).StartsWith("net", StringComparison.OrdinalIgnoreCase)))
        {
            var target = subDir is null ? tfmDir : Path.Combine(tfmDir, subDir);
            if (Directory.Exists(target) &&
                Directory.GetFiles(target, "*.dll").Length > 0)
                return target;
        }
        return null;
    }

    // ── Dateien für Paket auswählen ───────────────────────────────────────────

    private static IEnumerable<string> GetPackageableFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ext      = Path.GetExtension(file);
            var fileName = Path.GetFileName(file);

            if (ExcludedExtensions.Contains(ext))     continue;
            if (ExcludedFileNames.Contains(fileName)) continue;

            yield return file;
        }
    }

    // ── Alte Pakete bereinigen ────────────────────────────────────────────────

    private static void CleanOldPackages(string outputDir, string moduleId)
    {
        // Alle .aaiaext-Dateien mit derselben Modul-ID löschen
        foreach (var old in Directory.EnumerateFiles(outputDir, "*.aaiaext")
                     .Where(f => Path.GetFileName(f).StartsWith(moduleId,
                                     StringComparison.OrdinalIgnoreCase)))
        {
            try { File.Delete(old); } catch { /* Löschen auf Best-Effort-Basis */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLower();
    }
}
