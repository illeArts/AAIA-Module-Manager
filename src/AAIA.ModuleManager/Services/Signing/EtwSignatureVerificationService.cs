using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Services.Signing;

/// <summary>
/// Phase 4.1 — Verifizierung der ETW-Signatur.
///
/// Prüft:
///   1. Strukturelle Vollständigkeit von signature-info.json
///   2. Ob der gespeicherte canonicalPayload mit den gespeicherten Hashes übereinstimmt
///   3. RSA-Signatur mathematisch korrekt (mit eingebettetem Public Key)
///   4. .aaiaext-Paket und inspection-report.json gegen gespeicherte Hashes
///
/// Hinweis: release-info.json wird von Phase 4.1 selbst modifiziert (isSigned: true etc.),
/// daher wird dessen Hash NICHT gegen die Post-Signatur-Version geprüft.
/// </summary>
public static class EtwSignatureVerificationService
{
    public static async Task<EtwVerificationResult> VerifyAsync(
        string            releaseDir,
        CancellationToken ct = default)
    {
        var result = new EtwVerificationResult();

        var sigInfoPath = Path.Combine(releaseDir, "signature-info.json");
        if (!File.Exists(sigInfoPath))
        {
            result.Issues.Add(Err("ETW-Prüfung",
                "Keine Signaturdatei gefunden",
                "signature-info.json existiert nicht. Führe zuerst Phase 4.0 und 4.1 aus."));
            result.Summary = "Prüfung nicht möglich — keine Signatur vorhanden.";
            return result;
        }

        // ── signature-info.json einlesen ─────────────────────────────────────

        JsonNode? sig;
        try { sig = JsonNode.Parse(await File.ReadAllTextAsync(sigInfoPath, ct)); }
        catch
        {
            result.Issues.Add(Err("ETW-Prüfung",
                "signature-info.json nicht lesbar",
                "Die Signaturdatei ist beschädigt oder kein gültiges JSON."));
            result.Summary = "Prüfung fehlgeschlagen — Signaturdatei beschädigt.";
            return result;
        }

        // ── Felder extrahieren ────────────────────────────────────────────────

        var trustLevel              = sig?["trustLevel"]?.GetValue<string>()               ?? "Unsigned";
        var isCryptoSigned          = sig?["isCryptographicallySigned"]?.GetValue<bool>()  ?? false;
        var signatureBase64         = sig?["signature"]?.GetValue<string>()                ?? "";
        var publicKeyPem            = sig?["publicKey"]?.GetValue<string>()                ?? "";
        var storedCanonical         = sig?["canonicalPayload"]?.GetValue<string>()         ?? "";
        var developerEtwId          = sig?["developerEtwId"]?.GetValue<string>()           ?? "";
        var signedAtUtc             = sig?["signedAtUtc"]?.GetValue<string>()              ?? "";
        var extensionId             = sig?["extensionId"]?.GetValue<string>()              ?? "";
        var extensionVersion        = sig?["extensionVersion"]?.GetValue<string>()         ?? "";
        var packageSha256           = sig?["packageSha256"]?.GetValue<string>()            ?? "";
        var releaseInfoSha256       = sig?["releaseInfoSha256"]?.GetValue<string>()        ?? "";
        var inspectionReportSha256  = sig?["inspectionReportSha256"]?.GetValue<string>()   ?? "";

        var keyFingerprint = sig?["keyFingerprint"]?.GetValue<string>() ?? "";

        result.TrustLevel    = trustLevel;
        result.SignerEtwId   = developerEtwId;
        result.SignedAtUtc   = signedAtUtc;
        result.KeyFingerprint = keyFingerprint;

        // ── Kryptografische Signatur vorhanden? ───────────────────────────────

        if (!isCryptoSigned || string.IsNullOrEmpty(signatureBase64) || string.IsNullOrEmpty(publicKeyPem))
        {
            result.Issues.Add(Warn("ETW-Prüfung",
                "Keine kryptografische ETW-Signatur vorhanden",
                "Die Datei enthält nur Hash-Vorbereitung (Phase 4.0). " +
                "Führe zuerst 'ETW-Signatur erstellen' (Phase 4.1) aus."));
            result.SignatureStructureValid = false;
            result.Summary = "⚠️ Keine ETW-Signatur vorhanden — nur Hash-Vorbereitung.";
            return result;
        }

        result.SignatureStructureValid = true;

        // ── Kanonischen Payload rekonstruieren und vergleichen ────────────────

        var rebuiltCanonical = EtwSigningService.BuildCanonicalPayload(
            extensionId, extensionVersion, developerEtwId,
            packageSha256, releaseInfoSha256, inspectionReportSha256,
            signedAtUtc);

        result.HashesIntact = storedCanonical == rebuiltCanonical;

        if (!result.HashesIntact)
        {
            result.Issues.Add(Err("ETW-Prüfung",
                "Kanonischer Payload manipuliert",
                "Der gespeicherte Payload stimmt nicht mit den Hash-Feldern überein. " +
                "Die Signaturdatei wurde nach der Signierung modifiziert."));
        }

        // ── RSA-Signatur mathematisch prüfen ─────────────────────────────────

        try
        {
            using var rsa = EtwKeyStoreService.LoadPublicKeyFromPem(publicKeyPem);
            if (rsa is null)
            {
                result.Issues.Add(Err("ETW-Prüfung",
                    "Öffentlicher Schlüssel konnte nicht geladen werden",
                    "Der in signature-info.json eingebettete Public Key ist ungültig."));
                result.RsaSignatureValid = false;
            }
            else
            {
                // Signatur über den REKONSTRUIERTEN Payload prüfen (nicht den gespeicherten),
                // damit Manipulation der canonicalPayload-Felder erkannt wird.
                var payloadBytes   = Encoding.UTF8.GetBytes(rebuiltCanonical);
                var signatureBytes = Convert.FromBase64String(signatureBase64);

                result.RsaSignatureValid = await Task.Run(
                    () => rsa.VerifyData(payloadBytes, signatureBytes,
                              HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    ct);
            }
        }
        catch (Exception ex)
        {
            result.Issues.Add(Err("ETW-Prüfung",
                "RSA-Verifizierung fehlgeschlagen",
                $"Technischer Fehler: {ex.Message}"));
            result.RsaSignatureValid = false;
        }

        if (!result.RsaSignatureValid && result.Issues.TrueForAll(i => i.Title != "RSA-Signatur ungültig"))
        {
            result.Issues.Add(Err("ETW-Prüfung",
                "RSA-Signatur ungültig",
                "Die kryptografische Signatur stimmt nicht überein. " +
                "Das Paket oder die Signaturfelder wurden nach der Signierung verändert."));
        }

        // ── Dateihashes gegen Release-Ordner prüfen ───────────────────────────

        await CheckFileHashesAsync(releaseDir, packageSha256, inspectionReportSha256, result, ct);

        // ── Gesamtergebnis ────────────────────────────────────────────────────

        result.IsValid = result.SignatureStructureValid
                      && result.RsaSignatureValid
                      && result.HashesIntact
                      && result.Issues.TrueForAll(i => i.Severity != "Error");

        result.Summary = result.IsValid
            ? $"✅ ETW-Signatur gültig — signiert von {developerEtwId} am {signedAtUtc}"
            : $"⛔ {result.Issues.Count} Problem(e) — Signatur ungültig oder Dateien manipuliert.";

        return result;
    }

    // ── Dateihashes ───────────────────────────────────────────────────────────

    private static async Task CheckFileHashesAsync(
        string                releaseDir,
        string                expectedPackageHash,
        string                expectedInspectionHash,
        EtwVerificationResult result,
        CancellationToken     ct)
    {
        // .aaiaext-Paket (unveränderlich nach Paketbau)
        var pkgFiles = Directory.GetFiles(releaseDir, "*.aaiaext");
        if (pkgFiles.Length > 0)
        {
            var current = await Sha256(pkgFiles[0], ct);
            if (!string.Equals(current, expectedPackageHash, StringComparison.OrdinalIgnoreCase))
                result.Issues.Add(Err("Datei-Integrität",
                    "Paket nach Signatur verändert",
                    $"Das .aaiaext-Paket wurde nach der ETW-Signierung modifiziert.\n" +
                    $"Erwartet: {Trunc(expectedPackageHash)}\n" +
                    $"Aktuell:  {Trunc(current)}"));
        }
        else
        {
            result.Issues.Add(Err("Datei-Integrität",
                "Paketdatei nicht gefunden",
                "Das .aaiaext-Paket wurde aus dem Release-Ordner entfernt."));
        }

        // inspection-report.json (sollte sich nach Signatur nicht ändern)
        var irPath = Path.Combine(releaseDir, "inspection-report.json");
        if (File.Exists(irPath))
        {
            var current = await Sha256(irPath, ct);
            if (!string.Equals(current, expectedInspectionHash, StringComparison.OrdinalIgnoreCase))
                result.Issues.Add(Warn("Datei-Integrität",
                    "Inspektionsbericht nach Signatur verändert",
                    "inspection-report.json wurde nach der ETW-Signierung modifiziert."));
        }

        // Hinweis: release-info.json wird von Phase 4.1 selbst modifiziert (isSigned: true).
        // Dessen Hash-Vergleich gegen Phase-4.0-Wert ist erwartungsgemäß "falsch" und wird
        // daher hier nicht geprüft.
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> Sha256(string path, CancellationToken ct)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, ct)).ToLower();
    }

    private static string Trunc(string hex)
        => hex.Length > 16 ? hex[..16] + "…" : hex;

    private static ValidationIssue Err(string cat, string title, string msg)
        => new() { Severity = "Error", Category = cat, Title = title, Message = msg };

    private static ValidationIssue Warn(string cat, string title, string msg)
        => new() { Severity = "Warning", Category = cat, Title = title, Message = msg };
}
