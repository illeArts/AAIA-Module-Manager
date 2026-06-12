using AAIA.Shared.Contracts.Marketplace;
using AAIA.Shared.Contracts.Publisher;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// HTTP-Client gegen die AAIA Marketplace API.
/// Kapselt alle Aufrufe — Developer-Auth, Publish, Lizenz-Check.
/// </summary>
public sealed class MarketplaceApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MarketplaceApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Developer Auth ─────────────────────────────────────────────────────────

    public async Task<DeveloperRegisterResponse> RegisterAsync(
        DeveloperRegisterRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/developers/register", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DeveloperRegisterResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    public async Task<DeveloperLoginResponse> LoginAsync(
        DeveloperLoginRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/developers/login", req, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<DeveloperLoginResponse>(_json, ct)
                     ?? throw new InvalidOperationException("Leere Antwort vom Server.");

        SetBearer(result.AccessToken);
        return result;
    }

    public async Task<DeveloperAccountDto?> GetProfileAsync(
        string etwId,
        CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/developers/{Uri.EscapeDataString(etwId)}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DeveloperAccountDto>(_json, ct);
    }

    public async Task<RegisterPublisherKeyResponse> RegisterKeyAsync(
        RegisterPublisherKeyRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/admin/publisher-keys", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RegisterPublisherKeyResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── Publish ────────────────────────────────────────────────────────────────

    public async Task<ModulePublishResponse> PublishModuleAsync(
        string moduleId,
        ModulePublishRequest req,
        CancellationToken ct = default)
    {
        var url  = $"api/marketplace/modules/{Uri.EscapeDataString(moduleId)}/publish";
        var resp = await _http.PostAsJsonAsync(url, req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ModulePublishResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── License ────────────────────────────────────────────────────────────────

    public async Task<LicenseCheckResult> CheckLicenseAsync(
        string moduleId,
        string email,
        CancellationToken ct = default)
    {
        var url  = $"api/marketplace/licenses/check?moduleId={Uri.EscapeDataString(moduleId)}&email={Uri.EscapeDataString(email)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<LicenseCheckResult>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── Token Management ───────────────────────────────────────────────────────

    public void SetBearer(string token)
    {
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearBearer() =>
        _http.DefaultRequestHeaders.Authorization = null;

    public void Dispose() => _http.Dispose();
}
