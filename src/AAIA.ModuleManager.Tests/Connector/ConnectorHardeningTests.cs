using System.Net;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using Xunit;

namespace AAIA.ModuleManager.Tests.Connector;

/// <summary>Tests für ConnectorHardening — localhost-check, rate-limit, patch-validation.</summary>
public sealed class ConnectorHardeningTests
{
    // ── IsLocalhost ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1",   true)]
    [InlineData("::1",         true)]
    [InlineData("0.0.0.0",     false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("10.0.0.1",    false)]
    [InlineData("8.8.8.8",     false)]
    public void IsLocalhost_KnownAddresses(string ip, bool expected)
    {
        var addr = IPAddress.Parse(ip);
        Assert.Equal(expected, ConnectorHardening.IsLocalhost(addr));
    }

    [Fact]
    public void IsLocalhost_Null_ReturnsFalse()
        => Assert.False(ConnectorHardening.IsLocalhost(null));

    [Fact]
    public void IsLocalhost_IPv4MappedIPv6Loopback_ReturnsTrue()
    {
        // ::ffff:127.0.0.1 — IPv4-mapped loopback
        var mapped = IPAddress.Parse("::ffff:127.0.0.1");
        Assert.True(ConnectorHardening.IsLocalhost(mapped));
    }

    // ── IsBodyTooLarge ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,                                              false)]
    [InlineData(512 * 1024 - 1,                                false)]
    [InlineData(512 * 1024,                                    false)]
    [InlineData(512 * 1024 + 1,                                true)]
    [InlineData(1024 * 1024,                                   true)]
    public void IsBodyTooLarge_Boundary(long bytes, bool expected)
        => Assert.Equal(expected, ConnectorHardening.IsBodyTooLarge(bytes));

    // ── IsRateLimited ─────────────────────────────────────────────────────────

    [Fact]
    public void IsRateLimited_UnderLimit_ReturnsFalse()
    {
        const string id = "test-rate-ok";
        ConnectorHardening.ResetRateWindow(id);

        for (int i = 0; i < ConnectorHardening.MaxRequestsPerMinute; i++)
            Assert.False(ConnectorHardening.IsRateLimited(id));
    }

    [Fact]
    public void IsRateLimited_AtLimit_ReturnsTrue()
    {
        const string id = "test-rate-limit";
        ConnectorHardening.ResetRateWindow(id);

        for (int i = 0; i < ConnectorHardening.MaxRequestsPerMinute; i++)
            ConnectorHardening.IsRateLimited(id);

        Assert.True(ConnectorHardening.IsRateLimited(id));
    }

    // ── ValidatePatchTarget ───────────────────────────────────────────────────

    [Theory]
    [InlineData("src/MyExtension/aaia-extension.json", null)]
    [InlineData("src/MyExtension/MyService.cs",        null)]
    [InlineData("README.md",                            null)]
    public void ValidatePatchTarget_ValidPaths_ReturnsNull(string path, string? _)
        => Assert.Null(ConnectorHardening.ValidatePatchTarget(path));

    [Theory]
    [InlineData("../escape/file.cs")]
    [InlineData("../../etc/passwd")]
    [InlineData("src/../../config.json")]
    public void ValidatePatchTarget_PathTraversal_ReturnsError(string path)
        => Assert.NotNull(ConnectorHardening.ValidatePatchTarget(path));

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env")]
    [InlineData("secrets/api_key.json")]
    [InlineData("src/credentials.json")]
    [InlineData("private.pem")]
    [InlineData("cert.pfx")]
    [InlineData("appsettings.production.json")]
    public void ValidatePatchTarget_SensitivePaths_ReturnsError(string path)
        => Assert.NotNull(ConnectorHardening.ValidatePatchTarget(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidatePatchTarget_EmptyOrNull_ReturnsError(string? path)
        => Assert.NotNull(ConnectorHardening.ValidatePatchTarget(path));

    [Fact]
    public void ValidatePatchTarget_AbsolutePath_ReturnsError()
        => Assert.NotNull(ConnectorHardening.ValidatePatchTarget(@"C:\Windows\System32\cmd.exe"));

    // ── ValidatePatchRequest ──────────────────────────────────────────────────

    [Fact]
    public void ValidatePatchRequest_ValidRequest_NoErrors()
    {
        var req = new AiPatchRequest
        {
            ProtocolVersion = "aaia-connector-v1",
            Patches =
            [
                new AiPatchItem { TargetFile = "src/File.cs", Kind = "FullFileReplacement", Content = "class X {}" }
            ]
        };
        var errors = ConnectorHardening.ValidatePatchRequest(req);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidatePatchRequest_EmptyPatches_ReturnsError()
    {
        var req = new AiPatchRequest { Patches = [] };
        var errors = ConnectorHardening.ValidatePatchRequest(req);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidatePatchRequest_TooManyPatches_ReturnsError()
    {
        var req = new AiPatchRequest();
        for (int i = 0; i < 25; i++)
            req.Patches.Add(new AiPatchItem { TargetFile = $"src/File{i}.cs", Kind = "FullFileReplacement", Content = "" });

        var errors = ConnectorHardening.ValidatePatchRequest(req);
        Assert.Contains(errors, e => e.Contains("Zu viele Patches"));
    }

    [Fact]
    public void ValidatePatchRequest_InvalidKind_ReturnsError()
    {
        var req = new AiPatchRequest
        {
            Patches = [ new AiPatchItem { TargetFile = "src/File.cs", Kind = "HackTheWorld", Content = "" } ]
        };
        var errors = ConnectorHardening.ValidatePatchRequest(req);
        Assert.Contains(errors, e => e.Contains("Kind"));
    }

    [Fact]
    public void ValidatePatchRequest_SensitiveTarget_ReturnsError()
    {
        var req = new AiPatchRequest
        {
            Patches = [ new AiPatchItem { TargetFile = ".env", Kind = "FullFileReplacement", Content = "" } ]
        };
        var errors = ConnectorHardening.ValidatePatchRequest(req);
        Assert.NotEmpty(errors);
    }
}
