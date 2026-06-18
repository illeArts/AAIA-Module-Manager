using AAIA.Shared.Contracts.Marketplace;
using AAIA.Shared.Contracts.Publisher;
using AAIA.Shared.Contracts.Routes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Fasade für alle Marketplace-API-Aufrufe.
///
/// Auth-Methoden (Register, Login, VerifyTotp, DeleteAccount, GetProfile, UploadPublicKey)
/// leiten intern an <see cref="WpMarketplaceClient"/> weiter — sie landen jetzt direkt
/// bei der WordPress REST API (aaiagent.de/wp-json/aaia/v1/...).
///
/// Nicht-Auth-Methoden (Publish, CheckLicense) verwenden weiterhin den internen
/// HttpClient gegen die konfigurierte Basis-URL (Rückwärtskompatibilität).
///
/// Die öffentliche Signatur aller Methoden bleibt identisch — keine Änderungen
/// an Viewmodels oder aufrufenden Services nötig.
/// </summary>
public sealed class MarketplaceApiClient : IDisposable
{
    /// <summary>
    /// Interner HttpClient für Nicht-Auth-Aufrufe (Publish, LicenseCheck).
    /// Wird nur noch für Methoden genutzt, die noch keine WP-REST-Route haben.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Delegiert alle Auth-Methoden an die WordPress REST API.
    /// </summary>
    private readonly WpMarketplaceClient _wp;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <param name="wpApiUrl">
    ///   WordPress REST Basis-URL, z.B.
    ///   "https://aaiagent.de/index.php?rest_route=/aaia/v1".
    ///   Wird für Auth-Methoden an WpMarketplaceClient übergeben.
    ///   Der interne HttpClient nutzt dieselbe URL als Fallback-Basis.
    /// </param>
    public MarketplaceApiClient(string wpApiUrl)
    {
        // WpMarketplaceClient übernimmt alle Auth-Routen
        _wp = new WpMarketplaceClient(wpApiUrl);

        // Interner HttpClient für verbleibende Routen (Publish etc.)
        // Basis-URL: Host aus der WP-URL ohne Query-String
        var wpUri    = new Uri(wpApiUrl);
        var baseUri  = new Uri($"{wpUri.Scheme}://{wpUri.Authority}/");

        var handler = new HttpClientHandler();
        if (wpUri.Host is "localhost" or "127.0.0.1" or "::1")
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { BaseAddress = baseUri };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Developer Auth — delegiert an WpMarketplaceClient ────────────────────

    /// <summary>
    /// Registriert einen neuen ETW-Developer-Account.
    /// POST /aaia/v1/auth/register (WordPress REST API)
    /// </summary>
    public async Task<DeveloperRegisterResponse> RegisterAsync(
        DeveloperRegisterRequest req,
        CancellationToken ct = default)
        => await _wp.RegisterDeveloperAsync(req, ct);

    /// <summary>
    /// Login mit E-Mail + Passwort, optional TOTP-Code.
    /// POST /aaia/v1/auth/login (WordPress REST API)
    /// Setzt den Bearer-Token intern nach erfolgreichem Login.
    /// </summary>
    public async Task<DeveloperLoginResponse> LoginAsync(
        DeveloperLoginRequest req,
        CancellationToken ct = default)
    {
        var result = await _wp.LoginDeveloperAsync(req, ct);
        // Bearer auch im internen HttpClient setzen (für verbleibende Nicht-Auth-Routen)
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return result;
    }

    /// <summary>
    /// TOTP nach Registrierung bestätigen, Account aktivieren, JWT erhalten.
    /// POST /aaia/v1/auth/verify-totp (WordPress REST API)
    /// </summary>
    public async Task<DeveloperLoginResponse> VerifyTotpAsync(
        string etwId,
        string totpCode,
        CancellationToken ct = default)
    {
        var result = await _wp.VerifyTotpAsync(etwId, totpCode, ct);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return result;
    }

    /// <summary>
    /// Entwickler-Profil abrufen.
    /// GET /aaia/v1/developers/{etwId} (WordPress REST API)
    /// </summary>
    public async Task<DeveloperAccountDto?> GetProfileAsync(
        string etwId,
        CancellationToken ct = default)
        => await _wp.GetDeveloperProfileAsync(etwId, ct);

    /// <summary>
    /// Eigenes Profil abrufen (/developers/me — WP: via JWT aus Token).
    /// Fällt zurück auf GetProfileAsync mit der gespeicherten ETW-ID.
    /// </summary>
    public async Task<DeveloperAccountDto?> GetMyProfileAsync(
        CancellationToken ct = default)
        => await _wp.GetMyProfileAsync(ct);

    /// <summary>
    /// Publisher-Schlüssel registrieren.
    /// POST /aaia/v1/publishers/keys (WordPress REST API)
    /// </summary>
    public async Task<RegisterPublisherKeyResponse> RegisterKeyAsync(
        RegisterPublisherKeyRequest req,
        CancellationToken ct = default)
        => await _wp.RegisterPublisherKeyAsync(req, ct);

    /// <summary>
    /// Pending-Account löschen (Registrierung abbrechen).
    /// DELETE /aaia/v1/auth/account (WordPress REST API)
    /// </summary>
    public async Task DeleteAccountAsync(
        string etwId,
        string email,
        CancellationToken ct = default)
        => await _wp.DeleteAccountAsync(etwId, email, ct);

    // ── Publish ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Modul veröffentlichen über WordPress REST API (POST /aaia/v1/developers/modules).
    /// <paramref name="filePath"/> muss auf die .aaix-Datei zeigen (multipart upload).
    /// Ist kein Pfad angegeben, schlägt die Operation mit einem klaren Fehler fehl.
    /// </summary>
    public async Task<ModulePublishResponse> PublishModuleAsync(
        string moduleId,
        ModulePublishRequest req,
        CancellationToken ct = default,
        string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return new ModulePublishResponse(
                Success:        false,
                ModuleId:       moduleId,
                Version:        req.Version,
                DownloadUrl:    null,
                MarketplaceUrl: null,
                Error:          "Kein gültiger Dateipfad angegeben. Marketplace-Upload erfordert die .aaix-Datei.");
        }

        try
        {
            using var resultDoc = await _wp.SubmitModuleAsync(new ModuleUploadRequest
            {
                Title          = moduleId,
                Version        = req.Version,
                Type           = "module",
                Description    = req.Changelog ?? string.Empty,
                FilePath       = filePath,
            }, ct);

            var root      = resultDoc.RootElement;
            var productId = root.TryGetProperty("productId", out var pid) ? pid.GetInt32() : 0;
            var url       = root.TryGetProperty("url",       out var u)   ? u.GetString()  : null;

            return new ModulePublishResponse(
                Success:        productId > 0,
                ModuleId:       moduleId,
                Version:        req.Version,
                DownloadUrl:    null,
                MarketplaceUrl: url,
                Error:          productId == 0 ? "Server lieferte keine Produkt-ID." : null);
        }
        catch (Exception ex)
        {
            return new ModulePublishResponse(
                Success:        false,
                ModuleId:       moduleId,
                Version:        req.Version,
                DownloadUrl:    null,
                MarketplaceUrl: null,
                Error:          ex.Message);
        }
    }

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
                using var doc = JsonDocument.Parse(body);
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

    // ── Token Management ───────────────────────────────────────────────────────

    public void SetBearer(string token)
    {
        _wp.SetBearer(token);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearBearer()
    {
        _wp.ClearBearer();
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public void Dispose()
    {
        _wp.Dispose();
        _http.Dispose();
    }
}
