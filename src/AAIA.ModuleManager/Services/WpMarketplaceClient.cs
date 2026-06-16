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
using AAIA.Shared.Contracts.Publisher;

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

public sealed record ModuleFeedItem(
    [property: JsonPropertyName("product_id")] int     ProductId,
    [property: JsonPropertyName("code")]       string  Code,
    [property: JsonPropertyName("type")]       string  Type,
    [property: JsonPropertyName("name")]       string  Name,
    [property: JsonPropertyName("version")]    string  Version,
    [property: JsonPropertyName("price")]      decimal Price,
    [property: JsonPropertyName("currency")]   string  Currency,
    [property: JsonPropertyName("url")]        string  Url
);

public sealed record ModuleFeedResponse(
    [property: JsonPropertyName("count")] int                  Count,
    [property: JsonPropertyName("items")] List<ModuleFeedItem> Items
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

    /// <summary>
    /// POST /aaia/v1/developers/link
    /// Verknüpft den ETW-Account (aus dem Bearer-JWT) mit dem WordPress-User.
    /// Legt den WP-User bei Bedarf automatisch an.
    /// JWT erforderlich. Non-fatal — Fehler werden still ignoriert.
    /// </summary>
    public async Task<bool> LinkAccountAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync(Url("/developers/link"),
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Auth-Methoden (Migration von ASP.NET Core → WordPress) ───────────────

    /// <summary>
    /// POST /aaia/v1/auth/register
    /// Registriert einen neuen ETW-Developer-Account im WordPress-Plugin.
    /// </summary>
    public async Task<DeveloperRegisterResponse> RegisterDeveloperAsync(
        DeveloperRegisterRequest req,
        CancellationToken ct = default)
    {
        var body = new
        {
            displayName   = req.DisplayName,
            email         = req.Email,
            password      = req.Password,
            gitHubAccount = req.GitHubAccount,
        };
        var resp = await _http.PostAsJsonAsync(Url("/auth/register"), body, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeveloperRegisterResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Server.");
    }

    /// <summary>
    /// POST /aaia/v1/auth/login
    /// Login mit E-Mail + Passwort, optional TOTP-Code.
    /// </summary>
    public async Task<DeveloperLoginResponse> LoginDeveloperAsync(
        DeveloperLoginRequest req,
        CancellationToken ct = default)
    {
        var body = new
        {
            email    = req.Email,
            password = req.Password,
            totpCode = req.TotpCode,
        };
        var resp = await _http.PostAsJsonAsync(Url("/auth/login"), body, ct);
        await EnsureSuccessAsync(resp, ct);
        var result = await resp.Content.ReadFromJsonAsync<DeveloperLoginResponse>(_json, ct)
                     ?? throw new InvalidOperationException("Leere Antwort vom Server.");
        SetBearer(result.AccessToken);
        return result;
    }

    /// <summary>
    /// POST /aaia/v1/auth/verify-totp
    /// TOTP nach Registrierung bestätigen, Account aktivieren, JWT erhalten.
    /// </summary>
    public async Task<DeveloperLoginResponse> VerifyTotpAsync(
        string etwId,
        string totpCode,
        CancellationToken ct = default)
    {
        var body = new { etwId, totpCode };
        var resp = await _http.PostAsJsonAsync(Url("/auth/verify-totp"), body, ct);
        await EnsureSuccessAsync(resp, ct);
        var result = await resp.Content.ReadFromJsonAsync<DeveloperLoginResponse>(_json, ct)
                     ?? throw new InvalidOperationException("Leere Antwort vom Server.");
        SetBearer(result.AccessToken);
        return result;
    }

    /// <summary>
    /// DELETE /aaia/v1/auth/account
    /// Pending-Account löschen (Registrierung abbrechen — kein JWT nötig).
    /// </summary>
    public async Task DeleteAccountAsync(
        string etwId,
        string email,
        CancellationToken ct = default)
    {
        var body    = new { etwId, email };
        var json    = System.Text.Json.JsonSerializer.Serialize(body);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Delete, Url("/auth/account"))
        {
            Content = content,
        };
        var resp = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    /// <summary>
    /// GET /aaia/v1/developers/{etwId}
    /// Öffentliches Entwickler-Profil abrufen (JWT erforderlich).
    /// </summary>
    public async Task<DeveloperAccountDto?> GetDeveloperProfileAsync(
        string etwId,
        CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url($"/developers/{Uri.EscapeDataString(etwId)}"), ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp, ct);
        var raw = await resp.Content.ReadFromJsonAsync<WpDeveloperProfileDto>(_json, ct);
        if (raw is null) return null;
        return raw.ToAccountDto();
    }

    /// <summary>
    /// GET /aaia/v1/developers/me — Eigenes Profil abrufen.
    /// Da WP keinen /me-Alias hat, nutzen wir /developers/me (link) wenn vorhanden.
    /// Fällt zurück auf GetDeveloperProfileAsync mit der ETW-ID aus dem JWT.
    /// </summary>
    public async Task<DeveloperAccountDto?> GetMyProfileAsync(
        CancellationToken ct = default)
    {
        // Versuche zuerst den /developers/me Endpunkt (AAIA_Developer_Auth)
        var resp = await _http.GetAsync(Url("/developers/me"), ct);
        if (resp.IsSuccessStatusCode)
        {
            var raw = await resp.Content.ReadFromJsonAsync<WpDeveloperProfileDto>(_json, ct);
            return raw?.ToAccountDto();
        }
        return null;
    }

    /// <summary>
    /// POST /aaia/v1/publishers/keys
    /// Registriert den öffentlichen Publisher-Schlüssel (PEM) beim WordPress-Plugin.
    /// JWT erforderlich.
    /// </summary>
    public async Task<RegisterPublisherKeyResponse> RegisterPublisherKeyAsync(
        RegisterPublisherKeyRequest req,
        CancellationToken ct = default)
    {
        var body = new
        {
            keyId     = req.KeyId,
            publicKey = req.PublicKeyPem,
        };
        var resp = await _http.PostAsJsonAsync(Url("/publishers/keys"), body, ct);
        await EnsureSuccessAsync(resp, ct);
        var raw = await resp.Content.ReadFromJsonAsync<WpPublisherKeyResponseDto>(_json, ct)
                  ?? throw new InvalidOperationException("Leere Antwort vom Server.");
        return new RegisterPublisherKeyResponse(
            KeyId:        raw.KeyId,
            EtwId:        raw.EtwId,
            RegisteredAt: DateTimeOffset.TryParse(raw.RegisteredAt, out var dt) ? dt : DateTimeOffset.UtcNow);
    }

    // ── Interne DTOs für WordPress-JSON-Mapping ────────────────────────────────

    private sealed record WpDeveloperProfileDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("etwId")]       string  EtwId,
        [property: System.Text.Json.Serialization.JsonPropertyName("displayName")] string  DisplayName,
        [property: System.Text.Json.Serialization.JsonPropertyName("email")]       string  Email,
        [property: System.Text.Json.Serialization.JsonPropertyName("role")]        string  Role,
        [property: System.Text.Json.Serialization.JsonPropertyName("reputation")]  float   Reputation,
        [property: System.Text.Json.Serialization.JsonPropertyName("moduleCount")] int     ModuleCount,
        [property: System.Text.Json.Serialization.JsonPropertyName("keyId")]       string? KeyId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")]      string  Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("verified")]    bool    Verified)
    {
        public DeveloperAccountDto ToAccountDto()
        {
            var role   = Enum.TryParse<DeveloperRole>(Role, true, out var r)   ? r : DeveloperRole.Community;
            var status = Enum.TryParse<DeveloperStatus>(Status, true, out var s) ? s : DeveloperStatus.Active;
            return new DeveloperAccountDto(
                EtwId:        EtwId,
                DisplayName:  DisplayName,
                Email:        Email,
                GitHubAccount: null,
                NuGetProfile:  null,
                Role:         role,
                Status:       status,
                Verified:     Verified,
                KeyId:        KeyId,
                Reputation:   Reputation,
                CreatedAt:    DateTimeOffset.UtcNow,
                ModuleCount:  ModuleCount);
        }
    }

    private sealed record WpPublisherKeyResponseDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("keyId")]        string KeyId,
        [property: System.Text.Json.Serialization.JsonPropertyName("etwId")]        string EtwId,
        [property: System.Text.Json.Serialization.JsonPropertyName("registeredAt")] string RegisteredAt);

    // ── Fehler-Hilfsmethode ────────────────────────────────────────────────────

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
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error",   out var e)) detail = e.GetString() ?? string.Empty;
                else if (root.TryGetProperty("message", out var m)) detail = m.GetString() ?? string.Empty;
                else detail = body;
            }
            else if (!string.IsNullOrWhiteSpace(body))
            {
                detail = $"HTTP {(int)resp.StatusCode}: Server antwortete kein JSON. Möglicherweise Cloudflare-Block oder falsche API-URL.";
            }
        }
        catch { /* Fallback auf Status */ }

        var msg = string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
            : detail;

        throw new HttpRequestException(msg, null, resp.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
