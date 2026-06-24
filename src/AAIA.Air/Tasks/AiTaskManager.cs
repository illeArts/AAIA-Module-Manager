using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.Air.Tasks;

/// <summary>
/// Verwaltet Aufgaben (Tasks) als Ebene über den Tool-Calls. Eine KI kann eine Aufgabe
/// erstellen, übernehmen ("Ich übernehme Aufgabe X") und ihre Schritte ausführen lassen.
/// Die Ausführung läuft über denselben Runtime-Pfad — jeder Schritt durchläuft die volle
/// Sicherheits-Kette (Permission/Lock/Approval/Audit).
/// </summary>
public sealed class AiTaskManager
{
    private readonly ConcurrentDictionary<string, AiTask> _tasks = new(StringComparer.Ordinal);

    /// <summary>
    /// Step-Executor. Wird vom AiRuntimeService gesetzt:
    /// (sessionId, toolName, input, ct) => InvokeToolAsync(...).
    /// Gibt (success, jsonResult) zurück.
    /// </summary>
    public Func<string, string, JsonElement, CancellationToken, Task<(bool Success, string Json)>>? Executor { get; set; }

    public event Action<AiTask>? TaskChanged;

    public AiTask Create(string title, string description = "", string? project = null,
                         IEnumerable<AiTaskStep>? steps = null)
    {
        var task = new AiTask { Title = title, Description = description, Project = project };
        if (steps is not null) task.Steps.AddRange(steps);
        _tasks[task.Id] = task;
        TaskChanged?.Invoke(task);
        return task;
    }

    public AiTask? Get(string id) => _tasks.TryGetValue(id, out var t) ? t : null;

    public IReadOnlyList<AiTask> List(string? project = null)
        => _tasks.Values
            .Where(t => project is null || string.Equals(t.Project, project, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.CreatedAt)
            .ToList();

    /// <summary>Eine Session übernimmt die Aufgabe. Eine bereits übernommene Aufgabe bleibt beim Owner.</summary>
    public bool Claim(string id, AiSession session, out string? conflict)
    {
        conflict = null;
        if (!_tasks.TryGetValue(id, out var task))
        {
            conflict = "Aufgabe nicht gefunden.";
            return false;
        }
        lock (task)
        {
            if (task.OwnerSessionId is not null && task.OwnerSessionId != session.SessionId)
            {
                conflict = $"Bereits übernommen von {task.OwnerClientName} ({task.OwnerSessionId}).";
                return false;
            }
            if (task.Status is not (AiTaskStatus.Pending or AiTaskStatus.Claimed))
            {
                conflict = $"Aufgabe kann im Status {task.Status} nicht übernommen werden.";
                return false;
            }
            task.OwnerSessionId = session.SessionId;
            task.OwnerClientName = session.ClientName;
            if (task.Status == AiTaskStatus.Pending) task.Status = AiTaskStatus.Claimed;
            task.UpdatedAt = DateTime.UtcNow;
        }
        TaskChanged?.Invoke(task);
        return true;
    }

    /// <summary>Gibt einen noch nicht gestarteten Claim frei, z. B. nach Lease-Ablauf.</summary>
    public bool ReleaseClaim(string id, string sessionId)
    {
        if (!_tasks.TryGetValue(id, out var task)) return false;
        lock (task)
        {
            if (task.Status != AiTaskStatus.Claimed || task.OwnerSessionId != sessionId)
                return false;
            task.OwnerSessionId = null;
            task.OwnerClientName = null;
            task.Status = AiTaskStatus.Pending;
            task.UpdatedAt = DateTime.UtcNow;
        }
        TaskChanged?.Invoke(task);
        return true;
    }

    /// <summary>
    /// Führt die Schritte der übernommenen Aufgabe sequenziell aus. Nur der Owner darf
    /// ausführen. Bricht beim ersten fehlgeschlagenen Schritt ab (Status = Failed).
    /// </summary>
    public async Task<AiTask> RunAsync(string id, AiSession owner, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(id, out var task))
            throw new InvalidOperationException("Aufgabe nicht gefunden.");
        if (Executor is null)
            throw new InvalidOperationException("Kein Executor gesetzt.");
        if (task.OwnerSessionId != owner.SessionId)
            throw new InvalidOperationException("Nur der Owner darf die Aufgabe ausführen.");

        lock (task)
        {
            if (task.Status != AiTaskStatus.Claimed)
                throw new InvalidOperationException($"Aufgabe kann im Status {task.Status} nicht gestartet werden.");
            task.Status = AiTaskStatus.InProgress;
            task.UpdatedAt = DateTime.UtcNow;
        }
        TaskChanged?.Invoke(task);

        try
        {
            foreach (var step in task.Steps)
            {
                ct.ThrowIfCancellationRequested();
                step.Status = AiTaskStepStatus.Running;
                TaskChanged?.Invoke(task);

                var (ok, json) = await Executor(owner.SessionId, step.ToolName, step.Input, ct).ConfigureAwait(false);
                step.ResultJson = json;

                if (ok)
                {
                    step.Status = AiTaskStepStatus.Done;
                }
                else
                {
                    step.Status = AiTaskStepStatus.Failed;
                    step.Error = json;
                    task.Status = AiTaskStatus.Failed;
                    task.UpdatedAt = DateTime.UtcNow;
                    TaskChanged?.Invoke(task);
                    return task;
                }
            }
        }
        catch (OperationCanceledException)
        {
            var running = task.Steps.FirstOrDefault(step => step.Status == AiTaskStepStatus.Running);
            if (running is not null) running.Status = AiTaskStepStatus.Skipped;
            task.Status = AiTaskStatus.Cancelled;
            task.UpdatedAt = DateTime.UtcNow;
            TaskChanged?.Invoke(task);
            throw;
        }
        catch (Exception ex)
        {
            var running = task.Steps.FirstOrDefault(step => step.Status == AiTaskStepStatus.Running);
            if (running is not null)
            {
                running.Status = AiTaskStepStatus.Failed;
                running.Error = ex.Message;
            }
            task.Status = AiTaskStatus.Failed;
            task.UpdatedAt = DateTime.UtcNow;
            TaskChanged?.Invoke(task);
            throw;
        }

        task.Status = AiTaskStatus.Completed;
        task.UpdatedAt = DateTime.UtcNow;
        TaskChanged?.Invoke(task);
        return task;
    }

    public int Count => _tasks.Count;

    internal void RestoreDurableTasks(IEnumerable<AiTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (!_tasks.IsEmpty)
            throw new InvalidOperationException("Tasks können nur in einen leeren Manager wiederhergestellt werden.");

        foreach (var task in tasks)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentException.ThrowIfNullOrWhiteSpace(task.Id);
            task.OwnerSessionId = null;
            task.OwnerClientName = null;
            if (!_tasks.TryAdd(task.Id, task))
            {
                _tasks.Clear();
                throw new InvalidOperationException($"Doppelte Task-ID im Snapshot: {task.Id}");
            }
        }
    }

    internal void ClearDurableRestore() => _tasks.Clear();

    internal bool RemoveDurableRetry(string taskId) => _tasks.TryRemove(taskId, out _);

    internal bool FailRecoveryRequired(string taskId, string reason)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return false;
        lock (task)
        {
            if (task.Status != AiTaskStatus.RecoveryRequired) return false;
            var interrupted = task.Steps.FirstOrDefault(step => step.Status == AiTaskStepStatus.Running);
            if (interrupted is not null)
            {
                interrupted.Status = AiTaskStepStatus.Failed;
                interrupted.Error = reason;
            }
            task.Status = AiTaskStatus.Failed;
            task.UpdatedAt = DateTime.UtcNow;
        }
        TaskChanged?.Invoke(task);
        return true;
    }

    internal AiTask CreateRecoveryRetry(string taskId)
    {
        var source = Get(taskId) ?? throw new InvalidOperationException("Aufgabe nicht gefunden.");
        lock (source)
        {
            if (source.Status != AiTaskStatus.RecoveryRequired)
                throw new InvalidOperationException("Nur RecoveryRequired-Aufgaben können erneut erzeugt werden.");
            var retry = Create(
                source.Title,
                source.Description,
                source.Project,
                source.Steps.Select(step => new AiTaskStep
                {
                    ToolName = step.ToolName,
                    Input = step.Input.Clone(),
                    Status = AiTaskStepStatus.Pending
                }));
            return retry;
        }
    }
}
