using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.Air;

/// <summary>
/// Definition eines Runtime-Tools. Gehört in die AI Runtime — NICHT in MCP.
/// MCP (und spätere Adapter) registrieren nur diese Definitionen.
/// </summary>
public sealed class AiToolDefinition
{
    public required string Name { get; init; }

    /// <summary>SemVer, z. B. "1.0.0". Versionierung statt "build2".</summary>
    public string Version { get; init; } = "1.0.0";

    public bool Deprecated { get; init; }

    /// <summary>Ab welcher Runtime-Version verfügbar.</summary>
    public string? Since { get; init; }

    public required string Description { get; init; }

    public required AiRiskLevel RiskLevel { get; init; }

    public bool RequiresApproval { get; init; }

    /// <summary>Capabilities, die der Client besitzen muss, damit das Tool angeboten wird.</summary>
    public string[] RequiredCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>Permissions, die die Session besitzen muss, damit das Tool ausgeführt wird.</summary>
    public AiPermission RequiredPermissions { get; init; } = AiPermission.None;

    /// <summary>JSON-Schema des Inputs (für das SDK-Tool-Listing).</summary>
    public JsonElement InputSchema { get; init; }

    /// <summary>Tatsächliche Implementierung — ruft Phase-6-Services auf.</summary>
    public required Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> Handler { get; init; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Kontext eines einzelnen Tool-Aufrufs.</summary>
public sealed class AiToolInvocation
{
    public required AiSession Session { get; init; }
    public required string ToolName { get; init; }
    public required JsonElement Input { get; init; }
}

/// <summary>Ergebnis eines Tool-Aufrufs (adapter-neutral).</summary>
public sealed class AiToolResult
{
    public bool Success { get; init; }
    public JsonElement Payload { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static AiToolResult Ok(object payload)
        => new() { Success = true, Payload = ToElement(payload) };

    public static AiToolResult Fail(string error, string? code = null)
        => new() { Success = false, Error = error, ErrorCode = code };

    private static JsonElement ToElement(object value)
    {
        var json = JsonSerializer.SerializeToElement(value);
        return json;
    }
}

/// <summary>
/// Module bringen eigene Tools mit. Die Runtime lädt sie automatisch,
/// ohne sie fest zu kennen (FRITZ!Box, WordPress, Scanner …).
/// </summary>
public interface IAiToolProvider
{
    /// <summary>Eindeutige Provider-Kennung (z. B. Modul-Id) — für Audit.</summary>
    string ProviderId { get; }

    /// <summary>Liefert die vom Modul bereitgestellten Tool-Definitionen.</summary>
    System.Collections.Generic.IEnumerable<AiToolDefinition> GetTools();
}
