using AAIA.ModuleManager.Services.AiAdapter;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Baut den aktiven IAiProviderService aus der AppConfig.
/// Gibt null zurueck wenn kein API-Key konfiguriert ist.
///
/// Phase 6.0: Zusätzlich CreateAdapter() für den zentralen AI Adapter.
/// Der direkte Provider-Weg bleibt für Rückwärtskompatibilität (AiPanelViewModel).
/// </summary>
public static class AiServiceFactory
{
    // ── Legacy: direkter Provider (für AiPanelViewModel / Chat-Panel) ─────────

    public static IAiProviderService? Create(AppConfig config)
    {
        return config.AiProvider switch
        {
            "OpenAI" when !string.IsNullOrWhiteSpace(config.OpenAiApiKey)
                => new OpenAiProvider(config.OpenAiApiKey, config.OpenAiModel),

            "Gemini" when !string.IsNullOrWhiteSpace(config.GeminiApiKey)
                => new GeminiAiProvider(config.GeminiApiKey, config.GeminiModel),

            // "Claude" oder unbekannter Wert → Fallback auf Claude
            _ when !string.IsNullOrWhiteSpace(config.ClaudeApiKey)
                => new ClaudeAiProvider(config.ClaudeApiKey, config.ClaudeModel),

            _ => null
        };
    }

    /// <summary>Zeigt den konfigurierten Anbieter-Namen ohne Instanz zu bauen.</summary>
    public static string ActiveProviderName(AppConfig config) => config.AiProvider switch
    {
        "OpenAI"  => "OpenAI",
        "Gemini"  => "Gemini",
        _         => "Claude"
    };

    // ── Phase 6.0: Zentraler AI Adapter ──────────────────────────────────────

    /// <summary>
    /// Erzeugt den zentralen IAiAdapterService.
    /// Nutzt AiAdapterSettings aus der AppConfig.
    /// Niemals null — ManualHandoff funktioniert immer ohne Keys.
    /// </summary>
    public static IAiAdapterService CreateAdapter(AppConfig config,
        AaiasConnectionService? aaias = null)
        => new AiAdapterService(config, config.AiAdapter, aaias);
}
