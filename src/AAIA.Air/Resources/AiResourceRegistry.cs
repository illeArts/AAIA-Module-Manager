using System.Collections.Concurrent;

namespace AAIA.Air.Resources;

/// <summary>Thread-sichere interne Registry für Profile und Lasttelemetrie.</summary>
public sealed class AiResourceRegistry
{
    private readonly ConcurrentDictionary<string, AiResourceProfile> _profiles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AiResourceTelemetry> _telemetry = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _enabledOverrides = new(StringComparer.Ordinal);

    public void Register(AiResourceProfile profile)
    {
        ValidateProfile(profile);
        if (!_profiles.TryAdd(profile.ResourceId, Clone(profile)))
            throw new InvalidOperationException($"Ressource '{profile.ResourceId}' ist bereits registriert.");
    }

    /// <summary>Aktualisiert ein Host-Profil, ohne einen Provider-Wechsel derselben ID zuzulassen.</summary>
    public void RegisterOrUpdate(AiResourceProfile profile)
    {
        ValidateProfile(profile);
        var normalized = _enabledOverrides.TryGetValue(profile.ResourceId, out var enabled)
            ? WithEnabled(profile, enabled)
            : profile;
        _profiles.AddOrUpdate(
            profile.ResourceId,
            _ => Clone(normalized),
            (_, current) => string.Equals(current.ProviderId, normalized.ProviderId, StringComparison.Ordinal)
                ? Clone(normalized)
                : throw new InvalidOperationException("ProviderId einer registrierten Ressource darf nicht wechseln."));
    }

    public bool Unregister(string resourceId)
    {
        _telemetry.TryRemove(resourceId, out _);
        _enabledOverrides.TryRemove(resourceId, out _);
        return _profiles.TryRemove(resourceId, out _);
    }

    public bool SetEnabled(string resourceId, bool enabled)
    {
        if (!_profiles.ContainsKey(resourceId)) return false;
        _enabledOverrides[resourceId] = enabled;
        while (_profiles.TryGetValue(resourceId, out var current))
        {
            var updated = WithEnabled(current, enabled);
            if (_profiles.TryUpdate(resourceId, updated, current)) return true;
        }
        return false;
    }

    public void UpdateTelemetry(AiResourceTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!_profiles.ContainsKey(telemetry.ResourceId))
            throw new InvalidOperationException("Ressource ist nicht registriert.");
        if (telemetry.ExternalRunningExecutions < 0 || telemetry.RequestsInCurrentMinute < 0 ||
            telemetry.TokensInCurrentMinute < 0 || telemetry.WorkUnitsInCurrentMinute < 0 ||
            telemetry.QueueLatencyMs < 0 || telemetry.P95ExecutionLatencyMs < 0 ||
            telemetry.FailureRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(telemetry), "Telemetriewerte liegen außerhalb des gültigen Bereichs.");
        _telemetry[telemetry.ResourceId] = telemetry;
    }

    public AiResourceProfile? GetProfile(string resourceId)
        => _profiles.TryGetValue(resourceId, out var profile) ? Clone(profile) : null;

    public AiResourceTelemetry? GetTelemetry(string resourceId)
        => _telemetry.TryGetValue(resourceId, out var telemetry) ? telemetry : null;

    public IReadOnlyList<AiResourceProfile> ListProfiles()
        => _profiles.Values.OrderBy(profile => profile.ResourceId, StringComparer.Ordinal).Select(Clone).ToArray();

    private static void ValidateCapacity(AiResourceCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        if (capacity.MaxConcurrentExecutions is <= 0 || capacity.ContextWindowTokens is <= 0 ||
            capacity.RequestsPerMinute is <= 0 || capacity.TokensPerMinute is <= 0 ||
            capacity.WorkUnitsPerMinute is <= 0 || capacity.MemoryMiB is <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Bekannte Kapazitäten müssen größer als null sein.");
    }

    private static void ValidateProfile(AiResourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ProviderId);
        ValidateCapacity(profile.Capacity);
        if (profile.CostRate is not null &&
            (string.IsNullOrWhiteSpace(profile.CostRate.CostUnit) ||
             profile.CostRate.FixedPerExecution < 0 ||
             profile.CostRate.PerThousandInputUnits < 0 ||
             profile.CostRate.PerThousandOutputUnits < 0 ||
             profile.CostRate.PerWorkUnit < 0))
            throw new ArgumentOutOfRangeException(nameof(profile), "Kostensätze dürfen nicht negativ sein.");
    }

    private static AiResourceProfile Clone(AiResourceProfile profile) => new()
    {
        ResourceId = profile.ResourceId,
        ProviderId = profile.ProviderId,
        DisplayName = profile.DisplayName,
        Kind = profile.Kind,
        Enabled = profile.Enabled,
        Locality = profile.Locality,
        Capabilities = (profile.Capabilities ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Capacity = profile.Capacity,
        CostRate = profile.CostRate
    };

    private static AiResourceProfile WithEnabled(AiResourceProfile profile, bool enabled) => new()
    {
        ResourceId = profile.ResourceId,
        ProviderId = profile.ProviderId,
        DisplayName = profile.DisplayName,
        Kind = profile.Kind,
        Enabled = enabled,
        Locality = profile.Locality,
        Capabilities = profile.Capabilities,
        Capacity = profile.Capacity,
        CostRate = profile.CostRate
    };
}
