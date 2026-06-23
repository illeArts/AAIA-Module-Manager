using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime;

namespace AAIA.ModuleManager.Services.Ai.Mcp;

/// <summary>
/// Übersetzt zwischen MCP und der AI Runtime. Enthält BEWUSST keine Tool-Logik und
/// keine Registry — nur Übersetzung. Diese Klasse ist SDK-frei (arbeitet mit Runtime
/// + JSON), damit die SDK-Abhängigkeit allein im AaiaMcpServer liegt.
/// </summary>
public sealed class AaiaMcpAdapter
{
    private readonly AiRuntimeService     _runtime;
    private readonly AiSessionManager     _sessions;
    private readonly AiCapabilityManager  _capabilities;
    private readonly AaiaMcpBridgeOptions  _options;

    public AaiaMcpAdapter(
        AiRuntimeService runtime,
        AiSessionManager sessions,
        AiCapabilityManager capabilities,
        AaiaMcpBridgeOptions options)
    {
        _runtime      = runtime;
        _sessions     = sessions;
        _capabilities = capabilities;
        _options      = options;
    }

    /// <summary>Plain-Beschreibung eines Tools für das MCP-Tool-Listing.</summary>
    public sealed record McpToolInfo(string Name, string Description, JsonElement InputSchema);

    /// <summary>
    /// Tools, die einer Session angeboten werden — bereits nach Capability gefiltert.
    /// Der Server mappt das auf die MCP-Tool-Struktur.
    /// </summary>
    public IReadOnlyList<McpToolInfo> ListTools(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session is null) return Array.Empty<McpToolInfo>();

        return _runtime.Tools.ListForSession(session)
            .Select(t => new McpToolInfo(t.Name, t.Description, t.InputSchema))
            .ToList();
    }

    /// <summary>
    /// Führt einen Tool-Call aus. Gibt (success, jsonPayload) zurück; der Server verpackt
    /// das in MCP-Content. Die komplette Sicherheits-Kette läuft in der Runtime.
    /// </summary>
    public async Task<(bool Success, string Json)> CallToolAsync(
        string sessionId, string toolName, JsonElement arguments, CancellationToken ct)
    {
        var result = await _runtime.InvokeToolAsync(sessionId, toolName, arguments, ct).ConfigureAwait(false);

        var payload = result.Success
            ? result.Payload
            : JsonSerializer.SerializeToElement(new { error = result.Error, code = result.ErrorCode });

        return (result.Success, payload.GetRawText());
    }

    /// <summary>
    /// Findet oder erzeugt die Session eines MCP-Clients. Identität/Capabilities kommen
    /// aus optionalen Headern (X-AAIA-Client-*) bzw. MCP-clientInfo; Default-Permissions
    /// aus den UI-Toggles. So bekommt jeder Client seine eigene Session (Mehrclient ab Start).
    /// </summary>
    public AiSession ResolveSession(string clientKey, AiClientIdentity identity, IEnumerable<string>? capabilities)
    {
        var existing = _sessions.Active.FirstOrDefault(s => s.ClientId == clientKey);
        if (existing is not null) return existing;

        var caps = capabilities?.ToList() ?? AiCapabilityManager.DefaultMcpCapabilities().ToList();
        var session = _sessions.Create(identity, caps, DefaultPermissionsFromOptions());
        _capabilities.Negotiate(session, caps);
        return session;
    }

    /// <summary>Default-Permissions neuer Sessions aus den Bridge-Toggles. Sign/Marketplace nie.</summary>
    public AiPermission DefaultPermissionsFromOptions()
    {
        var p = AiPermission.Read | AiPermission.Validate; // Read + Validate sind harmlos
        if (_options.AllowFileChanges) p |= AiPermission.CreateProject | AiPermission.ProposePatch;
        if (_options.AllowBuild)       p |= AiPermission.Build | AiPermission.Package;
        if (_options.AllowTerminal)    p |= AiPermission.RunSafeTerminal;
        if (_options.AllowOpenIde)     p |= AiPermission.OpenIde;
        // Sign/Marketplace bleiben in 7.0 immer gesperrt.
        return p;
    }
}
