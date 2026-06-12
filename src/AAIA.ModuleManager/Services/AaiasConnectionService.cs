using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record AaiasServerStatus(bool Online, string Version = "", string Environment = "");

public sealed record AaiasExtensionInfo(
    string Id,
    string DisplayName,
    string Version,
    string Kind,
    bool Enabled,
    string TrustStatus,
    string? PackagePath);

public sealed record AaiasInstallResult(
    string Id,
    bool Installed,
    bool Updated,
    bool Enabled,
    string TrustStatus,
    string? PackagePath,
    bool RestartRequired,
    string? PreviousVersion);

public sealed record AaiasEnableResult(string Id, bool Enabled, bool RestartRequired);

public sealed record AaiasDiagnosticsEvent(
    string Severity,
    string Source,
    string? ComponentId,
    string Message,
    string? Hint,
    string? ErrorCode,
    string? StackTrace,
    DateTimeOffset Timestamp);

// ── Connection Service ────────────────────────────────────────────────────────

/// <summary>
/// Manages HTTP communication with a running AAIAS server.
/// Call ConnectAsync first; all other methods check IsConnected.
/// </summary>
public sealed class AaiasConnectionService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient? _http;
    private string?     _token;

    public bool   IsConnected  { get; private set; }
    public string BaseUrl      { get; private set; } = "";
    public string ConnectedAs  { get; private set; } = "";

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    /// <summary>Ping server and login. Returns error message or null on success.</summary>
    public async Task<string?> ConnectAsync(string baseUrl, string username, string password)
    {
        IsConnected = false;
        _token      = null;
        BaseUrl     = baseUrl.TrimEnd('/');

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(10) };

        // 1. Ping
        try
        {
            var ping = await _http.GetAsync("api/server/status");
            if (!ping.IsSuccessStatusCode)
                return $"Server antwortet nicht (HTTP {(int)ping.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"Kann AAIAS nicht erreichen: {ex.Message}";
        }

        // 2. Login (optional when credentials provided)
        if (!string.IsNullOrWhiteSpace(username))
        {
            try
            {
                var loginResp = await _http.PostAsJsonAsync("api/auth/login",
                    new { username, password });

                if (!loginResp.IsSuccessStatusCode)
                {
                    var err = await loginResp.Content.ReadAsStringAsync();
                    return $"Login fehlgeschlagen ({(int)loginResp.StatusCode}): {ExtractError(err)}";
                }

                var loginJson = await loginResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(loginJson);
                _token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
                _token ??= doc.RootElement.TryGetProperty("accessToken", out var at) ? at.GetString() : null;

                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

                ConnectedAs = username;
            }
            catch (Exception ex)
            {
                return $"Login-Fehler: {ex.Message}";
            }
        }

        IsConnected = true;
        return null;
    }

    public void Disconnect()
    {
        IsConnected = false;
        _token      = null;
        _http?.Dispose();
        _http = null;
    }

    // ── Extensions API ─────────────────────────────────────────────────────────

    public async Task<List<AaiasExtensionInfo>> GetInstalledAsync()
    {
        EnsureConnected();
        var resp = await _http!.GetAsync("api/extensions/installed");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = new List<AaiasExtensionInfo>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new AaiasExtensionInfo(
                Id:          el.GetStringOrEmpty("id"),
                DisplayName: el.GetStringOrEmpty("displayName"),
                Version:     el.GetStringOrEmpty("version"),
                Kind:        el.GetStringOrEmpty("kind"),
                Enabled:     el.TryGetProperty("enabled", out var en) && en.GetBoolean(),
                TrustStatus: el.GetStringOrEmpty("trustStatus"),
                PackagePath: el.TryGetProperty("packagePath", out var pp) && pp.ValueKind != JsonValueKind.Null
                                 ? pp.GetString() : null));
        }
        return list;
    }

    /// <summary>Install extension from a local build output folder.</summary>
    public async Task<(AaiasInstallResult? Result, string? Error)> InstallFromPathAsync(
        string sourcePath, bool overwrite = true, bool allowDowngrade = false)
    {
        EnsureConnected();
        var resp = await _http!.PostAsJsonAsync("api/extensions/install-from-path",
            new { sourcePath, overwrite, allowDowngrade });

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
                return (null, $"Konflikt: {ExtractError(body)} — versuche mit Overwrite=true.");
            return (null, $"Install fehlgeschlagen ({(int)resp.StatusCode}): {ExtractError(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var el = doc.RootElement;
        return (new AaiasInstallResult(
            Id:              el.GetStringOrEmpty("id"),
            Installed:       el.TryGetProperty("installed",  out var ins) && ins.GetBoolean(),
            Updated:         el.TryGetProperty("updated",    out var upd) && upd.GetBoolean(),
            Enabled:         el.TryGetProperty("enabled",    out var enb) && enb.GetBoolean(),
            TrustStatus:     el.GetStringOrEmpty("trustStatus"),
            PackagePath:     el.TryGetProperty("packagePath", out var pp) && pp.ValueKind != JsonValueKind.Null
                                 ? pp.GetString() : null,
            RestartRequired: el.TryGetProperty("restartRequired", out var rr) && rr.GetBoolean(),
            PreviousVersion: el.TryGetProperty("previousVersion", out var pv) && pv.ValueKind != JsonValueKind.Null
                                 ? pv.GetString() : null), null);
    }

    public async Task<(AaiasEnableResult? Result, string? Error)> EnableAsync(
        string id, bool allowUnsigned = true)
    {
        EnsureConnected();
        var resp = await _http!.PostAsJsonAsync($"api/extensions/{Uri.EscapeDataString(id)}/enable",
            new { allowUnsigned });

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return (null, $"Enable fehlgeschlagen ({(int)resp.StatusCode}): {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        var el = doc.RootElement;
        return (new AaiasEnableResult(
            Id:              el.GetStringOrEmpty("id"),
            Enabled:         el.TryGetProperty("enabled",         out var en) && en.GetBoolean(),
            RestartRequired: el.TryGetProperty("restartRequired", out var rr) && rr.GetBoolean()), null);
    }

    public async Task<string?> DisableAsync(string id)
    {
        EnsureConnected();
        var resp = await _http!.PostAsync($"api/extensions/{Uri.EscapeDataString(id)}/disable",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        if (!resp.IsSuccessStatusCode)
            return $"Disable fehlgeschlagen: {ExtractError(await resp.Content.ReadAsStringAsync())}";
        return null;
    }

    // ── Dev Mode ───────────────────────────────────────────────────────────────

    public async Task<bool> IsDevModeActiveAsync()
    {
        EnsureConnected();
        try
        {
            var resp = await _http!.GetAsync("api/dev/status");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<(bool Success, string? Error)> HotReloadAsync(string id)
    {
        EnsureConnected();
        var resp = await _http!.PostAsync(
            $"api/dev/extensions/{Uri.EscapeDataString(id)}/reload",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        if (!resp.IsSuccessStatusCode)
        {
            var err = ExtractError(await resp.Content.ReadAsStringAsync());
            return (false, err);
        }
        return (true, null);
    }

    public async Task<string?> GetDiagnosticsJsonAsync(string id)
    {
        EnsureConnected();
        var resp = await _http!.GetAsync($"api/dev/extensions/{Uri.EscapeDataString(id)}/diagnostics");
        if (!resp.IsSuccessStatusCode)
            return null;
        return await resp.Content.ReadAsStringAsync();
    }

    /// <summary>Simulate a WorkOrder against a loaded module.</summary>
    public async Task<(string? ResultJson, string? Error)> SimulateWorkOrderAsync(
        string moduleId, string workOrderJson)
    {
        EnsureConnected();
        var resp = await _http!.PostAsJsonAsync("api/dev/workorder/simulate",
            new { moduleId, workOrderJson });

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return (null, ExtractError(body));

        return (body, null);
    }

    // ── SSE Log Stream ─────────────────────────────────────────────────────────

    /// <summary>
    /// Streams diagnostic events from /api/dev/logs/stream via SSE.
    /// Yields events until cancellation or disconnection.
    /// </summary>
    public async IAsyncEnumerable<AaiasDiagnosticsEvent> StreamLogsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureConnected();

        // Long-running SSE client — own HttpClient with infinite timeout
        using var sseClient = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
        if (_token != null)
            sseClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        sseClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/dev/logs/stream");
        using var response = await sseClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode) yield break;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!ct.IsCancellationRequested && !reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (string.IsNullOrWhiteSpace(data) || data == "ping") continue;

            AaiasDiagnosticsEvent? evt = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var el = doc.RootElement;
                evt = new AaiasDiagnosticsEvent(
                    Severity:    el.GetStringOrEmpty("severity"),
                    Source:      el.GetStringOrEmpty("source"),
                    ComponentId: el.TryGetProperty("componentId", out var cid) && cid.ValueKind == JsonValueKind.String
                                     ? cid.GetString() : null,
                    Message:     el.GetStringOrEmpty("message"),
                    Hint:        el.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                                     ? h.GetString() : null,
                    ErrorCode:   el.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String
                                     ? ec.GetString() : null,
                    StackTrace:  el.TryGetProperty("stackTrace", out var st) && st.ValueKind == JsonValueKind.String
                                     ? st.GetString() : null,
                    Timestamp:   el.TryGetProperty("timestamp", out var ts)
                                     ? ts.GetDateTimeOffset()
                                     : DateTimeOffset.UtcNow);
            }
            catch { /* malformed line */ }

            if (evt is not null)
                yield return evt;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!IsConnected || _http is null)
            throw new InvalidOperationException("Nicht mit AAIAS verbunden. ConnectAsync zuerst aufrufen.");
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? json;
        }
        catch { }
        return json.Length > 300 ? json[..300] + "…" : json;
    }

    public void Dispose()
    {
        _http?.Dispose();
    }
}

// ── JsonElement extension ─────────────────────────────────────────────────────

internal static class JsonElementExtensions
{
    public static string GetStringOrEmpty(this JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
