using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Ruft die Anthropic Claude Messages API auf.
/// Unterstützt einfache Request/Response-Kommunikation (kein Streaming).
/// </summary>
public class ClaudeApiService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _http;

    public ClaudeApiService(string apiKey, string model = "claude-haiku-4-5-20251001")
    {
        _apiKey = apiKey;
        _model  = model;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// Sendet eine Konversation und gibt die Antwort zurück.
    /// </summary>
    public async Task<string> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        string                     systemPrompt,
        CancellationToken          ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Kein Claude API Key konfiguriert. Bitte unter Einstellungen → KI-Assistent eintragen.");

        var body = new
        {
            model      = _model,
            max_tokens = 2048,
            system     = systemPrompt,
            messages   = BuildMessages(messages)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic API Fehler {(int)response.StatusCode}: {ExtractError(errorBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(
            cancellationToken: ct);

        return result?.Content?[0]?.Text ?? "(Keine Antwort)";
    }

    private static object[] BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var list = new List<object>(messages.Count);
        foreach (var m in messages)
            list.Add(new { role = m.Role, content = m.Content });
        return list.ToArray();
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

    private sealed class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock>? Content { get; init; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("text")] public string? Text { get; init; }
    }
}

/// <summary>
/// Baut den Kontext-System-Prompt für den KI-Assistenten.
/// </summary>
public static class AiContextBuilder
{
    public static string Build(string? currentProjectName = null, string? currentProjectType = null, string? lastError = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Du bist ein erfahrener AAIA SDK Entwicklungsassistent.");
        sb.AppendLine("Du hilfst ETW-Entwicklern (Extension Trusted Workers) dabei,");
        sb.AppendLine("AAIA-Module, Client-Plugins und Sprachpakete zu entwickeln.");
        sb.AppendLine();
        sb.AppendLine("## AAIA-Architektur-Überblick");
        sb.AppendLine("- **AAIAS** = AAIA Server — hostet Server-Module (IAaiaModule, ASP.NET Core)");
        sb.AppendLine("- **AAIAC** = AAIA Client — hostet Client-Plugins (IAaiacPlugin)");
        sb.AppendLine("- **AAIA SDK** = NuGet-Paket `AAIA.Shared.Contracts` — DTOs, Interfaces, Enums");
        sb.AppendLine("- **Server-Modul**: IAaiaModule → AddServices() + MapRoutes(), plattformneutral");
        sb.AppendLine("- **Client-Plugin**: IAaiacPlugin → Initialise/Activate/Stop Lifecycle");
        sb.AppendLine("- **Manifest**: aaia-manifest.json mit Id, Kind, Host, Permissions, Routes");
        sb.AppendLine();
        sb.AppendLine("## Wichtige Regeln");
        sb.AppendLine("- ServerModule MUSS supportedPlatforms=[\"all\"] haben");
        sb.AppendLine("- Orange/Red Permissions brauchen RequiresDuki=true");
        sb.AppendLine("- Routen müssen mit /api/ beginnen");
        sb.AppendLine("- NuGet-Paket-ID Pflicht für kostenpflichtige Module");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(currentProjectName))
        {
            sb.AppendLine($"## Aktuelles Projekt");
            sb.AppendLine($"- Name: {currentProjectName}");
            if (!string.IsNullOrWhiteSpace(currentProjectType))
                sb.AppendLine($"- Typ: {currentProjectType}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            sb.AppendLine("## Zuletzt aufgetretener Fehler");
            sb.AppendLine("```");
            sb.AppendLine(lastError);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("Antworte auf Deutsch, präzise und mit konkreten Code-Beispielen.");

        return sb.ToString();
    }
}
