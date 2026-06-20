using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Strukturiertes Ergebnis der KI-Ideen-Analyse.
/// Wird als JSON von Claude zurückgegeben und direkt deserialisiert.
/// </summary>
public sealed class IdeaAnalysisResult
{
    [JsonPropertyName("RecommendedExtensionType")]
    public string RecommendedExtensionType { get; set; } = "";

    [JsonPropertyName("TargetRuntime")]
    public string TargetRuntime { get; set; } = "";

    [JsonPropertyName("Reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("SuggestedTemplate")]
    public string SuggestedTemplate { get; set; } = "";

    [JsonPropertyName("SuggestedPermissions")]
    public List<string> SuggestedPermissions { get; set; } = [];

    [JsonPropertyName("NextSteps")]
    public List<string> NextSteps { get; set; } = [];

    [JsonPropertyName("RiskLevel")]
    public string RiskLevel { get; set; } = "Green";

    /// <summary>True wenn Claude nicht erreichbar war — ETW muss manuell auswählen.</summary>
    [JsonIgnore]
    public bool IsOfflineFallback { get; set; }

    /// <summary>Konvertiert den API-String in den internen Enum-Wert.</summary>
    [JsonIgnore]
    public NewProjectType? MappedProjectType => RecommendedExtensionType switch
    {
        "ServerModule" => NewProjectType.ServerModule,
        "ClientModule" => NewProjectType.ClientPlugin,
        "HybridModule" => NewProjectType.HybridModule,
        "LanguagePack" => NewProjectType.LanguagePack,
        _              => null
    };

    /// <summary>Anzeigename für den empfohlenen Typ.</summary>
    [JsonIgnore]
    public string DisplayTypeName => RecommendedExtensionType switch
    {
        "ServerModule" => "🖥  Server-Modul (AAIAS)",
        "ClientModule" => "📦  Client-Plugin (AAIAC)",
        "HybridModule" => "🔀  Hybrid-Modul (AAIAS + AAIAC)",
        "LanguagePack" => "🌐  Sprachpaket",
        _              => RecommendedExtensionType
    };

    [JsonIgnore]
    public string RiskEmoji => RiskLevel switch
    {
        "Green"  => "🟢",
        "Yellow" => "🟡",
        "Orange" => "🟠",
        "Red"    => "🔴",
        _        => "⚪"
    };

    [JsonIgnore]
    public string RiskLabel => RiskLevel switch
    {
        "Green"  => "Unkritisch",
        "Yellow" => "Normale Rechte (Netzwerk/Dateien)",
        "Orange" => "Erhöhte Rechte",
        "Red"    => "Sicherheitskritisch",
        _        => RiskLevel
    };

    [JsonIgnore]
    public bool HasSuggestedPermissions => SuggestedPermissions.Count > 0;

    [JsonIgnore]
    public bool HasNextSteps => NextSteps.Count > 0;
}

/// <summary>
/// Analysiert die Idee des ETW mit Claude und gibt eine strukturierte Empfehlung zurück.
/// Fällt auf manuelle Auswahl zurück, wenn Claude nicht verfügbar ist.
/// </summary>
public static class IdeaAnalyzerService
{
    private const string SystemPrompt =
        """
        Du bist der AAIA Module Manager Idea Analyzer.
        Analysiere die Idee des ETW und klassifiziere sie präzise.

        Erlaubte TargetRuntime-Werte:
        - AAIAS  (nur Serverlogik, keine Benutzeroberfläche nötig)
        - AAIAC  (nur Client-UI oder Client-Logik)
        - Hybrid (braucht sowohl Server- als auch Client-Teil)

        Erlaubte RecommendedExtensionType-Werte:
        - ServerModule  → läuft auf AAIAS, implementiert IAaiaModule
        - ClientModule  → erweitert AAIAC, implementiert IAaiacPlugin
        - HybridModule  → Server + Client zusammen
        - LanguagePack  → JSON-Übersetzungen, kein C# nötig

        Erlaubte RiskLevel-Werte: Green, Yellow, Orange, Red
        - Green  = keine besonderen Rechte nötig
        - Yellow = Netzwerk- oder Dateizugriff
        - Orange = erhöhte Rechte (Geräte, Admin)
        - Red    = sicherheitskritisch

        Regeln:
        - Wähle HybridModule nur bei klarer Server+Client-Notwendigkeit
        - Erfinde keine APIs oder Systeme
        - Antworte ausschließlich als valides JSON — kein Markdown, kein Code-Block
        - Verwende deutschsprachige Reason und NextSteps
        - NextSteps: maximal 4 konkrete, knappe Schritte

        Antworte in genau diesem Format ohne weitere Zeichen davor oder danach:
        {
          "RecommendedExtensionType": "...",
          "TargetRuntime": "...",
          "Reason": "...",
          "SuggestedTemplate": "...",
          "SuggestedPermissions": ["...", "..."],
          "NextSteps": ["...", "...", "..."],
          "RiskLevel": "..."
        }
        """;

    public static async Task<IdeaAnalysisResult> AnalyzeAsync(
        IAiProviderService provider,
        string             ideaText,
        CancellationToken  ct = default)
    {
        if (string.IsNullOrWhiteSpace(ideaText))
            return OfflineFallback();

        try
        {
            var messages = new List<ChatMessage>
            {
                new("user", $"Idee des ETW:\n\n{ideaText.Trim()}")
            };

            var aiResponse = await provider.SendAsync(
                new AiRequest(messages, SystemPrompt, MaxTokens: 1024), ct);

            if (!aiResponse.Success)
                return OfflineFallback();

            // Markdown-Code-Fences entfernen falls vorhanden
            var json = aiResponse.Text.Trim();
            if (json.StartsWith("```"))
            {
                var lineEnd = json.IndexOf('\n');
                var codeEnd = json.LastIndexOf("```");
                if (lineEnd > 0 && codeEnd > lineEnd)
                    json = json[(lineEnd + 1)..codeEnd].Trim();
            }

            var opts        = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<IdeaAnalysisResult>(json, opts);
            return parsed ?? OfflineFallback();
        }
        catch
        {
            return OfflineFallback();
        }
    }

    public static IdeaAnalysisResult OfflineFallback() => new()
    {
        IsOfflineFallback        = true,
        RecommendedExtensionType = "",
        TargetRuntime            = "",
        Reason                   = "",
        RiskLevel                = "Green"
    };
}
