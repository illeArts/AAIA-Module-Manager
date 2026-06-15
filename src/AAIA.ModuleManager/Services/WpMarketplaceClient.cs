using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record MarketplaceModuleDto(
    [property: JsonPropertyName("productId")]      int     ProductId,
    [property: JsonPropertyName("title")]          string  Title,
    [property: JsonPropertyName("status")]         string  Status,
    [property: JsonPropertyName("type")]           string  Type,
    [property: JsonPropertyName("productCode")]    string  ProductCode,
    [property: JsonPropertyName("version")]        string  Version,
    [property: JsonPropertyName("licenseCount")]   int     LicenseCount,
    [property: JsonPropertyName("price")]          decimal Price,
    [property: JsonPropertyName("currency")]       string  Currency,
    [property: JsonPropertyName("url")]            string  Url
);

public sealed record DeveloperStatsDto(
    [property: JsonPropertyName("totalModules")]       int     TotalModules,
    [property: JsonPropertyName("publishedModules")]   int     PublishedModules,
    [property: JsonPropertyName("totalLicenses")]      int     TotalLicenses,
    [property: JsonPropertyName("activeLicenses")]     int     ActiveLicenses,
    [property: JsonPropertyName("last30DaysLicenses")] int     Last30DaysLicenses,
    [property: JsonPropertyName("revenueShare")]       decimal RevenueShare
);

public sealed class ModuleUploadRequest
{
    public string Title         { get; init; } = "";
    public string Version       { get; init; } = "1.0.0";
    /// <summary>plugin | module | languagepack</summary>
    public string Type          { get; init; } = "plugin";
    public decimal Price        { get; init; } = 0m;
    public string Description   { get; init; } = "";
    public string MinAaiaVersion { get; init; } = "1.0.0";
    /// <summary>Absolute path to the ZIP file on disk.</summary>
    public string FilePath      { get; init; } = "";
}

// ── Client ─────────────────────────────────────────────────────────────────────

/// <summary>
/// HTTP-Client gegen die AAIA Marketplace WordPress REST API.
/// Basis-URL aus AppConfig.MarketplaceApiUrl, z.B.
///   "https://aaiagent.de/index.php?rest_route=/aaia/v1"
/// Auth: Bearer JWT (ETW-JWT aus Login).
/// </summary>
public sealed class WpMarketplaceClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Basis-Host-URL ohne Query, z.B. "https://aaiagent.de/index.php"</summary>
    private readonly string _hostUrl;

    /// <summary>Rest-Route-Prefix, z.B. "/aaia/v1"</summary>
    private readonly string _routePrefix;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <param name="wpApiUrl">
    ///   Vollständige WP-REST-Basis-URL, z.B.
    ///   "https://aaiagent.de/index.php?rest_route=/aaia/v1"
    /// </param>
    public WpMarketplaceClient(string wpApiUrl)
    {
        // Zerlege "https://host/index.php?rest_route=/aaia/v1"
        // in _hostUrl = "https://host/index.php" und _routePrefix = "/aaia/v1"
        var uri = new Uri(wpApiUrl);
        _hostUrl     = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
        _routePrefix = System.Web.HttpUtility.ParseQueryString(uri.Query)["rest_route"] ?? "/aaia/v1";

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Token ──────────────────────────────────────────────────────────────────

    public void SetBearer(string token) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    public void ClearBearer() =>
        _http.DefaultRequestHeaders.Authorization = null;

    // ── URL-Builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Baut die vollständige WordPress-REST-URL für einen Pfad-Suffix.
    /// Beispiel: "/developers/me/modules"
    /// → "https://aaiagent.de/index.php?rest_route=/aaia/v1/developers/me/modules"
    /// </summary>
    private string Url(string path) =>
        $"{_hostUrl}?rest_route={Uri.EscapeDataString(_routePrefix + path)}";

    // ── API-Methoden ──────────────────────────────────────────────────────────

    /// <summary>
    /// GET /aaia/v1/developers/me/modules
    /// Gibt alle eigenen Marketplace-Module des eingeloggten ETW zurück.
    /// JWT erforderlich.
    /// </summary>
    public async Task<List<MarketplaceModuleDto>> GetMyModulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/developers/me/modules"), ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<List<MarketplaceModuleDto>>(_json, ct)
               ?? new List<MarketplaceModuleDto>();
    }

    /// <summary>
    /// GET /aaia/v1/developers/me/stats
    /// Statistiken des eingeloggten ETW (Module, Lizenzen, Revenue).
    /// JWT erforderlich.
    /// </summary>
    public async Task<DeveloperStatsDto> GetMyStatsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/developers/me/stats"), ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeveloperStatsDto>(_json, ct)
               ?? new DeveloperStatsDto(0, 0, 0, 0, 0, 0m);
    }

    /// <summary>
    /// POST /aaia/v1/developers/modules  (multipart/form-data)
    /// Lädt ein neues Modul als ZIP hoch. Erstellt ein WooCommerce-Produkt (status=pending).
    /// JWT erforderlich.
    /// </summary>
    public async Task<JsonDocument> SubmitModuleAsync(
        ModuleUploadRequest req,
        CancellationToken ct = default)
    {
        if (!File.Exists(req.FilePath))
            throw new FileNotFoundException("ZIP-Datei nicht gefunden.", req.FilePath);

        using var form = new MultipartFormDataContent();

        // Text-Felder
        form.Add(new StringContent(req.Title),                           "title");
        form.Add(new StringContent(req.Version),                         "version");
        form.Add(new StringContent(req.Type),                            "type");
        form.Add(new StringContent(req.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)), "price");
        form.Add(new StringContent(req.Description),                     "description");
        form.Add(new StringContent(req.MinAaiaVersion),                  "minAaiaVersion");

        // ZIP-Datei
        var fileBytes  = await File.ReadAllBytesAsync(req.FilePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, "file", Path.GetFileName(req.FilePath));

        var resp = await _http.PostAsync(Url("/developers/modules"), form, ct);
        await EnsureSuccessAsync(resp, ct);

        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body);
    }

    // ── Fehler-Hilfsmethode ───────────────────────────────────────────────────

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string detail = string.Empty;
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                // WordPress gibt Fehler als {"code":"...", "message":"...", "data":{...}}
                if (root.TryGetProperty("message", out var m)) detail = m.GetString() ?? string.Empty;
                else if (root.TryGetProperty("error", out var e)) detail = e.GetString() ?? string.Empty;
                else detail = body;
            }
        }
        catch { /* Fallback */ }

        var msg = string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
            : detail;

        throw new HttpRequestException(msg, null, resp.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
