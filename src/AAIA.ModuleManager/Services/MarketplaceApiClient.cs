using AAIA.Shared.Contracts.Marketplace;
using AAIA.Shared.Contracts.Publisher;
using AAIA.Shared.Contracts.Routes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// HTTP-Client gegen die AAIA Marketplace Backend-API (ASP.NET Core).
/// Basis-URL: AppConfig.MarketplaceBackendApiUrl, z.B. "https://api.marketplace.aaia.app".
/// Alle Routen werden aus AaiaApiRoutes-Konstanten gebaut (/api/...).
/// </summary>
public sealed class MarketplaceApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public MarketplaceApiClient(string baseUrl)
    {
        var uri = new Uri(baseUrl.TrimEnd('/') + "/");

        // SSL-Fehler auf localhost ignorieren (Entwicklungsumgebung).
        var handler = new HttpClientHandler();
        if (uri.Host is "localhost" or "127.0.0.1" or "::1")
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { BaseAddress = uri };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }



    // ── Developer Auth ─────────────────────────────────────────────────────────

    public async Task<DeveloperRegisterResponse> RegisterAsync(
        DeveloperRegisterRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(AaiaApiRoutes.Developers.Register, req, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeveloperRegisterResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    public async Task<DeveloperLoginResponse> LoginAsync(
        DeveloperLoginRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(AaiaApiRoutes.Developers.Login, req, ct);
        await EnsureSuccessAsync(resp, ct);
        var result = await resp.Content.ReadFromJsonAsync<DeveloperLoginResponse>(_json, ct)
                     ?? throw new InvalidOperationException("Leere Antwort vom Server.");
        SetBearer(result.AccessToken);
        return result;
    }

    /// <summary>
    /// TOTP nach Registrierung bestätigen.
    /// Server aktiviert den Account und stellt direkt einen JWT aus.
    /// </summary>
    public async Task<DeveloperLoginResponse> VerifyTotpAsync(
        string etwId,
        string totpCode,
        CancellationToken ct = default)
    {
        var body = new { etwId, totpCode };
        var resp = await _http.PostAsJsonAsync(AaiaApiRoutes.Developers.VerifyTotp, body, ct);
        await EnsureSuccessAsync(resp, ct);
        var result = await resp.Content.ReadFromJsonAsync<DeveloperLoginResponse>(_json, ct)
                     ?? throw new InvalidOperationException("Leere Antwort vom Server.");
        SetBearer(result.AccessToken);
        return result;
    }

    public async Task<DeveloperAccountDto?> GetProfileAsync(
        string etwId,
        CancellationToken ct = default)
    {
        var url  = AaiaApiRoutes.Developers.GetById.Replace("{etwId}", Uri.EscapeDataString(etwId));
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeveloperAccountDto>(_json, ct);
    }

    public async Task<DeveloperAccountDto?> GetMyProfileAsync(
        CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(AaiaApiRoutes.Developers.Me, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeveloperAccountDto>(_json, ct);
    }

    public async Task<RegisterPublisherKeyResponse> RegisterKeyAsync(
        RegisterPublisherKeyRequest req,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(AaiaApiRoutes.Developers.RegisterKey, req, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<RegisterPublisherKeyResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── Publish ────────────────────────────────────────────────────────────────

    public async Task<ModulePublishResponse> PublishModuleAsync(
        string moduleId,
        ModulePublishRequest req,
        CancellationToken ct = default)
    {
        var url  = AaiaApiRoutes.Marketplace.Publish
                       .Replace("{id}", Uri.EscapeDataString(moduleId));
        var resp = await _http.PostAsJsonAsync(url, req, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ModulePublishResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── Account löschen ───────────────────────────────────────────────────────

    /// <summary>
    /// Pending-Account löschen (nur E-Mail als Credential — kein Bearer nötig).
    /// </summary>
    public async Task DeleteAccountAsync(
        string etwId,
        string email,
        CancellationToken ct = default)
    {
        var url  = AaiaApiRoutes.Developers.Delete
                       .Replace("{etwId}", Uri.EscapeDataString(etwId));
        var body = new { email };
        var resp = await _http.PostAsJsonAsync(url, body, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // ── License ────────────────────────────────────────────────────────────────

    public async Task<LicenseCheckResult> CheckLicenseAsync(
        string moduleId,
        string email,
        CancellationToken ct = default)
    {
        var url  = $"{AaiaApiRoutes.Marketplace.LicenseCheck}" +
                   $"?moduleId={Uri.EscapeDataString(moduleId)}" +
                   $"&email={Uri.EscapeDataString(email)}";
        var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<LicenseCheckResult>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    // ── Fehler-Hilfsmethode ────────────────────────────────────────────────────

    /// <summary>
    /// Liest bei HTTP-Fehler den JSON-Body aus und wirft eine Exception
    /// mit dem "error"- oder "message"-Feld des Servers — statt des generischen Statuscodes.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string detail = string.Empty;
        try
        {
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!string.IsNullOrWhiteSpace(body) && contentType.Contains("application/json"))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error",   out var e)) detail = e.GetString() ?? string.Empty;
                else if (root.TryGetProperty("message", out var m)) detail = m.GetString() ?? string.Empty;
                else detail = body;
            }
            else if (!string.IsNullOrWhiteSpace(body))
            {
                // Kein JSON — wahrscheinlich Cloudflare-Block (HTML-Fehlerseite)
                detail = $"HTTP {(int)resp.StatusCode}: Server antwortete kein JSON. Möglicherweise Cloudflare-Block oder falsche API-URL.";
            }
        }
        catch { /* Fallback auf Status */ }

        var msg = string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
            : detail;

        throw new HttpRequestException(msg, null, resp.StatusCode);
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
