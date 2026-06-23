using AAIA.Air.Contracts;
using AAIA.Air.SDK;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class AirSdkTests
{
    [Fact]
    public void Provider_Builds_PublicToolDefinition()
    {
        var tool = Assert.Single(new DemoProvider().GetTools());

        Assert.Equal("demo.status", tool.Name);
        Assert.Equal(AiRiskLevel.Green, tool.RiskLevel);
        Assert.Equal(AiPermission.Read, tool.RequiredPermissions);
        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void Builder_Rejects_DuplicateToolNames()
    {
        var builder = new AirToolBuilder()
            .Add("demo.status", "Status", AiRiskLevel.Green, SuccessAsync);

        Assert.Throws<InvalidOperationException>(() =>
            builder.Add("DEMO.STATUS", "Duplicate", AiRiskLevel.Green, SuccessAsync));
    }

    private static Task<AiToolResult> SuccessAsync(AiToolInvocation _, CancellationToken __)
        => Task.FromResult(AiToolResult.Ok(new { status = "ok" }));

    private sealed class DemoProvider : AirToolProviderBase
    {
        public override string ProviderId => "demo";

        protected override void Configure(AirToolBuilder tools)
            => tools.Add("demo.status", "Status", AiRiskLevel.Green, SuccessAsync);
    }
}
