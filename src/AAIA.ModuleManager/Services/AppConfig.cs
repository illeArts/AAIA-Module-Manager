using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AAIA.Shared.Contracts.Publisher;

namespace AAIA.ModuleManager.Services;

public class AppConfig
{
    private static readonly string ConfigPath = GetConfigPath();

    /// <summary>
    /// Gibt den plattformgerechten Pfad zur config.json zurück.
    /// Windows : %AppData%\AAIAModuleManager\config.json
    /// macOS   : ~/Library/Application Support/AAIAModuleManager/config.json
    /// Linux   : ~/.config/AAIAModuleManager/config.json
    /// </summary>
    private static string GetConfigPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "AAIAModuleManager", "config.json");
        }
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(xdg, "AAIAModuleManager", "config.json");
        }
        // Windows
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AAIAModuleManager", "config.json");
    }

    /// <summary>Plattformgerechte Standard-Pfade (leer lassen wenn unbekannt).</summary>
    private static string DefaultSdkPath() => OperatingSystem.IsWindows()
        ? @"H:\AAIAGitHub\aaia-sdk"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AAIAGitHub", "aaia-sdk");

    private static string DefaultMonorepoPath() => OperatingSystem.IsWindows()
        ? @"C:\Users\Andre Iljaschow\OneDrive\Dokumente\Codex\AndreAIAgent\Universal AAIA"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AAIAGitHub", "Universal AAIA");

    private static string DefaultRegistryPath() => OperatingSystem.IsWindows()
        ? @"H:\AAIAGitHub\aaia-extension-registry"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AAIAGitHub", "aaia-extension-registry");

    public string SdkPath      { get; set; } = DefaultSdkPath();
    public string MonorepoPath { get; set; } = DefaultMonorepoPath();
    public string RegistryPath { get; set; } = DefaultRegistryPath();

    // V2 — AAIAS connection
    public string AaiasUrl      { get; set; } = "http://localhost:5174";
    public string AaiasUsername { get; set; } = "";
    public string AaiasPassword { get; set; } = "";

    // UI Language: "de" | "en"
    public string Language { get; set; } = "de";

    // ── Marketplace API (aaia-marketplace-api) ────────────────────────────────
    public string MarketplaceApiUrl    { get; set; } = "https://aaiagent.de/index.php?rest_route=/aaia/v1";
    /// <summary>Gespeicherter JWT nach Login. Wird beim Start geladen.</summary>
    public string MarketplaceToken     { get; set; } = "";
    /// <summary>ETW-ID des eingeloggten Entwicklers. Null wenn nicht eingeloggt.</summary>
    public string? DeveloperEtwId      { get; set; }
    /// <summary>Anzeigename des eingeloggten Entwicklers.</summary>
    public string? DeveloperDisplayName { get; set; }
    /// <summary>Rolle des eingeloggten Entwicklers (Community / Owner / …).</summary>
    public DeveloperRole DeveloperRole { get; set; } = DeveloperRole.Community;
    /// <summary>KeyId des registrierten Publisher-Schlüssels (aaia-sign keygen).</summary>
    public string? PublisherKeyId { get; set; }
    /// <summary>Pfad zum privaten Publisher-Schlüssel (lokal, nie auf Server).</summary>
    public string? PublisherPrivateKeyPath { get; set; }

    // ── Marketplace Backend (ASP.NET Core — aaia-marketplace-api) ─────────────
    /// <summary>
    /// Basis-URL der AAIA Marketplace Backend-API (ASP.NET Core).
    /// Verschieden von MarketplaceApiUrl (WordPress).
    /// Beispiel: "https://api.marketplace.aaia.app"
    /// </summary>
    public string MarketplaceBackendApiUrl { get; set; } = "https://api.marketplace.aaia.app";

    /// <summary>Set after LoadAsync() — allows services to access config without DI.</summary>
    public static AppConfig? Current { get; internal set; }

    /// <summary>Synchronous load — safe to call from UI thread startup code.</summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { /* use defaults */ }
        return new AppConfig();
    }

    /// <summary>Async variant — use from background tasks only, not from the UI startup path.</summary>
    public static async Task<AppConfig> LoadAsync()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = await File.ReadAllTextAsync(ConfigPath).ConfigureAwait(false);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { /* defaults */ }
        return new AppConfig();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(ConfigPath, JsonSerializer.Serialize(this, opts));
    }
}
