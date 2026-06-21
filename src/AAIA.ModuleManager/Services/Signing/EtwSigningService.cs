using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Services.Signing;

/// <summary>
/// Phase 4.1 — ETW-Entwicklersignatur.
///
/// Liest die SHA256-Hashes aus Phase 4.0 (signature-info.json),
/// bildet einen kanonischen Payload, signiert ihn mit dem lokalen RSA-Schlüssel
/// des ETW und erweitert signature-info.json um die kryptografische Signatur.
///
/// Trust-Level danach: EtwLocalSigned.
/// isCryptographicallySigned: true.
/// marketplaceReady: weiterhin false bis Phase 5.
///
/// Kanonischer Payload (UTF-8, zeilengetrennt):
///   etw-signature-v1
///   extensionId:{id}
///   extensionVersion:{ver}
///   developerEtwId:{etwId}
///   packageSha256:{hash}
///   releaseInfoSha256:{hash}
///   inspectionReportSha256:{hash}
///   signedAtUtc:{ts}
/// </summary>
public static class EtwSigningService
{
    // ── Kanonischer Payload ───────────────────────────────────────────────────

    internal static string BuildCanonicalPayload(
        string extensionId,
        string extensionVersion,
        string developerEtwId,
        string packageSha256,
        string releaseInfoSha256,
        string inspectionReportSha256,
        string signedAtUtc)
        => $"etw-signature-v1\n"                                +
           $"extensionId:{extensionId}\n"                       +
           $"extensionVersion:{extensionVersion}\n"             +
           $"developerEtwId:{developerEtwId}\n"                 +
           $"packageSha256:{packageSha256}\n"                   +
           $"releaseInfoSha256:{releaseInfoSha256}\n"           +
           $"inspectionReportSha256:{inspectionReportSha256}\n" +
           $"signedAtUtc:{signedAtUtc}\n";

    // ── SignAsync ─────────────────────────────────────────────────────────────

    public static async Task<EtwSigningResult> SignAsync(
        string            releaseDir,
        string            developerEtwId,
        string            developerDisplayName,
        CancellationToken ct = default)
    {
        var result = new EtwSigningResult { EtwId = developerEtwId, DisplayName = developerDisplayName };

        // ── Voraussetzungen ───────────────────────────────────────────────────

        if (string.IsNullOrWhiteSpace(developerEtwId))
        {
            result.Issues.Add(Err("ETW-Signatur",
                "Keine ETW-ID konfiguriert",
                "Trage unter Einstellungen eine ETW-Entwickler-ID ein."));
            return result;
        }

        var sigInfoPath = Path.Combine(releaseDir, "signature-info.json");
        if (!File.Exists(sigInfoPath))
        {
            result.Issues.Add(Err("ETW-Signatur",
                "Keine Hash-Vorbereitung gefunden",
                "Führe zuerst 'Signatur vorbereiten' (Phase 4.0) aus."));
            return result;
        }

        // ── Hashes aus Phase-4.0-signature-info lesen ────────────────────────

        JsonNode? sigInfo;
        try { sigInfo = JsonNode.Parse(await File.ReadAllTextAsync(sigInfoPath, ct)); }
        catch
        {
            result.Issues.Add(Err("ETW-Signatur",
                "signature-info.json nicht lesbar",
                "Die Signaturdatei ist beschädigt oder kein gültiges JSON."));
            return result;
        }

        var extensionId             = sigInfo?["extensionId"]?.GetValue<string>()             ?? "";
        var extensionVersion        = sigInfo?["extensionVersion"]?.GetValue<string>()        ?? "";
        var packageSha256           = sigInfo?["packageSha256"]?.GetValue<string>()           ?? "";
        var releaseInfoSha256       = sigInfo?["releaseInfoSha256"]?.GetValue<string>()       ?? "";
        var inspectionReportSha256  = sigInfo?["inspectionReportSha256"]?.GetValue<string>()  ?? "";
        var preparedAtUtc           = sigInfo?["preparedAtUtc"]?.GetValue<string>()           ?? "";

        if (string.IsNullOrEmpty(packageSha256))
        {
            result.Issues.Add(Err("ETW-Signatur",
                "Hashes fehlen in signature-info.json",
                "Führe Phase 4.0 'Signatur vorbereiten' erneut aus."));
            return result;
        }

        // ── RSA-Schlüssel laden ───────────────────────────────────────────────

        using var rsaPriv = EtwKeyStoreService.LoadPrivateKey(developerEtwId);
        if (rsaPriv is null)
        {
            result.Issues.Add(Err("ETW-Signatur",
                "Privater ETW-Schlüssel nicht gefunden",
                "Erzeuge zuerst einen ETW-Schlüssel über '🔑 ETW-Schlüssel erzeugen'."));
            return result;
        }

        var publicKeyPem  = "";
        var keyFingerprint = "";
        using (var rsaPub = EtwKeyStoreService.LoadPublicKey(developerEtwId))
        {
            if (rsaPub is not null)
            {
                publicKeyPem   = rsaPub.ExportSubjectPublicKeyInfoPem();
                keyFingerprint = EtwKeyStoreService.ComputeFingerprint(rsaPub);
            }
        }

        // ── Kanonischen Payload bilden und signieren ──────────────────────────

        var signedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var canonical = BuildCanonicalPayload(
            extensionId, extensionVersion, developerEtwId,
            packageSha256, releaseInfoSha256, inspectionReportSha256,
            signedAtUtc);

        var payloadBytes  = Encoding.UTF8.GetBytes(canonical);
        var payloadSha256 = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLower();

        byte[] signatureBytes;
        try
        {
            signatureBytes = await Task.Run(
                () => rsaPriv.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                ct);
        }
        catch (Exception ex)
        {
            result.Issues.Add(Err("ETW-Signatur",
                "RSA-Signierung fehlgeschlagen",
                $"Fehler: {ex.Message}"));
            return result;
        }

        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // ── Erweitertes signature-info.json schreiben ─────────────────────────

        var fullSigInfo = new
        {
            // Signaturformat-Versionierung (Phase 4.2)
            signatureVersion          = "etw-signature-v1",
            signatureFormatVersion    = "1.0",
            signaturePhase            = "EtwLocalSigned",
            extensionId,
            extensionVersion,
            developerEtwId,
            developerDisplayName,
            algorithm                 = "SHA256",
            rsaAlgorithm              = "RSA-SHA256-PKCS1v15",
            keySize                   = 2048,
            keyFingerprint,                // SHA256:XX:XX:... (Phase 4.2)
            keyStorageMode            = "LocalFile",
            preparedAtUtc,
            signedAtUtc,
            packageSha256,
            releaseInfoSha256,
            inspectionReportSha256,
            canonicalPayload          = canonical,
            payloadSha256,
            signature                 = signatureBase64,
            publicKey                 = publicKeyPem,
            signatureMode             = "EtwLocalSigned",
            trustLevel                = TrustLevels.EtwLocalSigned,
            isCryptographicallySigned = true,
            marketplaceReady          = false,
            note                      = "Lokale ETW-Entwicklersignatur (RSA-2048 PKCS#1v15, etw-signature-v1). " +
                                         "Marketplace-Verifikation und Trust-Chain-Erweiterung folgen in Phase 5."
        };

        await File.WriteAllTextAsync(sigInfoPath,
            JsonSerializer.Serialize(fullSigInfo,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        // ── release-info.json aktualisieren ──────────────────────────────────

        var releaseInfoPath = Path.Combine(releaseDir, "release-info.json");
        await PatchReleaseInfoAsync(releaseInfoPath,
            isSigned:       true,
            trustLevel:     "EtwLocalSigned",
            signaturePhase: "EtwLocalSigned",
            signedAtUtc,
            developerEtwId,
            ct);

        result.Success          = true;
        result.TrustLevel       = "EtwLocalSigned";
        result.SignatureInfoPath = sigInfoPath;
        result.Algorithm         = "RSA-SHA256-PKCS1v15";
        result.SignedAtUtc       = signedAtUtc;

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task PatchReleaseInfoAsync(
        string path, bool isSigned, string trustLevel,
        string signaturePhase, string signedAtUtc, string developerEtwId,
        CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        try
        {
            var node = JsonNode.Parse(await File.ReadAllTextAsync(path, ct))?.AsObject();
            if (node is null) return;

            node["isSigned"]         = isSigned;
            node["trustLevel"]       = trustLevel;
            node["signaturePhase"]   = signaturePhase;
            node["signedAtUtc"]      = signedAtUtc;
            node["developerEtwId"]   = developerEtwId;
            node["marketplaceReady"] = false;  // bleibt false bis Phase 5

            await File.WriteAllTextAsync(path,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                ct);
        }
        catch { /* Best-Effort */ }
    }

    private static ValidationIssue Err(string cat, string title, string msg)
        => new() { Severity = "Error", Category = cat, Title = title, Message = msg };
}
