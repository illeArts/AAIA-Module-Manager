using System;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Capability Negotiation: Beim Connect meldet der CLIENT seine Fähigkeiten,
/// nicht umgekehrt. Die Runtime bietet nur Tools an, deren RequiredCapabilities
/// der Client erfüllt.
/// </summary>
public sealed class AiCapabilityManager
{
    /// <summary>Übernimmt die vom Client gemeldeten Capabilities in die Session.</summary>
    public void Negotiate(AiSession session, IEnumerable<string>? clientCapabilities)
    {
        session.Capabilities.Clear();
        if (clientCapabilities is null) return;
        foreach (var c in clientCapabilities.Where(c => !string.IsNullOrWhiteSpace(c)))
            session.Capabilities.Add(c.Trim());
    }

    /// <summary>
    /// Erweiterte Negotiation aus einem strukturierten Profil (Kontextfenster, Reasoning,
    /// Vision, Files, Terminal, MCP-Version …). Die Runtime passt sich daran an.
    /// </summary>
    public void Negotiate(AiSession session, AiClientCapabilities profile)
    {
        session.CapabilityProfile = profile;
        Negotiate(session, profile.ToCapabilityTags());
    }

    /// <summary>True, wenn die Session alle vom Tool verlangten Capabilities besitzt.</summary>
    public bool Supports(AiSession session, AiToolDefinition tool)
        => tool.RequiredCapabilities.All(session.HasCapability);

    /// <summary>Capabilities, die ein MCP-Client standardmäßig mitbringt.</summary>
    public static IReadOnlyList<string> DefaultMcpCapabilities() => new[]
    {
        AiCapabilities.Mcp,
        AiCapabilities.ToolCalling
    };
}
