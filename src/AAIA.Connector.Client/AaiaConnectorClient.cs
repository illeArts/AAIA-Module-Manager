using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.Connector.Client;

/// <summary>
/// AAIA AI Connector Client SDK.
///
/// Kapselt das gesamte AAIA Connector Protocol (aaia-connector-v1).
/// Kann in jeden C#-basierten AI-Agent oder Service eingebettet werden
/// der mit dem AAIA Module Manager kommunizieren soll.
///
/// Verwendung:
/// <code>
///   using var client = new AaiaConnectorClient("my-agent", "Mein KI-Agent");
///   var caps = await client.GetCapabilitiesAsync();
///   var ctx  = await client.GetContextSummaryAsync();
///   if (ctx?.HasErrors == true)
///   {
///       var result = await client.ProposePatchAsync(request);
///       // Auf Nutzer-Entscheidung warten via PollStatusAsync
///   }
/// </code>
///
/// SICHERHEITSHINWEIS:
///   - Der Server läuft ausschließlich auf localhost:39157.
///   - Patches werden NIEMALS ohne explizite Nutzer-Genehmigung angewendet.
///   - Keine Private Keys, API-Tokens oder .env-Dateien dürfen als Patch-Ziele angegeben werden.
/// </summary>
public sealed class AaiaConnectorClient : IDisposable
{
    public const string DefaultBaseUrl     = "http://localhost:39157";
    public const string ProtocolVersion    = "aaia-connector-v1";
    public const string ApiPrefix          = "/aaia/v1";

    private readonly HttpClient _http;
    private readonly string     _connectorId;
    private readonly string     _connectorName;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Erstellt einen neuen Connector-Client.
    /// </summary>
    /// <param name="connectorId">
    ///   Eindeutige ID des Connectors — bekannte Werte: "chatgpt", "claude", "gemini", "codex".
    ///   Für eigene Agents: beliebiger Kleinbuchstaben-String.
    /// </param>
    /// <param name="connectorName">Anzeige-Name der im AAIA Module Manager geloggt wird.</param>
    /// <param name="baseUrl">Server-URL — Standard: http://localhost:39157</param>
    public AaiaConnectorClient(
        string connectorId,
        string connectorName,
        string baseUrl = DefaultBaseUrl)
    {
        _connectorId   = connectorId;
        _connectorName = connectorName;

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-AAIA-Connector-Id",   connectorId);
        _http.DefaultRequestHeaders.Add("X-AAIA-Connector-Name", connectorName);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // ── GET /capabilities ─────────────────────────────────────────────────────

    /// <summary>
    /// Gibt Server-Capabilities und erlaubte Endpunkte zurück.
    /// Benötigt keine Permissions — immer aufrufbar.
    /// </summary>
    public async Task<ConnectorCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{ApiPrefix}/capabilities", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ConnectorCapabilities>(JsonOpts, ct);
    }

    // ── GET /context/current ──────────────────────────────────────────────────

    /// <summary>
    /// Gibt einen kompakten Überblick über den aktuellen Projektzustand.
    /// Enthält: Extension-ID, aktuellen Schritt, Fehleranzahl.
    /// Keine Quelltexte, keine Keys.
    /// </summary>
    public async Task<ProjectContextSummary?> GetContextSummaryAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{ApiPrefix}/context/current", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // kein aktives Projekt
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ProjectContextSummary>(JsonOpts, ct);
    }

    // ── GET /context/project ──────────────────────────────────────────────────

    /// <summary>
    /// Gibt den vollständigen Projektzustand als JSON zurück.
    /// Enthält: Pipeline-Flags, Fehler-Listen, Trust-Level.
    /// </summary>
    public async Task<System.Text.Json.JsonElement?> GetFullContextAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{ApiPrefix}/context/project", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(JsonOpts, ct);
    }

    // ── POST /patch/propose ───────────────────────────────────────────────────

    /// <summary>
    /// Sendet einen Patch-Vorschlag an den AAIA Module Manager.
    /// Der Nutzer muss den Patch im UI genehmigen — der HTTP-Request gibt sofort 202 zurück.
    /// Status über <see cref="PollPatchStatusAsync"/> abfragen.
    /// </summary>
    /// <exception cref="ConnectorException">Bei Validierungsfehlern oder fehlender Permission.</exception>
    public async Task<PatchResponse> ProposePatchAsync(PatchRequest request, CancellationToken ct = default)
    {
        request.ProtocolVersion = ProtocolVersion;

        var json    = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await _http.PostAsync($"{ApiPrefix}/patch/propose", content, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts, ct);
            throw new ConnectorException(resp.StatusCode, err?.Error ?? "Unbekannter Fehler", err?.Details);
        }

        return (await resp.Content.ReadFromJsonAsync<PatchResponse>(JsonOpts, ct))!;
    }

    // ── GET /patch/{id}/status ────────────────────────────────────────────────

    /// <summary>
    /// Fragt den Status eines eingereichten Patch-Vorschlags ab.
    /// </summary>
    public async Task<PatchStatusResponse?> GetPatchStatusAsync(string proposalId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{ApiPrefix}/patch/{proposalId}/status", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<PatchStatusResponse>(JsonOpts, ct);
    }

    /// <summary>
    /// Wartet (polling) bis der Nutzer den Patch-Vorschlag genehmigt oder abgelehnt hat.
    /// </summary>
    /// <param name="proposalId">Die ID aus <see cref="ProposePatchAsync"/>.</param>
    /// <param name="pollIntervalMs">Polling-Interval in Millisekunden (Standard: 2000).</param>
    /// <param name="timeoutMs">Timeout in Millisekunden (Standard: 10 Minuten).</param>
    public async Task<PatchStatusResponse?> PollPatchStatusAsync(
        string proposalId,
        int    pollIntervalMs = 2_000,
        int    timeoutMs      = 10 * 60 * 1_000,
        CancellationToken ct  = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var status = await GetPatchStatusAsync(proposalId, ct);
            if (status?.Status is "approved" or "rejected")
                return status;

            await Task.Delay(pollIntervalMs, ct);
        }

        return null; // Timeout
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob der AAIA Connector Server erreichbar ist.
    /// Gibt true zurück wenn der Server auf Capabilities antwortet.
    /// </summary>
    public async Task<bool> IsServerRunningAsync(CancellationToken ct = default)
    {
        try
        {
            var caps = await GetCapabilitiesAsync(ct);
            return caps?.ProtocolVersion == ProtocolVersion;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

// ── Exception ─────────────────────────────────────────────────────────────────

/// <summary>Wird ausgelöst wenn der AAIA Connector Server einen Fehler zurückgibt.</summary>
public sealed class ConnectorException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public System.Collections.Generic.List<string>? Details { get; }

    public ConnectorException(
        System.Net.HttpStatusCode statusCode,
        string message,
        System.Collections.Generic.List<string>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Details    = details;
    }
}
