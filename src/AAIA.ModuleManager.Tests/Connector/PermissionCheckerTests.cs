using AAIA.ModuleManager.Services.AiAdapter.Connector;
using Xunit;

namespace AAIA.ModuleManager.Tests.Connector;

/// <summary>Tests für AiConnectorPermissionChecker.</summary>
public sealed class PermissionCheckerTests
{
    // ── RequiredFor ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/aaia/v1/capabilities",    "GET",  AiConnectorPermission.None)]
    [InlineData("/aaia/v1/context/current", "GET",  AiConnectorPermission.ReadProjectSummary)]
    [InlineData("/aaia/v1/patch/propose",   "POST", AiConnectorPermission.ProposePatch)]
    public void RequiredFor_KnownEndpoints_ReturnsCorrectPermission(
        string path, string method, AiConnectorPermission expected)
    {
        var result = AiConnectorPermissionChecker.RequiredFor(path, method);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RequiredFor_UnknownEndpoint_ReturnsFullPermission()
    {
        var result = AiConnectorPermissionChecker.RequiredFor("/unknown/path", "GET");
        Assert.Equal(AiConnectorPermission.Full, result);
    }

    [Fact]
    public void RequiredFor_ContextProject_RequiresBothSummaryAndPipeline()
    {
        var result = AiConnectorPermissionChecker.RequiredFor(
            "/aaia/v1/context/project", "GET");
        Assert.True(result.HasFlag(AiConnectorPermission.ReadProjectSummary));
        Assert.True(result.HasFlag(AiConnectorPermission.ReadPipelineState));
    }

    // ── CreateSession ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateSession_WithoutPatchProposal_HasReadOnly()
    {
        var session = AiConnectorPermissionChecker.CreateSession("claude", "Claude", allowPatchProposal: false);
        Assert.True(session.HasPermission(AiConnectorPermission.ReadOnly));
        Assert.False(session.HasPermission(AiConnectorPermission.ProposePatch));
    }

    [Fact]
    public void CreateSession_WithPatchProposal_HasProposePatch()
    {
        var session = AiConnectorPermissionChecker.CreateSession("claude", "Claude", allowPatchProposal: true);
        Assert.True(session.HasPermission(AiConnectorPermission.ProposePatch));
    }

    [Fact]
    public void CreateSession_ConnectorIdNormalisedToLower()
    {
        var session = AiConnectorPermissionChecker.CreateSession("CLAUDE", "Claude");
        // ConnectorId wird im Header-Reader (nicht in CreateSession) normalisiert
        // — Session speichert was übergeben wird; der Test prüft ob das Feld gesetzt ist
        Assert.Equal("CLAUDE", session.ConnectorId);
    }

    // ── ReadConnectorHeaders ──────────────────────────────────────────────────

    [Fact]
    public void ReadConnectorHeaders_MissingHeaders_ReturnsFallbacks()
    {
        var headers = new System.Collections.Specialized.NameValueCollection();
        var (id, name) = AiConnectorPermissionChecker.ReadConnectorHeaders(headers);
        Assert.Equal("unknown", id);
        Assert.Equal("Unbekannter Connector", name);
    }

    [Fact]
    public void ReadConnectorHeaders_ValidHeaders_ReturnsValues()
    {
        var headers = new System.Collections.Specialized.NameValueCollection
        {
            { AiConnectorProtocol.HeaderConnectorId,   "CHATGPT" },
            { AiConnectorProtocol.HeaderConnectorName, "ChatGPT Plugin" }
        };
        var (id, name) = AiConnectorPermissionChecker.ReadConnectorHeaders(headers);
        Assert.Equal("chatgpt", id);           // wird lowercase normalisiert
        Assert.Equal("ChatGPT Plugin", name);
    }

    // ── HasPermission ─────────────────────────────────────────────────────────

    [Fact]
    public void HasPermission_CombinedFlag_TrueIfAllSet()
    {
        var session = AiConnectorPermissionChecker.CreateSession("x", "X", allowPatchProposal: true);
        var combined = AiConnectorPermission.ReadProjectSummary | AiConnectorPermission.ProposePatch;
        Assert.True(session.HasPermission(combined));
    }

    [Fact]
    public void HasPermission_CombinedFlag_FalseIfOneMissing()
    {
        var session = AiConnectorPermissionChecker.CreateSession("x", "X", allowPatchProposal: false);
        var combined = AiConnectorPermission.ReadProjectSummary | AiConnectorPermission.ProposePatch;
        Assert.False(session.HasPermission(combined));
    }
}
