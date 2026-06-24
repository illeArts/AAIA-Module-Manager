using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AAIA.Air.Contracts;

public enum AiTaskStatus
{
    Pending,     // erstellt, niemand übernommen
    Claimed,     // eine KI hat übernommen
    InProgress,  // läuft
    RecoveryRequired,
    Completed,
    Failed,
    Cancelled
}

public enum AiTaskStepStatus { Pending, Running, Done, Failed, Skipped }

/// <summary>Ein einzelner Schritt einer Aufgabe = ein Tool-Aufruf.</summary>
public sealed class AiTaskStep
{
    public required string ToolName { get; init; }
    public JsonElement Input { get; init; }
    public AiTaskStepStatus Status { get; set; } = AiTaskStepStatus.Pending;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Eine Aufgabe als eigene Ebene ÜBER den Tool-Calls. "FRITZ!Box-Modul erstellen"
/// wird intern zu einer Folge von Tool-Schritten. Eine KI kann eine Aufgabe übernehmen.
/// </summary>
public sealed class AiTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string? Project { get; init; }

    public AiTaskStatus Status { get; set; } = AiTaskStatus.Pending;

    /// <summary>Session, die die Aufgabe übernommen hat (Owner).</summary>
    public string? OwnerSessionId { get; set; }
    public string? OwnerClientName { get; set; }

    public List<AiTaskStep> Steps { get; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
