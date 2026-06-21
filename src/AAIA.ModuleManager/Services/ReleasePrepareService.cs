using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Ergebnis der Release-Vorbereitung.
/// </summary>
public sealed class ReleaseResult
{
    public bool    Success         { get; set; }
    public string  ReleaseFolderPath { get; set; } = "";
    public string  ExtensionId     { get; set; } = "";
    public string  Version         { get; set; } = "";

    public List<ValidationIssue> Issues { get; set; } = [];
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Bereitet einen kontrollierten Release-Zustand vor.
///
/// Erzeugt:
///   dist/release/&lt;extension-id&gt;/&lt;version&gt;/
///     ├── aaia.&lt;id&gt;.&lt;version&gt;.aaiaext
///     ├── release-info.json
///     ├── inspection-report.json
///     ├── README.md       (optional)
///     ├── CHANGELOG.md    (optional)
///     └── LICENSE         (optional)
///
/// Keine Signatur — Phase 4.
/// MarketplaceReady bleibt false, solange IsSigned false ist.
/// </summary>
public static class ReleasePrepareService
{
    public static async Task<ReleaseResult> PrepareAsync(
        string                  projectDir,
        PackageResult           packageResult,
        PackageInspectionResult inspectionResult,
        string                  developerEtwId   = "",
        CancellationToken       ct               = default)
    {
        var result = new ReleaseResult();

        // ── Manifest lesen ────────────────────────────────────────────────────

        var manifestPath = Path.Combine(projectDir, "aaia-manifest.json");
        var (id, version) = ReadManifestIdVersion(manifestPath);

        result.ExtensionId = id;
        result.Version     = version;

        // ── Guards ────────────────────────────────────────────────────────────

        if (!packageResult.Success || packageResult.PackagePath is null)
        {
            result.Issues.Add(Error("Release",
                "Kein gültiges Paket vorhanden",
                "Erstelle zuerst ein gültiges .aaiaext-Paket."));
            return result;
        }

        if (inspectionResult.HasBlockers)
        {
            result.Issues.Add(Error("Release",
                "Paket enthält Blocker",
                "Löse alle Blocker in der Paketprüfung bevor ein Release vorbereitet werden kann."));
            return result;
        }

        // ── Release-Ordner ────────────────────────────────────────────────────

        var releaseDir = Path.Combine(projectDir, "dist", "release", id, version);
        Directory.CreateDirectory(releaseDir);

        result.ReleaseFolderPath = releaseDir;

        // ── .aaiaext kopieren ─────────────────────────────────────────────────

        var pkgDest = Path.Combine(releaseDir, packageResult.PackageName);
        File.Copy(packageResult.PackagePath, pkgDest, overwrite: true);

        // ── release-info.json ─────────────────────────────────────────────────

        var releaseInfo = new
        {
            releaseFormatVersion = "1.0",
            extensionId          = id,
            extensionVersion     = version,
            packageName          = packageResult.PackageName,
            packageHash          = packageResult.PackageHash,
            manifestHash         = packageResult.ManifestHash,
            createdAtUtc         = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            createdBy            = "AAIA Module Manager",
            moduleManagerVersion = GitHubUpdateService.CurrentVersion,
            developerEtwId       = developerEtwId,
            isSigned             = false,
            signatureRequired    = true,
            signaturePhase       = "Phase4Pending",
            inspectionStatus     = inspectionResult.HasBlockers ? "Blocked"
                                 : inspectionResult.WarningCount > 0 ? "PassedWithWarnings"
                                 : "Passed",
            fileCount            = inspectionResult.FileCount,
            blockerCount         = inspectionResult.BlockerCount,
            warningCount         = inspectionResult.WarningCount,
            packageSizeBytes     = inspectionResult.PackageSizeBytes,
            marketplaceReady     = false,
            note                 = "marketplaceReady wird erst nach erfolgreicher Signierung auf true gesetzt (Phase 4)."
        };

        var releaseInfoPath = Path.Combine(releaseDir, "release-info.json");
        await File.WriteAllTextAsync(releaseInfoPath,
            JsonSerializer.Serialize(releaseInfo,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        // ── inspection-report.json ────────────────────────────────────────────

        var reportEntries = inspectionResult.Files.ConvertAll(f => new
        {
            path      = f.Path,
            extension = f.Extension,
            sizeBytes = f.SizeBytes,
            category  = f.Category,
            riskHint  = f.RiskHint
        });

        var reportIssues = inspectionResult.Issues.ConvertAll(i => new
        {
            severity = i.Severity,
            category = i.Category,
            title    = i.Title,
            message  = i.Message
        });

        var report = new
        {
            generatedAtUtc   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            packagePath      = packageResult.PackagePath,
            packageSizeBytes = inspectionResult.PackageSizeBytes,
            fileCount        = inspectionResult.FileCount,
            blockerCount     = inspectionResult.BlockerCount,
            warningCount     = inspectionResult.WarningCount,
            files            = reportEntries,
            issues           = reportIssues
        };

        var reportPath = Path.Combine(releaseDir, "inspection-report.json");
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        // ── Dokumentation kopieren ────────────────────────────────────────────

        foreach (var doc in new[] { "README.md", "README.txt", "CHANGELOG.md", "LICENSE", "LICENSE.md", "LICENSE.txt" })
        {
            var src = Path.Combine(projectDir, doc);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(releaseDir, doc), overwrite: true);
        }

        result.Success = true;
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string id, string version) ReadManifestIdVersion(string path)
    {
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            var id   = root?["id"]?.GetValue<string>()      ?? "aaia.module.unknown";
            var ver  = root?["version"]?.GetValue<string>() ?? "0.1.0";
            return (id, ver);
        }
        catch { return ("aaia.module.unknown", "0.1.0"); }
    }

    private static ValidationIssue Error(string cat, string title, string msg) =>
        new() { Severity = "Error", Category = cat, Title = title, Message = msg };
}
