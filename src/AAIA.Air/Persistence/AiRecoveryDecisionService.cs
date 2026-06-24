using AAIA.Air.Messaging;

namespace AAIA.Air.Persistence;

/// <summary>Lokale, autorisierte Entscheidungen für unsichere Crash-Recovery-Zustände.</summary>
public sealed class AiRecoveryDecisionService
{
    private readonly AiRuntimeService _runtime;
    private readonly IAiRecoveryAuthorizer? _authorizer;
    private readonly object _gate = new();

    public AiRecoveryDecisionService(AiRuntimeService runtime, IAiRecoveryAuthorizer? authorizer)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _authorizer = authorizer;
    }

    public bool ResolveAsFailed(string executionId, string actorId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Authorize(actorId, "fail");
        if (reason.Length > 4_000 || AiMessageSafetyPolicy.ContainsSensitiveContent(reason))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                "Recovery-Begründung enthält sensible Daten.");
        lock (_gate)
        {
            var execution = _runtime.Scheduler.Get(executionId);
            if (execution is null || execution.State != AiExecutionState.RecoveryRequired) return false;
            if (_runtime.Tasks.Get(execution.Request.TaskId)?.Status != AiTaskStatus.RecoveryRequired) return false;
            if (!_runtime.Tasks.FailRecoveryRequired(execution.Request.TaskId, reason)) return false;
            if (!_runtime.Scheduler.FailRecoveryRequired(executionId, reason)) return false;
            Publish(executionId, execution.Request.TaskId, "failed", actorId: actorId);
            return true;
        }
    }

    public AiExecutionSnapshot CreateRetry(string executionId, string actorId)
    {
        Authorize(actorId, "retry");
        lock (_gate)
        {
            var source = _runtime.Scheduler.Get(executionId)
                ?? throw new InvalidOperationException("Execution nicht gefunden.");
            if (source.State != AiExecutionState.RecoveryRequired ||
                _runtime.Tasks.Get(source.Request.TaskId)?.Status != AiTaskStatus.RecoveryRequired)
                throw new InvalidOperationException("Execution ist nicht recovery-pflichtig.");

            var retryTask = _runtime.Tasks.CreateRecoveryRetry(source.Request.TaskId);
            AiExecutionSnapshot? retry = null;
            try
            {
                retry = _runtime.Scheduler.Enqueue(
                    retryTask.Id, source.Request.Priority, source.Request.RequiredRole,
                    source.Request.RequiredCapabilities, notBeforeUtc: null,
                    maxAttempts: source.Request.MaxAttempts,
                    resourceRequirements: Clone(source.Request.ResourceRequirements),
                    submittedBySessionId: null,
                    submittedByClientId: source.Request.SubmittedByClientId);
                const string reason = "runtime_recovery_retry_created";
                if (!_runtime.Tasks.FailRecoveryRequired(source.Request.TaskId, reason) ||
                    !_runtime.Scheduler.FailRecoveryRequired(executionId, reason))
                    throw new InvalidOperationException("Recovery-Ausgangszustand hat sich geändert.");
            }
            catch
            {
                if (retry is not null) _runtime.Scheduler.RemoveDurableRetry(retry.Request.Id);
                _runtime.Tasks.RemoveDurableRetry(retryTask.Id);
                throw;
            }
            Publish(executionId, source.Request.TaskId, "retry", retry!.Request.Id, actorId);
            return retry;
        }
    }

    private void Authorize(string actorId, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (actorId.Length > 4_000 || AiMessageSafetyPolicy.ContainsSensitiveContent(actorId))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                "Recovery-Akteur enthält unzulässige Metadaten.");
        string? denialReason = null;
        if (_authorizer is null || !_authorizer.IsAuthorized(actorId, action, out denialReason))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryForbidden,
                string.IsNullOrWhiteSpace(denialReason)
                    ? "Recovery-Entscheidung ist nicht autorisiert."
                    : denialReason);
    }

    private void Publish(
        string executionId, string taskId, string resolution,
        string? retryId = null, string? actorId = null)
        => _runtime.Events.Publish(new AiRuntimeEvent
        {
            Type = AiRuntimeEventType.ExecutionRecoveryResolved,
            Message = executionId,
            Data = new Dictionary<string, object?>
            {
                ["taskId"] = taskId,
                ["resolution"] = resolution,
                ["retryExecutionId"] = retryId,
                ["actorId"] = actorId
            }
        });

    private static AiResourceRequirements? Clone(AiResourceRequirements? value) => value is null ? null : new()
    {
        Kind = value.Kind,
        RequiredCapabilities = value.RequiredCapabilities.ToArray(),
        MinimumContextTokens = value.MinimumContextTokens,
        MinimumMemoryMiB = value.MinimumMemoryMiB,
        MinimumWorkUnitsPerMinute = value.MinimumWorkUnitsPerMinute,
        EstimatedInputUnits = value.EstimatedInputUnits,
        EstimatedOutputUnits = value.EstimatedOutputUnits,
        EstimatedWorkUnits = value.EstimatedWorkUnits,
        CostUnit = value.CostUnit,
        PinnedResourceId = value.PinnedResourceId,
        ReservationDuration = value.ReservationDuration
    };
}
