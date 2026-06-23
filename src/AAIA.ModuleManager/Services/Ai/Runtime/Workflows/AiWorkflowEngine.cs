using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.Ai.Runtime.Workflows;

/// <summary>
/// Führt Workflows aus — ganze Abläufe über mehrere Phasen. Jede Phase läuft über
/// denselben Runtime-Pfad wie ein einzelner Tool-Call (volle Sicherheits-Kette).
/// Getrennt vom AiTaskManager: Task = eine Aufgabe, Workflow = kompletter Ablauf.
/// </summary>
public sealed class AiWorkflowEngine
{
    private readonly ConcurrentDictionary<string, AiWorkflow> _workflows = new(StringComparer.Ordinal);

    /// <summary>Executor wird vom AiRuntimeService gesetzt (= InvokeToolAsync).</summary>
    public Func<string, string, JsonElement, CancellationToken, Task<(bool Success, string Json)>>? Executor { get; set; }

    public event Action<AiWorkflow>? WorkflowChanged;

    public AiWorkflow Create(string name, string? project, IEnumerable<AiWorkflowPhase> phases)
    {
        var wf = new AiWorkflow { Name = name, Project = project };
        wf.Phases.AddRange(phases);
        _workflows[wf.Id] = wf;
        WorkflowChanged?.Invoke(wf);
        return wf;
    }

    public AiWorkflow? Get(string id) => _workflows.TryGetValue(id, out var w) ? w : null;
    public IReadOnlyList<AiWorkflow> List() => _workflows.Values.OrderBy(w => w.CreatedAt).ToList();

    public async Task<AiWorkflow> RunAsync(string id, AiSession owner, CancellationToken ct = default)
    {
        if (!_workflows.TryGetValue(id, out var wf))
            throw new InvalidOperationException("Workflow nicht gefunden.");
        if (Executor is null)
            throw new InvalidOperationException("Kein Executor gesetzt.");

        wf.OwnerSessionId = owner.SessionId;
        wf.OwnerClientName = owner.ClientName;
        wf.Status = AiWorkflowStatus.Running;
        wf.UpdatedAt = DateTime.UtcNow;
        WorkflowChanged?.Invoke(wf);

        foreach (var phase in wf.Phases)
        {
            ct.ThrowIfCancellationRequested();
            phase.Status = AiWorkflowPhaseStatus.Running;
            WorkflowChanged?.Invoke(wf);

            var (ok, json) = await Executor(owner.SessionId, phase.ToolName, phase.Input, ct).ConfigureAwait(false);
            phase.ResultJson = json;

            if (ok)
            {
                phase.Status = AiWorkflowPhaseStatus.Done;
            }
            else if (phase.ContinueOnError)
            {
                phase.Status = AiWorkflowPhaseStatus.Skipped;
            }
            else
            {
                phase.Status = AiWorkflowPhaseStatus.Failed;
                wf.Status = AiWorkflowStatus.Failed;
                wf.UpdatedAt = DateTime.UtcNow;
                WorkflowChanged?.Invoke(wf);
                return wf;
            }
        }

        wf.Status = AiWorkflowStatus.Completed;
        wf.UpdatedAt = DateTime.UtcNow;
        WorkflowChanged?.Invoke(wf);
        return wf;
    }

    /// <summary>
    /// Standard-Entwicklungsablauf als Vorlage: erstellen → validieren → build → package.
    /// Signatur/Marketplace sind in Phase 7.0 NICHT enthalten (nicht implementiert).
    /// </summary>
    public static IReadOnlyList<AiWorkflowPhase> StandardPipeline(string projectPath)
    {
        JsonElement P() => JsonDocument.Parse($$"""{"projectPath":{{JsonSerializer.Serialize(projectPath)}}}""").RootElement.Clone();
        return new[]
        {
            new AiWorkflowPhase { Name = "Validate", ToolName = "aaia.project.validate", Input = P() },
            new AiWorkflowPhase { Name = "Build",    ToolName = "aaia.project.build",    Input = P() },
            new AiWorkflowPhase { Name = "Package",  ToolName = "aaia.project.package",  Input = P() }
        };
    }
}
