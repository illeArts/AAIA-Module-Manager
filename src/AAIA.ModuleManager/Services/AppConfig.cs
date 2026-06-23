using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.AiAdapter;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
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

    /// <summary>Zielordner für neue Projekte aus dem Wizard.</summary>
    public string NewProjectPath { get; set; } = DefaultNewProjectPath();

    private static string DefaultNewProjectPath() => OperatingSystem.IsWindows()
        ? @"H:\AAIAGitHub"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AAIAGitHub");

    // ── KI-Assistent ─────────────────────────────────────────────────────────

    /// <summary>Aktiver KI-Anbieter: "Claude" | "OpenAI" | "Gemini".</summary>
    public string AiProvider { get; set; } = "Claude";

    // Claude / Anthropic
    /// <summary>Anthropic Claude API Key.</summary>
    public string ClaudeApiKey { get; set; } = "";
    /// <summary>Claude-Modell.</summary>
    public string ClaudeModel  { get; set; } = "claude-haiku-4-5-20251001";

    // OpenAI / GPT
    /// <summary>OpenAI API Key.</summary>
    public string OpenAiApiKey { get; set; } = "";
    /// <summary>OpenAI-Modell.</summary>
    public string OpenAiModel  { get; set; } = "gpt-4o-mini";

    // Google Gemini
    /// <summary>Google Gemini API Key.</summary>
    public string GeminiApiKey { get; set; } = "";
    /// <summary>Gemini-Modell.</summary>
    public string GeminiModel  { get; set; } = "gemini-2.0-flash";

    // ── AAIAS-KI-Kontext ──────────────────────────────────────────────────────
    /// <summary>Aktuelles Projekt an KI uebergeben.</summary>
    public bool AiContextSendProject     { get; set; } = true;
    /// <summary>Buildfehler an KI uebergeben.</summary>
    public bool AiContextSendBuildErrors { get; set; } = true;
    /// <summary>Manifest und Rechte an KI uebergeben.</summary>
    public bool AiContextSendManifest    { get; set; } = true;
    /// <summary>AAIAS-Status an KI uebergeben.</summary>
    public bool AiContextSendAaiasStatus { get; set; } = true;
    /// <summary>Quellcode-Dateien automatisch einbeziehen (standardmaessig aus).</summary>
    public bool AiContextSendSourceFiles { get; set; } = false;

    // ── Phase 6.0 — Zentraler AI Adapter ─────────────────────────────────────

    /// <summary>
    /// Adapter-Einstellungen: welches Target mit welchem Modus.
    /// Standard: alles ManualHandoff, bis API-Keys konfiguriert sind.
    /// </summary>
    public AiAdapterSettings AiAdapter { get; set; } = new();

    // ── Phase 6.2 — Connector-Server ─────────────────────────────────────────

    /// <summary>Einstellungen für den lokalen AI Connector-Server.</summary>
    public AiConnectorServerSettings AiConnector { get; set; } = new();

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

    /// <summary>
    /// Basis-URL der AAIA Marketplace API (ASP.NET Core — aaia-marketplace-api).
    /// Für signierte Uploads (Phase 5.1), Registry (Phase 5.2) und Downloads (Phase 5.3).
    /// Unabhängig von MarketplaceApiUrl (WordPress REST API).
    /// </summary>
    public string EtwMarketplaceApiUrl { get; set; } = "https://marketplace.aaiagent.de";

    /// <summary>
    /// Lokales Verzeichnis für heruntergeladene .aaiaext-Pakete.
    /// Wird beim Download angelegt wenn nicht vorhanden.
    /// </summary>
    public string DownloadDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "AAIA");

    // ── Marketplace Backend (jetzt: WordPress REST API) ──────────────────────
    /// <summary>
    /// Basis-URL der Marketplace-Backend-API.
    /// Nach der Migration zur WordPress REST API ist das identisch mit MarketplaceApiUrl.
    /// Diese Property bleibt für Rückwärtskompatibilität erhalten — existierende
    /// config.json-Werte werden beim Laden ignoriert (Setter ist no-op).
    /// Aufrufer, die MarketplaceBackendApiUrl setzen oder lesen, nutzen fortan
    /// automatisch die WordPress-URL.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string MarketplaceBackendApiUrl
    {
        get => MarketplaceApiUrl;
        // Setter: no-op — Wert aus config.json ("https://api.marketplace.aaia.app") wird ignoriert.
        // Die URL wird aus MarketplaceApiUrl abgeleitet.
        set { /* deliberately empty — rückwärtskompatibel, kein Effekt */ }
    }

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
