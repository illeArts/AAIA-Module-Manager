using System;
using System.Collections.Generic;
using AAIA.Air.Roles;

namespace AAIA.Air;

/// <summary>
/// Eine aktive Sitzung eines KI-Clients. Mehrere Sessions existieren gleichzeitig —
/// das ist Designziel ab Phase 7.0. Ohne gültige Session kein Tool-Aufruf.
/// </summary>
public sealed class AiSession
{
    public required string SessionId { get; init; }

    /// <summary>Stabile Client-Kennung (z. B. Fingerprint oder Connector-Id).</summary>
    public required string ClientId { get; init; }

    public required AiClientIdentity Identity { get; init; }

    public string ClientName => Identity.Name;
    public string Vendor     => Identity.Vendor;
    public string Model      => Identity.Model;

    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    // ── Aktueller Arbeitskontext (vom letzten Tool-Aufruf aktualisiert) ─────────
    public string? CurrentProject     { get; set; }
    public string? CurrentTask        { get; set; }
    public string? CurrentBranch      { get; set; }
    public string? CurrentPipelineStep { get; set; }

    /// <summary>Beim Connect ausgehandelte Fähigkeiten des Clients.</summary>
    public HashSet<string> Capabilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Strukturiertes Fähigkeitsprofil (erweiterte Capability Negotiation).</summary>
    public AiClientCapabilities? CapabilityProfile { get; set; }

    /// <summary>Rollen/Verantwortlichkeiten dieser Session (z. B. Developer, Reviewer).</summary>
    public HashSet<AiRole> Roles { get; } = new();

    public bool HasRole(AiRole role) => Roles.Contains(role);

    /// <summary>Erteilte Berechtigungen dieser Session (Default: nur Read).</summary>
    public AiPermission GrantedPermissions { get; set; } = AiPermission.Read;

    /// <summary>IDs der aktuell von dieser Session gehaltenen Workspace-Locks.</summary>
    public HashSet<string> ActiveLocks { get; } = new(StringComparer.Ordinal);

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    public bool HasCapability(string capability) => Capabilities.Contains(capability);

    public bool HasPermission(AiPermission permission)
        => permission == AiPermission.None || (GrantedPermissions & permission) == permission;
}
