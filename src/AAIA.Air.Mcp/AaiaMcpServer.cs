using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.Air;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;   // UseUrls (Class-Library-SDK importiert das nicht automatisch)
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// SDK-Nahtstelle — offizielles C# MCP SDK 1.4.0:
//   ModelContextProtocol            (Builder-Extensions: WithHttpTransport/WithListToolsHandler/WithCallToolHandler)
//   ModelContextProtocol.AspNetCore (Streamable-HTTP-Transport, MapMcp)
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AAIA.Air.Mcp;

/// <summary>
/// Hostet die MCP-Bridge auf dem offiziellen C# MCP SDK (Streamable HTTP) — lokal,
/// nur 127.0.0.1, token-geschützt, standardmäßig deaktiviert. Getrennt vom bestehenden
/// Connector-Server (39157) auf eigenem Port (39158).
///
/// Selbst gebaut wird NUR die Verdrahtung; JSON-RPC/Transport/Tool-Listing liefert das SDK.
/// </summary>
public sealed class AaiaMcpServer : IAsyncDisposable
{
    private readonly AaiaMcpBridgeOptions _options;
    private readonly AaiaMcpAuthHandler   _auth;
    private readonly AaiaMcpAdapter       _adapter;

    private WebApplication? _app;
    private volatile bool   _running;

    public bool   IsRunning => _running;
    public int    Port      => _options.Port;
    public string Url       => $"http://127.0.0.1:{_options.Port}{_options.Path}";

    public event Action<string>? Log;

    public AaiaMcpServer(AaiaMcpBridgeOptions options, AaiaMcpAuthHandler auth, AaiaMcpAdapter adapter)
    {
        _options = options;
        _auth    = auth;
        _adapter = adapter;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return;
        if (!_options.Enabled) throw new InvalidOperationException("MCP-Bridge ist deaktiviert.");
        if (_options.Transport != AaiaMcpTransportMode.StreamableHttp)
            throw new NotSupportedException("In Phase 7.0 ist nur StreamableHttp implementiert.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // Nur an Loopback binden — Remote-Zugriff unmöglich.
        builder.WebHost.UseUrls($"http://127.0.0.1:{_options.Port}");

        // ── SDK 1.4.0: MCP-Server mit dynamischen Handlern aus der Runtime-Registry ──
        // ListTools/CallTool sind in SDK 1.x Builder-Extension-Methods (keine Properties
        // mehr auf ToolsCapability). Die Tools-Capability wird dadurch automatisch gesetzt.
        builder.Services
            .AddMcpServer(opts =>
            {
                opts.ServerInfo = new Implementation { Name = "aaia-module-manager", Version = "7.0.0" };
            })
            .WithHttpTransport()
            .WithListToolsHandler((ctx, token) =>
            {
                var session = ResolveSession(ctx);
                var tools = _adapter.ListTools(session.SessionId)
                    .Select(t => new Tool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = t.InputSchema
                    })
                    .ToList();
                return ValueTask.FromResult(new ListToolsResult { Tools = tools });
            })
            .WithCallToolHandler(async (ctx, token) =>
            {
                var session = ResolveSession(ctx);
                var name = ctx.Params?.Name ?? "";
                var args = ctx.Params?.Arguments is { } a
                    ? JsonSerializer.SerializeToElement(a)
                    : JsonDocument.Parse("{}").RootElement;

                var (ok, json) = await _adapter
                    .CallToolAsync(session.SessionId, name, args, token)
                    .ConfigureAwait(false);

                return new CallToolResult
                {
                    IsError = !ok,
                    Content = [new TextContentBlock { Text = json }]
                };
            });

        var app = builder.Build();

        // ── Bridge-Token-Pflicht: jeder Request braucht gültiges Bearer-Token ────
        app.Use(async (context, next) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (!_auth.ValidateAuthorizationHeader(header))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: gültiges Bridge-Token erforderlich.");
                return;
            }
            await next();
        });

        app.MapMcp(_options.Path);

        _app = app;
        await app.StartAsync(ct).ConfigureAwait(false);
        _running = true;
        Log?.Invoke($"MCP-Bridge gestartet: {Url}");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_running || _app is null) return;
        _running = false;
        try { await _app.StopAsync(ct).ConfigureAwait(false); }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
            Log?.Invoke("MCP-Bridge gestoppt.");
        }
    }

    /// <summary>
    /// Leitet aus dem MCP-Request-Kontext eine stabile Client-Session ab, damit mehrere
    /// Clients gleichzeitig getrennt arbeiten. Identität kommt aus der MCP-clientInfo.
    /// </summary>
    private AiSession ResolveSession<TParams>(RequestContext<TParams> ctx)
    {
        // Modellneutral: Identität kommt ausschließlich aus der vom Client gemeldeten
        // clientInfo. KEINE Verzweigung nach Modellname (kein "if Claude / if GPT").
        var info = ctx.Server?.ClientInfo;
        var identity = new AiClientIdentity
        {
            Name        = info?.Name ?? "MCP Client",
            Version     = info?.Version ?? "",
            Vendor      = "", // wird nicht aus dem Namen abgeleitet
            Fingerprint = $"{info?.Name}:{info?.Version}"
        };
        var clientKey = identity.Fingerprint;
        return _adapter.ResolveSession(clientKey, identity, AiCapabilityManager.DefaultMcpCapabilities());
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
