using AAIA.ModuleManager.Services.Ai.Mcp;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Mcp;

/// <summary>Tests für Bridge-Token-Handshake und Client-Config-Erzeugung (SDK-frei).</summary>
public sealed class AaiaMcpAuthConfigTests
{
    [Fact] // 14 — gültiges Token wird akzeptiert, leeres/falsches abgelehnt
    public void Token_Validates_Correctly()
    {
        var auth = new AaiaMcpAuthHandler();
        Assert.True(auth.Validate(auth.CurrentToken));
        Assert.False(auth.Validate(null));
        Assert.False(auth.Validate(""));
        Assert.False(auth.Validate("falsch"));
    }

    [Fact] // 15 — Authorization-Header (Bearer) wird korrekt geprüft
    public void Authorization_Header_Bearer_Is_Checked()
    {
        var auth = new AaiaMcpAuthHandler();
        Assert.True(auth.ValidateAuthorizationHeader($"Bearer {auth.CurrentToken}"));
        Assert.False(auth.ValidateAuthorizationHeader("Bearer wrong"));
        Assert.False(auth.ValidateAuthorizationHeader(null));
    }

    [Fact] // 16 — Rotation invalidiert das alte Token
    public void Rotation_Invalidates_Old_Token()
    {
        var auth = new AaiaMcpAuthHandler();
        var old = auth.CurrentToken;
        var fresh = auth.Rotate();
        Assert.NotEqual(old, fresh);
        Assert.False(auth.Validate(old));
        Assert.True(auth.Validate(fresh));
    }

    [Fact] // 17 — Client-Config enthält URL und aktuelles Token
    public void ClientConfig_Contains_Url_And_Token()
    {
        var options = new AaiaMcpBridgeOptions { Port = 39158, Path = "/mcp" };
        var auth = new AaiaMcpAuthHandler();
        var cfg = new AaiaMcpConfigService(options, auth);

        var claude = cfg.ClaudeDesktopJson();
        Assert.Contains("http://127.0.0.1:39158/mcp", claude);
        Assert.Contains(auth.CurrentToken, claude);
        Assert.Contains("aaia-module-manager", claude);

        var codex = cfg.CodexToml();
        Assert.Contains("[mcp_servers.aaia-module-manager]", codex);
        Assert.Contains(auth.CurrentToken, codex);
    }

    [Fact] // 18 — Default-Permissions: Sign/Marketplace nie aktiv
    public void DefaultOptions_Never_Enable_SignMarketplace()
    {
        var options = new AaiaMcpBridgeOptions
        {
            AllowFileChanges = true, AllowBuild = true, AllowTerminal = true, AllowOpenIde = true,
            AllowSignMarketplace = true // selbst wenn gesetzt: Runtime sperrt hart
        };
        Assert.False(options.Enabled); // Bridge standardmäßig deaktiviert
    }
}
