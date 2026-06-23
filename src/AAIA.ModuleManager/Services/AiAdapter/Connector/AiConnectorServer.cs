using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

/// <summary>
/// Lokaler HTTP-Server auf localhost:39157.
/// Stellt das AAIA AI Connector Protocol bereit.
///
/// Architektur:
///   • Läuft auf einem Background-Thread (kein UI-Block).
///   • Patch-Proposals werden via PatchProposalReceived-Event an den UI-Thread übergeben.
///   • Der aufrufende Code (ViewModel) zeigt das Approval-Fenster und
///     ruft ApprovePatch() oder RejectPatch() auf.
///   • Der HTTP-Request wartet (via TaskCompletionSource) bis der User entschieden hat.
/// </summary>
public sealed class AiConnectorServer : IDisposable
{
    private readonly AppConfig                   _config;
    private          HttpListener?               _listener;
    private          CancellationTokenSource?    _cts;
    private          Task?                       _serverTask;
    private volatile bool                        _running;

    // Pending Patch-Proposals: proposalId → TCS<bool> (true = approved)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    // Letzter bekannter Projektzustand (vom ViewModel aktualisiert)
    private volatile AiHandoffContext? _currentContext;
    private volatile string?           _projectRoot;

    // ── Ereignisse ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ausgelöst wenn ein Connector einen Patch-Vorschlag einreicht.
    /// Der Handler muss auf dem UI-Thread laufen und
    /// ApprovePatch / RejectPatch aufrufen wenn der User entschieden hat.
    /// </summary>
    public event Action<string, AiPatchRequest>? PatchProposalReceived;

    /// <summary>Ausgelöst wenn ein Connector sich verbindet (für Logging/UI).</summary>
    public event Action<string>? ConnectorConnected;

    // ── Status ────────────────────────────────────────────────────────────────

    public bool IsRunning => _running;
    public int  Port      => AiConnectorProtocol.Port;
    public string BaseUrl  => AiConnectorProtocol.BaseUrl;

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public AiConnectorServer(AppConfig config)
    {
        _config = config;
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (_running) return;

        _cts      = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add(AiConnectorProtocol.FullPrefix);

        try
        {
            _listener.Start();
            _running    = true;
            _serverTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _running = false;
            throw new InvalidOperationException(
                $"Connector-Server konnte nicht gestartet werden (Port {Port}): {ex.Message}", ex);
        }
    }

    public async Task StopAsync()
    {
        if (!_running) return;

        _running = false;
        _cts?.Cancel();

        try { _listener?.Stop(); }
        catch { /* ignorieren */ }

        if (_serverTask is not null)
        {
            await _serverTask.ConfigureAwait(false);
        }

        // Alle wartenden Requests ablehnen
        foreach (var tcs in _pending.Values)
            tcs.TrySetResult(false);
        _pending.Clear();
    }

    // ── Kontext aktualisieren ─────────────────────────────────────────────────

    /// <summary>
    /// Wird vom ViewModel aufgerufen wenn sich der Projektzustand ändert.
    /// Thread-safe: volatile write.
    /// </summary>
    public void UpdateContext(AiHandoffContext ctx, string? projectRoot = null)
    {
        _currentContext = ctx;
        _projectRoot    = projectRoot;
    }

    // ── Approval-API ──────────────────────────────────────────────────────────

    /// <summary>Vom ViewModel aufgerufen: Nutzer hat Patch genehmigt.</summary>
    public void ApprovePatch(string proposalId)
    {
        if (_pending.TryRemove(proposalId, out var tcs))
            tcs.TrySetResult(true);
    }

    /// <summary>Vom ViewModel aufgerufen: Nutzer hat Patch abgelehnt.</summary>
    public void RejectPatch(string proposalId)
    {
        if (_pending.TryRemove(proposalId, out var tcs))
            tcs.TrySetResult(false);
    }

    // ── Listen-Loop ───────────────────────────────────────────────────────────

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = HandleRequestAsync(ctx); // fire-and-forget pro Request
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                // Einzelner Request-Fehler stoppt den Server nicht
            }
        }
    }

    // ── Request-Handler ───────────────────────────────────────────────────────

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        try
        {
            // ── Localhost-Only ────────────────────────────────────────────────
            if (!ConnectorHardening.IsLocalhost(req.RemoteEndPoint?.Address))
            {
                await WriteJsonAsync(resp, AiConnectorProtocol.StatusForbidden,
                    new { error = "Nur lokale Verbindungen erlaubt." });
                return;
            }

            // CORS — nur für localhost (kein wildcard in Produktion nötig)
            resp.AddHeader("Access-Control-Allow-Origin",  "http://localhost");
            resp.AddHeader("Access-Control-Allow-Headers",
                $"{AiConnectorProtocol.HeaderConnectorId}, {AiConnectorProtocol.HeaderConnectorName}, Content-Type");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            // ── Body-Size-Limit ───────────────────────────────────────────────
            if (req.ContentLength64 > ConnectorHardening.MaxBodyBytes)
            {
                await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                    new { error = $"Request zu groß (max {ConnectorHardening.MaxBodyBytes / 1024} KB)." });
                return;
            }

            // Connector identifizieren
            var (connId, connName) = AiConnectorPermissionChecker.ReadConnectorHeaders(req.Headers);

            // ── Rate-Limiting ─────────────────────────────────────────────────
            if (ConnectorHardening.IsRateLimited(connId))
            {
                await WriteJsonAsync(resp, 429,
                    new { error = $"Rate-Limit erreicht (max {ConnectorHardening.MaxRequestsPerMinute} Requests/min)." });
                return;
            }

            ConnectorConnected?.Invoke($"{connName} ({connId})");

            // Permission prüfen
            var required = AiConnectorPermissionChecker.RequiredFor(req.Url?.AbsolutePath ?? "", req.HttpMethod);
            var session  = AiConnectorPermissionChecker.CreateSession(
                connId, connName,
                allowPatchProposal: _config.AiConnector.AllowPatchProposals);

            if (!session.HasPermission(required))
            {
                await WriteJsonAsync(resp, AiConnectorProtocol.StatusForbidden,
                    new { error = "Aktion nicht erlaubt.", required = required.ToString() });
                return;
            }

            // Routing
            var path = req.Url?.AbsolutePath ?? "";

            if (req.HttpMethod == "GET")
            {
                if (path.Equals(AiConnectorProtocol.Endpoints.Capabilities, StringComparison.OrdinalIgnoreCase))
                    await HandleCapabilitiesAsync(resp, session);

                else if (path.Equals(AiConnectorProtocol.Endpoints.ContextCurrent, StringComparison.OrdinalIgnoreCase))
                    await HandleContextCurrentAsync(resp);

                else if (path.Equals(AiConnectorProtocol.Endpoints.ContextProject, StringComparison.OrdinalIgnoreCase))
                    await HandleContextProjectAsync(resp);

                else if (path.Equals(AiConnectorProtocol.Endpoints.HandoffLatest, StringComparison.OrdinalIgnoreCase))
                    await HandleHandoffLatestAsync(resp);

                else if (path.StartsWith(AiConnectorProtocol.Endpoints.PatchStatus, StringComparison.OrdinalIgnoreCase))
                    await HandlePatchStatusAsync(resp, path);

                else
                    await WriteJsonAsync(resp, AiConnectorProtocol.StatusNotFound,
                        new { error = "Endpunkt nicht gefunden.", path });
            }
            else if (req.HttpMethod == "POST" &&
                     path.Equals(AiConnectorProtocol.Endpoints.PatchPropose, StringComparison.OrdinalIgnoreCase))
            {
                await HandlePatchProposeAsync(req, resp);
            }
            else
            {
                await WriteJsonAsync(resp, AiConnectorProtocol.StatusMethodNotAllowed,
                    new { error = "Methode nicht erlaubt." });
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(resp, AiConnectorProtocol.StatusServerError,
                    new { error = "Interner Serverfehler.", detail = ex.Message });
            }
            catch { /* Response bereits geschlossen */ }
        }
    }

    // ── GET /capabilities ─────────────────────────────────────────────────────

    private async Task HandleCapabilitiesAsync(HttpListenerResponse resp,
        AiConnectorSession session)
    {
        var caps = new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            serverVersion   = "2.5.0",
            connectorId     = session.ConnectorId,
            permissions     = session.Permissions.ToString(),
            endpoints = new[]
            {
                new { path = AiConnectorProtocol.Endpoints.Capabilities,   method = "GET",  description = "Server-Capabilities" },
                new { path = AiConnectorProtocol.Endpoints.ContextCurrent, method = "GET",  description = "Aktueller Projektzustand (kompakt)" },
                new { path = AiConnectorProtocol.Endpoints.ContextProject, method = "GET",  description = "Vollständige Projekt-Zusammenfassung" },
                new { path = AiConnectorProtocol.Endpoints.HandoffLatest,  method = "GET",  description = "Letztes Handoff-Paket" },
                new { path = AiConnectorProtocol.Endpoints.PatchPropose,   method = "POST", description = "Patch-Vorschlag einreichen (erfordert User-Approval)" }
            },
            securityNote = "Patches werden NIEMALS ohne explizite Nutzer-Genehmigung angewendet. ETW-Signatur und Marketplace-Upload sind über den Connector nicht zugänglich."
        };
        await WriteJsonAsync(resp, AiConnectorProtocol.StatusOk, caps);
    }

    // ── GET /context/current ──────────────────────────────────────────────────

    private async Task HandleContextCurrentAsync(HttpListenerResponse resp)
    {
        var ctx = _currentContext;
        if (ctx is null)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusNotFound,
                new { error = "Kein aktives Projekt." });
            return;
        }

        var summary = new
        {
            extensionId  = ctx.ExtensionId,
            displayName  = ctx.DisplayName,
            currentStep  = ctx.CurrentStep,
            nextStep     = ctx.NextStep,
            trustLevel   = ctx.TrustLevel,
            hasErrors    = ctx.ValidationErrors.Count > 0
                        || ctx.InspectionBlockers.Count > 0
                        || ctx.SignatureErrors.Count > 0,
            errorCount   = ctx.ValidationErrors.Count
                        + ctx.InspectionBlockers.Count
                        + ctx.SignatureErrors.Count
        };
        await WriteJsonAsync(resp, AiConnectorProtocol.StatusOk, summary);
    }

    // ── GET /context/project ──────────────────────────────────────────────────

    private async Task HandleContextProjectAsync(HttpListenerResponse resp)
    {
        var ctx = _currentContext;
        if (ctx is null)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusNotFound,
                new { error = "Kein aktives Projekt." });
            return;
        }

        var project = new
        {
            extensionId              = ctx.ExtensionId,
            displayName              = ctx.DisplayName,
            projectType              = ctx.ProjectType,
            currentStep              = ctx.CurrentStep,
            nextStep                 = ctx.NextStep,
            trustLevel               = ctx.TrustLevel,
            developerEtwId           = ctx.DeveloperEtwId,
            pipeline = new
            {
                isProjectCreated         = ctx.IsProjectCreated,
                isValidated              = ctx.IsValidated,
                hasValidationBlockers    = ctx.HasValidationBlockers,
                isBuilt                  = ctx.IsBuilt,
                isPackaged               = ctx.IsPackaged,
                isInspected              = ctx.IsInspected,
                hasInspectionBlockers    = ctx.HasInspectionBlockers,
                isReleasePrepared        = ctx.IsReleasePrepared,
                isSignaturePrepared      = ctx.IsSignaturePrepared,
                etwKeyExists             = ctx.EtwKeyExists,
                isEtwSigned              = ctx.IsEtwSigned,
                isEtwSignatureVerified   = ctx.IsEtwSignatureVerified,
                canContinueToMarketplace = ctx.CanContinueToMarketplace
            },
            errors = new
            {
                validation  = ctx.ValidationErrors,
                inspection  = ctx.InspectionBlockers,
                signature   = ctx.SignatureErrors
            },
            securityNote = "Quelltexte, Private Keys und API-Tokens sind nicht enthalten."
        };
        await WriteJsonAsync(resp, AiConnectorProtocol.StatusOk, project);
    }

    // ── GET /handoff/latest ───────────────────────────────────────────────────

    private async Task HandleHandoffLatestAsync(HttpListenerResponse resp)
    {
        // Zeigt den Standard-Handoff-Pfad — keine tatsächlichen Dateien
        var basePath = HandoffPackage.AiHandoffPackageExporter.GetDefaultBasePath();
        var info = new
        {
            handoffDirectory = basePath,
            note = "Verwende POST /aaia/v1/patch/propose um Vorschläge einzureichen."
        };
        await WriteJsonAsync(resp, AiConnectorProtocol.StatusOk, info);
    }

    // ── POST /patch/propose ───────────────────────────────────────────────────

    private async Task HandlePatchProposeAsync(HttpListenerRequest req,
        HttpListenerResponse resp)
    {
        // Body lesen
        AiPatchRequest? patchReq;
        try
        {
            using var reader = new System.IO.StreamReader(req.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            patchReq = JsonSerializer.Deserialize<AiPatchRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                new { error = "Ungültiger JSON-Body.", detail = ex.Message });
            return;
        }

        if (patchReq is null || patchReq.Patches.Count == 0)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                new { error = "Keine Patches im Request." });
            return;
        }

        if (patchReq.ProtocolVersion != AiConnectorProtocol.ProtocolVersion)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                new { error = $"Falsche Protocol-Version. Erwartet: {AiConnectorProtocol.ProtocolVersion}" });
            return;
        }

        // ── Patch-Safety-Validation ───────────────────────────────────────────
        var projectRoot = _projectRoot;
        var patchErrors = ConnectorHardening.ValidatePatchRequest(patchReq, projectRoot);
        if (patchErrors.Count > 0)
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                new { error = "Patch-Validierung fehlgeschlagen.", details = patchErrors });
            return;
        }

        // Proposal-ID erzeugen und in Pending-Liste eintragen
        var proposalId = Guid.NewGuid().ToString("N")[..16];
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[proposalId] = tcs;

        // UI-Thread benachrichtigen
        PatchProposalReceived?.Invoke(proposalId, patchReq);

        // Antwort 202: Proposal eingereicht, warte auf User-Entscheidung
        await WriteJsonAsync(resp, AiConnectorProtocol.StatusAccepted, new AiPatchResponse
        {
            ProposalId = proposalId,
            Status     = "pending",
            Message    = "Patch-Vorschlag wurde eingereicht. Warte auf Nutzer-Genehmigung."
        });

        // Hinweis: Der HTTP-Response ist bereits geschlossen (202 sofort).
        // Status-Polling via GET /patch/{id}/status.
        // TCS wird aufgelöst wenn User im UI entscheidet.
    }

    // ── GET /patch/{id}/status ────────────────────────────────────────────────

    private async Task HandlePatchStatusAsync(HttpListenerResponse resp, string path)
    {
        // Pfad: /aaia/v1/patch/{id}/status
        var parts = path.TrimEnd('/').Split('/');
        var id    = parts.Length >= 5 ? parts[^2] : "";

        if (string.IsNullOrEmpty(id))
        {
            await WriteJsonAsync(resp, AiConnectorProtocol.StatusBadRequest,
                new { error = "Proposal-ID fehlt." });
            return;
        }

        var isPending = _pending.ContainsKey(id);
        var status    = isPending ? "pending" : "resolved";

        await WriteJsonAsync(resp, AiConnectorProtocol.StatusOk, new AiPatchStatusResponse
        {
            ProposalId   = id,
            Status       = status,
            PendingCount = isPending ? 1 : 0
        });
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static async Task WriteJsonAsync(HttpListenerResponse resp, int statusCode, object body)
    {
        var json  = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetBytes(json);

        resp.StatusCode   = statusCode;
        resp.ContentType  = AiConnectorProtocol.ContentTypeJson;
        resp.ContentLength64 = bytes.Length;

        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
        _cts?.Dispose();
    }
}
