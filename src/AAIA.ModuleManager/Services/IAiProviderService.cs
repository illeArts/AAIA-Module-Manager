using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Eine KI-Anfrage mit Verlauf und System-Prompt.
/// Provider-unabhängig — jeder KI-Anbieter übersetzt das in sein eigenes Format.
/// </summary>
public sealed record AiRequest(
    IReadOnlyList<ChatMessage> Messages,
    string SystemPrompt,
    int MaxTokens = 2048
);

/// <summary>
/// Antwort eines KI-Anbieters.
/// </summary>
public sealed record AiResponse(
    string Text,
    bool   Success,
    string? Error = null
);

/// <summary>
/// Abstraktion über KI-Anbieter: Claude, OpenAI, Gemini — und später AAIAS-eigene KI.
/// </summary>
public interface IAiProviderService
{
    /// <summary>Anzeigename, z. B. "Claude", "OpenAI", "Gemini".</summary>
    string ProviderName { get; }

    /// <summary>True wenn API-Key vorhanden und konfiguriert.</summary>
    bool IsConfigured { get; }

    /// <summary>Sendet eine Anfrage und gibt die Antwort zurück.</summary>
    Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Einfacher Verbindungstest — gibt true zurück wenn der Anbieter antwortet.</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
