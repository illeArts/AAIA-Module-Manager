using System.Text.Json;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;
using Xunit;

namespace AAIA.ModuleManager.Tests.Connector;

/// <summary>Tests für AiPatchRequest — JSON-Deserialisierung und ToPatchProposal-Konvertierung.</summary>
public sealed class PatchRequestTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Deserialisierung ──────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MinimalValidRequest_Succeeds()
    {
        const string json = """
        {
            "protocolVersion": "aaia-connector-v1",
            "patches": [
                {
                    "kind": "FullFileReplacement",
                    "targetFile": "src/MyExtension/MyService.cs",
                    "content": "namespace MyExtension; public class MyService {}",
                    "language": "csharp"
                }
            ]
        }
        """;

        var req = JsonSerializer.Deserialize<AiPatchRequest>(json, JsonOpts);

        Assert.NotNull(req);
        Assert.Equal("aaia-connector-v1", req.ProtocolVersion);
        Assert.Single(req.Patches);
        Assert.Equal("FullFileReplacement", req.Patches[0].Kind);
        Assert.Equal("src/MyExtension/MyService.cs", req.Patches[0].TargetFile);
    }

    [Fact]
    public void Deserialize_WithRationale_ReadsRationale()
    {
        const string json = """
        {
            "protocolVersion": "aaia-connector-v1",
            "rationale": "Die Klasse fehlte im Build.",
            "patches": [
                { "kind": "FullFileReplacement", "targetFile": "src/X.cs", "content": "", "language": "csharp" }
            ]
        }
        """;

        var req = JsonSerializer.Deserialize<AiPatchRequest>(json, JsonOpts);
        Assert.Equal("Die Klasse fehlte im Build.", req!.Rationale);
    }

    [Fact]
    public void Deserialize_EmptyPatches_ReturnsEmptyList()
    {
        const string json = """{ "protocolVersion": "aaia-connector-v1", "patches": [] }""";
        var req = JsonSerializer.Deserialize<AiPatchRequest>(json, JsonOpts);
        Assert.NotNull(req);
        Assert.Empty(req!.Patches);
    }

    [Fact]
    public void Deserialize_MultiplePatches_AllRead()
    {
        const string json = """
        {
            "protocolVersion": "aaia-connector-v1",
            "patches": [
                { "kind": "FullFileReplacement", "targetFile": "src/A.cs", "content": "A", "language": "csharp" },
                { "kind": "UnifiedDiff",          "targetFile": "src/B.cs", "content": "B", "language": "csharp" }
            ]
        }
        """;

        var req = JsonSerializer.Deserialize<AiPatchRequest>(json, JsonOpts);
        Assert.Equal(2, req!.Patches.Count);
    }

    // ── ToPatchProposal ───────────────────────────────────────────────────────

    [Fact]
    public void ToPatchProposal_FullFileReplacement_MapsCorrectly()
    {
        var item = new AiPatchItem
        {
            Kind       = "FullFileReplacement",
            TargetFile = "src/MyService.cs",
            Content    = "namespace X;\npublic class Y {}",
            Language   = "csharp",
            Description = "Klasse hinzugefügt"
        };

        var proposal = item.ToPatchProposal();

        Assert.Equal(PatchKind.FullFileReplacement, proposal.Kind);
        Assert.Equal("src/MyService.cs", proposal.SuggestedFile);
        Assert.Equal("csharp", proposal.Language);
        Assert.Equal(2, proposal.LineCount);
    }

    [Fact]
    public void ToPatchProposal_UnifiedDiff_MapsToUnifiedDiff()
    {
        var item = new AiPatchItem { Kind = "UnifiedDiff", Content = "--- a\n+++ b\n@@ -1 +1 @@\n-old\n+new", Language = "diff" };
        var proposal = item.ToPatchProposal();
        Assert.Equal(PatchKind.UnifiedDiff, proposal.Kind);
    }

    [Fact]
    public void ToPatchProposal_UnknownKind_FallsBackToCodeSnippet()
    {
        var item = new AiPatchItem { Kind = "SomethingNew", Content = "x", Language = "cs" };
        var proposal = item.ToPatchProposal();
        Assert.Equal(PatchKind.CodeSnippet, proposal.Kind);
    }

    [Fact]
    public void ToPatchProposal_RawBlock_ContainsLanguageAndContent()
    {
        var item = new AiPatchItem { Kind = "FullFileReplacement", Content = "int x = 1;", Language = "csharp" };
        var proposal = item.ToPatchProposal();
        Assert.Contains("csharp", proposal.RawBlock);
        Assert.Contains("int x = 1;", proposal.RawBlock);
    }
}
