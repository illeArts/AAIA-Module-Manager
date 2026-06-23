using System.Collections.Generic;

namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

/// <summary>
/// Bekannte externe AI-Connectors und ihre Standard-Konfiguration.
/// Jeder Connector startet mit ReadOnly-Rechten.
/// ProposePatch muss explizit aktiviert werden.
/// </summary>
public sealed class AiConnectorDefinition
{
    public string                  Id           { get; init; } = "";
    public string                  DisplayName  { get; init; } = "";
    public string                  Description  { get; init; } = "";
    public AiConnectorPermission   DefaultPerms { get; init; } = AiConnectorPermission.ReadOnly;
    public bool                    IsEnabled    { get; set; }  = false;
    public bool                    AllowPatches { get; set; }  = false;
    public string?                 DocsUrl      { get; init; }

    /// <summary>Endpunkt-URL die der Connector kennen muss um sich zu verbinden.</summary>
    public string ConnectorUrl => $"{AiConnectorProtocol.BaseUrl}{AiConnectorProtocol.ApiPrefix}";
}

/// <summary>
/// Registry aller bekannten Connectors.
/// Phase 6.2.6 — Vorbereitung für externe Integration.
/// </summary>
public static class AiConnectorRegistry
{
    public static IReadOnlyList<AiConnectorDefinition> All { get; } =
    [
        new()
        {
            Id          = AiConnectorProtocol.KnownConnectors.ChatGpt,
            DisplayName = "ChatGPT",
            Description = "OpenAI ChatGPT — Planung und Architektur-Reviews",
            DefaultPerms = AiConnectorPermission.ReadOnly,
            DocsUrl     = "https://platform.openai.com/docs/plugins"
        },
        new()
        {
            Id          = AiConnectorProtocol.KnownConnectors.Claude,
            DisplayName = "Claude",
            Description = "Anthropic Claude — große Implementierungen und Code-Generierung",
            DefaultPerms = AiConnectorPermission.ReadOnly,
            DocsUrl     = "https://docs.anthropic.com/mcp"
        },
        new()
        {
            Id          = AiConnectorProtocol.KnownConnectors.Gemini,
            DisplayName = "Google Gemini",
            Description = "Google Gemini — Code-Reviews und Alternativen-Vorschläge",
            DefaultPerms = AiConnectorPermission.ReadOnly,
            DocsUrl     = "https://ai.google.dev"
        },
        new()
        {
            Id          = AiConnectorProtocol.KnownConnectors.Codex,
            DisplayName = "GitHub Copilot / Codex",
            Description = "GitHub Copilot — konkrete Code-Patches und Autocomplete",
            DefaultPerms = AiConnectorPermission.ReadOnly | AiConnectorPermission.ProposePatch,
            AllowPatches = true,
            DocsUrl     = "https://docs.github.com/en/copilot"
        }
    ];

    public static AiConnectorDefinition? FindById(string id)
    {
        foreach (var c in All)
            if (c.Id == id) return c;
        return null;
    }
}
