namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Adapter-spezifische Einstellungen pro KI-Target.
/// Wird als Teil von AppConfig serialisiert.
/// </summary>
public sealed class AiAdapterSettings
{
    /// <summary>Standard-Ziel wenn der Nutzer nichts auswählt.</summary>
    public AiTarget DefaultTarget { get; set; } = AiTarget.Claude;

    /// <summary>
    /// Bevorzugter Modus pro Target.
    /// Wenn kein Key vorhanden → automatisch ManualHandoff.
    /// </summary>
    public AiExecutionMode ChatGptMode   { get; set; } = AiExecutionMode.ManualHandoff;
    public AiExecutionMode ClaudeMode    { get; set; } = AiExecutionMode.ManualHandoff;
    public AiExecutionMode GeminiMode    { get; set; } = AiExecutionMode.ManualHandoff;
    public AiExecutionMode CodexMode     { get; set; } = AiExecutionMode.ManualHandoff;
    public AiExecutionMode LocalMode     { get; set; } = AiExecutionMode.LocalModel;

    /// <summary>URL für lokale Modelle (Ollama-kompatibel).</summary>
    public string LocalModelUrl   { get; set; } = "http://localhost:11434";
    /// <summary>Modellname für lokales Modell.</summary>
    public string LocalModelName  { get; set; } = "llama3";

    /// <summary>
    /// Gibt den konfigurierten Modus für ein Target zurück.
    /// Gibt ManualHandoff zurück für unbekannte Targets (safe default).
    /// </summary>
    public AiExecutionMode ModeFor(AiTarget target) => target switch
    {
        AiTarget.ChatGPT    => ChatGptMode,
        AiTarget.Claude     => ClaudeMode,
        AiTarget.Gemini     => GeminiMode,
        AiTarget.Codex      => CodexMode,
        AiTarget.LocalModel => LocalMode,
        AiTarget.AaiasAgent => AiExecutionMode.ConnectorBridge,
        _                   => AiExecutionMode.ManualHandoff
    };
}
