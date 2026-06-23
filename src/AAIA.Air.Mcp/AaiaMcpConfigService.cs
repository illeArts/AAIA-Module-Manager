using System.Text;

namespace AAIA.Air.Mcp;

/// <summary>
/// Erzeugt Client-Konfigurationen (Claude Desktop, Codex) inkl. aktuellem Bridge-Token.
/// Das Token ist der Bridge-Zugangsschlüssel — KEIN Projekt-Secret.
/// </summary>
public sealed class AaiaMcpConfigService
{
    private readonly AaiaMcpBridgeOptions _options;
    private readonly AaiaMcpAuthHandler   _auth;

    public AaiaMcpConfigService(AaiaMcpBridgeOptions options, AaiaMcpAuthHandler auth)
    {
        _options = options;
        _auth    = auth;
    }

    public string Url => $"http://127.0.0.1:{_options.Port}{_options.Path}";

    /// <summary>Claude-Desktop-Konfiguration (Streamable HTTP, Token im Header).</summary>
    public string ClaudeDesktopJson()
    {
        var token = _auth.CurrentToken;
        return $$"""
        {
          "mcpServers": {
            "aaia-module-manager": {
              "url": "{{Url}}",
              "headers": { "Authorization": "Bearer {{token}}" }
            }
          }
        }
        """;
    }

    /// <summary>Codex-Konfiguration (~/.codex/config.toml).</summary>
    public string CodexToml()
    {
        var token = _auth.CurrentToken;
        var sb = new StringBuilder();
        sb.AppendLine("[mcp_servers.aaia-module-manager]");
        sb.AppendLine($"url = \"{Url}\"");
        sb.AppendLine($"headers = {{ Authorization = \"Bearer {token}\" }}");
        return sb.ToString();
    }
}
