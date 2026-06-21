using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AAIA.Shared.Contracts.Marketplace;
using AAIA.Shared.Contracts.Routes;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Lädt ein verifiziertes Extension-Paket vom Marketplace herunter,
/// prüft den SHA-256-Hash und speichert es lokal.
///
/// Flow:
///   1. GET /api/marketplace/extensions/{id}/releases/{version}/download
///   2. SHA256-Hash aus X-Package-Sha256-Header lesen
///   3. Datei in DownloadDirectory speichern
///   4. Lokalen SHA256 berechnen + gegen Header-Hash prüfen
///   5. LocalDownloadResult zurückgeben
///
/// Der Caller kann IProgress{double} übergeben für Fortschrittsanzeige (0..1).
/// </summary>
public sealed class ExtensionDownloadService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _downloadDirectory;

    public ExtensionDownloadService(string marketplaceApiBaseUrl, string downloadDirectory)
    {
        _downloadDirectory = downloadDirectory;
        Directory.CreateDirectory(downloadDirectory);

        var baseUri = new Uri(marketplaceApiBaseUrl.TrimEnd('/') + "/");
        var handler = new HttpClientHandler();

        if (baseUri.Host is "localhost" or "127.0.0.1" or "::1")
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        _http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout     = TimeSpan.FromMinutes(10)   // große Pakete
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
    }

    // ── Öffentliche API ───────────────────────────────────────────────────────

    public async Task<LocalDownloadResult> DownloadAsync(
        string extensionId,
        string version,
        IProgress<double>? progress = null,
        CancellationToken  ct = default)
    {
        var route = AaiaApiRoutes.Marketplace.DownloadRelease
            .Replace("{extensionId}", Uri.EscapeDataString(extensionId))
            .Replace("{version}",     Uri.EscapeDataString(version));

        HttpResponseMessage resp;
        try
        {
            // ResponseHeadersRead: ermöglicht Streaming-Progress
            resp = await _http.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            return Fail(extensionId, version, $"Netzwerkfehler: {ex.Message}");
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);

            // Lizenz-Gate: 401 = kein Token, 403 = keine/abgelaufene Lizenz
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var (reason, detail) = ParseLicenseError(body, resp.StatusCode);
                return new LocalDownloadResult(
                    Success:       false,
                    ExtensionId:   extensionId,
                    Version:       version,
                    LocalPath:     null,
                    ErrorMessage:  detail,
                    HashVerified:  false,
                    FileSizeBytes: 0,
                    DeniedReason:  reason);
            }

            return Fail(extensionId, version,
                $"Server antwortete {(int)resp.StatusCode}: {body.Truncate(200)}");
        }

        // SHA256 aus Header lesen (zur Verifikation nach dem Download)
        var expectedHash = resp.Headers.TryGetValues("X-Package-Sha256", out var hv)
            ? hv.FirstOrDefault()
            : null;

        // Dateiname bestimmen
        var safeId  = Sanitize(extensionId);
        var fileName = $"{safeId}-{version}.aaiaext";
        var filePath = Path.Combine(_downloadDirectory, fileName);

        // Streaming-Download mit Progress
        long totalBytes    = resp.Content.Headers.ContentLength ?? -1;
        long downloadedBytes = 0;

        try
        {
            await using var responseStream = await resp.Content.ReadAsStreamAsync(ct);
            await using var fileStream     = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            int read;
            while ((read = await responseStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloadedBytes += read;

                if (progress is not null && totalBytes > 0)
                    progress.Report((double)downloadedBytes / totalBytes);
            }
        }
        catch (Exception ex)
        {
            // Unvollständige Datei aufräumen
            TryDelete(filePath);
            return Fail(extensionId, version, $"Download-Fehler: {ex.Message}");
        }

        progress?.Report(1.0);

        // SHA256 lokal berechnen
        string actualHash;
        try
        {
            var bytes   = await File.ReadAllBytesAsync(filePath, ct);
            actualHash  = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            TryDelete(filePath);
            return Fail(extensionId, version, $"Hash-Berechnung fehlgeschlagen: {ex.Message}");
        }

        // Hash-Verifikation
        bool hashOk = string.IsNullOrEmpty(expectedHash) ||
                      string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);

        if (!hashOk)
        {
            TryDelete(filePath);
            return new LocalDownloadResult(
                Success:        false,
                ExtensionId:    extensionId,
                Version:        version,
                LocalPath:      null,
                ErrorMessage:   $"Hash-Prüfung fehlgeschlagen. Erwartet: {expectedHash}, Berechnet: {actualHash}",
                HashVerified:   false,
                FileSizeBytes:  downloadedBytes);
        }

        return new LocalDownloadResult(
            Success:        true,
            ExtensionId:    extensionId,
            Version:        version,
            LocalPath:      filePath,
            ErrorMessage:   null,
            HashVerified:   !string.IsNullOrEmpty(expectedHash),
            FileSizeBytes:  downloadedBytes);
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static LocalDownloadResult Fail(string extId, string ver, string msg)
        => new(false, extId, ver, null, msg, false, 0);

    /// <summary>Wertet den JSON-Body eines 401/403 aus und gibt Reason + lesbaren Text zurück.</summary>
    private static (DownloadDeniedReason, string) ParseLicenseError(string body, HttpStatusCode statusCode)
    {
        try
        {
            using var doc  = JsonDocument.Parse(body);
            var root       = doc.RootElement;
            var errorCode  = root.TryGetProperty("error",  out var e) ? e.GetString() ?? "" : "";
            var detail     = root.TryGetProperty("detail", out var d) ? d.GetString() : null;

            var reason = errorCode switch
            {
                "AuthRequired"        => DownloadDeniedReason.AuthRequired,
                "LicenseRequired"     => DownloadDeniedReason.LicenseRequired,
                "SubscriptionExpired" => DownloadDeniedReason.SubscriptionExpired,
                "LicenseRevoked"      => DownloadDeniedReason.LicenseRevoked,
                _                     => statusCode == HttpStatusCode.Unauthorized
                                             ? DownloadDeniedReason.AuthRequired
                                             : DownloadDeniedReason.LicenseRequired
            };

            var message = detail ?? reason switch
            {
                DownloadDeniedReason.AuthRequired        => "🔒 Anmeldung erforderlich. Diese Extension ist kostenpflichtig.",
                DownloadDeniedReason.LicenseRequired     => "💳 Keine gültige Lizenz. Bitte erwerben Sie diese Extension.",
                DownloadDeniedReason.SubscriptionExpired => "⏰ Ihr Abonnement ist abgelaufen.",
                DownloadDeniedReason.LicenseRevoked      => "🚫 Ihre Lizenz wurde widerrufen.",
                _                                        => "Download nicht erlaubt."
            };

            return (reason, message);
        }
        catch
        {
            var defaultReason = statusCode == HttpStatusCode.Unauthorized
                ? DownloadDeniedReason.AuthRequired
                : DownloadDeniedReason.LicenseRequired;
            return (defaultReason, "Zugriff verweigert.");
        }
    }

    private static string Sanitize(string s)
        => System.Text.RegularExpressions.Regex.Replace(s, @"[^a-zA-Z0-9.\-_]", "_");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Ignorieren */ }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Hilfsmethoden für String-Verarbeitung.</summary>
file static class StringExtensions
{
    public static string Truncate(this string s, int maxLength)
        => s.Length <= maxLength ? s : s[..maxLength] + "…";
}
