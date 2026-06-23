using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AAIA.Connector.Client;

// ── Capabilities ──────────────────────────────────────────────────────────────

public sealed class ConnectorCapabilities
{
    [JsonPropertyName("protocolVersion")] public string   ProtocolVersion { get; set; } = "";
    [JsonPropertyName("serverVersion")]   public string   ServerVersion   { get; set; } = "";
    [JsonPropertyName("connectorId")]     public string   ConnectorId     { get; set; } = "";
    [JsonPropertyName("permissions")]     public string   Permissions     { get; set; } = "";
    [JsonPropertyName("securityNote")]    public string   SecurityNote    { get; set; } = "";
}

// ── Context ───────────────────────────────────────────────────────────────────

public sealed class ProjectContextSummary
{
    [JsonPropertyName("extensionId")]  public string ExtensionId  { get; set; } = "";
    [JsonPropertyName("displayName")]  public string DisplayName  { get; set; } = "";
    [JsonPropertyName("currentStep")]  public string CurrentStep  { get; set; } = "";
    [JsonPropertyName("nextStep")]     public string? NextStep    { get; set; }
    [JsonPropertyName("trustLevel")]   public string TrustLevel   { get; set; } = "";
    [JsonPropertyName("hasErrors")]    public bool   HasErrors    { get; set; }
    [JsonPropertyName("errorCount")]   public int    ErrorCount   { get; set; }
}

// ── Patch ─────────────────────────────────────────────────────────────────────

/// <summary>Einzelner Patch-Vorschlag der an den AAIA Module Manager gesendet wird.</summary>
public sealed class PatchItem
{
    /// <summary>Art des Patches: FullFileReplacement | UnifiedDiff | CodeSnippet</summary>
    [JsonPropertyName("kind")]        public string  Kind       { get; set; } = "FullFileReplacement";
    /// <summary>Zieldatei relativ zum Projekt-Root (keine Path-Traversal, keine absoluten Pfade).</summary>
    [JsonPropertyName("targetFile")]  public string? TargetFile { get; set; }
    /// <summary>Neuer vollständiger Dateiinhalt (bei FullFileReplacement).</summary>
    [JsonPropertyName("content")]     public string  Content    { get; set; } = "";
    /// <summary>Programmiersprache (csharp, axaml, json, …).</summary>
    [JsonPropertyName("language")]    public string  Language   { get; set; } = "";
    /// <summary>Kurze Erklärung was dieser Patch ändert.</summary>
    [JsonPropertyName("description")] public string? Description { get; set; }
}

/// <summary>Vollständiger Patch-Request der an POST /aaia/v1/patch/propose gesendet wird.</summary>
public sealed class PatchRequest
{
    /// <summary>Muss "aaia-connector-v1" sein.</summary>
    [JsonPropertyName("protocolVersion")] public string         ProtocolVersion { get; set; } = "aaia-connector-v1";
    /// <summary>Optionale Erklärung der KI warum diese Patches nötig sind.</summary>
    [JsonPropertyName("rationale")]       public string?        Rationale       { get; set; }
    [JsonPropertyName("patches")]         public List<PatchItem> Patches        { get; set; } = [];
}

/// <summary>Antwort des Servers auf einen Patch-Vorschlag (HTTP 202).</summary>
public sealed class PatchResponse
{
    [JsonPropertyName("proposalId")] public string ProposalId { get; set; } = "";
    [JsonPropertyName("status")]     public string Status     { get; set; } = "";
    [JsonPropertyName("message")]    public string Message    { get; set; } = "";
}

/// <summary>Antwort auf GET /aaia/v1/patch/{id}/status.</summary>
public sealed class PatchStatusResponse
{
    [JsonPropertyName("proposalId")]   public string ProposalId   { get; set; } = "";
    [JsonPropertyName("status")]       public string Status       { get; set; } = "";
    [JsonPropertyName("approvedCount")] public int  ApprovedCount { get; set; }
    [JsonPropertyName("rejectedCount")] public int  RejectedCount { get; set; }
    [JsonPropertyName("pendingCount")]  public int  PendingCount  { get; set; }
}

/// <summary>Generische Fehlerantwort des Servers.</summary>
public sealed class ErrorResponse
{
    [JsonPropertyName("error")]   public string        Error   { get; set; } = "";
    [JsonPropertyName("details")] public List<string>? Details { get; set; }
}
