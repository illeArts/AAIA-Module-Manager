using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public record RegistryEntry(
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("version")]     string Version,
    [property: JsonPropertyName("contracts")]   string Contracts,
    [property: JsonPropertyName("author")]      string Author,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("repository")]  string Repository
);

/// <summary>
/// Neues Index-Format: { "version":"1", "modules":[…], "plugins":[…] }
/// Löst das alte Flat-Array-Format ab.
/// </summary>
file record RegistryIndex(
    [property: JsonPropertyName("version")]     string?             Version,
    [property: JsonPropertyName("description")] string?             Description,
    [property: JsonPropertyName("modules")]     List<RegistryEntry>? Modules,
    [property: JsonPropertyName("plugins")]     List<RegistryEntry>? Plugins
);

public class RegistryService
{
    private readonly string _registryPath;
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public RegistryService(string registryRoot)
    {
        _registryPath = Path.Combine(registryRoot, "registry", "index.json");
    }

    public async Task<List<RegistryEntry>> LoadAsync()
    {
        if (!File.Exists(_registryPath)) return [];

        var json = await File.ReadAllTextAsync(_registryPath);

        // Neues Format: JSON-Objekt { "modules":[], "plugins":[] }
        if (json.TrimStart().StartsWith('{'))
        {
            var idx = JsonSerializer.Deserialize<RegistryIndex>(json, _jsonOpts);
            var all = new List<RegistryEntry>();
            if (idx?.Modules != null) all.AddRange(idx.Modules);
            if (idx?.Plugins  != null) all.AddRange(idx.Plugins);
            return all;
        }

        // Legacy: flaches Array [...]
        return JsonSerializer.Deserialize<List<RegistryEntry>>(json, _jsonOpts) ?? [];
    }

    public async Task<List<RegistryEntry>> SearchAsync(string query)
    {
        var all = await LoadAsync();
        if (string.IsNullOrWhiteSpace(query)) return all;

        var q = query.Trim().ToLower();
        return all.FindAll(e =>
            e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(RegistryEntry entry)
    {
        // Bestehenden Index lesen (neues oder altes Format)
        RegistryIndex? existingIdx = null;
        if (File.Exists(_registryPath))
        {
            var raw = await File.ReadAllTextAsync(_registryPath);
            if (raw.TrimStart().StartsWith('{'))
                existingIdx = JsonSerializer.Deserialize<RegistryIndex>(raw, _jsonOpts);
        }

        var modules = new List<RegistryEntry>(existingIdx?.Modules ?? []);
        var plugins = new List<RegistryEntry>(existingIdx?.Plugins  ?? []);

        // Nach Typ einsortieren
        if (string.Equals(entry.Type, "plugin", StringComparison.OrdinalIgnoreCase))
        {
            var i = plugins.FindIndex(e => e.Name == entry.Name);
            if (i >= 0) plugins[i] = entry; else plugins.Add(entry);
        }
        else
        {
            var i = modules.FindIndex(e => e.Name == entry.Name);
            if (i >= 0) modules[i] = entry; else modules.Add(entry);
        }

        var newIdx = new RegistryIndex(
            existingIdx?.Version     ?? "1",
            existingIdx?.Description ?? "AAIA Extension Registry",
            modules, plugins);

        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        await File.WriteAllTextAsync(_registryPath, JsonSerializer.Serialize(newIdx, _jsonOpts));
    }

    /// <summary>Liest die aktuelle Contracts-Version aus einer .csproj-Datei.</summary>
    public static string? GetContractsVersion(string csprojPath)
    {
        if (!File.Exists(csprojPath)) return null;
        var content = File.ReadAllText(csprojPath);
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"Include=""AAIA\.Shared\.Contracts""\s+Version=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Liest die Version aus einer .csproj-Datei.</summary>
    public static string? GetProjectVersion(string csprojPath)
    {
        if (!File.Exists(csprojPath)) return null;
        var content = File.ReadAllText(csprojPath);
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"<Version>([^<]+)</Version>");
        return match.Success ? match.Groups[1].Value : null;
    }
}
