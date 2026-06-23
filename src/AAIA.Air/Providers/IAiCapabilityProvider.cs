using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAIA.Air.Contracts;

namespace AAIA.Air.Providers;

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
