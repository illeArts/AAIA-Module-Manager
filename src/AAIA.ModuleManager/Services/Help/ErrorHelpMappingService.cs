using AAIA.ModuleManager.Services.Marketplace;

namespace AAIA.ModuleManager.Services.Help;

/// <summary>
/// Bildet Fehlercodes, Fehlerkategorien und Upload-Status auf HelpArticle-IDs ab.
/// Alle Mappings sind statisch und offline verfügbar — kein Netzwerk, keine KI.
/// </summary>
public static class ErrorHelpMappingService
{
    // ── Build-Fehlercode → Artikel-ID ──────────────────────────────────────────

    /// <summary>
    /// Gibt die passende HelpArticle-ID für einen Build-Fehlercode zurück,
    /// oder null wenn kein Mapping vorhanden.
    /// </summary>
    public static string? GetArticleIdForBuildCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // NETSDK / SDK — fehlendes oder falsches .NET SDK
        if (code.StartsWith("NETSDK", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("SDK",    StringComparison.OrdinalIgnoreCase))
            return "troubleshooting.dotnet-sdk-missing";

        // NuGet-Fehler — Restore-Probleme
        if (code.StartsWith("NU", StringComparison.OrdinalIgnoreCase))
            return "troubleshooting.build-failed";

        // Roslyn-Kompilierungsfehler (CS*) und MSBuild-Fehler (MSB*)
        if (code.StartsWith("CS",  StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("MSB", StringComparison.OrdinalIgnoreCase))
            return "troubleshooting.build-failed";

        // Intern: NuGet-Restore fehlt
        if (code == "RESTORE_NEEDED")
            return "troubleshooting.build-failed";

        return null;
    }

    // ── ValidationIssue-Kategorie → Artikel-ID ─────────────────────────────────

    /// <summary>
    /// Gibt die passende HelpArticle-ID für eine ValidationIssue-Kategorie zurück.
    /// </summary>
    public static string? GetArticleIdForValidationCategory(string? category) => category switch
    {
        "Manifest"        => "troubleshooting.manifest-missing",
        "Struktur"        => "validation.overview",
        "Kompatibilitaet" => "troubleshooting.dotnet-sdk-missing",
        "Risiko"          => "security.key-safety",
        "Signatur"        => "troubleshooting.signature-verification-failed",
        "Marketplace"     => "troubleshooting.marketplace-upload-failed",
        "Lizenz"          => "troubleshooting.license-required",
        "Publish"         => "troubleshooting.publish-gate-blocked",
        _                 => null
    };

    // ── MarketplaceUploadStatus → Artikel-ID ───────────────────────────────────

    /// <summary>
    /// Gibt die passende HelpArticle-ID für einen fehlgeschlagenen Upload-Status zurück.
    /// </summary>
    public static string? GetArticleIdForUploadStatus(MarketplaceUploadStatus status) => status switch
    {
        MarketplaceUploadStatus.Rejected     => "troubleshooting.marketplace-upload-failed",
        MarketplaceUploadStatus.Unauthorized => "troubleshooting.marketplace-upload-failed",
        MarketplaceUploadStatus.NetworkError => "troubleshooting.marketplace-upload-failed",
        MarketplaceUploadStatus.ServerError  => "troubleshooting.marketplace-upload-failed",
        _                                    => null
    };

    // ── EtwVerificationResult → Artikel-ID ─────────────────────────────────────

    /// <summary>
    /// Gibt die passende HelpArticle-ID für ein fehlgeschlagenes Verifikationsergebnis zurück.
    /// </summary>
    public static string? GetArticleIdForEtwVerification(bool isValid, string? trustLevel)
    {
        if (isValid) return null;
        return "troubleshooting.signature-verification-failed";
    }
}
