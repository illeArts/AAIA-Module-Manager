using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AAIA.Air.Workflows;

public enum AiWorkflowStatus { Pending, Running, Completed, Failed, Cancelled }
public enum AiWorkflowPhaseStatus { Pending, Running, Done, Failed, Skipped }

/// <summary>
/// Eine Phase eines Workflows = ein benannter Schritt, der ein Tool aufruft.
/// </summary>
public sealed class AiWorkflowPhase
{
    public required string Name { get; init; }
    public required string ToolName { get; init; }
    public JsonElement Input { get; init; }
    /// <summary>Wenn true, stoppt ein Fehler den Workflow nicht (z. B. optionaler Fix-Schritt).</summary>
    public bool ContinueOnError { get; init; }
    public AiWorkflowPhaseStatus Status { get; set; } = AiWorkflowPhaseStatus.Pending;
    public string? ResultJson { get; set; }
}

/// <summary>
/// Ein Workflow ist ein kompletter Ablauf ÜBER mehreren Tasks/Phasen — im Gegensatz zu
/// einer einzelnen Aufgabe. Beispiel: Projekt erstellen → Build → Fix → Tests → Package →
/// Signatur → Marketplace. So kann eine KI ganze Abläufe übernehmen.
/// </summary>
public sealed class AiWorkflow
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string Name { get; init; }
    public string? Project { get; init; }
    public AiWorkflowStatus Status { get; set; } = AiWorkflowStatus.Pending;
    public string? OwnerSessionId { get; set; }
    public string? OwnerClientName { get; set; }
    public List<AiWorkflowPhase> Phases { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
