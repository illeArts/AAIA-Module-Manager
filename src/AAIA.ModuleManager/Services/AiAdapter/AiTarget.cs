namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Welche KI soll das Handoff-Paket / die API-Anfrage empfangen.
/// </summary>
public enum AiTarget
{
    /// <summary>ChatGPT / GPT-4o — gut für Planung und Architektur.</summary>
    ChatGPT,

    /// <summary>Anthropic Claude — gut für große Implementierungen.</summary>
    Claude,

    /// <summary>Google Gemini — gut für Reviews und Alternativen.</summary>
    Gemini,

    /// <summary>GitHub Copilot / Codex — gut für konkrete Code-Patches.</summary>
    Codex,

    /// <summary>Lokales Modell (Ollama, LM Studio, …).</summary>
    LocalModel,

    /// <summary>AAIAS-eigener Agent (zukünftig).</summary>
    AaiasAgent
}
