using AAIA.Air.Contracts;

namespace AAIA.ModuleManager.Services.Ai.Integration;

/// <summary>Bindet die AIR-Wartungsautorisierung an die lokale Owner/Admin-Rolle.</summary>
public sealed class LocalStateMaintenanceAuthorizer(Func<bool> isOwnerOrAdministrator)
    : IAiStateMaintenanceAuthorizer, IAiRecoveryAuthorizer
{
    public bool IsAuthorized(string actorId, string action, bool confirmed, out string? denialReason)
    {
        if (!isOwnerOrAdministrator())
        {
            denialReason = "Lokale Owner-/Administratorrechte erforderlich.";
            return false;
        }
        if (!confirmed)
        {
            denialReason = "Wartungsaktion wurde nicht bestätigt.";
            return false;
        }
        denialReason = null;
        return true;
    }

    public bool IsAuthorized(string actorId, string action, out string? denialReason)
    {
        if (!isOwnerOrAdministrator())
        {
            denialReason = "Lokale Owner-/Administratorrechte erforderlich.";
            return false;
        }
        denialReason = null;
        return true;
    }
}
