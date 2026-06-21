using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public sealed record UpdateInfo(
    string LatestVersion,
    string CurrentVersion,
    string ReleaseUrl,
    string ReleaseName,
    bool   IsUpdateAvailable
);

/// <summary>
/// Prüft beim Start ob eine neuere Version des AAIA Module Managers auf GitHub verfügbar ist.
/// ETWs können auf GitHub nur lesen — kein Push/Commit nötig.
/// </summary>
public static class GitHubUpdateService
{
    // Aktuell eingebaute App-Version
    public const string CurrentVersion = "2.5.0-beta";

    private const string ApiUrl =
        "https://api.github.com/repos/illeArts/AAIA-Module-Manager/releases/latest";

    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent",    "AAIA-Module-Manager/2.0" },
            { "Accept",        "application/vnd.github+json" },
            { "X-GitHub-Api-Version", "2022-11-28" }
        },
        Timeout = TimeSpan.FromSeconds(8)
    };

    /// <summary>
    /// Fragt die GitHub Releases API ab und vergleicht mit der aktuellen Version.
    /// Gibt null zurück wenn kein Update verfügbar oder Netzwerkfehler.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GhRelease>(ApiUrl, ct);
            if (release is null) return null;

            var tag = release.TagName?.TrimStart('v') ?? "";
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var isNewer = CompareVersions(tag, CurrentVersion) > 0;

            return new UpdateInfo(
                LatestVersion    : tag,
                CurrentVersion   : CurrentVersion,
                ReleaseUrl       : release.HtmlUrl ?? "",
                ReleaseName      : release.Name ?? tag,
                IsUpdateAvailable: isNewer);
        }
        catch
        {
            // Keine Verbindung, Rate-Limit, etc. → kein Update-Banner
            return null;
        }
    }

    private static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName  { get; init; }
        [JsonPropertyName("name")]     public string? Name     { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl  { get; init; }
        [JsonPropertyName("prerelease")] public bool  Prerelease { get; init; }
        [JsonPropertyName("draft")]    public bool    Draft    { get; init; }
    }
}
