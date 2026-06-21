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

    // ── Phase 5.9: Developer Dashboard ───────────────────────────────────────

    /// <summary>Lädt das vollständige ETW-Dashboard (alle Extensions + Statistiken).</summary>
    public async Task<DeveloperDashboardDto?> GetDeveloperDashboardAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync(AaiaApiRoutes.Developers.Dashboard, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<DeveloperDashboardDto>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetDeveloperDashboardAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>Verkaufsstatistiken für eine Extension (letzte 12 Monate).</summary>
    public async Task<ExtensionSalesSummaryDto?> GetSalesSummaryAsync(
        string extensionId, CancellationToken ct = default)
    {
        try
        {
            var url  = AaiaApiRoutes.Developers.ExtensionSalesSummary
                           .Replace("{extensionId}", Uri.EscapeDataString(extensionId));
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ExtensionSalesSummaryDto>(JsonOptions, ct);
        }
        catch { return null; }
    }

    /// <summary>Webhook-Events für eine Extension (max. 50, neueste zuerst)
    // ── Phase 5.10: Admin Marketplace Console ────────────────────────────────

    /// <summary>Gesamtübersicht der Plattform (nur Owner/Admin).</summary>
    public async Task<MarketplaceOverviewDto?> GetMarketplaceOverviewAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync(AaiaApiRoutes.Admin.MarketplaceOverview, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<MarketplaceOverviewDto>(JsonOptions, ct);
        }
        catch { return null; }
    }

    /// <summary>Extension-Liste für Admin-View.</summary>
    public async Task<IReadOnlyList<AdminExtensionDto>> GetAdminExtensionsAsync(
        string? search = null, string? riskLevel = null, bool? published = null,
        CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string> { "pageSize=100" };
            if (search    is not null) qs.Add($"search={Uri.EscapeDataString(search)}");
            if (riskLevel is not null) qs.Add($"riskLevel={Uri.EscapeDataString(riskLevel)}");
            if (published.HasValue)    qs.Add($"published={published.Value.ToString().ToLower()}");
            var resp = await _http.GetAsync($"{AaiaApiRoutes.Admin.AdminExtensions}?{string.Join("&", qs)}", ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<AdminExtensionDto>();
            var list = await resp.Content.ReadFromJsonAsync<List<AdminExtensionDto>>(JsonOptions, ct);
            return list ?? (IReadOnlyList<AdminExtensionDto>)Array.Empty<AdminExtensionDto>();
        }
        catch { return Array.Empty<AdminExtensionDto>(); }
    }

    /// <summary>Pending-Review-Releases (Owner-only).</summary>
    public async Task<IReadOnlyList<PendingReviewReleaseDto>> GetPendingReviewsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync(AaiaApiRoutes.Admin.PendingReviews, ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<PendingReviewReleaseDto>();
            var list = await resp.Content.ReadFromJsonAsync<List<PendingReviewReleaseDto>>(JsonOptions, ct);
            return list ?? (IReadOnlyList<PendingReviewReleaseDto>)Array.Empty<PendingReviewReleaseDto>();
        }
        catch { return Array.Empty<PendingReviewReleaseDto>(); }
    }

    /// <summary>Release blockieren.</summary>
    public async Task<BlockReleaseResponse?> BlockReleaseAsync(
        int releaseId, string reason, CancellationToken ct = default)
    {
        try
        {
            var url  = AaiaApiRoutes.Admin.BlockRelease.Replace("{releaseId:int}", releaseId.ToString());
            var resp = await _http.PostAsJsonAsync(url, new BlockReleaseRequest(reason), ct);
            return await resp.Content.ReadFromJsonAsync<BlockReleaseResponse>(JsonOptions, ct);
        }
        catch { return null; }
    }

    /// <summary>Release-Blockierung aufheben.</summary>
    public async Task<BlockReleaseResponse?> UnblockReleaseAsync(int releaseId, CancellationToken ct = default)
    {
        try
        {
            var url  = AaiaApiRoutes.Admin.UnblockRelease.Replace("{releaseId:int}", releaseId.ToString());
            var resp = await _http.PostAsJsonAsync(url, new { }, ct);
            return await resp.Content.ReadFromJsonAsync<BlockReleaseResponse>(JsonOptions, ct);
        }
        catch { return null; }
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();
}
