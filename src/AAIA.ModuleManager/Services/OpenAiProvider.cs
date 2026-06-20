using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// IAiProviderService-Implementierung für OpenAI (GPT).
/// Nutzt die Chat Completions API v1.
/// </summary>
public sealed class OpenAiProvider : IAiProviderService
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    private readonly string     _apiKey;
    private readonly string     _model;
    private readonly HttpClient _http;

    public string ProviderName => "OpenAI";
    public bool   IsConfigured => true;

    public OpenAiProvider(string apiKey, string model = "gpt-4o-mini")
    {
        _apiKey = apiKey;
        _model  = model;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default)
    {
        try
        {
            // OpenAI: System-Prompt als erstes Message mit role "system"
            var msgs = new List<object>();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
                msgs.Add(new { role = "system", content = request.SystemPrompt });

            foreach (var m in request.Messages)
                msgs.Add(new { role = m.Role, content = m.Content });

            var body = new
            {
                model      = _model,
                messages   = msgs,
                max_tokens = request.MaxTokens
            };

            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(ApiUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return new AiResponse("", false, $"OpenAI Fehler {(int)response.StatusCode}: {ExtractError(err)}");
            }

            var raw    = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);

            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "(Keine Antwort)";

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
            var req  = new AiRequest([new ChatMessage("user", "Say: OK")], "Always respond with exactly: OK", 5);
            var resp = await SendAsync(req, ct);
            return resp.Success;
        }
        catch { return false; }
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? json;
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }
}
