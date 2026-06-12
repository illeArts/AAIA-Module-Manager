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

public class RegistryService
{
    private readonly string _registryPath;

    public RegistryService(string registryRoot)
    {
        _registryPath = Path.Combine(registryRoot, "registry", "index.json");
    }

    public async Task<List<RegistryEntry>> LoadAsync()
    {
        if (!File.Exists(_registryPath))
            return [];

        var json = await File.ReadAllTextAsync(_registryPath);
        return JsonSerializer.Deserialize<List<RegistryEntry>>(json)
               ?? [];
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
        var list = await LoadAsync();
        var idx  = list.FindIndex(e => e.Name == entry.Name);

        if (idx >= 0) list[idx] = entry;
        else          list.Add(entry);

        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(_registryPath, JsonSerializer.Serialize(list, opts));
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
