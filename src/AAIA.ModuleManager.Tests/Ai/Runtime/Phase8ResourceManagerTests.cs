using System.Collections.Concurrent;
using AAIA.Air.Contracts;
using AAIA.Air.Resources;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase8ResourceManagerTests
{
    [Fact]
    public void MissingCapability_IsHardRejected()
    {
        var c = CreateContext();
        Register(c, Profile("r1", capabilities: new[] { "files" }));

        var decision = c.Manager.SelectAndReserve(Request(capabilities: new[] { "vision" }));

        Assert.Equal(AiResourceDecisionStatus.Denied, decision.Status);
        Assert.Contains(decision.Rejections, r => r.ReasonCode == AiResourceReasonCodes.NoMatchingResource);
    }

    [Fact]
    public void UnknownMinimumCapacity_IsNotUnlimited()
    {
        var c = CreateContext();
        Register(c, Profile("r1", contextTokens: null));

        var decision = c.Manager.SelectAndReserve(Request(minimumContext: 10_000));

        Assert.Equal(AiResourceDecisionStatus.Denied, decision.Status);
        Assert.Contains(decision.Rejections, r => r.ReasonCode == AiResourceReasonCodes.CapacityUnavailable);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void DisabledUnhealthyOrThrottled_IsExcluded(bool enabled, bool healthy, bool throttled)
    {
        var c = CreateContext();
        Register(c, Profile("r1", enabled: enabled), healthy, throttled);

        var decision = c.Manager.SelectAndReserve(Request());

        Assert.Equal(AiResourceDecisionStatus.Denied, decision.Status);
    }

    [Fact]
    public void PinnedResource_DoesNotFallback()
    {
        var c = CreateContext();
        Register(c, Profile("healthy"));
        Register(c, Profile("pinned", enabled: false));

        var decision = c.Manager.SelectAndReserve(Request(pinned: "pinned"));

        Assert.Equal(AiResourceReasonCodes.PinnedResourceUnavailable, decision.ReasonCode);
        Assert.Null(decision.SelectedResourceId);
    }

    [Fact]
    public void StaleTelemetry_IsRetryableDenial()
    {
        var c = CreateContext();
        c.Manager.Registry.Register(Profile("r1"));
        c.Manager.Registry.UpdateTelemetry(Telemetry("r1", c.Time.UtcNow.UtcDateTime.AddMinutes(-3)));

        var decision = c.Manager.SelectAndReserve(Request());

        Assert.True(decision.Retryable);
        Assert.Contains(decision.Rejections, r => r.ReasonCode == AiResourceReasonCodes.TelemetryStale);
    }

    [Fact]
    public void ConcurrentSlotLimit_IsExact()
    {
        var c = CreateContext();
        Register(c, Profile("r1", slots: 1));

        var first = c.Manager.SelectAndReserve(Request(execution: "e1"));
        var second = c.Manager.SelectAndReserve(Request(execution: "e2"));

        Assert.Equal(AiResourceDecisionStatus.Selected, first.Status);
        Assert.Equal(AiResourceReasonCodes.CapacityUnavailable, second.ReasonCode);
    }

    [Fact]
    public void ParallelReservations_CannotDoubleBookLastSlot()
    {
        var c = CreateContext();
        Register(c, Profile("r1", slots: 1));
        var decisions = new ConcurrentBag<AiResourceDecision>();

        Parallel.For(0, 20, i => decisions.Add(c.Manager.SelectAndReserve(Request(execution: $"e{i}"))));

        Assert.Single(decisions, d => d.Status == AiResourceDecisionStatus.Selected);
    }

    [Fact]
    public void ReleaseAndExpiry_FreeCapacityExactlyOnce()
    {
        var c = CreateContext();
        Register(c, Profile("r1", slots: 1));
        var first = c.Manager.SelectAndReserve(Request(execution: "e1", duration: TimeSpan.FromMinutes(1)));
        Assert.True(c.Manager.Release(first.Reservation!.Id, out _));
        Assert.True(c.Manager.Release(first.Reservation.Id, out _));
        Assert.Equal(AiResourceDecisionStatus.Selected,
            c.Manager.SelectAndReserve(Request(execution: "e2", duration: TimeSpan.FromMinutes(1))).Status);
        c.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, c.Manager.ExpireReservations());
        Assert.Equal(0, c.Manager.ExpireReservations());
        Assert.Equal(AiResourceDecisionStatus.Selected,
            c.Manager.SelectAndReserve(Request(execution: "e3")).Status);
    }

    [Fact]
    public void BudgetCountsSpentAndReserved()
    {
        var c = CreateContext();
        Register(c, Profile("r1", slots: 3, fixedCost: 4));
        c.Manager.SetBudget(Budget(c, hardLimit: 10));

        var first = c.Manager.SelectAndReserve(Request(execution: "e1"));
        Assert.True(c.Manager.Commit(first.Reservation!.Id, 5, out _));
        Assert.Equal(AiResourceDecisionStatus.Selected,
            c.Manager.SelectAndReserve(Request(execution: "e2")).Status);
        var denied = c.Manager.SelectAndReserve(Request(execution: "e3"));

        Assert.Equal(AiResourceReasonCodes.BudgetExceeded, denied.ReasonCode);
        var budget = Assert.Single(c.Manager.ListBudgets());
        Assert.Equal(5, budget.Spent);
        Assert.Equal(4, budget.Reserved);
    }

    [Fact]
    public void ParallelBudgetReservations_CannotExceedLimit()
    {
        var c = CreateContext();
        Register(c, Profile("r1", slots: 20, fixedCost: 3));
        c.Manager.SetBudget(Budget(c, hardLimit: 10));
        var decisions = new ConcurrentBag<AiResourceDecision>();

        Parallel.For(0, 20, i => decisions.Add(c.Manager.SelectAndReserve(Request(execution: $"e{i}"))));

        Assert.Equal(3, decisions.Count(d => d.Status == AiResourceDecisionStatus.Selected));
        Assert.Equal(9, Assert.Single(c.Manager.ListBudgets()).Reserved);
    }

    [Fact]
    public void CostUnitMismatch_IsRejected()
    {
        var c = CreateContext();
        Register(c, Profile("r1", costUnit: "USD", fixedCost: 1));

        var decision = c.Manager.SelectAndReserve(Request(costUnit: "EUR"));

        Assert.Equal(AiResourceReasonCodes.CostUnitMismatch, decision.ReasonCode);
    }

    [Fact]
    public void Commit_IsIdempotentAndChargesOnce()
    {
        var c = CreateContext();
        Register(c, Profile("r1", fixedCost: 2));
        c.Manager.SetBudget(Budget(c, hardLimit: 20));
        var selected = c.Manager.SelectAndReserve(Request());

        Assert.True(c.Manager.Commit(selected.Reservation!.Id, 3, out _));
        Assert.True(c.Manager.Commit(selected.Reservation.Id, 3, out _));

        var budget = Assert.Single(c.Manager.ListBudgets());
        Assert.Equal(3, budget.Spent);
        Assert.Equal(0, budget.Reserved);
    }

    [Fact]
    public void Ranking_IsDeterministicAndIgnoresDisplayName()
    {
        var c = CreateContext();
        Register(c, Profile("a", displayName: "Vendor Z", locality: AiResourceLocality.Remote, fixedCost: 1));
        Register(c, Profile("b", displayName: "Vendor A", locality: AiResourceLocality.Local, fixedCost: 10));

        var first = c.Manager.SelectAndReserve(Request(execution: "e1"));
        Assert.Equal("a", first.SelectedResourceId); // cost weight beats locality weight
        Assert.True(c.Manager.Release(first.Reservation!.Id, out _));
        var second = c.Manager.SelectAndReserve(Request(execution: "e2"));
        Assert.Equal(first.SelectedResourceId, second.SelectedResourceId);
    }

    [Fact]
    public void StableTieBreak_UsesResourceId()
    {
        var c = CreateContext();
        Register(c, Profile("b", displayName: "First Name"));
        Register(c, Profile("a", displayName: "Second Name"));

        var decision = c.Manager.SelectAndReserve(Request());

        Assert.Equal("a", decision.SelectedResourceId);
    }

    [Fact]
    public void AvailablePinnedResource_IsSelectedExactly()
    {
        var c = CreateContext();
        Register(c, Profile("a"));
        Register(c, Profile("b"));

        var decision = c.Manager.SelectAndReserve(Request(pinned: "b"));

        Assert.Equal("b", decision.SelectedResourceId);
    }

    [Fact]
    public void CostEstimate_CombinesAllConfiguredRates()
    {
        var c = CreateContext();
        Register(c, Profile("r1", fixedCost: 1, inputRate: 2, outputRate: 4, workRate: 3));
        var request = Request();
        request = new AiResourceRequest
        {
            ExecutionRequestId = request.ExecutionRequestId,
            TaskId = request.TaskId,
            ProjectId = request.ProjectId,
            SessionId = request.SessionId,
            Requirements = new AiResourceRequirements
            {
                Kind = AiResourceKind.Inference,
                CostUnit = "EUR",
                EstimatedInputUnits = 1_000,
                EstimatedOutputUnits = 500,
                EstimatedWorkUnits = 2
            }
        };

        var decision = c.Manager.SelectAndReserve(request);

        Assert.Equal(11, decision.Reservation!.EstimatedCost);
    }

    [Fact]
    public void RuntimeAndProjectBudgets_AreReservedTogether()
    {
        var c = CreateContext();
        Register(c, Profile("r1", fixedCost: 2));
        c.Manager.SetBudget(Budget(c, 10));
        c.Manager.SetBudget(new AiResourceBudget
        {
            Scope = AiBudgetScope.Project,
            ScopeId = "project",
            CostUnit = "EUR",
            Window = AiBudgetWindow.Day,
            HardLimit = 5,
            WindowStartsAtUtc = c.Time.UtcNow.UtcDateTime.AddHours(-1),
            WindowEndsAtUtc = c.Time.UtcNow.UtcDateTime.AddHours(23)
        });

        Assert.Equal(AiResourceDecisionStatus.Selected, c.Manager.SelectAndReserve(Request()).Status);
        Assert.All(c.Manager.ListBudgets(), budget => Assert.Equal(2, budget.Reserved));
    }

    [Fact]
    public void Release_RollsBackEveryBudgetReservation()
    {
        var c = CreateContext();
        Register(c, Profile("r1", fixedCost: 2));
        c.Manager.SetBudget(Budget(c, 10));
        var selected = c.Manager.SelectAndReserve(Request());

        Assert.True(c.Manager.Release(selected.Reservation!.Id, out _));

        var budget = Assert.Single(c.Manager.ListBudgets());
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(0, budget.Spent);
    }

    [Fact]
    public void Registry_RejectsInvalidKnownCapacity()
    {
        var c = CreateContext();
        var invalid = Profile("r1", slots: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => c.Manager.Registry.Register(invalid));
    }

    private static TestContext CreateContext()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 6, 23, 18, 0, 0, TimeSpan.Zero));
        return new TestContext(new AiResourceManager(time), time);
    }

    private static void Register(TestContext c, AiResourceProfile profile, bool healthy = true, bool throttled = false)
    {
        c.Manager.Registry.Register(profile);
        c.Manager.Registry.UpdateTelemetry(Telemetry(profile.ResourceId, c.Time.UtcNow.UtcDateTime, healthy, throttled));
    }

    private static AiResourceProfile Profile(
        string id,
        bool enabled = true,
        int? slots = 5,
        int? contextTokens = 100_000,
        string[]? capabilities = null,
        string displayName = "resource",
        AiResourceLocality locality = AiResourceLocality.Remote,
        string costUnit = "EUR",
        decimal fixedCost = 0,
        decimal inputRate = 0,
        decimal outputRate = 0,
        decimal workRate = 0)
        => new()
        {
            ResourceId = id,
            ProviderId = "test-provider",
            DisplayName = displayName,
            Kind = AiResourceKind.Inference,
            Enabled = enabled,
            Locality = locality,
            Capabilities = capabilities ?? Array.Empty<string>(),
            Capacity = new AiResourceCapacity
            {
                MaxConcurrentExecutions = slots,
                ContextWindowTokens = contextTokens,
                RequestsPerMinute = 100,
                TokensPerMinute = 1_000_000
            },
            CostRate = new AiResourceCostRate
            {
                CostUnit = costUnit,
                FixedPerExecution = fixedCost,
                PerThousandInputUnits = inputRate,
                PerThousandOutputUnits = outputRate,
                PerWorkUnit = workRate
            }
        };

    private static AiResourceTelemetry Telemetry(
        string id, DateTime observedAt, bool healthy = true, bool throttled = false)
        => new()
        {
            ResourceId = id,
            ObservedAtUtc = observedAt,
            Healthy = healthy,
            Throttled = throttled,
            P95ExecutionLatencyMs = 100,
            FailureRate = 0
        };

    private static AiResourceRequest Request(
        string execution = "execution",
        string[]? capabilities = null,
        int? minimumContext = null,
        string? pinned = null,
        string costUnit = "EUR",
        TimeSpan? duration = null)
        => new()
        {
            ExecutionRequestId = execution,
            TaskId = $"task-{execution}",
            ProjectId = "project",
            SessionId = "session",
            Requirements = new AiResourceRequirements
            {
                Kind = AiResourceKind.Inference,
                RequiredCapabilities = capabilities ?? Array.Empty<string>(),
                MinimumContextTokens = minimumContext,
                CostUnit = costUnit,
                PinnedResourceId = pinned,
                ReservationDuration = duration ?? TimeSpan.FromMinutes(5)
            }
        };

    private static AiResourceBudget Budget(TestContext c, decimal hardLimit)
        => new()
        {
            Scope = AiBudgetScope.Runtime,
            CostUnit = "EUR",
            Window = AiBudgetWindow.Day,
            HardLimit = hardLimit,
            WindowStartsAtUtc = c.Time.UtcNow.UtcDateTime.AddHours(-1),
            WindowEndsAtUtc = c.Time.UtcNow.UtcDateTime.AddHours(23)
        };

    private sealed record TestContext(AiResourceManager Manager, ManualTimeProvider Time);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
