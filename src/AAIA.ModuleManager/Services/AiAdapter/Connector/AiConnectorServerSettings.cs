namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

/// <summary>
/// Persistierte Einstellungen für den lokalen AI Connector-Server.
/// Wird als Teil von AppConfig serialisiert.
/// </summary>
public sealed class AiConnectorServerSettings
{
    /// <summary>True = Server läuft automatisch beim Start des Module Managers.</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// True = Connectors dürfen Patch-Vorschläge einreichen.
    /// Standardmäßig aus — muss bewusst aktiviert werden.
    /// </summary>
    public bool AllowPatchProposals { get; set; } = false;

    /// <summary>
    /// Port des lokalen Servers.
    /// Änderungen erfordern Neustart des Servers.
    /// </summary>
    public int Port { get; set; } = AiConnectorProtocol.Port;

    /// <summary>
    /// Pfad zum Projekt-Root — wird für Patch-Anwendung benötigt.
    /// Wird vom ViewModel automatisch gesetzt wenn ein Projekt geladen ist.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Maximale Anzahl gleichzeitiger Proposals die in der Warteschlange warten dürfen.
    /// Schützt vor Flood-Angriffen.
    /// </summary>
    public int MaxPendingProposals { get; set; } = 5;

    /// <summary>
    /// Timeout in Sekunden nach dem ein Proposal automatisch abgelehnt wird.
    /// 0 = kein Timeout.
    /// </summary>
    public int ProposalTimeoutSeconds { get; set; } = 300; // 5 Minuten
}
