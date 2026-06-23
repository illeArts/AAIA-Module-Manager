using System;
using System.Collections.Generic;

namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

// ── Permission-Enum ───────────────────────────────────────────────────────────

/// <summary>
/// Feingranulare Rechte die einem Connector erteilt werden können.
/// Ein Connector bekommt NIEMALS mehr als das Minimum das für seine Aufgabe nötig ist.
/// </summary>
[Flags]
public enum AiConnectorPermission
{
    None                    = 0,

    // Lese-Rechte
    ReadProjectSummary      = 1 << 0,
    ReadManifest            = 1 << 1,
    ReadBuildErrors         = 1 << 2,
    ReadValidationReport    = 1 << 3,
    ReadSelectedFiles       = 1 << 4,  // nur explizit freigegebene Dateien
    ReadPipelineState       = 1 << 5,
    ReadHandoffPackage      = 1 << 6,

    // Schreib-Rechte (erfordern User-Approval)
    ProposePatch            = 1 << 7,

    // Verbotene Rechte — können NIEMALS erteilt werden (nur zur Dokumentation)
    // ApplyPatchWithoutApproval = niemals
    // TriggerEtwSignature       = niemals
    // ReadPrivateKey             = niemals
    // UploadToMarketplace        = niemals

    /// <summary>Standard-Read-Only-Set für alle bekannten Connectors.</summary>
    ReadOnly = ReadProjectSummary | ReadManifest | ReadBuildErrors
             | ReadValidationReport | ReadPipelineState | ReadHandoffPackage,

    /// <summary>Vollständiger erlaubter Satz (Read + ProposePatch).</summary>
    Full = ReadOnly | ReadSelectedFiles | ProposePatch
}

// ── Connector-Session ─────────────────────────────────────────────────────────

/// <summary>
/// Repräsentiert eine aktive Verbindung eines externen AI-Connectors.
/// Wird beim ersten Request erstellt und hält Rechte + Identität.
/// </summary>
public sealed class AiConnectorSession
{
    public string                  SessionId   { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string                  ConnectorId { get; init; } = AiConnectorProtocol.KnownConnectors.Unknown;
    public string                  DisplayName { get; init; } = "Unbekannter Connector";
    public AiConnectorPermission   Permissions { get; init; } = AiConnectorPermission.ReadOnly;
    public DateTime                ConnectedAt { get; init; } = DateTime.UtcNow;
    public int                     RequestCount { get; set; } = 0;

    public bool HasPermission(AiConnectorPermission required)
        => (Permissions & required) == required;
}

// ── Permission-Checker ────────────────────────────────────────────────────────

/// <summary>
/// Prüft ob ein eingehender Request von einem Connector erlaubt ist.
/// </summary>
public static class AiConnectorPermissionChecker
{
    /// <summary>
    /// Gibt die erforderliche Permission für einen Endpunkt zurück.
    /// </summary>
    public static AiConnectorPermission RequiredFor(string path, string method)
    {
        if (method == "GET")
        {
            if (path.StartsWith(AiConnectorProtocol.Endpoints.ContextCurrent,
                    StringComparison.OrdinalIgnoreCase))
                return AiConnectorPermission.ReadProjectSummary;

            if (path.StartsWith(AiConnectorProtocol.Endpoints.ContextProject,
                    StringComparison.OrdinalIgnoreCase))
                return AiConnectorPermission.ReadProjectSummary
                     | AiConnectorPermission.ReadPipelineState;

            if (path.StartsWith(AiConnectorProtocol.Endpoints.HandoffLatest,
                    StringComparison.OrdinalIgnoreCase))
                return AiConnectorPermission.ReadHandoffPackage;

            if (path.StartsWith(AiConnectorProtocol.Endpoints.Capabilities,
                    StringComparison.OrdinalIgnoreCase))
                return AiConnectorPermission.None; // Capabilities immer offen

            if (path.StartsWith(AiConnectorProtocol.Endpoints.PatchStatus,
                    StringComparison.OrdinalIgnoreCase))
                return AiConnectorPermission.ProposePatch;
        }

        if (method == "POST" &&
            path.StartsWith(AiConnectorProtocol.Endpoints.PatchPropose,
                StringComparison.OrdinalIgnoreCase))
            return AiConnectorPermission.ProposePatch;

        // Unbekannte Endpunkte → maximale Restriktion
        return AiConnectorPermission.Full;
    }

    /// <summary>Liest Connector-ID und -Name aus den Request-Headern.</summary>
    public static (string id, string name) ReadConnectorHeaders(
        System.Collections.Specialized.NameValueCollection headers)
    {
        var id   = headers[AiConnectorProtocol.HeaderConnectorId]
                   ?? AiConnectorProtocol.KnownConnectors.Unknown;
        var name = headers[AiConnectorProtocol.HeaderConnectorName]
                   ?? "Unbekannter Connector";
        return (id.ToLowerInvariant(), name);
    }

    /// <summary>
    /// Erstellt eine Standard-Session für einen eingehenden Connector.
    /// Alle bekannten Connectors bekommen ReadOnly; ProposePatch muss explizit aktiviert sein.
    /// </summary>
    public static AiConnectorSession CreateSession(string connectorId, string displayName,
        bool allowPatchProposal = false)
    {
        var perms = AiConnectorPermission.ReadOnly;
        if (allowPatchProposal)
            perms |= AiConnectorPermission.ProposePatch;

        return new AiConnectorSession
        {
            ConnectorId = connectorId,
            DisplayName = displayName,
            Permissions = perms
        };
    }
}
