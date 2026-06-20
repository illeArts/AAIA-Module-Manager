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
using AAIA.Shared.Contracts.Marketplace;
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
    [property: JsonPropertyName("product_id")]    int          ProductId,
    [property: JsonPropertyName("code")]          string       Code,
    [property: JsonPropertyName("type")]          string       Type,
    [property: JsonPropertyName("name")]          string       Name,
    [property: JsonPropertyName("version")]       string       Version,
    [property: JsonPropertyName("price")]         decimal      Price,
    [property: JsonPropertyName("currency")]      string       Currency,
    [property: JsonPropertyName("url")]           string       Url,
    [property: JsonPropertyName("permissions")]   JsonElement? Permissions    = null,
    [property: JsonPropertyName("approvalToken")] string?      ApprovalToken  = null
);

public sealed record ModuleFeedResponse(
    [property: JsonPropertyName("count")] int                  Count,
    [property: JsonPropertyName("items")] List<ModuleFeedItem> Items
);

public sealed class ModuleUploadRequest
{
    public string Title          { get; init; } = "";
    public string Version        { get; init; } = "1.0.0";
    /// <summary>plugin | module | languagepack</summary>
    public string Type           { get; init; } = "module";
    public decimal Price         { get; init; } = 0m;
    public string Description    { get; init; } = "";
    public string MinAaiaVersion { get; init; } = "1.0.0";
    /// <summary>Absolute path to the ZIP file on disk.</summary>
    public string FilePath       { get; init; } = "";
    /// <summary>Zielsprache für LanguagePacks (z.B. "de-DE"). Null für Module/Plugins.</summary>
    public string? TargetLocale  { get; init; } = null;
}

// ── Debug DTOs ────────────────────────────────────────────────────────────────

public sealed class DebugSystemInfoDto
{
    [JsonPropertyName("php_version")]         public string PhpVersion         { get; init; } = "";
    [JsonPropertyName("wp_version")]          public string WpVersion          { get; init; } = "";
    [JsonPropertyName("wc_version")]          public string WcVersion          { get; init; } = "";
    [JsonPropertyName("aaia_plugin_version")] public string AaiaPluginVersion  { get; init; } = "";
    [JsonPropertyName("aaia_db_version")]     public string AaiaDbVersion      { get; init; } = "";
    [JsonPropertyName("aaia_db_expected")]    public string AaiaDbExpected     { get; init; } = "";
    [JsonPropertyName("db_prefix")]           public string DbPrefix           { get; init; } = "";
    [JsonPropertyName("php_memory_limit")]    public string PhpMemoryLimit     { get; init; } = "";
    [JsonPropertyName("memory_peak_mb")]      public double MemoryPeakMb       { get; init; }
    [JsonPropertyName("upload_max_filesize")] public string UploadMaxFilesize  { get; init; } = "";
    [JsonPropertyName("post_max_size")]       public string PostMaxSize        { get; init; } = "";
    [JsonPropertyName("max_execution_time")]  public string MaxExecutionTime   { get; init; } = "";
    [JsonPropertyName("wp_debug")]            public bool   WpDebug            { get; init; }
    [JsonPropertyName("wp_debug_log")]        public bool   WpDebugLog         { get; init; }
    [JsonPropertyName("wp_debug_display")]    public bool   WpDebugDisplay     { get; init; }
    [JsonPropertyName("rest_url")]            public string RestUrl            { get; init; } = "";
    [JsonPropertyName("home_url")]            public string HomeUrl            { get; init; } = "";
    [JsonPropertyName("time_utc")]            public string TimeUtc            { get; init; } = "";
    [JsonPropertyName("timezone")]            public string Timezone           { get; init; } = "";
    [JsonPropertyName("active_plugins")]      public List<DebugPluginDto> ActivePlugins { get; init; } = new();
}

public sealed class DebugPluginDto
{
    [JsonPropertyName("file")]    public string File    { get; init; } = "";
    [JsonPropertyName("name")]    public string Name    { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
}

public sealed class DebugLogSectionDto
{
    [JsonPropertyName("path")]     public string   Path     { get; init; } = "";
    [JsonPropertyName("size")]     public long     Size     { get; init; }
    [JsonPropertyName("modified")] public string?  Modified { get; init; }
    [JsonPropertyName("lines")]    public string[] Lines    { get; init; } = Array.Empty<string>();
}

public sealed class DebugLogsDto
{
    [JsonPropertyName("requested_lines")] public int                 RequestedLines { get; init; }
    [JsonPropertyName("wp_debug_log")]    public DebugLogSectionDto? WpDebugLog     { get; init; }
    [JsonPropertyName("php_error_log")]   public DebugLogSectionDto? PhpErrorLog    { get; init; }
}

public sealed class DebugTableDto
{
    [JsonPropertyName("name")]         public string Name        { get; init; } = "";
    [JsonPropertyName("rows")]         public int    Rows        { get; init; }
    [JsonPropertyName("data_bytes")]   public long   DataBytes   { get; init; }
    [JsonPropertyName("index_bytes")]  public long   IndexBytes  { get; init; }
    [JsonPropertyName("engine")]       public string Engine      { get; init; } = "";
    [JsonPropertyName("collation")]    public string Collation   { get; init; } = "";
}

// ── Client ─────────────────────────────────────────────────────────────────────

/// <summary>
/// HTTP-Client gegen die AAIA Marketplace WordPress REST API.
/// Basis-URL aus AppConfig.MarketplaceApiUrl, z.B.
///   "https://aaiagent.de/index.php?rest_route=/aaia/v1"
/// Auth: Bearer JWT (ETW-JWT aus Login).
/// </summary>
public sealed partial class WpMarketplaceClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Basis-Host-URL ohne Query, z.B. "https://aaiagent.de/index.php"</summary>
    private readonly string _hostUrl;

    /// <summary>Rest-Route-Prefix, z.B. "/aaia/v1"</summary>
    private readonly string _routePrefix;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Wiederverwendbare Options-Instanz für indentiertes JSON-Serialisieren.</summary>
    private static readonly JsonSerializerOptions _jsonIndented = new() { WriteIndented = true };

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
    /// Die API gibt ein Wrapper-Objekt zurück: { etwId, count, items: [...] }
    /// </summary>
    public async Task<List<MarketplaceModuleDto>> GetMyModulesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/developers/me/modules"), ct);
        await EnsureSuccessAsync(resp, ct);

        // API liefert { etwId, count, items: [...] } — kein raw Array
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        // Wrapper-Format (AAIA_ETW_Modules)
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var itemsEl))
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<MarketplaceModuleDto>>(
                itemsEl.GetRawText(), _json) ?? new List<MarketplaceModuleDto>();
        }

        // Fallback: direktes Array-Format
        return System.Text.Json.JsonSerializer.Deserialize<List<MarketplaceModuleDto>>(
            root.GetRawText(), _json) ?? new List<MarketplaceModuleDto>();
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
    /// GET /aaia/v1/modules/feed
    /// Öffentlicher Marketplace-Feed — alle verfügbaren Module (kein JWT nötig).
    /// </summary>
    public async Task<ModuleFeedResponse> GetModulesFeedAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/modules/feed"), ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ModuleFeedResponse>(_json, ct)
               ?? new ModuleFeedResponse(0, new List<ModuleFeedItem>());
    }

    /// <summary>
    /// GET /aaia/v1/modules/{id}/verify-approval?token={token}
    /// Prüft ob ein Modul den AAIA Parkausweis (Admin-Freigabe) hat.
    /// Gibt nur true/false zurück — keine Scan-Details, kein Regelwerk.
    /// Kein JWT erforderlich (öffentlicher Endpoint).
    /// </summary>
    public async Task<bool> VerifyApprovalAsync(
        int    productId,
        string approvalToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(approvalToken)) return false;
        try
        {
            var url  = Url($"/modules/{productId}/verify-approval") + "&token=" + Uri.EscapeDataString(approvalToken);
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("approved", out var v) && v.GetBoolean();
        }
        catch
        {
            // Im Offline-Fall oder Netzwerkfehler: Token aus dem Feed als Vertrauensbasis nutzen
            return false;
        }
    }

    /// <summary>
    /// POST /aaia/v1/licenses/activate
    /// Aktiviert eine WooCommerce-Lizenz im Module Manager.
    /// Kein JWT erforderlich — LicenseKey + BuyerEmail sind das Credential.
    /// Gibt LicenseJwt (Module/Plugin) oder DownloadUrl (LanguagePack) zurück.
    /// </summary>
    public async Task<LicenseActivationResponse> ActivateLicenseAsync(
        LicenseActivationRequest req,
        CancellationToken ct = default)
    {
        var body = new
        {
            licenseKey = req.LicenseKey,
            buyerEmail = req.BuyerEmail,
            deviceId   = req.DeviceId,
            moduleId   = req.ModuleId,
        };
        var resp = await _http.PostAsJsonAsync(Url("/licenses/activate"), body, _json, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<LicenseActivationResponse>(_json, ct)
               ?? throw new InvalidOperationException("Leere Antwort vom Marketplace-Server.");
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
        if (!string.IsNullOrWhiteSpace(req.TargetLocale))
            form.Add(new StringContent(req.TargetLocale),                "targetLocale");

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

    /// <summary>
    /// Wirft eine <see cref="HttpRequestException"/> wenn:
    ///   (a) der HTTP-Status kein 2xx ist, oder
    ///   (b) der Body HTML enthält (WordPress PHP-Absturz mit 200 OK).
    /// Letzteres verhindert, dass raw HTML als JSON geparst wird und als
    /// kryptische JsonException nach oben durchsickert.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var body        = string.Empty;

        try { body = await resp.Content.ReadAsStringAsync(ct); }
        catch { /* Fallback: leerer Body */ }

        // ── 200 OK + HTML = WordPress-PHP-Absturz ─────────────────────────────
        if (resp.IsSuccessStatusCode && IsHtmlBody(contentType, body))
        {
            var plain = StripHtml(body);
            throw new HttpRequestException(
                $"Server-Fehler (WordPress): {plain}",
                null,
                System.Net.HttpStatusCode.InternalServerError);
        }

        if (resp.IsSuccessStatusCode) return;

        // ── Nicht-2xx ─────────────────────────────────────────────────────────
        string detail = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(body) && contentType.Contains("application/json"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                if      (root.TryGetProperty("error",   out var e)) detail = e.GetString() ?? string.Empty;
                else if (root.TryGetProperty("message", out var m)) detail = m.GetString() ?? string.Empty;
                else detail = body;
            }
            else if (IsHtmlBody(contentType, body))
            {
                detail = $"HTTP {(int)resp.StatusCode}: {StripHtml(body)}";
            }
            else if (!string.IsNullOrWhiteSpace(body))
            {
                detail = $"HTTP {(int)resp.StatusCode}: Server antwortete kein JSON.";
            }
        }
        catch { /* Fallback auf Status */ }

        var msg = string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
            : detail;

        throw new HttpRequestException(msg, null, resp.StatusCode);
    }

    private static bool IsHtmlBody(string contentType, string body) =>
        contentType.Contains("text/html") ||
        (body.TrimStart().StartsWith('<') && body.Contains("</"));

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
    private static partial System.Text.RegularExpressions.Regex HtmlTagRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex WhitespaceRegex();

    /// <summary>Entfernt HTML-Tags und dekodiert die häufigsten HTML-Entities.</summary>
    private static string StripHtml(string html)
    {
        var text = HtmlTagRegex().Replace(html, " ");
        text = text.Replace("&amp;",  "&")
                   .Replace("&lt;",   "<")
                   .Replace("&gt;",   ">")
                   .Replace("&quot;", "\"")
                   .Replace("&#039;", "'")
                   .Replace("&nbsp;", " ");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    // ── Owner-only Debug-Methoden ─────────────────────────────────────────────

    /// <summary>
    /// GET /aaia/v1/debug/info  (Owner-JWT erforderlich)
    /// System-Info: PHP, WP, WC, Plugin-Version, Speicher, aktive Plugins.
    /// </summary>
    public async Task<DebugSystemInfoDto> GetDebugInfoAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/debug/info"), ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DebugSystemInfoDto>(_json, ct)
               ?? new DebugSystemInfoDto();
    }

    /// <summary>
    /// GET /aaia/v1/debug/logs?lines=N  (Owner-JWT erforderlich)
    /// WP-Debug-Log + PHP-Error-Log, letzte N Zeilen.
    /// </summary>
    public async Task<DebugLogsDto> GetDebugLogsAsync(int lines = 200, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url($"/debug/logs") + $"&lines={lines}", ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DebugLogsDto>(_json, ct)
               ?? new DebugLogsDto();
    }

    /// <summary>
    /// DELETE /aaia/v1/debug/logs  (Owner-JWT erforderlich)
    /// Leert das WP-Debug-Log.
    /// </summary>
    public async Task<string> ClearDebugLogsAsync(CancellationToken ct = default)
    {
        var req  = new HttpRequestMessage(HttpMethod.Delete, Url("/debug/logs"));
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "OK" : "OK";
    }

    /// <summary>
    /// GET /aaia/v1/debug/last-error  (Owner-JWT erforderlich)
    /// Letzter vom PHP Fatal-Catcher aufgezeichneter Fehler (Transient).
    /// Gibt Pretty-Printed JSON zurück.
    /// </summary>
    public async Task<string> GetLastRestFatalAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/debug/last-error"), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, _jsonIndented);
        }
        catch { return body; }
    }

    /// <summary>
    /// GET /aaia/v1/debug/tables  (Owner-JWT erforderlich)
    /// AAIA-Datenbanktabellen mit Zeilenzahlen und Größen.
    /// </summary>
    public async Task<List<DebugTableDto>> GetDebugTablesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(Url("/debug/tables"), ct);
        await EnsureSuccessAsync(resp, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("tables", out var tables))
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<DebugTableDto>>(
                tables.GetRawText(), _json) ?? new List<DebugTableDto>();
        }
        return new List<DebugTableDto>();
    }

    public void Dispose() => _http.Dispose();
}
