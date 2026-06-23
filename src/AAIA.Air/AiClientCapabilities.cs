using System;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.Air;

/// <summary>
/// Strukturiertes Fähigkeitsprofil eines Clients. Erweiterte Capability Negotiation:
/// nicht nur "kann Events?", sondern auch Kontextfenster, Reasoning, Streaming, Vision,
/// Dateien, Terminal und MCP-Version. Die Runtime passt sich daran an — modellneutral,
/// ohne jede modellspezifische Verzweigung.
/// </summary>
public sealed class AiClientCapabilities
{
    /// <summary>Maximales Kontextfenster in Tokens (0 = unbekannt).</summary>
    public int ContextWindowTokens { get; init; }

    public bool Reasoning { get; init; }
    public bool Streaming { get; init; }
    public bool Vision { get; init; }
    public bool Files { get; init; }
    public bool Terminal { get; init; }
    public bool Events { get; init; }

    /// <summary>Vom Client gemeldete MCP-Protokollversion (z. B. "2025-11-25").</summary>
    public string McpVersion { get; init; } = "";

    /// <summary>Freie zusätzliche Capability-Tags.</summary>
    public IReadOnlyList<string> Extra { get; init; } = Array.Empty<string>();

    /// <summary>Übersetzt das Profil in die flache Capability-Menge der Session.</summary>
    public IEnumerable<string> ToCapabilityTags()
    {
        var tags = new List<string> { AiCapabilities.Mcp, AiCapabilities.ToolCalling };
        if (Reasoning) tags.Add(AiCapabilities.Reasoning);
        if (Streaming) tags.Add(AiCapabilities.Streaming);
        if (Vision)    tags.Add(AiCapabilities.Vision);
        if (Files)     tags.Add(AiCapabilities.Files);
        if (Terminal)  tags.Add(AiCapabilities.Terminal);
        if (Events)    tags.Add(AiCapabilities.Events);
        if (ContextWindowTokens >= 200_000) tags.Add(AiCapabilities.LargeContext);
        tags.AddRange(Extra);
        return tags.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
