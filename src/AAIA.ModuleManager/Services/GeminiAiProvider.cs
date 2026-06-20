using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// IAiProviderService-Implementierung fuer Google Gemini.
/// Nutzt die generateContent REST API v1beta.
/// </summary>
public sealed class GeminiAiProvider : IAiProviderService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    private readonly string     _apiKey;
    private readonly string     _model;
    private readonly HttpClient _http;

    public string ProviderName => "Gemini";
    public bool   IsConfigured => true;

    public GeminiAiProvider(string apiKey, string model = "gemini-2.0-flash")
    {
        _apiKey = apiKey;
        _model  = model;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken ct = default)
    {
        try
        {
            var url = string.Format(BaseUrl, Uri.EscapeDataString(_model), Uri.EscapeDataString(_apiKey));

            // Gemini: "user"/"model" statt "user"/"assistant"
            var contents = new List<object>();
            foreach (var m in request.Messages)
            {
                var role = m.Role == "assistant" ? "model" : "user";
                contents.Add(new
                {
                    role  = role,
                    parts = new[] { new { text = m.Content } }
                });
            }

            // System-Instruction separat (Gemini 1.5+ feature)
            object body;
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                body = new
                {
                    system_instruction = new { parts = new[] { new { text = request.SystemPrompt } } },
                    contents           = contents,
                    generationConfig   = new { maxOutputTokens = request.MaxTokens }
                };
            }
            else
            {
                body = new
                {
                    contents         = contents,
                    generationConfig = new { maxOutputTokens = request.MaxTokens }
                };
            }

            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return new AiResponse("", false, $"Gemini Fehler {(int)response.StatusCode}: {ExtractError(err)}");
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);

            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
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
