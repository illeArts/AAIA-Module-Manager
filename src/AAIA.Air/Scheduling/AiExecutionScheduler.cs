using AAIA.Air.Tasks;
using AAIA.Air.Resources;

namespace AAIA.Air.Scheduling;

/// <summary>
/// Priorisierte, alternde Execution Queue. Der Scheduler weist bestehende AIR-Tasks
/// geeigneten Sessions zu; ausgeführt wird ausschließlich über den AiTaskManager.
/// </summary>
public sealed class AiExecutionScheduler
{
    private sealed class ScheduledItem
    {
        public required AiExecutionRequest Request { get; init; }
        public AiExecutionState State { get; set; } = AiExecutionState.Queued;
        public int AttemptCount { get; set; }
        public AiExecutionLease? Lease { get; set; }
        public string? LastError { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public string? ResourceId { get; set; }
        public string? ResourceReservationId { get; set; }
        public int ResourceDeferralCount { get; set; }
        public DateTime? ResourceDeferredUntilUtc { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, ScheduledItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sessionLastAssigned = new(StringComparer.Ordinal);
    private readonly AiTaskManager _tasks;
    private readonly AiSessionManager _sessions;
    private readonly AiRuntimeEventBus _events;
    private readonly TimeProvider _time;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _agingInterval;
    private readonly AiResourceManager? _resources;
    private readonly int _maxResourceDeferrals;
    private long _assignmentSequence;
    private bool _recoveryBlocked;

    internal Action<string>? DurableTransitionRequired { get; set; }

    public AiExecutionScheduler(
        AiTaskManager tasks,
        AiSessionManager sessions,
        AiRuntimeEventBus events,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? agingInterval = null,
        AiResourceManager? resources = null,
        int maxResourceDeferrals = 10)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _time = timeProvider ?? TimeProvider.System;
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(2);
        _agingInterval = agingInterval ?? TimeSpan.FromMinutes(5);
        _resources = resources;
        _maxResourceDeferrals = maxResourceDeferrals;
        if (_leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (_agingInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(agingInterval));
        if (_maxResourceDeferrals <= 0) throw new ArgumentOutOfRangeException(nameof(maxResourceDeferrals));
    }

    public AiExecutionSnapshot Enqueue(
        string taskId,
        AiExecutionPriority priority = AiExecutionPriority.Normal,
        AiRole? requiredRole = null,
        IEnumerable<string>? requiredCapabilities = null,
        DateTime? notBeforeUtc = null,
        int maxAttempts = 3,
        AiResourceRequirements? resourceRequirements = null,
        string? submittedBySessionId = null,
        string? submittedByClientId = null)
    {
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        var task = _tasks.Get(taskId) ?? throw new InvalidOperationException("Aufgabe nicht gefunden.");
        if (task.Status != AiTaskStatus.Pending || task.OwnerSessionId is not null)
            throw new InvalidOperationException("Nur nicht übernommene Pending-Aufgaben können eingeplant werden.");

        var now = UtcNow;
        var request = new AiExecutionRequest
        {
            TaskId = taskId,
            SubmittedBySessionId = submittedBySessionId,
            SubmittedByClientId = submittedByClientId,
            Priority = priority,
            RequiredRole = requiredRole,
            RequiredCapabilities = requiredCapabilities?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>(),
            EnqueuedAtUtc = now,
            NotBeforeUtc = notBeforeUtc,
            MaxAttempts = maxAttempts,
            ResourceRequirements = CloneRequirements(resourceRequirements)
        };

        ScheduledItem item;
        lock (_gate)
        {
            if (_items.Values.Any(existing => existing.Request.TaskId == taskId && !IsTerminal(existing.State)))
                throw new InvalidOperationException("Aufgabe ist bereits eingeplant.");
            item = new ScheduledItem { Request = request, UpdatedAtUtc = now };
            _items[request.Id] = item;
        }

        _events.Publish(new AiRuntimeEvent
        {
            Type = AiRuntimeEventType.ExecutionQueued,
            Message = request.Id,
            Data = new Dictionary<string, object?> { ["taskId"] = taskId, ["priority"] = priority.ToString() }
        });
        DurableTransitionRequired?.Invoke("execution.queued");
        return Snapshot(item);
    }

    public bool TryAssignNext(out AiExecutionLease? lease)
    {
        RecoverExpiredLeases();
        lease = null;
        AiSession? selectedSession = null;
        ScheduledItem? selectedItem = null;
        var now = UtcNow;

        lock (_gate)
        {
            if (_recoveryBlocked) return false;

            var busySessions = _items.Values
                .Where(item => item.State is AiExecutionState.Leased or AiExecutionState.Running or AiExecutionState.Cancelling)
                .Select(item => item.Lease?.SessionId)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.Ordinal);

            var candidates = _items.Values
                .Where(item => item.State == AiExecutionState.Queued &&
                               (!item.Request.NotBeforeUtc.HasValue || item.Request.NotBeforeUtc <= now) &&
                               (!item.ResourceDeferredUntilUtc.HasValue || item.ResourceDeferredUntilUtc <= now))
                .OrderByDescending(item => EffectivePriority(item.Request, now))
                .ThenBy(item => item.Request.EnqueuedAtUtc)
                .ThenBy(item => item.Request.Id)
                .ToArray();

            foreach (var item in candidates)
            {
                var session = _sessions.Active
                    .Where(candidate => !busySessions.Contains(candidate.SessionId))
                    .Where(candidate => !item.Request.RequiredRole.HasValue ||
                                        candidate.HasRole(item.Request.RequiredRole.Value))
                    .Where(candidate => item.Request.RequiredCapabilities.All(candidate.HasCapability))
                    .OrderBy(candidate => _sessionLastAssigned.GetValueOrDefault(candidate.SessionId, long.MinValue))
                    .ThenBy(candidate => candidate.ActiveLocks.Count)
                    .ThenBy(candidate => candidate.ConnectedAt)
                    .FirstOrDefault();

                if (session is null) continue;
                if (!_tasks.Claim(item.Request.TaskId, session, out var conflict))
                {
                    item.State = AiExecutionState.Failed;
                    item.LastError = conflict;
                    item.UpdatedAtUtc = now;
                    continue;
                }

                item.AttemptCount++;
                item.State = AiExecutionState.Leased;
                item.Lease = new AiExecutionLease
                {
                    RequestId = item.Request.Id,
                    TaskId = item.Request.TaskId,
                    SessionId = session.SessionId,
                    Attempt = item.AttemptCount,
                    LeasedAtUtc = now,
                    ExpiresAtUtc = now + _leaseDuration
                };
                item.UpdatedAtUtc = now;
                _sessionLastAssigned[session.SessionId] = ++_assignmentSequence;
                selectedItem = item;
                selectedSession = session;
                lease = item.Lease;
                break;
            }
        }

        if (lease is null || selectedSession is null || selectedItem is null) return false;
        _events.Publish(AiRuntimeEventType.ExecutionLeased, selectedSession, message: lease.RequestId,
            data: new Dictionary<string, object?> { ["taskId"] = lease.TaskId, ["attempt"] = lease.Attempt });
        DurableTransitionRequired?.Invoke("execution.leased");
        return true;
    }

    public async Task<AiExecutionSnapshot> RunAsync(
        string requestId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        AiExecutionLease lease;
        AiSession session;
        CancellationTokenSource? executionCancellation = null;
        ScheduledItem item;

        lock (_gate)
        {
            item = GetItem(requestId);
            if (item.State != AiExecutionState.Leased || item.Lease?.SessionId != sessionId)
                throw new InvalidOperationException("Execution besitzt keine gültige Lease für diese Session.");
            if (item.Lease.ExpiresAtUtc <= UtcNow)
                throw new InvalidOperationException("Execution-Lease ist abgelaufen.");
            if (!_sessions.TryGet(sessionId, out session!))
                throw new InvalidOperationException("Zugewiesene Session ist nicht mehr aktiv.");

            lease = item.Lease;
        }

        if (item.Request.ResourceRequirements is not null)
        {
            if (_resources is null)
                return ApplyResourceDenial(item, session,
                    new AiResourceDecision
                    {
                        Status = AiResourceDecisionStatus.Denied,
                        ReasonCode = AiResourceReasonCodes.NoMatchingResource,
                        Retryable = false
                    });

            var task = _tasks.Get(lease.TaskId)!;
            var decision = _resources.SelectAndReserve(new AiResourceRequest
            {
                ExecutionRequestId = requestId,
                TaskId = lease.TaskId,
                ProjectId = task.Project,
                SessionId = sessionId,
                Requirements = item.Request.ResourceRequirements
            });
            if (decision.Status == AiResourceDecisionStatus.Denied)
                return ApplyResourceDenial(item, session, decision);

            lock (_gate)
            {
                // Cancel kann während der Resource-Auswahl die Lease freigeben.
                if (item.State != AiExecutionState.Leased || item.Lease?.SessionId != sessionId)
                {
                    _resources.Release(decision.Reservation!.Id, out _);
                    return Snapshot(item);
                }
                item.ResourceId = decision.SelectedResourceId;
                item.ResourceReservationId = decision.Reservation!.Id;
            }
        }

        lock (_gate)
        {
            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            item.Cancellation = executionCancellation;
            item.State = AiExecutionState.Running;
            item.UpdatedAtUtc = UtcNow;
        }
        _events.Publish(AiRuntimeEventType.ExecutionStarted, session, message: requestId,
            data: new Dictionary<string, object?> { ["taskId"] = lease.TaskId });

        AiRuntimeEventType finalEvent;
        try
        {
            var task = await _tasks.RunAsync(lease.TaskId, session, executionCancellation.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                item.State = task.Status == AiTaskStatus.Completed
                    ? AiExecutionState.Completed
                    : AiExecutionState.Failed;
                item.LastError = task.Status == AiTaskStatus.Completed ? null : $"Task-Status: {task.Status}";
                item.UpdatedAtUtc = UtcNow;
            }
            finalEvent = item.State == AiExecutionState.Completed
                ? AiRuntimeEventType.ExecutionCompleted
                : AiRuntimeEventType.ExecutionFailed;
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                item.State = AiExecutionState.Cancelled;
                item.LastError = "Ausführung abgebrochen.";
                item.UpdatedAtUtc = UtcNow;
            }
            finalEvent = AiRuntimeEventType.ExecutionCancelled;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                item.State = AiExecutionState.Failed;
                item.LastError = ex.Message;
                item.UpdatedAtUtc = UtcNow;
            }
            finalEvent = AiRuntimeEventType.ExecutionFailed;
        }
        finally
        {
            lock (_gate) item.Cancellation = null;
            executionCancellation.Dispose();
        }

        if (_resources is not null && item.ResourceReservationId is not null)
        {
            var reservation = _resources.GetReservation(item.ResourceReservationId);
            if (reservation is not null &&
                !_resources.Commit(reservation.Id, reservation.EstimatedCost, out var resourceError))
            {
                lock (_gate)
                {
                    item.State = AiExecutionState.Failed;
                    item.LastError = $"Resource-Settlement fehlgeschlagen: {resourceError}";
                    item.UpdatedAtUtc = UtcNow;
                }
                finalEvent = AiRuntimeEventType.ExecutionFailed;
            }
        }

        _events.Publish(finalEvent, session, message: requestId);
        DurableTransitionRequired?.Invoke("execution.settled");

        lock (_gate) return Snapshot(item);
    }

    private AiExecutionSnapshot ApplyResourceDenial(
        ScheduledItem item,
        AiSession session,
        AiResourceDecision decision)
    {
        bool requeued;
        lock (_gate)
        {
            var lease = item.Lease;
            if (lease is null) return Snapshot(item);
            _tasks.ReleaseClaim(item.Request.TaskId, lease.SessionId);
            item.Lease = null;
            item.LastError = decision.ReasonCode;
            item.ResourceDeferralCount++;
            requeued = decision.Retryable && item.ResourceDeferralCount <= _maxResourceDeferrals;
            if (requeued)
            {
                item.State = AiExecutionState.Queued;
                item.ResourceDeferredUntilUtc = decision.RetryAfterUtc ?? UtcNow.AddSeconds(30);
                item.AttemptCount = Math.Max(0, item.AttemptCount - 1);
            }
            else
            {
                item.State = AiExecutionState.Failed;
            }
            item.UpdatedAtUtc = UtcNow;
        }

        _events.Publish(requeued ? AiRuntimeEventType.ExecutionRecovered : AiRuntimeEventType.ExecutionFailed,
            session, message: item.Request.Id,
            data: new Dictionary<string, object?> { ["resourceReason"] = decision.ReasonCode });
        DurableTransitionRequired?.Invoke("execution.resource-decision");
        lock (_gate) return Snapshot(item);
    }

    public bool Cancel(string requestId)
    {
        AiRuntimeEventType? eventType = null;
        AiSession? session = null;
        var handled = false;
        lock (_gate)
        {
            if (!_items.TryGetValue(requestId, out var item) || IsTerminal(item.State)) return false;
            if (item.State == AiExecutionState.Queued)
            {
                handled = true;
                item.State = AiExecutionState.Cancelled;
                item.UpdatedAtUtc = UtcNow;
                eventType = AiRuntimeEventType.ExecutionCancelled;
            }
            else if (item.State == AiExecutionState.Leased && item.Lease is not null)
            {
                handled = true;
                _tasks.ReleaseClaim(item.Request.TaskId, item.Lease.SessionId);
                _sessions.TryGet(item.Lease.SessionId, out session!);
                item.State = AiExecutionState.Cancelled;
                item.UpdatedAtUtc = UtcNow;
                eventType = AiRuntimeEventType.ExecutionCancelled;
            }
            else if (item.State == AiExecutionState.Running)
            {
                handled = true;
                item.State = AiExecutionState.Cancelling;
                item.UpdatedAtUtc = UtcNow;
                item.Cancellation?.Cancel();
            }
        }

        if (eventType.HasValue)
            _events.Publish(eventType.Value, session, message: requestId);
        if (handled) DurableTransitionRequired?.Invoke("execution.cancelled");
        return handled;
    }

    public int RecoverExpiredLeases()
    {
        var recovered = new List<(string RequestId, AiSession? Session, bool Failed)>();
        var now = UtcNow;
        lock (_gate)
        {
            foreach (var item in _items.Values.Where(item => item.State == AiExecutionState.Leased).ToArray())
            {
                var lease = item.Lease!;
                if (lease.ExpiresAtUtc > now && _sessions.TryGet(lease.SessionId, out _)) continue;

                _tasks.ReleaseClaim(item.Request.TaskId, lease.SessionId);
                _sessions.TryGet(lease.SessionId, out var session);
                var failed = item.AttemptCount >= item.Request.MaxAttempts;
                item.State = failed ? AiExecutionState.Failed : AiExecutionState.Queued;
                item.LastError = failed ? "Maximale Lease-Versuche überschritten." : null;
                item.Lease = null;
                item.UpdatedAtUtc = now;
                recovered.Add((item.Request.Id, session, failed));
            }
        }

        foreach (var entry in recovered)
            _events.Publish(entry.Failed ? AiRuntimeEventType.ExecutionFailed : AiRuntimeEventType.ExecutionRecovered,
                entry.Session, message: entry.RequestId);
        if (recovered.Count > 0) DurableTransitionRequired?.Invoke("execution.lease-recovered");
        return recovered.Count;
    }

    public AiExecutionSnapshot? Get(string requestId)
    {
        lock (_gate) return _items.TryGetValue(requestId, out var item) ? Snapshot(item) : null;
    }

    public IReadOnlyList<AiExecutionSnapshot> List()
    {
        lock (_gate)
            return _items.Values.OrderBy(item => item.Request.EnqueuedAtUtc).Select(Snapshot).ToArray();
    }

    internal (int ReleasedLeases, int RecoveryRequired) RestoreDurableExecutions(
        IEnumerable<AiDurableExecutionSnapshot> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);
        lock (_gate)
        {
            if (_items.Count != 0)
                throw new InvalidOperationException("Executions können nur in einen leeren Scheduler wiederhergestellt werden.");

            var releasedLeases = 0;
            var recoveryRequired = 0;
            try
            {
                foreach (var persisted in executions)
                {
                    ArgumentNullException.ThrowIfNull(persisted);
                    if (_tasks.Get(persisted.TaskId) is null)
                        throw new InvalidOperationException($"Task der Execution fehlt: {persisted.TaskId}");
                    var state = persisted.State;
                    var attempts = persisted.AttemptCount;
                    if (state == AiExecutionState.Leased)
                    {
                        state = AiExecutionState.Queued;
                        attempts = Math.Max(0, attempts - 1);
                        releasedLeases++;
                    }
                    else if (state is AiExecutionState.Running or AiExecutionState.Cancelling)
                    {
                        state = AiExecutionState.RecoveryRequired;
                        recoveryRequired++;
                    }
                    else if (state == AiExecutionState.RecoveryRequired)
                    {
                        recoveryRequired++;
                    }

                    var request = new AiExecutionRequest
                    {
                        Id = persisted.Id,
                        TaskId = persisted.TaskId,
                        SubmittedBySessionId = null,
                        SubmittedByClientId = persisted.SubmittedByClientId,
                        Priority = persisted.Priority,
                        RequiredRole = persisted.RequiredRole,
                        RequiredCapabilities = persisted.RequiredCapabilities
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        EnqueuedAtUtc = persisted.EnqueuedAtUtc,
                        NotBeforeUtc = persisted.NotBeforeUtc,
                        MaxAttempts = persisted.MaxAttempts,
                        ResourceRequirements = CloneRequirements(persisted.ResourceRequirements)
                    };
                    var item = new ScheduledItem
                    {
                        Request = request,
                        State = state,
                        AttemptCount = attempts,
                        Lease = null,
                        LastError = persisted.LastErrorCode,
                        UpdatedAtUtc = persisted.UpdatedAtUtc,
                        ResourceId = persisted.ResourceId,
                        ResourceReservationId = persisted.ResourceReservationId,
                        ResourceDeferralCount = persisted.ResourceDeferralCount,
                        ResourceDeferredUntilUtc = persisted.ResourceDeferredUntilUtc
                    };
                    if (!_items.TryAdd(request.Id, item))
                        throw new InvalidOperationException($"Doppelte Execution-ID im Snapshot: {request.Id}");
                }
            }
            catch
            {
                _items.Clear();
                _recoveryBlocked = false;
                throw;
            }
            _recoveryBlocked = recoveryRequired > 0;
            return (releasedLeases, recoveryRequired);
        }
    }

    internal bool IsRecoveryRequired(string requestId)
    {
        lock (_gate)
            return _items.TryGetValue(requestId, out var item) &&
                   item.State == AiExecutionState.RecoveryRequired;
    }

    internal bool FailRecoveryRequired(string requestId, string reason)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(requestId, out var item) ||
                item.State != AiExecutionState.RecoveryRequired)
                return false;
            item.State = AiExecutionState.Failed;
            item.LastError = reason;
            item.UpdatedAtUtc = UtcNow;
            RefreshRecoveryBlock();
            return true;
        }
    }

    internal bool RemoveDurableRetry(string requestId)
    {
        lock (_gate)
            return _items.TryGetValue(requestId, out var item) &&
                   item.State == AiExecutionState.Queued &&
                   _items.Remove(requestId);
    }

    internal void ClearDurableRestore()
    {
        lock (_gate)
        {
            _items.Clear();
            _sessionLastAssigned.Clear();
            _assignmentSequence = 0;
            _recoveryBlocked = false;
        }
    }

    private void RefreshRecoveryBlock()
        => _recoveryBlocked = _items.Values.Any(item => item.State == AiExecutionState.RecoveryRequired);

    private ScheduledItem GetItem(string requestId)
        => _items.TryGetValue(requestId, out var item)
            ? item
            : throw new InvalidOperationException("Execution nicht gefunden.");

    private int EffectivePriority(AiExecutionRequest request, DateTime now)
    {
        var ageSteps = Math.Max(0, (int)((now - request.EnqueuedAtUtc).Ticks / _agingInterval.Ticks));
        return Math.Min((int)AiExecutionPriority.Critical, (int)request.Priority + ageSteps);
    }

    private static bool IsTerminal(AiExecutionState state)
        => state is AiExecutionState.Completed or AiExecutionState.Failed or AiExecutionState.Cancelled;

    private static AiResourceRequirements? CloneRequirements(AiResourceRequirements? requirements)
        => requirements is null ? null : new AiResourceRequirements
        {
            Kind = requirements.Kind,
            RequiredCapabilities = (requirements.RequiredCapabilities ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MinimumContextTokens = requirements.MinimumContextTokens,
            MinimumMemoryMiB = requirements.MinimumMemoryMiB,
            MinimumWorkUnitsPerMinute = requirements.MinimumWorkUnitsPerMinute,
            EstimatedInputUnits = requirements.EstimatedInputUnits,
            EstimatedOutputUnits = requirements.EstimatedOutputUnits,
            EstimatedWorkUnits = requirements.EstimatedWorkUnits,
            CostUnit = requirements.CostUnit,
            PinnedResourceId = requirements.PinnedResourceId,
            ReservationDuration = requirements.ReservationDuration
        };

    private static AiExecutionSnapshot Snapshot(ScheduledItem item) => new()
    {
        Request = item.Request,
        State = item.State,
        AttemptCount = item.AttemptCount,
        Lease = item.Lease,
        LastError = item.LastError,
        ResourceId = item.ResourceId,
        ResourceReservationId = item.ResourceReservationId,
        ResourceDeferralCount = item.ResourceDeferralCount,
        ResourceDeferredUntilUtc = item.ResourceDeferredUntilUtc,
        UpdatedAtUtc = item.UpdatedAtUtc
    };

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
}
