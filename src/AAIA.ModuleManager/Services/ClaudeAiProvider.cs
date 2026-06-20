using System;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// IAiProviderService-Implementierung für Anthropic Claude.
/// Wrapping des bestehenden ClaudeApiService für Rückwärtskompatibilität.
/// </summary>
public sealed class ClaudeAiProvider : IAiProviderService
{
    private readonly ClaudeApiService _service;

    public string ProviderName  => "Claude";
    public bool   IsConfigured  => true; // wird nur erzeugt wenn Key gesetzt ist

    public ClaudeAiProvider(string apiKey, string model = "claude-haiku-4-5-20251001")
    {
        _service = new ClaudeApiService(apiKey, model);
    }

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default)
    {
        try
        {
            var text = await _service.SendAsync(request.Messages, request.SystemPrompt, ct);
            return new AiResponse(text, true);
        }
        catch (OperationCanceledException)
        {
            return new AiResponse("", false, "Abgebrochen.");
        }
        catch (Exception ex)
        {
            return new AiResponse("", false, ex.Message);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var req  = new AiRequest([new ChatMessage("user", "Antworte mit: OK")], "Antworte immer mit genau: OK", 10);
            var resp = await SendAsync(req, ct);
            return resp.Success;
        }
        catch { return false; }
    }
}
