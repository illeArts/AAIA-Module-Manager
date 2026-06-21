using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Ergebnis der Signaturvorbereitung oder -Prüfung.
/// </summary>
public sealed class SignatureResult
{
    public bool              Success           { get; set; }
    public string            TrustLevel        { get; set; } = "Unsigned";
    public string?           SignatureInfoPath  { get; set; }

    /// <summary>Hashes die in signature-info.json gespeichert wurden.</summary>
    public string PackageSha256           { get; set; } = "";
    public string ReleaseInfoSha256       { get; set; } = "";
    public string InspectionReportSha256  { get; set; } = "";

    public List<ValidationIssue> Issues   { get; set; } = [];
}

/// <summary>
/// Ergebnis der Manipulationsprüfung.
/// </summary>
public sealed class VerificationResult
{
    public bool   IsIntact             { get; set; }
    public string TrustLevel           { get; set; } = "Unsigned";
    public bool   PackageIntact        { get; set; }
    public bool   ReleaseInfoIntact    { get; set; }
    public bool   InspectionIntact     { get; set; }
    public string Summary              { get; set; } = "";
    public List<ValidationIssue> Issues { get; set; } = [];
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Phase 4.0 — Hash-basierte Signaturvorbereitung.
///
/// Noch keine echte kryptografische Developer-/Marketplace-Signatur.
/// Erzeugt: signature-info.json mit SHA256-Hashes der Release-Dateien.
/// Aktualisiert: release-info.json → signaturePrepared: true, trustLevel: LocalHashPrepared.
/// Prüft: Manipulation nach Signaturvorbereitung (VerifySignaturePreparationAsync).
///
/// Trust-Levels:
///   Unsigned            → kein Release vorbereitet
///   LocalHashPrepared   → Phase 4.0: Hashes berechnet und gespeichert
///   DeveloperSigned     → Phase 4.1: echte ETW-Signatur (noch nicht implementiert)
///   MarketplaceVerified → Phase 5: Marketplace-Prüfung (noch nicht implementiert)
///   Blocked             → Blocker vorhanden
/// </summary>
public static class ReleaseSignatureService
{
    // ── Signaturvorbereitung ──────────────────────────────────────────────────

    public static async Task<SignatureResult> PrepareSignatureAsync(
        string            releaseDir,
        string            developerEtwId  = "",
        CancellationToken ct              = default)
    {
        var result = new SignatureResult();

        // ── Release-Dateien suchen ────────────────────────────────────────────

        var packagePath        = FindPackageFile(releaseDir);
        var releaseInfoPath    = Path.Combine(releaseDir, "release-info.json");
        var inspectionPath     = Path.Combine(releaseDir, "inspection-report.json");
        var signatureInfoPath  = Path.Combine(releaseDir, "signature-info.json");

        var missing = new List<string>();
        if (packagePath        is null)               missing.Add(".aaiaext-Paket");
        if (!File.Exists(releaseInfoPath))            missing.Add("release-info.json");
        if (!File.Exists(inspectionPath))             missing.Add("inspection-report.json");

        if (missing.Count > 0)
        {
            result.Issues.Add(Error("Signatur",
                "Fehlende Release-Dateien",
                "Folgende Dateien wurden nicht gefunden:\n" +
                string.Join("\n", missing.ConvertAll(m => $"• {m}")) + "\n\n" +
                "Stelle sicher dass Phase 3.3 (Release vorbereiten) abgeschlossen ist."));
            return result;
        }

        // ── Manifest-Daten lesen ──────────────────────────────────────────────

        var (extensionId, version) = ReadReleaseInfo(releaseInfoPath);

        // ── SHA256-Hashes berechnen ───────────────────────────────────────────

        var packageHash    = await ComputeSha256Async(packagePath!, ct);
        var releaseHash    = await ComputeSha256Async(releaseInfoPath, ct);
        var inspectionHash = await ComputeSha256Async(inspectionPath, ct);

        result.PackageSha256          = packageHash;
        result.ReleaseInfoSha256      = releaseHash;
        result.InspectionReportSha256 = inspectionHash;

        // ── signature-info.json erzeugen ──────────────────────────────────────

        var signatureInfo = new
        {
            signatureFormatVersion   = "1.0",
            extensionId              = extensionId,
            extensionVersion         = version,
            developerEtwId           = developerEtwId,
            preparedAtUtc            = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            algorithm                = "SHA256",
            packageSha256            = packageHash,
            releaseInfoSha256        = releaseHash,
            inspectionReportSha256   = inspectionHash,
            signatureMode            = "LocalDevelopment",
            trustLevel               = "LocalHashPrepared",
            isCryptographicallySigned = false,
            note                     = "Echte kryptografische Signatur folgt in Phase 4.1. " +
                                       "Diese Datei dient zur Manipulationserkennung zwischen Erstellung und Upload."
        };

        await File.WriteAllTextAsync(signatureInfoPath,
            JsonSerializer.Serialize(signatureInfo,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        // ── release-info.json aktualisieren ───────────────────────────────────

        await UpdateReleaseInfoAsync(releaseInfoPath,
            signaturePrepared: true,
            signaturePhase:    "LocalHashPrepared",
            trustLevel:        "LocalHashPrepared",
            ct:                ct);

        result.Success          = true;
        result.TrustLevel       = "LocalHashPrepared";
        result.SignatureInfoPath = signatureInfoPath;

        return result;
    }

    // ── Manipulationsprüfung ──────────────────────────────────────────────────

    public static async Task<VerificationResult> VerifySignaturePreparationAsync(
        string            releaseDir,
        CancellationToken ct          = default)
    {
        var result = new VerificationResult();

        var signatureInfoPath  = Path.Combine(releaseDir, "signature-info.json");
        var releaseInfoPath    = Path.Combine(releaseDir, "release-info.json");
        var inspectionPath     = Path.Combine(releaseDir, "inspection-report.json");
        var packagePath        = FindPackageFile(releaseDir);

        if (!File.Exists(signatureInfoPath))
        {
            result.Issues.Add(Error("Prüfung",
                "Keine Signaturvorbereitung gefunden",
                "signature-info.json existiert nicht. Führe zuerst 'Signatur vorbereiten' aus."));
            result.Summary = "Nicht geprüft — keine Signaturvorbereitung vorhanden.";
            return result;
        }

        // ── Gespeicherte Hashes lesen ─────────────────────────────────────────

        JsonNode? sigInfo;
        try { sigInfo = JsonNode.Parse(await File.ReadAllTextAsync(signatureInfoPath, ct)); }
        catch
        {
            result.Issues.Add(Error("Prüfung", "signature-info.json nicht lesbar",
                "Die Signaturdatei ist beschädigt oder ungültig."));
            result.Summary = "Prüfung fehlgeschlagen — Signaturdatei beschädigt.";
            return result;
        }

        var savedPackageHash    = sigInfo?["packageSha256"]?.GetValue<string>()          ?? "";
        var savedReleaseHash    = sigInfo?["releaseInfoSha256"]?.GetValue<string>()      ?? "";
        var savedInspectionHash = sigInfo?["inspectionReportSha256"]?.GetValue<string>() ?? "";
        result.TrustLevel       = sigInfo?["trustLevel"]?.GetValue<string>()             ?? "Unsigned";

        // ── Aktuelle Hashes berechnen ─────────────────────────────────────────

        // Paket
        if (packagePath is not null && File.Exists(packagePath))
        {
            var currentHash         = await ComputeSha256Async(packagePath, ct);
            result.PackageIntact    = string.Equals(currentHash, savedPackageHash, StringComparison.OrdinalIgnoreCase);
            if (!result.PackageIntact)
                result.Issues.Add(Error("Integrität", "Paket wurde verändert",
                    $"SHA256 stimmt nicht überein.\n" +
                    $"Erwartet: {savedPackageHash[..16]}…\n" +
                    $"Aktuell:  {currentHash[..16]}…"));
        }
        else
        {
            result.Issues.Add(Error("Integrität", "Paketdatei nicht gefunden",
                "Das .aaiaext-Paket wurde aus dem Release-Ordner entfernt."));
        }

        // release-info.json
        if (File.Exists(releaseInfoPath))
        {
            var currentHash           = await ComputeSha256Async(releaseInfoPath, ct);
            result.ReleaseInfoIntact  = string.Equals(currentHash, savedReleaseHash, StringComparison.OrdinalIgnoreCase);
            if (!result.ReleaseInfoIntact)
                result.Issues.Add(Warning("Integrität", "release-info.json wurde verändert",
                    "Der Release-Bericht wurde nach der Signaturvorbereitung modifiziert."));
        }

        // inspection-report.json
        if (File.Exists(inspectionPath))
        {
            var currentHash           = await ComputeSha256Async(inspectionPath, ct);
            result.InspectionIntact   = string.Equals(currentHash, savedInspectionHash, StringComparison.OrdinalIgnoreCase);
            if (!result.InspectionIntact)
                result.Issues.Add(Warning("Integrität", "inspection-report.json wurde verändert",
                    "Der Inspektionsbericht wurde nach der Signaturvorbereitung modifiziert."));
        }

        result.IsIntact = result.PackageIntact && result.ReleaseInfoIntact && result.InspectionIntact;

        result.Summary = result.IsIntact
            ? "✅ Alle Dateien unverändert — Release-Ordner integer."
            : $"⛔ {result.Issues.Count} Problem(e) gefunden — Release-Ordner möglicherweise manipuliert.";

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindPackageFile(string releaseDir)
        => Directory.GetFiles(releaseDir, "*.aaiaext").Length > 0
            ? Directory.GetFiles(releaseDir, "*.aaiaext")[0]
            : null;

    private static (string id, string version) ReadReleaseInfo(string releaseInfoPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(releaseInfoPath));
            return (root?["extensionId"]?.GetValue<string>()      ?? "aaia.module.unknown",
                    root?["extensionVersion"]?.GetValue<string>() ?? "0.1.0");
        }
        catch { return ("aaia.module.unknown", "0.1.0"); }
    }

    private static async Task UpdateReleaseInfoAsync(
        string  path,
        bool    signaturePrepared,
        string  signaturePhase,
        string  trustLevel,
        CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, ct);
            var node = JsonNode.Parse(text)?.AsObject();
            if (node is null) return;

            node["signaturePrepared"] = signaturePrepared;
            node["signaturePhase"]    = signaturePhase;
            node["trustLevel"]        = trustLevel;
            node["marketplaceReady"]  = false;  // bleibt false bis Phase 5
            node["isSigned"]          = false;  // bleibt false bis echte Signatur

            await File.WriteAllTextAsync(path,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                ct);
        }
        catch { /* Best-Effort */ }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLower();
    }

    private static ValidationIssue Error(string cat, string title, string msg) =>
        new() { Severity = "Error", Category = cat, Title = title, Message = msg };

    private static ValidationIssue Warning(string cat, string title, string msg) =>
        new() { Severity = "Warning", Category = cat, Title = title, Message = msg };
}
