using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Lokal gespeicherte aktivierte Lizenz.
/// </summary>
public sealed class ActivatedLicense
{
    public string  LicenseKey    { get; set; } = "";
    public string  ModuleId      { get; set; } = "";
    public string  ExtensionType { get; set; } = "Module";   // "Module" | "Plugin" | "LanguagePack"
    public string  BuyerEmail    { get; set; } = "";
    public string  DeviceId      { get; set; } = "";

    /// <summary>RS256-JWT für Module/Plugins — wird an AAIAS übergeben.</summary>
    public string? LicenseJwt   { get; set; }

    /// <summary>Direkte Download-URL für LanguagePacks.</summary>
    public string? DownloadUrl  { get; set; }

    /// <summary>Zielsprache des Sprachpakets (z.B. "de-DE").</summary>
    public string? Locale       { get; set; }

    public DateTimeOffset ActivatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt  { get; set; }

    [JsonIgnore]
    public bool IsExpired =>
        ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string StatusDisplay =>
        IsExpired ? "Abgelaufen" : "Aktiv";

    [JsonIgnore]
    public string ExpiresDisplay =>
        ExpiresAt.HasValue
            ? ExpiresAt.Value.ToLocalTime().ToString("dd.MM.yyyy")
            : "Unbegrenzt";
}

/// <summary>
/// Persistiert aktivierte Lizenzen in einer lokalen JSON-Datei.
/// Pfad: %AppData%\AAIAModuleManager\licenses.json
/// </summary>
public sealed class LicenseStore
{
    private static readonly string _path = GetStorePath();

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetStorePath()
    {
        string dir;
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dir = Path.Combine(home, "Library", "Application Support", "AAIAModuleManager");
        }
        else if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            dir = Path.Combine(xdg, "AAIAModuleManager");
        }
        else
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AAIAModuleManager");
        }
        return Path.Combine(dir, "licenses.json");
    }

    public static async Task<List<ActivatedLicense>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = await File.ReadAllTextAsync(_path);
            return JsonSerializer.Deserialize<List<ActivatedLicense>>(json, _opts) ?? [];
        }
        catch { return []; }
    }

    public static async Task SaveAsync(List<ActivatedLicense> licenses)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(licenses, _opts);
        await File.WriteAllTextAsync(_path, json);
    }
}
