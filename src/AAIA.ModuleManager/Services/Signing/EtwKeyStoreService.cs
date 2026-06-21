using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.Signing;

/// <summary>
/// Verwaltet lokale ETW-RSA-Schlüsselpaare.
///
/// Speicherort: %APPDATA%/AAIA/Keys/
///   {etwId}-private.pem   — PKCS#8 privater Schlüssel (NIEMALS teilen)
///   {etwId}-public.pem    — SubjectPublicKeyInfo öffentlicher Schlüssel
///   {etwId}-key-info.json — Metadaten (Erstellungsdatum, Algo, ...)
///
/// Phase 4.1: Schlüssel im Klartext gespeichert.
/// Phase 4.2 wird Passphrase-Schutz via PBKDF2+AES-256-GCM ergänzen.
/// </summary>
public static class EtwKeyStoreService
{
    // ── Pfade ─────────────────────────────────────────────────────────────────

    private static string GetKeysDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AAIA", "Keys");

    private static string SafeId(string etwId)
    {
        // Ungültige Dateinamenzeichen durch Bindestrich ersetzen
        var safe = etwId.ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '-');
        return safe.Replace(' ', '-');
    }

    private static string PrivPath(string etwId)
        => Path.Combine(GetKeysDir(), $"{SafeId(etwId)}-private.pem");

    private static string PubPath(string etwId)
        => Path.Combine(GetKeysDir(), $"{SafeId(etwId)}-public.pem");

    private static string InfoPath(string etwId)
        => Path.Combine(GetKeysDir(), $"{SafeId(etwId)}-key-info.json");

    // ── Fingerprint ───────────────────────────────────────────────────────────

    /// <summary>
    /// Berechnet den SHA256-Fingerprint des Public Key (DER-kodiert).
    /// Format: "SHA256:XX:XX:XX:..." — 32 Bytes, Doppelpunkt-getrennt.
    /// Dient zum Abgleich mit dem Marketplace-Registry.
    /// </summary>
    public static string ComputeFingerprint(RSA rsa)
    {
        // DER-kodierter SubjectPublicKeyInfo (SPKI) — plattformübergreifend eindeutig
        var derBytes = rsa.ExportSubjectPublicKeyInfo();
        var hash     = SHA256.HashData(derBytes);
        return "SHA256:" + string.Join(":", hash.Select(b => b.ToString("X2")));
    }

    // ── Schlüssel-Existenz ────────────────────────────────────────────────────

    public static bool HasKey(string? etwId)
        => !string.IsNullOrWhiteSpace(etwId)
           && File.Exists(PrivPath(etwId))
           && File.Exists(PubPath(etwId));

    // ── Schlüssel erzeugen ────────────────────────────────────────────────────

    /// <summary>
    /// Erzeugt ein neues RSA-2048-Schlüsselpaar und speichert es lokal.
    /// Überschreibt einen vorhandenen Schlüssel.
    /// </summary>
    public static async Task<EtwKeyInfo> GenerateKeyAsync(
        string            etwId,
        string            displayName,
        CancellationToken ct = default)
    {
        var keysDir   = GetKeysDir();
        Directory.CreateDirectory(keysDir);

        using var rsa = RSA.Create(2048);

        var privatePem  = rsa.ExportPkcs8PrivateKeyPem();
        var publicPem   = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = ComputeFingerprint(rsa);

        var privPath  = PrivPath(etwId);
        var pubPath   = PubPath(etwId);
        var infoPath  = InfoPath(etwId);
        var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Schlüsseldateien schreiben
        await File.WriteAllTextAsync(privPath, privatePem, ct);
        await File.WriteAllTextAsync(pubPath,  publicPem,  ct);

        // Metadaten (inkl. Fingerprint)
        var keyMeta = new
        {
            etwId,
            displayName,
            keyAlgorithm  = "RSA",
            keySizeBits   = 2048,
            signatureAlgo = "RSA-SHA256-PKCS1v15",
            keyStorageMode = "LocalFile",
            fingerprint,
            createdAtUtc  = createdAt,
            publicKeyFile = Path.GetFileName(pubPath),
            warning       = "Privater Schlüssel NIEMALS weitergeben oder in Versionskontrolle einchecken."
        };

        await File.WriteAllTextAsync(infoPath,
            JsonSerializer.Serialize(keyMeta,
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        return new EtwKeyInfo
        {
            EtwId          = etwId,
            DisplayName    = displayName,
            HasKey         = true,
            PublicKeyPem   = publicPem,
            PrivateKeyPath = privPath,
            PublicKeyPath  = pubPath,
            CreatedAtUtc   = createdAt,
            KeySizeBits    = 2048,
            KeyAlgorithm   = "RSA",
            Fingerprint    = fingerprint,
            StorageMode    = KeyStorageMode.LocalFile
        };
    }

    // ── Schlüssel-Metadaten lesen ─────────────────────────────────────────────

    public static EtwKeyInfo GetKeyInfo(string? etwId)
    {
        var info = new EtwKeyInfo { EtwId = etwId ?? "" };
        if (string.IsNullOrWhiteSpace(etwId)) return info;

        var privPath = PrivPath(etwId);
        var pubPath  = PubPath(etwId);

        if (!File.Exists(privPath) || !File.Exists(pubPath))
            return info;

        info.HasKey         = true;
        info.PrivateKeyPath = privPath;
        info.PublicKeyPath  = pubPath;

        try { info.PublicKeyPem = File.ReadAllText(pubPath); }
        catch { /* Best-Effort */ }

        var infoPath = InfoPath(etwId);
        if (File.Exists(infoPath))
        {
            try
            {
                var node           = JsonNode.Parse(File.ReadAllText(infoPath));
                info.DisplayName   = node?["displayName"]?.GetValue<string>()   ?? etwId;
                info.CreatedAtUtc  = node?["createdAtUtc"]?.GetValue<string>()  ?? "";
                info.KeySizeBits   = node?["keySizeBits"]?.GetValue<int>()      ?? 2048;
                info.KeyAlgorithm  = node?["keyAlgorithm"]?.GetValue<string>()  ?? "RSA";
                info.Fingerprint   = node?["fingerprint"]?.GetValue<string>()   ?? "";
                var storeMode      = node?["keyStorageMode"]?.GetValue<string>() ?? "LocalFile";
                info.StorageMode   = storeMode == "LocalFile"
                                     ? KeyStorageMode.LocalFile
                                     : KeyStorageMode.PlatformSecureStore;
            }
            catch { /* Best-Effort */ }
        }

        // Fingerprint on-the-fly berechnen falls nicht in key-info gespeichert (Altschlüssel)
        if (string.IsNullOrEmpty(info.Fingerprint) && info.PublicKeyPem is not null)
        {
            try
            {
                using var rsa  = RSA.Create();
                rsa.ImportFromPem(info.PublicKeyPem);
                info.Fingerprint = ComputeFingerprint(rsa);
            }
            catch { /* Best-Effort */ }
        }

        return info;
    }

    // ── Schlüssel laden ───────────────────────────────────────────────────────

    /// <summary>
    /// Lädt den privaten Schlüssel. Caller ist für Dispose verantwortlich.
    /// </summary>
    public static RSA? LoadPrivateKey(string? etwId)
    {
        if (string.IsNullOrWhiteSpace(etwId)) return null;
        var path = PrivPath(etwId);
        if (!File.Exists(path)) return null;

        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }
        catch { return null; }
    }

    /// <summary>
    /// Lädt den öffentlichen Schlüssel. Caller ist für Dispose verantwortlich.
    /// </summary>
    public static RSA? LoadPublicKey(string? etwId)
    {
        if (string.IsNullOrWhiteSpace(etwId)) return null;
        var path = PubPath(etwId);
        if (!File.Exists(path)) return null;

        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }
        catch { return null; }
    }

    /// <summary>
    /// Lädt einen öffentlichen Schlüssel aus einem PEM-String (z. B. aus signature-info.json).
    /// Caller ist für Dispose verantwortlich.
    /// </summary>
    public static RSA? LoadPublicKeyFromPem(string publicKeyPem)
    {
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa;
        }
        catch { return null; }
    }
}
