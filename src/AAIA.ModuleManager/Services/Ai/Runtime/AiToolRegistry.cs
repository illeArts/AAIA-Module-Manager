using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Zentrale Tool-Registry der AI Runtime. Tools werden einmal registriert und
/// über jeden Adapter (MCP heute, REST/Internal später) angeboten.
/// Black-Tools werden hart abgelehnt und niemals registriert.
/// </summary>
public sealed class AiToolRegistry
{
    // Name → Liste von Versionen
    private readonly ConcurrentDictionary<string, List<AiToolDefinition>> _tools = new(StringComparer.Ordinal);
    private readonly List<IAiToolProvider> _providers = new();
    private readonly object _gate = new();

    /// <summary>Registriert ein Tool. Black-Risk wird abgelehnt.</summary>
    public void Register(AiToolDefinition tool)
    {
        if (tool.RiskLevel == AiRiskLevel.Black)
            throw new InvalidOperationException($"Black-Tools werden nicht implementiert: {tool.Name}");

        lock (_gate)
        {
            var versions = _tools.GetOrAdd(tool.Name, _ => new List<AiToolDefinition>());
            versions.Add(tool);
        }
    }

    public void RegisterRange(IEnumerable<AiToolDefinition> tools)
    {
        foreach (var t in tools) Register(t);
    }

    /// <summary>
    /// Registriert einen Modul-Tool-Provider (IAiToolProvider). Dessen Tools
    /// erscheinen automatisch in der Registry, ohne dass der Kern sie kennt.
    /// </summary>
    public void RegisterProvider(IAiToolProvider provider)
    {
        lock (_gate)
        {
            _providers.Add(provider);
            foreach (var t in provider.GetTools())
                Register(t);
        }
    }

    public IReadOnlyList<IAiToolProvider> Providers
    {
        get { lock (_gate) return _providers.ToList(); }
    }

    /// <summary>Aktiviert/deaktiviert ein Tool (einzeln schaltbar).</summary>
    public void SetActive(string name, bool active)
    {
        lock (_gate)
        {
            if (_tools.TryGetValue(name, out var versions))
                foreach (var v in versions) v.IsActive = active;
        }
    }

    /// <summary>Löst die aktive (höchste, nicht-deprecatete) Version eines Tools auf.</summary>
    public AiToolDefinition? Resolve(string name)
    {
        lock (_gate)
        {
            if (!_tools.TryGetValue(name, out var versions) || versions.Count == 0)
                return null;
            return AiToolVersioning.ResolveActive(versions);
        }
    }

    /// <summary>Alle aktiven Tools (höchste Version je Name).</summary>
    public IReadOnlyList<AiToolDefinition> ListActive()
    {
        lock (_gate)
        {
            return _tools.Keys
                .Select(Resolve)
                .Where(t => t is { IsActive: true })
                .Select(t => t!)
                .ToList();
        }
    }

    /// <summary>
    /// Tools, die einer konkreten Session angeboten werden dürfen —
    /// gefiltert nach Capabilities (Capability Negotiation). Permissions werden
    /// erst bei der Ausführung geprüft, damit der Client sieht, was es gäbe.
    /// </summary>
    public IReadOnlyList<AiToolDefinition> ListForSession(AiSession session)
    {
        return ListActive()
            .Where(t => t.RequiredCapabilities.All(session.HasCapability))
            .ToList();
    }

    public bool Exists(string name)
    {
        lock (_gate) return _tools.ContainsKey(name);
    }
}
