using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Registry-Vorschau-Eintrag — so würde das Modul im Marketplace erscheinen.
/// Noch kein Upload — rein lokal.
/// </summary>
public sealed class RegistryPreview
{
    [JsonPropertyName("id")]            public string Id            { get; init; } = "";
    [JsonPropertyName("displayName")]   public string DisplayName   { get; init; } = "";
    [JsonPropertyName("version")]       public string Version       { get; init; } = "";
    [JsonPropertyName("kind")]          public string Kind          { get; init; } = "";
    [JsonPropertyName("host")]          public string Host          { get; init; } = "";
    [JsonPropertyName("riskLevel")]     public string RiskLevel     { get; init; } = "Green";
    [JsonPropertyName("licenseModel")]  public string LicenseModel  { get; init; } = "Free";
    [JsonPropertyName("developerEtwId")] public string DeveloperEtwId { get; init; } = "";
    [JsonPropertyName("description")]   public string Description   { get; init; } = "";
    [JsonPropertyName("packageHash")]   public string PackageHash   { get; init; } = "";
    [JsonPropertyName("manifestHash")]  public string ManifestHash  { get; init; } = "";
    [JsonPropertyName("generatedAt")]   public string GeneratedAt   { get; init; } = "";
    [JsonPropertyName("note")]          public string Note          { get; init; } =
        "Dies ist eine lokale Vorschau — noch kein Upload.";

    /// <summary>Formatiertes JSON für die Anzeige in der UI.</summary>
    public string ToDisplayJson()
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(this, opts);
    }
}

/// <summary>
/// Erzeugt eine lokale Vorschau des Registry-Eintrags aus Manifest + PackageResult.
/// Zeigt dem ETW: "So würde dein Modul im Marketplace erscheinen."
/// </summary>
public static class ExtensionRegistryPreviewService
{
    public static async Task<RegistryPreview> GenerateAsync(
        string         projectDir,
        AppConfig      config,
        PackageResult? packageResult = null,
        CancellationToken ct         = default)
    {
        var manifestPath = Path.Combine(projectDir, "aaia-manifest.json");

        JsonNode? root = null;
        if (File.Exists(manifestPath))
        {
            try { root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, ct)); }
            catch { /* ignorieren — Felder bleiben leer */ }
        }

        var id           = root?["id"]?.GetValue<string>()          ?? "";
        var displayName  = root?["displayName"]?.GetValue<string>()  ?? "";
        var version      = root?["version"]?.GetValue<string>()      ?? "0.1.0";
        var kind         = root?["kind"]?.GetValue<string>()         ?? "";
        var host         = root?["host"]?.GetValue<string>()         ?? "";
        var description  = root?["description"]?.GetValue<string>()  ?? "";
        var licenseModel = root?["licenseModel"]?.GetValue<string>() ?? "Free";

        // Risiko-Level aus Permissions ableiten
        var riskLevel = DeriveRiskLevel(root);

        // Publisher-ID aus Config
        var developerEtwId = config.DeveloperEtwId ?? config.DeveloperDisplayName ?? "";

        // Hashes aus PackageResult oder aus Dateien berechnen
        var packageHash  = packageResult?.PackageHash  ?? "";
        var manifestHash = packageResult?.ManifestHash ?? await HashFileAsync(manifestPath, ct);

        return new RegistryPreview
        {
            Id            = id,
            DisplayName   = displayName,
            Version       = version,
            Kind          = kind,
            Host          = host,
            RiskLevel     = riskLevel,
            LicenseModel  = licenseModel,
            DeveloperEtwId = developerEtwId,
            Description   = description,
            PackageHash   = packageHash,
            ManifestHash  = manifestHash,
            GeneratedAt   = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
        };
    }

    // ── Risk-Level aus Permissions ────────────────────────────────────────────

    private static string DeriveRiskLevel(JsonNode? root)
    {
        var permissions = root?["permissions"]?.AsArray();
        if (permissions is null || permissions.Count == 0) return "Green";

        var permsStr = permissions.ToString();

        // Red-Permissions
        if (permsStr.Contains("AdminPrivileges", StringComparison.OrdinalIgnoreCase) ||
            permsStr.Contains("UserImpersonation", StringComparison.OrdinalIgnoreCase))
            return "Red";

        // Orange-Permissions
        if (permsStr.Contains("ProcessExecution", StringComparison.OrdinalIgnoreCase) ||
            permsStr.Contains("SystemRegistry", StringComparison.OrdinalIgnoreCase) ||
            permsStr.Contains("CryptographyKeys", StringComparison.OrdinalIgnoreCase))
            return "Orange";

        // Yellow-Permissions
        if (permsStr.Contains("FileSystemWrite", StringComparison.OrdinalIgnoreCase) ||
            permsStr.Contains("NetworkAccess", StringComparison.OrdinalIgnoreCase) ||
            permsStr.Contains("DatabaseAccess", StringComparison.OrdinalIgnoreCase))
            return "Yellow";

        return "Green";
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return "";
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash         = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLower();
    }
}
