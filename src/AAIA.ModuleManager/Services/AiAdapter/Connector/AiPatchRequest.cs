using System.Collections.Generic;
using System.Text.Json.Serialization;
using AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

// ── Eingehende Patch-Anfrage vom Connector ────────────────────────────────────

/// <summary>
/// JSON-Body für POST /aaia/v1/patch/propose.
/// Externe KI sendet einen oder mehrere Patch-Vorschläge.
/// </summary>
public sealed class AiPatchRequest
{
    /// <summary>Protokoll-Version — muss "aaia-connector-v1" sein.</summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "";

    /// <summary>Optionale Erklärung der KI warum dieser Patch nötig ist.</summary>
    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    /// <summary>Liste der vorgeschlagenen Patches.</summary>
    [JsonPropertyName("patches")]
    public List<AiPatchItem> Patches { get; set; } = [];
}

/// <summary>
/// Ein einzelner Patch-Vorschlag innerhalb einer AiPatchRequest.
/// </summary>
public sealed class AiPatchItem
{
    /// <summary>Art des Patches.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "FullFileReplacement";

    /// <summary>Zieldatei relativ zum Projekt-Root.</summary>
    [JsonPropertyName("targetFile")]
    public string? TargetFile { get; set; }

    /// <summary>Neuer vollständiger Dateiinhalt (bei FullFileReplacement).</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>Programmiersprache (cs, axaml, json, …).</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>Kurze Beschreibung was dieser Patch ändert.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Konvertiert zu PatchProposal für den internen Approval-Flow.</summary>
    public PatchProposal ToPatchProposal() => new()
    {
        Kind          = ParseKind(Kind),
        Content       = Content,
        SuggestedFile = TargetFile,
        Language      = Language,
        LineCount     = Content.Split('\n').Length,
        RawBlock      = $"```{Language}\n{Content}\n```"
    };

    private static PatchKind ParseKind(string kind) => kind switch
    {
        "UnifiedDiff"        => PatchKind.UnifiedDiff,
        "FullFileReplacement" => PatchKind.FullFileReplacement,
        _                    => PatchKind.CodeSnippet
    };
}

// ── Server-Antworten ──────────────────────────────────────────────────────────

/// <summary>
/// Antwort auf POST /aaia/v1/patch/propose (HTTP 202 Accepted).
/// </summary>
public sealed class AiPatchResponse
{
    [JsonPropertyName("proposalId")]
    public string ProposalId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";  // pending | approved | rejected

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// Antwort auf GET /aaia/v1/patch/{id}/status.
/// </summary>
public sealed class AiPatchStatusResponse
{
    [JsonPropertyName("proposalId")]
    public string ProposalId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("approvedCount")]
    public int ApprovedCount { get; set; }

    [JsonPropertyName("rejectedCount")]
    public int RejectedCount { get; set; }

    [JsonPropertyName("pendingCount")]
    public int PendingCount { get; set; }
}
