namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Wie der Adapter mit der Ziel-KI kommuniziert.
/// </summary>
public enum AiExecutionMode
{
    /// <summary>
    /// Prompt wird erzeugt, in die Zwischenablage kopiert und vom Nutzer manuell eingefügt.
    /// Kein API-Key erforderlich. Funktioniert immer.
    /// </summary>
    ManualHandoff,

    /// <summary>
    /// Direkter API-Aufruf über konfigurierten API-Key.
    /// Schnellster Weg — erfordert gültigen Key im Einstellungsdialog.
    /// </summary>
    ApiDirect,

    /// <summary>
    /// Kommunikation über Plugin/MCP/Connector-Protokoll (Phase 6.2).
    /// Noch nicht implementiert — wird automatisch auf ManualHandoff zurückfallen.
    /// </summary>
    ConnectorBridge,

    /// <summary>
    /// Lokales Modell via HTTP (z. B. Ollama auf localhost:11434).
    /// </summary>
    LocalModel
}
