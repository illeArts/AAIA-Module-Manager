using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AAIA.Air.Providers;

/// <summary>Bekannte externe Modul-Fähigkeiten (nicht KI-Capabilities, sondern Ressourcen).</summary>
public static class AiRequiredCapabilities
{
    public const string Filesystem = "filesystem";
    public const string Scanner    = "scanner";
    public const string Router     = "router";
    public const string Docker     = "docker";
    public const string Git        = "git";
    public const string Network    = "network";
}

/// <summary>
/// Ein Modul deklariert, welche externen Fähigkeiten es benötigt (Filesystem, Scanner,
/// Router, Docker, Git …). Die Runtime weiß dadurch automatisch, welche Ressourcen ein
/// Modul braucht — Grundlage für spätere Freigaben/Sandbox-Entscheidungen.
/// Ergänzt <see cref="AAIA.Air.IAiToolProvider"/>.
/// </summary>
public interface IAiCapabilityProvider
{
    string ProviderId { get; }
    IEnumerable<string> RequiredCapabilities();
}

/// <summary>Sammelt die Capability-Anforderungen aller Module.</summary>
public sealed class AiCapabilityRequirementRegistry
{
    private readonly ConcurrentDictionary<string, string[]> _byProvider = new(StringComparer.Ordinal);

    public void Register(IAiCapabilityProvider provider)
        => _byProvider[provider.ProviderId] = provider.RequiredCapabilities().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyDictionary<string, string[]> ByProvider => _byProvider;

    /// <summary>Alle benötigten Fähigkeiten über alle Module (dedupliziert).</summary>
    public IReadOnlyList<string> All()
        => _byProvider.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
}
