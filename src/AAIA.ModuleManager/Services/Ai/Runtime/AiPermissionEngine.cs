using System;
using System.Collections.Concurrent;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Permissions sind nicht global, sondern pro Client (und optional pro Projekt).
/// Default für jeden neuen Client: nur Read. Alles andere muss explizit erteilt werden.
/// </summary>
public sealed class AiPermissionEngine
{
    // Schlüssel: clientId  bzw.  clientId|projectPath
    private readonly ConcurrentDictionary<string, AiPermission> _grants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default-Permissions, die neue Sessions erhalten (UI-Toggles steuern dies).</summary>
    public AiPermission DefaultPermissions { get; set; } = AiPermission.Read;

    private static string KeyOf(string clientId, string? projectPath)
        => string.IsNullOrEmpty(projectPath) ? clientId : $"{clientId}|{projectPath}";

    /// <summary>Erteilt einem Client Berechtigungen (optional projektspezifisch).</summary>
    public void Grant(string clientId, AiPermission permissions, string? projectPath = null)
        => _grants.AddOrUpdate(KeyOf(clientId, projectPath), permissions, (_, cur) => cur | permissions);

    /// <summary>Entzieht Berechtigungen.</summary>
    public void Revoke(string clientId, AiPermission permissions, string? projectPath = null)
        => _grants.AddOrUpdate(KeyOf(clientId, projectPath), DefaultPermissions & ~permissions,
                               (_, cur) => cur & ~permissions);

    /// <summary>Effektive Permissions: Default ∪ Client-Grant ∪ projektspezifischer Grant.</summary>
    public AiPermission Effective(string clientId, string? projectPath = null)
    {
        var result = DefaultPermissions;
        if (_grants.TryGetValue(clientId, out var clientGrant)) result |= clientGrant;
        if (!string.IsNullOrEmpty(projectPath) &&
            _grants.TryGetValue(KeyOf(clientId, projectPath), out var projGrant)) result |= projGrant;
        return result;
    }

    /// <summary>Prüft, ob die Session ein Tool ausführen darf. Sign/Marketplace sind in 7.0 gesperrt.</summary>
    public bool IsAllowed(AiSession session, AiToolDefinition tool, string? projectPath, out string reason)
    {
        // In Phase 7.0 nicht implementierte Stufen hart sperren
        if ((tool.RequiredPermissions & (AiPermission.Sign | AiPermission.Marketplace)) != 0)
        {
            reason = "Signatur/Marketplace ist in Phase 7.0 nicht über die AI Runtime verfügbar.";
            return false;
        }

        var effective = Effective(session.ClientId, projectPath) | session.GrantedPermissions;
        if ((effective & tool.RequiredPermissions) != tool.RequiredPermissions)
        {
            reason = $"Client '{session.ClientName}' hat keine Berechtigung für '{tool.Name}' " +
                     $"(benötigt {tool.RequiredPermissions}).";
            return false;
        }

        reason = "";
        return true;
    }
}
