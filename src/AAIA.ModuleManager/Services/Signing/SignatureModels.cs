using System.Collections.Generic;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Services.Signing;

// ── ETW-Schlüssel ─────────────────────────────────────────────────────────────

/// <summary>
/// Metadaten über einen lokalen ETW-RSA-Schlüssel.
/// </summary>
public sealed class EtwKeyInfo
{
    public string          EtwId          { get; set; } = "";
    public string          DisplayName    { get; set; } = "";
    public bool            HasKey         { get; set; }
    public string?         PublicKeyPem   { get; set; }
    public string?         PrivateKeyPath { get; set; }
    public string?         PublicKeyPath  { get; set; }
    public string          CreatedAtUtc   { get; set; } = "";
    public int             KeySizeBits    { get; set; } = 2048;
    public string          KeyAlgorithm   { get; set; } = "RSA";

    /// <summary>
    /// SHA256-Fingerprint des Public Key (DER-kodiert).
    /// Format: "SHA256:XX:XX:XX:..." (64 Hex-Bytes, Doppelpunkt-getrennt).
    /// Wird in key-info.json und signature-info.json gespeichert.
    /// Dient zum späteren Abgleich mit dem Marketplace-Registry.
    /// </summary>
    public string          Fingerprint    { get; set; } = "";

    /// <summary>Wo der private Schlüssel gespeichert ist.</summary>
    public KeyStorageMode  StorageMode    { get; set; } = KeyStorageMode.LocalFile;

    /// <summary>Kurzanzeige des Fingerprints (erste 24 Zeichen + …).</summary>
    public string FingerprintShort
        => Fingerprint.Length > 24 ? Fingerprint[..24] + "…" : Fingerprint;
}

// ── ETW-Signatur-Ergebnis ─────────────────────────────────────────────────────

/// <summary>
/// Ergebnis der ETW-Signierung (Phase 4.1).
/// </summary>
public sealed class EtwSigningResult
{
    public bool     Success           { get; set; }
    public string   TrustLevel        { get; set; } = "Unsigned";
    public string?  SignatureInfoPath  { get; set; }
    public string   EtwId             { get; set; } = "";
    public string   DisplayName       { get; set; } = "";
    public string   Algorithm         { get; set; } = "RSA-SHA256-PKCS1v15";
    public string   SignedAtUtc       { get; set; } = "";

    public List<ValidationIssue> Issues { get; set; } = [];
}

// ── ETW-Verifikations-Ergebnis ────────────────────────────────────────────────

/// <summary>
/// Ergebnis der ETW-Signaturprüfung (Phase 4.1).
/// </summary>
public sealed class EtwVerificationResult
{
    public bool   IsValid                 { get; set; }
    public string TrustLevel              { get; set; } = "Unsigned";
    public bool   SignatureStructureValid  { get; set; }

    /// <summary>RSA-Signatur mathematisch korrekt?</summary>
    public bool   RsaSignatureValid       { get; set; }

    /// <summary>Kanonischer Payload stimmt mit gespeicherten Hashes überein?</summary>
    public bool   HashesIntact            { get; set; }

    public string SignerEtwId             { get; set; } = "";
    public string SignedAtUtc             { get; set; } = "";
    public string KeyFingerprint          { get; set; } = "";
    public string Summary                 { get; set; } = "";

    public List<ValidationIssue> Issues   { get; set; } = [];
}
