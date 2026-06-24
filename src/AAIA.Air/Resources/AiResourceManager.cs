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
        public required string ResourceId { get; init; }
        public required string ExecutionRequestId { get; init; }
        public required string TaskId { get; init; }
        public string? SessionId { get; init; }
        public required string CostUnit { get; init; }
        public required decimal EstimatedCost { get; init; }
        public int EstimatedInputUnits { get; init; }
        public int EstimatedOutputUnits { get; init; }
        public decimal EstimatedWorkUnits { get; init; }
        public required DateTime ReservedAtUtc { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public required string[] BudgetIds { get; init; }
        public AiReservationState State { get; set; } = AiReservationState.Reserved;
        public decimal? ActualCost { get; set; }
        public DateTime? SettledAtUtc { get; set; }
        public string? SettlementReasonCode { get; set; }
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

    internal Action<string>? DurableMutationRequired { get; set; }

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

        AiResourceBudgetSnapshot snapshot;
        lock (_gate)
        {
            if (_budgets.ContainsKey(budget.Id)) throw new InvalidOperationException("Budget ist bereits registriert.");
            var state = new BudgetState { Budget = budget };
            _budgets[budget.Id] = state;
            snapshot = BudgetSnapshot(state);
        }
        DurableMutationRequired?.Invoke("resource.budget.set");
        return snapshot;
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
                ResourceId = selected.Candidate.Profile.ResourceId,
                ExecutionRequestId = request.ExecutionRequestId,
                TaskId = request.TaskId,
                SessionId = request.SessionId,
                CostUnit = selected.Candidate.CostUnit,
                EstimatedCost = selected.Candidate.EstimatedCost,
                EstimatedInputUnits = request.Requirements.EstimatedInputUnits,
                EstimatedOutputUnits = request.Requirements.EstimatedOutputUnits,
                EstimatedWorkUnits = request.Requirements.EstimatedWorkUnits,
                ReservedAtUtc = now,
                ExpiresAtUtc = now + request.Requirements.ReservationDuration,
                BudgetIds = matchingBudgets.Select(budget => budget.Budget.Id).ToArray()
            };
            foreach (var budget in matchingBudgets) budget.Reserved += reservation.EstimatedCost;
            _reservations[reservation.Id] = reservation;

            decision = new AiResourceDecision
            {
                Status = AiResourceDecisionStatus.Selected,
                SelectedResourceId = reservation.ResourceId,
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
        DurableMutationRequired?.Invoke("resource.reservation.created");
        return decision;
    }

    public bool Commit(string reservationId, decimal actualCost, out string? error)
        => Settle(reservationId, AiReservationState.Committed, actualCost, out error);

    public bool Release(string reservationId, out string? error)
        => Settle(reservationId, AiReservationState.Released, null, out error);

    public int ExpireReservations()
    {
        int expired;
        lock (_gate) expired = ExpireReservationsLocked(UtcNow);
        if (expired > 0) DurableMutationRequired?.Invoke("resource.reservation.expired");
        return expired;
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

    internal (IReadOnlyList<AiDurableBudgetSnapshot> Budgets,
        IReadOnlyList<AiDurableReservationSnapshot> Reservations) CaptureDurableState()
    {
        lock (_gate)
        {
            var budgets = _budgets.Values
                .OrderBy(state => state.Budget.Id, StringComparer.Ordinal)
                .Select(state => new AiDurableBudgetSnapshot
                {
                    Budget = CloneBudget(state.Budget),
                    Spent = state.Spent,
                    Reserved = state.Reserved
                }).ToArray();
            var reservations = _reservations.Values
                .OrderBy(state => state.Id, StringComparer.Ordinal)
                .Select(state => new AiDurableReservationSnapshot
                {
                    Id = state.Id,
                    ResourceId = state.ResourceId,
                    ExecutionRequestId = state.ExecutionRequestId,
                    TaskId = state.TaskId,
                    State = state.State,
                    CostUnit = state.CostUnit,
                    EstimatedCost = state.EstimatedCost,
                    ActualCost = state.ActualCost,
                    ReservedAtUtc = state.ReservedAtUtc,
                    ExpiresAtUtc = state.ExpiresAtUtc,
                    SettledAtUtc = state.SettledAtUtc,
                    SettlementReasonCode = state.SettlementReasonCode,
                    BudgetIds = state.BudgetIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()
                }).ToArray();
            return (budgets, reservations);
        }
    }

    internal int RestoreDurableState(
        IEnumerable<AiDurableBudgetSnapshot> budgets,
        IEnumerable<AiDurableReservationSnapshot> reservations,
        DateTime recoveredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(reservations);
        EnsureUtc(recoveredAtUtc, nameof(recoveredAtUtc));

        var restoredBudgets = new Dictionary<string, BudgetState>(StringComparer.Ordinal);
        var persistedBudgetValues = new Dictionary<string, (decimal Spent, decimal Reserved)>(StringComparer.Ordinal);
        foreach (var snapshot in budgets)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var budget = snapshot.Budget ?? throw new InvalidOperationException("Budget fehlt.");
            ValidateBudget(budget);
            if (snapshot.Spent < 0 || snapshot.Reserved < 0)
                throw new InvalidOperationException("Persistierter Budgetstand ist negativ.");
            if (!restoredBudgets.TryAdd(budget.Id, new BudgetState { Budget = CloneBudget(budget) }))
                throw new InvalidOperationException("Doppelte Budget-ID im Snapshot.");
            persistedBudgetValues.Add(budget.Id, (snapshot.Spent, snapshot.Reserved));
        }

        var restoredReservations = new Dictionary<string, ReservationState>(StringComparer.Ordinal);
        var released = 0;
        foreach (var snapshot in reservations)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ValidateReservation(snapshot);
            var budgetIds = snapshot.BudgetIds.Distinct(StringComparer.Ordinal).ToArray();
            if (budgetIds.Length != snapshot.BudgetIds.Count)
                throw new InvalidOperationException("Reservation enthält doppelte Budget-IDs.");
            foreach (var budgetId in budgetIds)
            {
                if (!restoredBudgets.TryGetValue(budgetId, out var budget))
                    throw new InvalidOperationException("Reservation verweist auf unbekanntes Budget.");
                if (!string.Equals(budget.Budget.CostUnit, snapshot.CostUnit, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reservation und Budget verwenden unterschiedliche Kosteneinheiten.");
                if (snapshot.State == AiReservationState.Committed)
                    budget.Spent += snapshot.ActualCost!.Value;
                else if (snapshot.State == AiReservationState.Reserved)
                    budget.Reserved += snapshot.EstimatedCost;
            }

            var state = snapshot.State;
            var settledAt = snapshot.SettledAtUtc;
            var reason = snapshot.SettlementReasonCode;
            if (state == AiReservationState.Reserved)
            {
                state = AiReservationState.Released;
                settledAt = recoveredAtUtc;
                reason = AiResourceReasonCodes.RuntimeRecovery;
                released++;
            }
            var restored = new ReservationState
            {
                Id = snapshot.Id,
                ResourceId = snapshot.ResourceId,
                ExecutionRequestId = snapshot.ExecutionRequestId,
                TaskId = snapshot.TaskId,
                SessionId = null,
                CostUnit = snapshot.CostUnit,
                EstimatedCost = snapshot.EstimatedCost,
                ReservedAtUtc = snapshot.ReservedAtUtc,
                ExpiresAtUtc = snapshot.ExpiresAtUtc,
                BudgetIds = budgetIds,
                State = state,
                ActualCost = snapshot.ActualCost,
                SettledAtUtc = settledAt,
                SettlementReasonCode = reason
            };
            if (!restoredReservations.TryAdd(restored.Id, restored))
                throw new InvalidOperationException("Doppelte Reservation-ID im Snapshot.");
        }

        foreach (var pair in restoredBudgets)
        {
            var persisted = persistedBudgetValues[pair.Key];
            if (pair.Value.Spent != persisted.Spent || pair.Value.Reserved != persisted.Reserved)
                throw new InvalidOperationException("Budgetstand stimmt nicht mit der Reservationshistorie überein.");
            // Offene Reservations sind nach dem Recovery released und binden kein Budget mehr.
            pair.Value.Reserved = 0;
        }

        lock (_gate)
        {
            if (_budgets.Count != 0 || _reservations.Count != 0)
                throw new InvalidOperationException("Ressourcenzustand kann nur leer wiederhergestellt werden.");
            foreach (var pair in restoredBudgets) _budgets.Add(pair.Key, pair.Value);
            foreach (var pair in restoredReservations) _reservations.Add(pair.Key, pair.Value);
        }
        return released;
    }

    internal void ClearDurableRestore()
    {
        lock (_gate)
        {
            _budgets.Clear();
            _reservations.Clear();
        }
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
                r.EstimatedInputUnits + r.EstimatedOutputUnits) + units >
            capacity.TokensPerMinute.Value) return false;
        if (capacity.WorkUnitsPerMinute.HasValue &&
            telemetry.WorkUnitsInCurrentMinute + reservations.Sum(r => r.EstimatedWorkUnits) +
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
        DurableMutationRequired?.Invoke(target == AiReservationState.Committed
            ? "resource.reservation.committed"
            : "resource.reservation.released");
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
        => _reservations.Values.Where(r => r.ResourceId == resourceId && r.State == AiReservationState.Reserved).ToArray();

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
            ResourceId = state.ResourceId,
            ExecutionRequestId = state.ExecutionRequestId,
            TaskId = state.TaskId,
            SessionId = state.SessionId,
            State = state.State,
            CostUnit = state.CostUnit,
            EstimatedCost = state.EstimatedCost,
            ActualCost = state.ActualCost,
            ReservedAtUtc = state.ReservedAtUtc,
            ExpiresAtUtc = state.ExpiresAtUtc,
            SettledAtUtc = state.SettledAtUtc,
            SettlementReasonCode = state.SettlementReasonCode
        };

    private static AiResourceBudget CloneBudget(AiResourceBudget budget) => new()
    {
        Id = budget.Id,
        Scope = budget.Scope,
        ScopeId = budget.ScopeId,
        CostUnit = budget.CostUnit,
        Window = budget.Window,
        HardLimit = budget.HardLimit,
        WarningThreshold = budget.WarningThreshold,
        WindowStartsAtUtc = budget.WindowStartsAtUtc,
        WindowEndsAtUtc = budget.WindowEndsAtUtc
    };

    private static void ValidateBudget(AiResourceBudget budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.CostUnit);
        if (!Enum.IsDefined(budget.Scope) || !Enum.IsDefined(budget.Window) ||
            budget.HardLimit < 0 || budget.WarningThreshold < 0 ||
            budget.WarningThreshold > budget.HardLimit || budget.WindowEndsAtUtc <= budget.WindowStartsAtUtc)
            throw new InvalidOperationException("Budget ist ungültig.");
        if (budget.Scope != AiBudgetScope.Runtime && string.IsNullOrWhiteSpace(budget.ScopeId))
            throw new InvalidOperationException("Nicht-globales Budget benötigt eine Scope-ID.");
        EnsureUtc(budget.WindowStartsAtUtc, nameof(budget.WindowStartsAtUtc));
        EnsureUtc(budget.WindowEndsAtUtc, nameof(budget.WindowEndsAtUtc));
    }

    private static void ValidateReservation(AiDurableReservationSnapshot reservation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservation.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservation.ResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservation.ExecutionRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservation.TaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservation.CostUnit);
        if (!Enum.IsDefined(reservation.State) || reservation.EstimatedCost < 0 || reservation.ActualCost < 0)
            throw new InvalidOperationException("Reservation ist ungültig.");
        EnsureUtc(reservation.ReservedAtUtc, nameof(reservation.ReservedAtUtc));
        EnsureUtc(reservation.ExpiresAtUtc, nameof(reservation.ExpiresAtUtc));
        if (reservation.ExpiresAtUtc <= reservation.ReservedAtUtc)
            throw new InvalidOperationException("Reservationszeitraum ist ungültig.");
        if (reservation.SettledAtUtc.HasValue) EnsureUtc(reservation.SettledAtUtc.Value, nameof(reservation.SettledAtUtc));
        if (reservation.State == AiReservationState.Reserved &&
            (reservation.ActualCost.HasValue || reservation.SettledAtUtc.HasValue))
            throw new InvalidOperationException("Offene Reservation besitzt Settlement-Daten.");
        if (reservation.State != AiReservationState.Reserved && !reservation.SettledAtUtc.HasValue)
            throw new InvalidOperationException("Terminale Reservation besitzt keinen Settlement-Zeitpunkt.");
        if (reservation.State == AiReservationState.Committed && !reservation.ActualCost.HasValue)
            throw new InvalidOperationException("Commit besitzt keine tatsächlichen Kosten.");
        if (reservation.State != AiReservationState.Committed && reservation.ActualCost.HasValue)
            throw new InvalidOperationException("Nicht-Commit besitzt tatsächliche Kosten.");
        if (reservation.BudgetIds is null)
            throw new InvalidOperationException("Reservation besitzt keine Budgetliste.");
    }

    private static void EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException($"{field} ist nicht UTC.");
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
}
