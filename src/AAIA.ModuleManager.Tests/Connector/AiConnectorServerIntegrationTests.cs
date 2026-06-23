using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.Services.Help;
using Xunit;

namespace AAIA.ModuleManager.Tests.Connector;

/// <summary>
/// Integration-Tests für AiConnectorServer.
/// Starten den echten HttpListener auf einem Test-Port (39197) und
/// senden echte HTTP-Requests über HttpClient.
///
/// HINWEIS: HttpListener erfordert auf Windows ggf. eine URL-Reservation
/// (netsh http add urlacl) oder Administratorrechte für beliebige Ports.
/// Port 39197 wird ausschließlich für Tests genutzt.
/// </summary>
[Collection("ConnectorServer")]  // Verhindert parallele Ausführung (gleicher Port)
public sealed class AiConnectorServerIntegrationTests : IAsyncLifetime
{
    private const int TestPort = 39197;
    private const string ApiBase = $"http://localhost:{TestPort}/aaia/v1";

    private readonly AiConnectorServer _server;
    private readonly HttpClient        _http;

    public AiConnectorServerIntegrationTests()
    {
        var config = new AppConfig();
        config.AiConnector.Port                = TestPort;
        config.AiConnector.AllowPatchProposals = true;

        _server = new AiConnectorServer(config);
        _http   = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{TestPort}"),
            Timeout     = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.Add("X-AAIA-Connector-Id",   "test-agent");
        _http.DefaultRequestHeaders.Add("X-AAIA-Connector-Name", "Test Agent");
    }

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync() => await _server.StartAsync();

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
        _http.Dispose();
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    [Fact]
    public void Server_IsRunning_AfterStart()
    {
        Assert.True(_server.IsRunning);
        Assert.Equal(TestPort, _server.Port);
    }

    [Fact]
    public async Task Server_StopsCleanly()
    {
        await _server.StopAsync();
        Assert.False(_server.IsRunning);

        // Restart für Dispose
        await _server.StartAsync();
    }

    // ── GET /capabilities ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetCapabilities_Returns200WithProtocolVersion()
    {
        var resp = await _http.GetAsync("/aaia/v1/capabilities");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AiConnectorProtocol.ProtocolVersion,
            json.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task GetCapabilities_ContainsPermissions()
    {
        var resp = await _http.GetAsync("/aaia/v1/capabilities");
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("permissions", out _),
            "Antwort muss 'permissions'-Feld enthalten.");
    }

    // ── GET /context/current ──────────────────────────────────────────────────

    [Fact]
    public async Task GetContextCurrent_Returns404_WhenNoContextSet()
    {
        // Kein UpdateContext() aufgerufen → kein aktives Projekt
        var resp = await _http.GetAsync("/aaia/v1/context/current");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetContextCurrent_Returns200_WhenContextSet()
    {
        var ctx = new AiHandoffContext
        {
            ExtensionId  = "test.extension",
            DisplayName  = "Test Extension",
            CurrentStep  = "Build",
            TrustLevel   = "Verified",
        };
        _server.UpdateContext(ctx, projectRoot: null);

        var resp = await _http.GetAsync("/aaia/v1/context/current");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test.extension", json.GetProperty("extensionId").GetString());
    }

    // ── POST /patch/propose ───────────────────────────────────────────────────

    [Fact]
    public async Task ProposePatch_ValidPatch_Returns202WithProposalId()
    {
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            rationale       = "Test-Patch",
            patches         = new[]
            {
                new
                {
                    kind       = "FullFileReplacement",
                    targetFile = "src/MyExt/test.cs",
                    content    = "// test",
                    language   = "csharp"
                }
            }
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("proposalId").GetString()),
            "proposalId darf nicht leer sein.");
        Assert.Equal("pending", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ProposePatch_ForbiddenTarget_Returns400()
    {
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches         = new[]
            {
                new
                {
                    kind       = "FullFileReplacement",
                    targetFile = ".env",
                    content    = "SECRET=bad",
                    language   = "text"
                }
            }
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("details", out _),
            "Fehlerantwort muss 'details'-Feld enthalten.");
    }

    [Fact]
    public async Task ProposePatch_PathTraversal_Returns400()
    {
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches         = new[]
            {
                new
                {
                    kind       = "FullFileReplacement",
                    targetFile = "../../etc/passwd",
                    content    = "pwned",
                    language   = "text"
                }
            }
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ProposePatch_BodyTooLarge_Returns400()
    {
        // Content-Length-Header simulieren ist mit HttpClient nicht direkt möglich,
        // daher senden wir einen echten Body über dem Limit.
        var hugePatch = new string('x', ConnectorHardening.MaxBodyBytes + 1);
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches         = new[]
            {
                new
                {
                    kind       = "FullFileReplacement",
                    targetFile = "src/big.cs",
                    content    = hugePatch,
                    language   = "csharp"
                }
            }
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        // Der Server prüft patch.Content.Length in ValidatePatchRequest → 400
        var resp = await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── GET /patch/{id}/status ────────────────────────────────────────────────

    [Fact]
    public async Task PatchStatus_UnknownId_Returns404()
    {
        var resp = await _http.GetAsync("/aaia/v1/patch/doesnotexist/status");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_PendingProposal_ReturnsPending()
    {
        // Patch einreichen
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches         = new[]
            {
                new { kind = "FullFileReplacement", targetFile = "src/x.cs", content = "// x", language = "csharp" }
            }
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var propose = await _http.PostAsync("/aaia/v1/patch/propose", content);
        Assert.Equal(HttpStatusCode.Accepted, propose.StatusCode);

        var proposeJson = await propose.Content.ReadFromJsonAsync<JsonElement>();
        var id = proposeJson.GetProperty("proposalId").GetString()!;

        // Status direkt abfragen — noch kein Approve/Reject → pending
        var status = await _http.GetAsync($"/aaia/v1/patch/{id}/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var statusJson = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", statusJson.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PatchStatus_AfterApprove_ReturnsApproved()
    {
        // Patch einreichen
        string? proposalId = null;
        _server.PatchProposalReceived += (id, _) => proposalId = id;

        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches = new[]
            {
                new { kind = "FullFileReplacement", targetFile = "src/y.cs", content = "// y", language = "csharp" }
            }
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.NotNull(proposalId);

        // Approve
        _server.ApprovePatch(proposalId!);

        var status = await _http.GetAsync($"/aaia/v1/patch/{proposalId}/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var json = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("approved", json.GetProperty("status").GetString());
        Assert.Equal(1, json.GetProperty("approvedCount").GetInt32());
    }

    [Fact]
    public async Task PatchStatus_AfterReject_ReturnsRejected()
    {
        string? proposalId = null;
        _server.PatchProposalReceived += (id, _) => proposalId = id;

        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = AiConnectorProtocol.ProtocolVersion,
            patches = new[]
            {
                new { kind = "FullFileReplacement", targetFile = "src/z.cs", content = "// z", language = "csharp" }
            }
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        await _http.PostAsync("/aaia/v1/patch/propose", content);

        Assert.NotNull(proposalId);
        _server.RejectPatch(proposalId!);

        var status = await _http.GetAsync($"/aaia/v1/patch/{proposalId}/status");
        var json = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", json.GetProperty("status").GetString());
        Assert.Equal(1, json.GetProperty("rejectedCount").GetInt32());
    }

    // ── 404 für unbekannte Pfade ──────────────────────────────────────────────

    [Fact]
    public async Task UnknownEndpoint_Returns404()
    {
        var resp = await _http.GetAsync("/aaia/v1/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}

// ── xUnit Collection-Definition (verhindert parallele Server-Starts) ──────────

[CollectionDefinition("ConnectorServer")]
public sealed class ConnectorServerCollectionDefinition { }
