namespace AAIA.Air.Resources;

/// <summary>
/// Filtert, bewertet und reserviert interne Ausführungsressourcen. Führt selbst keine
/// Tasks oder Tools aus und verändert weder Scheduler-Owner noch Permissions.
/// </summary>
public sealed class AiResourceManager
{
    private sealed class BudgetState
    {
        public required AiResourceBudget Budget { get; init; }
        public decimal Spent { get; set; }
        public decimal Reserved { get; set; }
    }

    private sealed class ReservationState
    {
        public required string Id { get; init; }
        public required AiResourceProfile Profile { get; init; }
        public required AiResourceRequest Request { get; init; }
        public required string CostUnit { get; init; }
        public required decimal EstimatedCost { get; init; }
        public required DateTime ReservedAtUtc { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public required string[] BudgetIds { get; init; }
        public AiReservationState State { get; set; } = AiReservationState.Reserved;
        public decimal? ActualCost { get; set; }
        public DateTime? SettledAtUtc { get; set; }
    }

    private sealed record Candidate(
        AiResourceProfile Profile,
        AiResourceTelemetry Telemetry,
        decimal EstimatedCost,
        string CostUnit,
        double LoadRatio);

    private readonly object _gate = new();
    private readonly Dictionary<string, BudgetState> _budgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReservationState> _reservations = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly TimeSpan _telemetryMaxAge;
    private readonly AiRuntimeEventBus? _events;

    public AiResourceRegistry Registry { get; } = new();

    public AiResourceManager(
        TimeProvider? timeProvider = null,
        TimeSpan? telemetryMaxAge = null,
        AiRuntimeEventBus? events = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _telemetryMaxAge = telemetryMaxAge ?? TimeSpan.FromMinutes(2);
        if (_telemetryMaxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(telemetryMaxAge));
        _events = events;
    }

    public AiResourceBudgetSnapshot SetBudget(AiResourceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.CostUnit);
        if (budget.HardLimit < 0 || budget.WarningThreshold < 0 ||
            budget.WarningThreshold > budget.HardLimit || budget.WindowEndsAtUtc <= budget.WindowStartsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (budget.Scope != AiBudgetScope.Runtime && string.IsNullOrWhiteSpace(budget.ScopeId))
            throw new ArgumentException("Nicht-globale Budgets benötigen eine ScopeId.", nameof(budget));

        lock (_gate)
        {
            if (_budgets.ContainsKey(budget.Id)) throw new InvalidOperationException("Budget ist bereits registriert.");
            var state = new BudgetState { Budget = budget };
            _budgets[budget.Id] = state;
            return BudgetSnapshot(state);
        }
    }

    public IReadOnlyList<AiResourceBudgetSnapshot> ListBudgets()
    {
        lock (_gate) return _budgets.Values.Select(BudgetSnapshot).ToArray();
    }

    public AiResourceDecision SelectAndReserve(AiResourceRequest request)
    {
        ValidateRequest(request);
        var now = UtcNow;
        AiResourceDecision decision;

        lock (_gate)
        {
            ExpireReservationsLocked(now);
            var profiles = Registry.ListProfiles();
            if (!string.IsNullOrEmpty(request.Requirements.PinnedResourceId) &&
                profiles.All(profile => profile.ResourceId != request.Requirements.PinnedResourceId))
                return Denied(AiResourceReasonCodes.PinnedResourceUnavailable, false, Array.Empty<AiResourceRejection>());

            var profilesToEvaluate = string.IsNullOrEmpty(request.Requirements.PinnedResourceId)
                ? profiles
                : profiles.Where(profile => profile.ResourceId == request.Requirements.PinnedResourceId).ToArray();

            var candidates = new List<Candidate>();
            var rejections = new List<AiResourceRejection>();
            foreach (var profile in profilesToEvaluate)
            {
                var rejection = Evaluate(profile, request, now, out var candidate);
                if (rejection is not null) rejections.Add(rejection);
                else candidates.Add(candidate!);
            }

            if (candidates.Count == 0)
            {
                var pinned = !string.IsNullOrEmpty(request.Requirements.PinnedResourceId);
                var primary = pinned
                    ? AiResourceReasonCodes.PinnedResourceUnavailable
                    : SelectPrimaryReason(rejections);
                var retryable = rejections.Count > 0 && rejections.All(rejection => rejection.Retryable);
                var retryAfter = rejections.Where(rejection => rejection.RetryAfterUtc.HasValue)
                    .Select(rejection => rejection.RetryAfterUtc).Min();
                return Denied(primary, retryable, rejections, retryAfter);
            }

            var scored = Score(candidates)
                .OrderByDescending(entry => entry.Score.Total)
                .ThenBy(entry => entry.Candidate.Profile.ResourceId, StringComparer.Ordinal)
                .ToArray();
            var selected = scored[0];
            var matchingBudgets = MatchingBudgets(request, selected.Candidate.CostUnit, now).ToArray();

            // Kapazität und alle Budgets werden unter demselben Lock reserviert.
            if (!HasCapacity(selected.Candidate.Profile, selected.Candidate.Telemetry, request))
                return Denied(AiResourceReasonCodes.CapacityUnavailable, true, rejections, now.AddSeconds(30));
            if (matchingBudgets.Any(budget => budget.Spent + budget.Reserved + selected.Candidate.EstimatedCost > budget.Budget.HardLimit))
                return Denied(AiResourceReasonCodes.BudgetExceeded, false, rejections);

            var reservation = new ReservationState
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Profile = selected.Candidate.Profile,
                Request = request,
                CostUnit = selected.Candidate.CostUnit,
                EstimatedCost = selected.Candidate.EstimatedCost,
                ReservedAtUtc = now,
                ExpiresAtUtc = now + request.Requirements.ReservationDuration,
                BudgetIds = matchingBudgets.Select(budget => budget.Budget.Id).ToArray()
            };
            foreach (var budget in matchingBudgets) budget.Reserved += reservation.EstimatedCost;
            _reservations[reservation.Id] = reservation;

            decision = new AiResourceDecision
            {
                Status = AiResourceDecisionStatus.Selected,
                SelectedResourceId = reservation.Profile.ResourceId,
                Reservation = ReservationSnapshot(reservation),
                Score = selected.Score,
                Rejections = rejections
            };
        }

        _events?.Publish(new AiRuntimeEvent
        {
            Type = AiRuntimeEventType.ResourceReserved,
            SessionId = request.SessionId,
            Message = decision.Reservation!.Id,
            Data = new Dictionary<string, object?> { ["resourceId"] = decision.SelectedResourceId }
        });
        return decision;
    }

    public bool Commit(string reservationId, decimal actualCost, out string? error)
        => Settle(reservationId, AiReservationState.Committed, actualCost, out error);

    public bool Release(string reservationId, out string? error)
        => Settle(reservationId, AiReservationState.Released, null, out error);

    public int ExpireReservations()
    {
        lock (_gate) return ExpireReservationsLocked(UtcNow);
    }

    public AiResourceReservation? GetReservation(string reservationId)
    {
        lock (_gate)
            return _reservations.TryGetValue(reservationId, out var state) ? ReservationSnapshot(state) : null;
    }

    public IReadOnlyList<AiResourceReservation> ListReservations()
    {
        lock (_gate) return _reservations.Values.Select(ReservationSnapshot).ToArray();
    }

    private AiResourceRejection? Evaluate(
        AiResourceProfile profile,
        AiResourceRequest request,
        DateTime now,
        out Candidate? candidate)
    {
        candidate = null;
        var required = request.Requirements;
        if (!string.IsNullOrEmpty(required.PinnedResourceId) && profile.ResourceId != required.PinnedResourceId)
            return Reject(profile, AiResourceReasonCodes.PinnedResourceUnavailable, false);
        if (!profile.Enabled) return Reject(profile, AiResourceReasonCodes.ResourceUnhealthy, false);
        if (profile.Kind != required.Kind ||
            (required.RequiredCapabilities ?? Array.Empty<string>()).Any(capability =>
                !profile.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase)))
            return Reject(profile, AiResourceReasonCodes.NoMatchingResource, false);

        if (!MeetsMinimum(profile.Capacity.ContextWindowTokens, required.MinimumContextTokens) ||
            !MeetsMinimum(profile.Capacity.MemoryMiB, required.MinimumMemoryMiB) ||
            !MeetsMinimum(profile.Capacity.WorkUnitsPerMinute, required.MinimumWorkUnitsPerMinute))
            return Reject(profile, AiResourceReasonCodes.CapacityUnavailable, false);

        var telemetry = Registry.GetTelemetry(profile.ResourceId);
        if (telemetry is null || now - telemetry.ObservedAtUtc > _telemetryMaxAge)
            return Reject(profile, AiResourceReasonCodes.TelemetryStale, true, now.AddSeconds(30));
        if (!telemetry.Healthy || telemetry.Throttled)
            return Reject(profile, AiResourceReasonCodes.ResourceUnhealthy, true, now.AddSeconds(30));
        if (!HasCapacity(profile, telemetry, request))
            return Reject(profile, AiResourceReasonCodes.CapacityUnavailable, true, now.AddSeconds(30));

        var rate = profile.CostRate;
        var requestedUnit = required.CostUnit;
        if (rate is not null && !string.Equals(rate.CostUnit, requestedUnit, StringComparison.Ordinal))
            return Reject(profile, AiResourceReasonCodes.CostUnitMismatch, false);
        var costUnit = rate?.CostUnit ?? requestedUnit ?? "unitless";
        var estimatedCost = EstimateCost(rate, required);

        var budgets = RelevantBudgets(request, now).ToArray();
        if (budgets.Any(budget => !string.Equals(budget.Budget.CostUnit, costUnit, StringComparison.Ordinal)))
            return Reject(profile, AiResourceReasonCodes.CostUnitMismatch, false);
        if (budgets.Any(budget => budget.Spent + budget.Reserved + estimatedCost > budget.Budget.HardLimit))
            return Reject(profile, AiResourceReasonCodes.BudgetExceeded, false);

        var maxSlots = profile.Capacity.MaxConcurrentExecutions!.Value;
        var occupied = telemetry.ExternalRunningExecutions + ActiveReservations(profile.ResourceId).Count;
        candidate = new Candidate(profile, telemetry, estimatedCost, costUnit,
            Math.Clamp((double)occupied / maxSlots, 0, 1));
        return null;
    }

    private bool HasCapacity(AiResourceProfile profile, AiResourceTelemetry telemetry, AiResourceRequest request)
    {
        var capacity = profile.Capacity;
        if (!capacity.MaxConcurrentExecutions.HasValue) return false;
        var reservations = ActiveReservations(profile.ResourceId);
        if (telemetry.ExternalRunningExecutions + reservations.Count >= capacity.MaxConcurrentExecutions.Value) return false;
        var units = request.Requirements.EstimatedInputUnits + request.Requirements.EstimatedOutputUnits;
        if (capacity.RequestsPerMinute.HasValue &&
            telemetry.RequestsInCurrentMinute + reservations.Count + 1 > capacity.RequestsPerMinute.Value) return false;
        if (capacity.TokensPerMinute.HasValue &&
            telemetry.TokensInCurrentMinute + reservations.Sum(r =>
                r.Request.Requirements.EstimatedInputUnits + r.Request.Requirements.EstimatedOutputUnits) + units >
            capacity.TokensPerMinute.Value) return false;
        if (capacity.WorkUnitsPerMinute.HasValue &&
            telemetry.WorkUnitsInCurrentMinute + reservations.Sum(r => r.Request.Requirements.EstimatedWorkUnits) +
            request.Requirements.EstimatedWorkUnits > capacity.WorkUnitsPerMinute.Value) return false;
        return true;
    }

    private IEnumerable<(Candidate Candidate, AiResourceScoreBreakdown Score)> Score(IReadOnlyList<Candidate> candidates)
    {
        var maxCost = candidates.Max(candidate => candidate.EstimatedCost);
        var minCost = candidates.Min(candidate => candidate.EstimatedCost);
        var knownLatencies = candidates.Select(TotalLatency).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var maxLatency = knownLatencies.Length == 0 ? 0 : knownLatencies.Max();
        var minLatency = knownLatencies.Length == 0 ? 0 : knownLatencies.Min();

        foreach (var candidate in candidates)
        {
            var capacity = 1 - candidate.LoadRatio;
            var cost = NormalizeLowerIsBetter((double)candidate.EstimatedCost, (double)minCost, (double)maxCost);
            var reliability = candidate.Telemetry.FailureRate.HasValue
                ? 1 - Math.Clamp(candidate.Telemetry.FailureRate.Value, 0, 1)
                : 0;
            var latencyValue = TotalLatency(candidate);
            var latency = latencyValue.HasValue
                ? NormalizeLowerIsBetter(latencyValue.Value, minLatency, maxLatency)
                : 0;
            var locality = candidate.Profile.Locality switch
            {
                AiResourceLocality.Local => 1d,
                AiResourceLocality.PrivateNetwork => .5d,
                _ => 0d
            };
            yield return (candidate, new AiResourceScoreBreakdown
            {
                Capacity = capacity,
                Cost = cost,
                Reliability = reliability,
                Latency = latency,
                Locality = locality,
                Total = capacity * .35 + cost * .30 + reliability * .20 + latency * .10 + locality * .05
            });
        }
    }

    private bool Settle(string reservationId, AiReservationState target, decimal? actualCost, out string? error)
    {
        error = null;
        AiResourceReservation? snapshot;
        lock (_gate)
        {
            if (!_reservations.TryGetValue(reservationId, out var state))
            {
                error = "Reservation nicht gefunden.";
                return false;
            }
            if (state.State == target) return true;
            if (state.State != AiReservationState.Reserved)
            {
                error = $"Reservation ist bereits {state.State}.";
                return false;
            }
            if (target == AiReservationState.Committed && actualCost is < 0)
            {
                error = "Tatsächliche Kosten dürfen nicht negativ sein.";
                return false;
            }

            foreach (var budgetId in state.BudgetIds)
            {
                var budget = _budgets[budgetId];
                budget.Reserved -= state.EstimatedCost;
                if (target == AiReservationState.Committed) budget.Spent += actualCost ?? state.EstimatedCost;
            }
            state.State = target;
            state.ActualCost = target == AiReservationState.Committed ? actualCost ?? state.EstimatedCost : null;
            state.SettledAtUtc = UtcNow;
            snapshot = ReservationSnapshot(state);
        }

        _events?.Publish(new AiRuntimeEvent
        {
            Type = target == AiReservationState.Committed
                ? AiRuntimeEventType.ResourceCommitted
                : AiRuntimeEventType.ResourceReleased,
            SessionId = snapshot.SessionId,
            Message = snapshot.Id,
            Data = new Dictionary<string, object?> { ["resourceId"] = snapshot.ResourceId }
        });
        return true;
    }

    private int ExpireReservationsLocked(DateTime now)
    {
        var expired = 0;
        foreach (var state in _reservations.Values.Where(r => r.State == AiReservationState.Reserved && r.ExpiresAtUtc <= now))
        {
            foreach (var budgetId in state.BudgetIds) _budgets[budgetId].Reserved -= state.EstimatedCost;
            state.State = AiReservationState.Expired;
            state.SettledAtUtc = now;
            expired++;
        }
        return expired;
    }

    private IReadOnlyList<ReservationState> ActiveReservations(string resourceId)
        => _reservations.Values.Where(r => r.Profile.ResourceId == resourceId && r.State == AiReservationState.Reserved).ToArray();

    private IEnumerable<BudgetState> RelevantBudgets(AiResourceRequest request, DateTime now)
        => _budgets.Values.Where(state => state.Budget.WindowStartsAtUtc <= now && state.Budget.WindowEndsAtUtc > now &&
            state.Budget.Scope switch
            {
                AiBudgetScope.Runtime => true,
                AiBudgetScope.Project => state.Budget.ScopeId == request.ProjectId,
                AiBudgetScope.Session => state.Budget.ScopeId == request.SessionId,
                AiBudgetScope.Task => state.Budget.ScopeId == request.TaskId,
                _ => false
            });

    private IEnumerable<BudgetState> MatchingBudgets(AiResourceRequest request, string costUnit, DateTime now)
        => RelevantBudgets(request, now).Where(state => state.Budget.CostUnit == costUnit);

    private static decimal EstimateCost(AiResourceCostRate? rate, AiResourceRequirements requirements)
        => rate is null ? 0 : rate.FixedPerExecution +
            requirements.EstimatedInputUnits / 1000m * rate.PerThousandInputUnits +
            requirements.EstimatedOutputUnits / 1000m * rate.PerThousandOutputUnits +
            requirements.EstimatedWorkUnits * rate.PerWorkUnit;

    private static bool MeetsMinimum<T>(T? available, T? required) where T : struct, IComparable<T>
        => !required.HasValue || available.HasValue && available.Value.CompareTo(required.Value) >= 0;

    private static double NormalizeLowerIsBetter(double value, double min, double max)
        => Math.Abs(max - min) < double.Epsilon ? 1 : 1 - Math.Clamp((value - min) / (max - min), 0, 1);

    private static double? TotalLatency(Candidate candidate)
        => candidate.Telemetry.P95ExecutionLatencyMs.HasValue || candidate.Telemetry.QueueLatencyMs.HasValue
            ? (candidate.Telemetry.P95ExecutionLatencyMs ?? 0) + (candidate.Telemetry.QueueLatencyMs ?? 0)
            : null;

    private static AiResourceRejection Reject(
        AiResourceProfile profile, string reason, bool retryable, DateTime? retryAfter = null)
        => new() { ResourceId = profile.ResourceId, ReasonCode = reason, Retryable = retryable, RetryAfterUtc = retryAfter };

    private static string SelectPrimaryReason(IReadOnlyList<AiResourceRejection> rejections)
        => rejections.Select(r => r.ReasonCode).Distinct().Count() == 1
            ? rejections[0].ReasonCode
            : AiResourceReasonCodes.NoMatchingResource;

    private AiResourceDecision Denied(
        string reason, bool retryable, IReadOnlyList<AiResourceRejection> rejections, DateTime? retryAfter = null)
    {
        var decision = new AiResourceDecision
        {
            Status = AiResourceDecisionStatus.Denied,
            ReasonCode = reason,
            Retryable = retryable,
            RetryAfterUtc = retryAfter,
            Rejections = rejections
        };
        _events?.Publish(new AiRuntimeEvent { Type = AiRuntimeEventType.ResourceDenied, Message = reason });
        return decision;
    }

    private static void ValidateRequest(AiResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Requirements);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutionRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
        if (request.Requirements.ReservationDuration <= TimeSpan.Zero ||
            request.Requirements.EstimatedInputUnits < 0 || request.Requirements.EstimatedOutputUnits < 0 ||
            request.Requirements.EstimatedWorkUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    private static AiResourceBudgetSnapshot BudgetSnapshot(BudgetState state)
        => new() { Budget = state.Budget, Spent = state.Spent, Reserved = state.Reserved };

    private static AiResourceReservation ReservationSnapshot(ReservationState state)
        => new()
        {
            Id = state.Id,
            ResourceId = state.Profile.ResourceId,
            ExecutionRequestId = state.Request.ExecutionRequestId,
            TaskId = state.Request.TaskId,
            SessionId = state.Request.SessionId,
            State = state.State,
            CostUnit = state.CostUnit,
            EstimatedCost = state.EstimatedCost,
            ActualCost = state.ActualCost,
            ReservedAtUtc = state.ReservedAtUtc,
            ExpiresAtUtc = state.ExpiresAtUtc,
            SettledAtUtc = state.SettledAtUtc
        };

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
}
