using System.IO;

namespace AAIA.ModuleManager.Services.Marketplace;

// ── Upload-Kontext ────────────────────────────────────────────────────────────

/// <summary>
/// Kontext für den Marketplace-Upload eines ETW-signierten Releases.
///
/// Sicherheitsregel (unveränderlich):
///   Dieser Kontext enthält NIEMALS:
///   - Private Keys
///   - API-Keys / Secrets
///   - Quellcode
///
///   Er enthält nur:
///   - .aaiaext-Paket (signiertes Binär-Paket)
///   - signature-info.json (mit eingebettetem Public Key, RSA-Signatur, Hashes)
///   - release-info.json (Metadaten ohne Secrets)
///   - inspection-report.json (Prüfbericht ohne Quellcode)
/// </summary>
public sealed class MarketplaceSignedUploadContext
{
    // Extension-Metadaten
    public string ExtensionId      { get; set; } = "";
    public string Version          { get; set; } = "";
    public string DisplayName      { get; set; } = "";
    public string DeveloperEtwId   { get; set; } = "";
    public string KeyFingerprint   { get; set; } = "";
    public string SignatureVersion { get; set; } = "etw-signature-v1";
    public string TrustLevel       { get; set; } = "";

    // Lokale Dateipfade (werden per multipart übertragen)
    public string PackagePath          { get; set; } = "";  // .aaiaext
    public string SignatureInfoPath    { get; set; } = "";  // signature-info.json
    public string ReleaseInfoPath      { get; set; } = "";  // release-info.json
    public string InspectionReportPath { get; set; } = "";  // inspection-report.json

    // API-Zugang (aus AppConfig)
    public string MarketplaceApiUrl { get; set; } = "";
    public string BearerToken       { get; set; } = "";     // JWT — aus MarketplaceToken

    // ── Berechnete Properties ─────────────────────────────────────────────────

    public bool PackageExists   => !string.IsNullOrEmpty(PackagePath)       && File.Exists(PackagePath);
    public bool SignatureExists => !string.IsNullOrEmpty(SignatureInfoPath)  && File.Exists(SignatureInfoPath);
    public bool ReleaseInfoExists => !string.IsNullOrEmpty(ReleaseInfoPath) && File.Exists(ReleaseInfoPath);
    public bool InspectionExists  => !string.IsNullOrEmpty(InspectionReportPath) && File.Exists(InspectionReportPath);

    public bool IsApiConfigured => !string.IsNullOrWhiteSpace(MarketplaceApiUrl);
    public bool IsLoggedIn      => !string.IsNullOrWhiteSpace(BearerToken);
    public bool IsReadyToUpload => PackageExists && SignatureExists && IsApiConfigured && IsLoggedIn;

    public long   PackageSizeBytes => PackageExists ? new FileInfo(PackagePath).Length : 0;
    public string PackageSizeLabel => PackageSizeBytes switch
    {
        0         => "–",
        < 1024    => $"{PackageSizeBytes} B",
        < 1048576 => $"{PackageSizeBytes / 1024.0:F1} KB",
        _         => $"{PackageSizeBytes / 1048576.0:F2} MB"
    };
    public string PackageFileName => PackageExists ? Path.GetFileName(PackagePath) : "–";
}

// ── Upload-Ergebnis ───────────────────────────────────────────────────────────

public enum MarketplaceUploadStatus
{
    Unknown,
    Uploading,
    Uploaded,             // Datei angenommen, Verifikation läuft
    PendingVerification,  // Server prüft Signatur
    PendingReview,        // Manuelle Überprüfung durch Marketplace-Team
    Verified,             // Server hat Signatur verifiziert
    Rejected,             // Abgelehnt (Signaturprüfung oder Policy)
    AlreadyExists,        // Diese Version existiert bereits
    Unauthorized,         // Nicht eingeloggt oder JWT abgelaufen
    NetworkError,         // Kein Netzwerk / Timeout
    ServerError           // HTTP 5xx oder unbekannter Fehler
}

public sealed class MarketplaceUploadResult
{
    public bool                    Success        { get; set; }
    public MarketplaceUploadStatus Status         { get; set; }
    public string?                 SubmissionId   { get; set; }
    public string?                 Message        { get; set; }
    public string?                 MarketplaceUrl { get; set; }
    public string?                 NextStep       { get; set; }
    public string?                 ErrorDetail    { get; set; }
    public int                     HttpStatusCode { get; set; }

    public string StatusLabel => Status switch
    {
        MarketplaceUploadStatus.Uploaded            => "✅  Hochgeladen",
        MarketplaceUploadStatus.PendingVerification => "⏳  Signaturprüfung läuft",
        MarketplaceUploadStatus.PendingReview       => "📋  Manuelle Überprüfung",
        MarketplaceUploadStatus.Verified            => "🎉  Verifiziert",
        MarketplaceUploadStatus.Rejected            => "⛔  Abgelehnt",
        MarketplaceUploadStatus.AlreadyExists       => "⚠️   Version bereits vorhanden",
        MarketplaceUploadStatus.Unauthorized        => "🔒  Nicht angemeldet",
        MarketplaceUploadStatus.NetworkError        => "🌐  Netzwerkfehler",
        MarketplaceUploadStatus.ServerError         => "⛔  Serverfehler",
        MarketplaceUploadStatus.Uploading           => "📤  Wird hochgeladen…",
        _                                           => "❓  Unbekannt"
    };

    public string StatusColor => Status switch
    {
        MarketplaceUploadStatus.Uploaded
            or MarketplaceUploadStatus.Verified        => "#4ade80",
        MarketplaceUploadStatus.PendingVerification
            or MarketplaceUploadStatus.PendingReview
            or MarketplaceUploadStatus.Uploading       => "#5865f2",
        MarketplaceUploadStatus.AlreadyExists          => "#c9a227",
        _                                              => "#e05252"
    };
}
