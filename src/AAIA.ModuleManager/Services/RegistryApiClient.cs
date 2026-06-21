using AAIA.Shared.Contracts.Marketplace;
using AAIA.Shared.Contracts.Routes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// HTTP-Client für die aaia-marketplace-api Registry-Endpunkte (Phase 5.2).
///
/// Trennt sich bewusst von WpMarketplaceClient (WordPress REST API) —
/// diese Klasse spricht ausschließlich gegen die ASP.NET Core Marketplace API.
///
/// Alle GET-Endpunkte sind anonym zugänglich.
/// PublishRelease erfordert einen Bearer-Token (Developer JWT).
/// </summary>
public sealed class RegistryApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public RegistryApiClient(string marketplaceApiBaseUrl)
    {
        var baseUri = new Uri(marketplaceApiBaseUrl.TrimEnd('/') + "/");
        var handler = new HttpClientHandler();

        // Localhost-Entwicklungsumgebung: Zertifikat-Prüfung deaktivieren
        if (baseUri.Host is "localhost" or "127.0.0.1" or "::1")
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        _http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout     = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Token ──────────────────────────────────────────────────────────────────

    public void SetBearer(string token)
        => _http.DefaultRequestHeaders.Authorization =
               new AuthenticationHeaderValue("Bearer", token);

    public void ClearBearer()
        => _http.DefaultRequestHeaders.Authorization = null;

    // ── GET: Extension-Liste ──────────────────────────────────────────────────

    /// <summary>
    /// Lädt den öffentlichen Marketplace-Katalog (alle veröffentlichten Extensions).
    /// Kein JWT erforderlich.
    /// </summary>
    public async Task<RegistryExtensionListResponse> GetExtensionsAsync(
        int page     = 1,
        int pageSize = 50,
        string? category = null,
        string? search   = null,
        CancellationToken ct = default)
    {
        var url = BuildUrl(AaiaApiRoutes.Marketplace.RegistryList, page, pageSize, category, search);
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<RegistryExtensionListResponse>(JsonOptions, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Registry-Endpoint.");
    }

    // ── GET: Extension-Details ────────────────────────────────────────────────

    /// <summary>Details zu einer Extension inkl. neuester Release-Info.</summary>
    public async Task<RegistryExtensionDto?> GetExtensionAsync(
        string extensionId,
        CancellationToken ct = default)
    {
        var url  = AaiaApiRoutes.Marketplace.RegistryById.Replace("{extensionId}", Uri.EscapeDataString(extensionId));
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<RegistryExtensionDto>(JsonOptions, ct);
    }

    // ── GET: Release-Liste ────────────────────────────────────────────────────

    /// <summary>Alle veröffentlichten Releases einer Extension.</summary>
    public async Task<RegistryReleaseListResponse?> GetReleasesAsync(
        string extensionId,
        CancellationToken ct = default)
    {
        var url  = AaiaApiRoutes.Marketplace.RegistryReleases.Replace("{extensionId}", Uri.EscapeDataString(extensionId));
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<RegistryReleaseListResponse>(JsonOptions, ct);
    }

    // ── GET: Release-Details ──────────────────────────────────────────────────

    public async Task<RegistryReleaseDto?> GetReleaseAsync(
        string extensionId,
        string version,
        CancellationToken ct = default)
    {
        var url = AaiaApiRoutes.Marketplace.RegistryRelease
            .Replace("{extensionId}", Uri.EscapeDataString(extensionId))
            .Replace("{version}",     Uri.EscapeDataString(version));
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<RegistryReleaseDto>(JsonOptions, ct);
    }

    // ── GET: Lizenzstatus (Phase 5.7b) ────────────────────────────────────────

    /// <summary>
    /// Lizenzstatus des eingeloggten Käufers für eine Extension.
    /// JWT muss gesetzt sein (SetBearer nach erfolgreichem Login).
    ///
    /// Rückgabe: null wenn Endpoint unerreichbar oder Extension nicht gefunden.
    /// Fehler werden als Exception weitergeleitet — Aufrufer kümmert sich um UX.
    /// </summary>
    public async Task<ExtensionLicenseStatusDto?> GetLicenseStatusAsync(
        string extensionId,
        CancellationToken ct = default)
    {
        var url = AaiaApiRoutes.Marketplace.ExtensionLicenseStatus
            .Replace("{extensionId}", Uri.EscapeDataString(extensionId));

        var resp = await _http.GetAsync(url, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)   return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Nicht angemeldet oder Token abgelaufen.");

        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ExtensionLicenseStatusDto>(JsonOptions, ct);
    }

    // ── POST: Release veröffentlichen ─────────────────────────────────────────

    /// <summary>
    /// Verifiziertes Release auf IsPublished=true setzen.
    /// JWT muss gesetzt sein (SetBearer).
    /// </summary>
    public async Task<PublishReleaseResponse> PublishReleaseAsync(
        string extensionId,
        string version,
        string? changelog = null,
        CancellationToken ct = default)
    {
        var url = AaiaApiRoutes.Marketplace.PublishRelease
            .Replace("{extensionId}", Uri.EscapeDataString(extensionId))
            .Replace("{version}",     Uri.EscapeDataString(version));

        var body = new PublishReleaseRequest(changelog);
        var resp = await _http.PostAsJsonAsync(url, body, ct);

        // Auch bei Fehler-HTTP-Status das DTO lesen (enthält Error-Detail)
        var dto = await resp.Content.ReadFromJsonAsync<PublishReleaseResponse>(JsonOptions, ct);
        return dto ?? new PublishReleaseResponse(false, extensionId, version, null, null, $"HTTP {(int)resp.StatusCode}");
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static string BuildUrl(string baseRoute, int page, int pageSize, string? category, string? search)
    {
        var qs = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };
        if (!string.IsNullOrWhiteSpace(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(search))   qs.Add($"search={Uri.EscapeDataString(search)}");
        return $"{baseRoute}?{string.Join("&", qs)}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string detail = $"HTTP {(int)resp.StatusCode}";
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error",   out var e)) detail = e.GetString() ?? detail;
                else if (root.TryGetProperty("message", out var m)) detail = m.GetString() ?? detail;
            }
        }
        catch { /* Fallback auf HTTP-Status */ }
        throw new HttpRequestException(detail, null, resp.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
