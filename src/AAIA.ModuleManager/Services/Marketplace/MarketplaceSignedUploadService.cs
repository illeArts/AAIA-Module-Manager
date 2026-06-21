using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.Marketplace;

/// <summary>
/// Führt den multipart-Upload eines ETW-signierten Releases an den Marketplace durch.
///
/// Sicherheitsregel (unveränderlich):
///   Dieser Service lädt NIEMALS hoch:
///   - Private Keys
///   - MarketplaceToken / API-Keys / Secrets
///   - Quellcode
///
///   Nur folgende Dateien werden per multipart übertragen:
///   - package      → .aaiaext (signiertes Binär-Paket)
///   - signatureInfo → signature-info.json (Public Key + Signatur, kein Private Key)
///   - releaseInfo   → release-info.json (optional, keine Secrets)
///   - inspectionReport → inspection-report.json (optional, kein Quellcode)
///
/// Endpoint: POST {MarketplaceApiUrl}/extensions/upload-signed
/// Auth:     Bearer {MarketplaceToken} (JWT)
/// </summary>
public static class MarketplaceSignedUploadService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Öffentliche API ───────────────────────────────────────────────────────

    /// <summary>
    /// Lädt das signierte Release-Paket an den Marketplace hoch.
    /// </summary>
    /// <param name="ctx">Upload-Kontext (aus ViewModel befüllt, ohne Secrets).</param>
    /// <param name="progress">Fortschrittsanzeige 0–100.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<MarketplaceUploadResult> UploadAsync(
        MarketplaceSignedUploadContext ctx,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        // ── Vorab-Validierung ─────────────────────────────────────────────────

        if (!ctx.IsLoggedIn)
            return Fail("Nicht angemeldet. Bitte zuerst im Marketplace-Tab einloggen.",
                        MarketplaceUploadStatus.Unauthorized);

        if (!ctx.IsApiConfigured)
            return Fail("Marketplace-API-URL ist nicht konfiguriert.",
                        MarketplaceUploadStatus.ServerError);

        if (!ctx.PackageExists)
            return Fail($"Paketdatei nicht gefunden: {ctx.PackagePath}",
                        MarketplaceUploadStatus.ServerError);

        if (!ctx.SignatureExists)
            return Fail($"signature-info.json nicht gefunden: {ctx.SignatureInfoPath}",
                        MarketplaceUploadStatus.ServerError);

        // ── HTTP-Client ───────────────────────────────────────────────────────

        try
        {
            using var handler = BuildHandler(ctx.MarketplaceApiUrl);
            using var http    = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ctx.BearerToken);

            // ── Multipart-Request ─────────────────────────────────────────────

            using var form = new MultipartFormDataContent();

            // Text-Felder (Metadaten)
            form.Add(new StringContent(ctx.ExtensionId),       "extensionId");
            form.Add(new StringContent(ctx.Version),           "version");
            form.Add(new StringContent(ctx.DeveloperEtwId),    "developerEtwId");
            form.Add(new StringContent(ctx.KeyFingerprint),    "keyFingerprint");
            form.Add(new StringContent(ctx.TrustLevel),        "trustLevel");
            form.Add(new StringContent(ctx.SignatureVersion),  "signatureVersion");

            // Pflicht-Dateien
            await AddFileAsync(form, ctx.PackagePath,       "package",       "application/octet-stream", ct);
            await AddFileAsync(form, ctx.SignatureInfoPath, "signatureInfo", "application/json",          ct);

            // Optionale Dateien
            if (ctx.ReleaseInfoExists)
                await AddFileAsync(form, ctx.ReleaseInfoPath,       "releaseInfo",      "application/json", ct);
            if (ctx.InspectionExists)
                await AddFileAsync(form, ctx.InspectionReportPath,  "inspectionReport", "application/json", ct);

            progress?.Report(25);

            // ── POST ──────────────────────────────────────────────────────────

            var endpoint = BuildEndpoint(ctx.MarketplaceApiUrl);
            var resp     = await http.PostAsync(endpoint, form, ct);

            progress?.Report(80);

            var body = await resp.Content.ReadAsStringAsync(ct);

            progress?.Report(100);

            return ParseResponse((int)resp.StatusCode, body);
        }
        catch (OperationCanceledException ex)
        {
            // TaskCanceledException (Unterklasse) = HttpClient-Timeout → Netzwerkfehler
            // Echte User-Cancellation (ct.IsCancellationRequested) → nach oben werfen
            if (ex is TaskCanceledException && !ct.IsCancellationRequested)
                return Fail("Upload-Timeout (>120 s). Bitte Verbindung prüfen.",
                            MarketplaceUploadStatus.NetworkError);
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Netzwerkfehler: {ex.Message}", MarketplaceUploadStatus.NetworkError);
        }
        catch (Exception ex)
        {
            return Fail($"Unbekannter Fehler: {ex.Message}", MarketplaceUploadStatus.ServerError);
        }
    }

    // ── Private Hilfsmethoden ─────────────────────────────────────────────────

    private static string BuildEndpoint(string apiUrl)
    {
        // "https://aaiagent.de/index.php?rest_route=/aaia/v1"
        // → "https://aaiagent.de/index.php?rest_route=/aaia/v1/extensions/upload-signed"
        var trimmed = apiUrl.TrimEnd('/');
        return trimmed + "/extensions/upload-signed";
    }

    private static HttpClientHandler BuildHandler(string apiUrl)
    {
        var handler = new HttpClientHandler();
        try
        {
            var uri = new Uri(apiUrl);
            if (uri.Host is "localhost" or "127.0.0.1" or "::1")
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        catch { /* Ungültige URL — wird beim POST als Fehler erscheinen */ }
        return handler;
    }

    private static async Task AddFileAsync(
        MultipartFormDataContent form,
        string path,
        string fieldName,
        string contentType,
        CancellationToken ct)
    {
        var bytes   = await File.ReadAllBytesAsync(path, ct);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, fieldName, Path.GetFileName(path));
    }

    /// <summary>
    /// Wertet HTTP-Statuscode + JSON-Body aus und erstellt ein <see cref="MarketplaceUploadResult"/>.
    ///
    /// Erwarteter Server-Response (Erfolg):
    /// <code>
    /// {
    ///   "success": true,
    ///   "status": "PendingVerification",
    ///   "submissionId": "abc-123",
    ///   "message": "Upload erfolgreich. Signaturprüfung läuft.",
    ///   "nextStep": "Deine Extension wird innerhalb von 24 h geprüft.",
    ///   "url": "https://aaiagent.de/marketplace/extensions/..."
    /// }
    /// </code>
    /// </summary>
    private static MarketplaceUploadResult ParseResponse(int httpStatus, string body)
    {
        // ── HTTP-Fehler direkt abbilden ────────────────────────────────────────
        if (httpStatus == 401 || httpStatus == 403)
            return Fail("JWT abgelaufen oder ungültig. Bitte neu einloggen.",
                        MarketplaceUploadStatus.Unauthorized, httpStatus);

        if (httpStatus == 409)
            return new MarketplaceUploadResult
            {
                Success        = false,
                Status         = MarketplaceUploadStatus.AlreadyExists,
                Message        = "Diese Version ist bereits im Marketplace vorhanden.",
                HttpStatusCode = httpStatus
            };

        if (httpStatus >= 500)
            return Fail($"Serverfehler (HTTP {httpStatus}).",
                        MarketplaceUploadStatus.ServerError, httpStatus, body);

        // ── JSON parsen ───────────────────────────────────────────────────────
        try
        {
            using var doc  = JsonDocument.Parse(body);
            var root       = doc.RootElement;

            var success    = root.TryGetProperty("success", out var s) && s.GetBoolean();
            var statusStr  = root.TryGetProperty("status",       out var st) ? st.GetString() : null;
            var message    = root.TryGetProperty("message",      out var m)  ? m.GetString()  : null;
            var subId      = root.TryGetProperty("submissionId", out var id) ? id.GetString() : null;
            var nextStep   = root.TryGetProperty("nextStep",     out var ns) ? ns.GetString() : null;
            var url        = root.TryGetProperty("url",          out var u)  ? u.GetString()  : null;
            var errDetail  = root.TryGetProperty("error",        out var e)  ? e.GetString()  : null;

            var status = ParseStatus(statusStr, success, httpStatus);

            return new MarketplaceUploadResult
            {
                Success        = success,
                Status         = status,
                SubmissionId   = subId,
                Message        = message,
                MarketplaceUrl = url,
                NextStep       = nextStep,
                ErrorDetail    = success ? null : (errDetail ?? message),
                HttpStatusCode = httpStatus
            };
        }
        catch (JsonException)
        {
            // Kein valides JSON — rohen Body als Fehlermeldung anzeigen
            if (httpStatus is >= 200 and < 300)
            {
                // 2xx ohne JSON → Erfolg annehmen
                return new MarketplaceUploadResult
                {
                    Success        = true,
                    Status         = MarketplaceUploadStatus.Uploaded,
                    Message        = "Upload abgeschlossen.",
                    HttpStatusCode = httpStatus
                };
            }
            return Fail($"Unerwartete Serverantwort (HTTP {httpStatus}).",
                        MarketplaceUploadStatus.ServerError, httpStatus, body);
        }
    }

    private static MarketplaceUploadStatus ParseStatus(string? raw, bool success, int httpStatus)
    {
        if (!string.IsNullOrEmpty(raw))
        {
            if (Enum.TryParse<MarketplaceUploadStatus>(raw, ignoreCase: true, out var parsed))
                return parsed;
        }

        // Fallback auf HTTP-Code
        if (httpStatus is >= 200 and < 300)
            return success ? MarketplaceUploadStatus.Uploaded : MarketplaceUploadStatus.PendingVerification;
        if (httpStatus == 409)
            return MarketplaceUploadStatus.AlreadyExists;
        if (httpStatus == 401 || httpStatus == 403)
            return MarketplaceUploadStatus.Unauthorized;
        if (httpStatus >= 500)
            return MarketplaceUploadStatus.ServerError;

        return MarketplaceUploadStatus.Unknown;
    }

    private static MarketplaceUploadResult Fail(
        string message,
        MarketplaceUploadStatus status,
        int httpStatus = 0,
        string? detail = null)
        => new()
        {
            Success        = false,
            Status         = status,
            Message        = message,
            ErrorDetail    = detail,
            HttpStatusCode = httpStatus
        };
}
