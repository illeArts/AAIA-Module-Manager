using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// KI-Provider der Anfragen über die lokale AAIAS-Instanz weiterleitet.
///
/// AAIAS-Endpunkt (erwartet):
///   POST /api/ai/chat
///   Body: { "prompt": "...", "systemPrompt": "...", "maxTokens": 2048 }
///   Response: { "text": "...", "model": "...", "finishReason": "..." }
///
/// Fallback: Gibt einen klaren Fehler zurück wenn AAIAS nicht verbunden oder
/// der Endpunkt nicht existiert. Der Aufrufer (AiAdapterService) behandelt
/// das als Fallback auf ManualHandoff.
///
/// Sicherheit: API-Keys werden NIEMALS an AAIAS übertragen.
/// AAIAS nutzt seine eigenen konfigurierten Modelle.
/// </summary>
public sealed class AaiasAiProvider : IAiProviderService
{
    private readonly AaiasConnectionService _aaias;

    public string ProviderName  => "AAIAS-Agent";
    public bool   IsConfigured  => _aaias.IsConnected;

    public AaiasAiProvider(AaiasConnectionService aaias)
    {
        _aaias = aaias;
    }

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default)
    {
        if (!_aaias.IsConnected)
            return new AiResponse("", false, "AAIAS nicht verbunden. Verbinde zuerst im Tester-Tab.");

        // Nachrichten zu einem einzelnen Prompt zusammenführen (AAIAS erwartet kein Chat-Format)
        var promptBuilder = new StringBuilder();
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "user")
                promptBuilder.AppendLine(msg.Content);
        }
        var prompt = promptBuilder.ToString().Trim();

        try
        {
            var result = await _aaias.SendAiChatAsync(
                prompt, request.SystemPrompt, request.MaxTokens, ct);

            if (result.Error is not null)
                return new AiResponse("", false, result.Error);

            return new AiResponse(result.Text ?? "", true);
        }
        catch (Exception ex)
        {
            return new AiResponse("", false, $"AAIAS-KI-Fehler: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!_aaias.IsConnected) return false;
        try
        {
            var result = await _aaias.SendAiChatAsync("ping", "", 10, ct);
            return result.Error is null;
        }
        catch { return false; }
    }
}
