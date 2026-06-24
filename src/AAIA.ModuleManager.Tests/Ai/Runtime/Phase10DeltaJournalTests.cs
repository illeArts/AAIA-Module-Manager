using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase10DeltaJournalTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Registry_CoversEveryMutationExactlyOnceWithoutSnapshotPayload()
    {
        var enumValues = Enum.GetValues<AiDurableMutationType>();

        Assert.Equal(enumValues.Length, AiDurableMutationRegistry.All.Count);
        Assert.Equal(enumValues.Length,
            AiDurableMutationRegistry.All.Select(item => item.EventType).Distinct(StringComparer.Ordinal).Count());
        Assert.All(enumValues, value => Assert.Equal(value, AiDurableMutationRegistry.Get(value).MutationType));
        Assert.DoesNotContain(AiDurableMutationRegistry.All,
            item => item.PayloadType == typeof(AiDurableOrchestrationSnapshot));
    }

    [Fact]
    public void EveryRegisteredPayload_RoundTripsThroughJournalCodec()
    {
        long sequence = 0;
        foreach (var registration in AiDurableMutationRegistry.All)
        {
            var envelope = AiDurableMutationCodec.CreateEnvelope(
                ++sequence, $"operation-{sequence}", registration.MutationType, Now,
                SamplePayload(registration.PayloadType));
            var journal = AiDurableMutationCodec.ToJournalEntry(envelope);
            var restored = AiDurableMutationCodec.FromJournalEntry(journal);

            Assert.Equal(registration.EventType, journal.EventType);
            Assert.Equal(envelope.MutationType, restored.MutationType);
            Assert.Equal(envelope.PayloadChecksumSha256, restored.PayloadChecksumSha256);
            Assert.Equal(envelope.Payload, restored.Payload);
        }
    }

    [Fact]
    public void UnknownJournalEvent_IsRejectedFailClosed()
    {
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            1, "operation", "air.mutation.unknown", Now, false, Encoding.UTF8.GetBytes("{}"));

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiDurableMutationCodec.FromJournalEntry(entry));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalEventUnknown, error.ReasonCode);
    }

    [Fact]
    public void JournalHeaderAndEnvelopeMismatch_IsRejected()
    {
        var envelope = Envelope(1, "operation", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("task") });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            1, "operation", AiDurableMutationRegistry.Get(AiDurableMutationType.TaskSettled).EventType,
            Now, false, bytes);

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiDurableMutationCodec.FromJournalEntry(entry));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalCorrupt, error.ReasonCode);
    }

    [Fact]
    public void TamperedDeltaPayloadChecksum_IsRejectedBeforeApply()
    {
        var original = Envelope(1, "operation", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("task") });
        var tampered = new AiDurableMutationEnvelope
        {
            Sequence = original.Sequence,
            OperationId = original.OperationId,
            MutationType = original.MutationType,
            OccurredAtUtc = original.OccurredAtUtc,
            Payload = original.Payload.Concat(new byte[] { 1 }).ToArray(),
            PayloadChecksumSha256 = original.PayloadChecksumSha256
        };

        var error = Assert.Throws<AiStateStoreException>(() =>
            new AiDurableMutationReducer().Apply(tampered));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalChecksumFailed, error.ReasonCode);
    }

    [Fact]
    public void MissingDeltaPayloadChecksum_IsRejectedFailClosed()
    {
        var original = Envelope(1, "operation", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("task") });
        var corrupt = new AiDurableMutationEnvelope
        {
            Sequence = original.Sequence,
            OperationId = original.OperationId,
            MutationType = original.MutationType,
            OccurredAtUtc = original.OccurredAtUtc,
            Payload = original.Payload,
            PayloadChecksumSha256 = null!
        };

        var error = Assert.Throws<AiStateStoreException>(() =>
            new AiDurableMutationReducer().Apply(corrupt));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalChecksumFailed, error.ReasonCode);
    }

    [Fact]
    public void Reducer_ReplaysTaskAndExecutionLifecycleDeterministically()
    {
        var journal = new AiInMemoryDurableMutationJournal();
        journal.Append("task-create", AiDurableMutationType.TaskCreated, Now,
            new AiTaskMutationPayload { Task = TaskSnapshot("task", AiTaskStatus.Pending) });
        journal.Append("task-claim", AiDurableMutationType.TaskClaimed, Now.AddSeconds(1),
            new AiTaskMutationPayload { Task = TaskSnapshot("task", AiTaskStatus.Claimed) });
        journal.Append("execution-queue", AiDurableMutationType.ExecutionQueued, Now.AddSeconds(2),
            new AiExecutionMutationPayload
            {
                Execution = ExecutionSnapshot("execution", "task", AiExecutionState.Queued)
            });
        journal.Append("execution-state", AiDurableMutationType.ExecutionStateChanged, Now.AddSeconds(3),
            new AiExecutionMutationPayload
            {
                Execution = ExecutionSnapshot("execution", "task", AiExecutionState.Completed)
            });
        journal.Append("task-settle", AiDurableMutationType.TaskSettled, Now.AddSeconds(4),
            new AiTaskMutationPayload { Task = TaskSnapshot("task", AiTaskStatus.Completed) });

        var expected = journal.CreateSnapshot(Now.AddMinutes(1));
        var replayed = AiInMemoryDurableMutationJournal.Replay(null, 0, journal.Entries())
            .CreateSnapshot(Now.AddMinutes(1));

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(replayed));
        Assert.Equal(AiTaskStatus.Completed, Assert.Single(replayed.Tasks).Status);
        Assert.Equal(AiExecutionState.Completed, Assert.Single(replayed.Executions).State);
    }

    [Fact]
    public void IdenticalOperationId_IsAppliedExactlyOnceButAdvancesSequence()
    {
        var reducer = new AiDurableMutationReducer();
        var payload = new AiTaskMutationPayload { Task = TaskSnapshot("task") };
        var first = Envelope(1, "same-operation", AiDurableMutationType.TaskCreated, payload);
        var duplicate = Envelope(2, "same-operation", AiDurableMutationType.TaskCreated, payload);

        Assert.True(reducer.Apply(first));
        Assert.False(reducer.Apply(duplicate));

        var snapshot = reducer.CreateSnapshot(Now);
        Assert.Single(snapshot.Tasks);
        Assert.Single(snapshot.AppliedOperations);
        Assert.Equal(2, reducer.LastSequence);
    }

    [Fact]
    public void ReusedOperationIdWithDifferentPayload_IsConflictWithoutPartialApply()
    {
        var reducer = new AiDurableMutationReducer();
        reducer.Apply(Envelope(1, "same-operation", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("task-a") }));

        var error = Assert.Throws<AiStateStoreException>(() => reducer.Apply(
            Envelope(2, "same-operation", AiDurableMutationType.TaskCreated,
                new AiTaskMutationPayload { Task = TaskSnapshot("task-b") })));

        Assert.Equal(AiRuntimeStateReasonCodes.OperationConflict, error.ReasonCode);
        Assert.Equal("task-a", Assert.Single(reducer.CreateSnapshot(Now).Tasks).Id);
        Assert.Equal(1, reducer.LastSequence);
    }

    [Fact]
    public void SequenceGap_IsRejectedWithoutMutation()
    {
        var reducer = new AiDurableMutationReducer();

        var error = Assert.Throws<AiStateStoreException>(() => reducer.Apply(
            Envelope(2, "operation", AiDurableMutationType.TaskCreated,
                new AiTaskMutationPayload { Task = TaskSnapshot("task") })));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalGap, error.ReasonCode);
        Assert.Empty(reducer.CreateSnapshot(Now).Tasks);
        Assert.Equal(0, reducer.LastSequence);
    }

    [Fact]
    public void Phase9Snapshot_RemainsValidBaseForDeltaReplay()
    {
        var phase9 = new AiDurableOrchestrationSnapshot
        {
            CreatedAtUtc = Now,
            Tasks = new[] { TaskSnapshot("existing") }
        };
        var reducer = new AiDurableMutationReducer(phase9, snapshotSequence: 7);

        reducer.Apply(Envelope(8, "new-task", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("new") }));

        var snapshot = reducer.CreateSnapshot(Now.AddMinutes(1));
        Assert.Equal(new[] { "existing", "new" }, snapshot.Tasks.Select(item => item.Id).ToArray());
        Assert.Single(snapshot.AppliedOperations);
    }

    [Fact]
    public void CorruptAppliedOperationInBaseSnapshot_IsRejected()
    {
        var phase10 = new AiDurableOrchestrationSnapshot
        {
            CreatedAtUtc = Now,
            AppliedOperations = new[]
            {
                new AiDurableAppliedOperation
                {
                    OperationId = "operation",
                    MutationType = AiDurableMutationType.TaskCreated,
                    PayloadChecksumSha256 = "not-a-checksum",
                    AppliedAtUtc = Now
                }
            }
        };

        var error = Assert.Throws<AiStateStoreException>(() =>
            new AiDurableMutationReducer(phase10));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalCorrupt, error.ReasonCode);
    }

    [Fact]
    public void ResourceIdempotencyAndAuditDeltas_UpdateOnlyTheirOwnCollections()
    {
        var journal = new AiInMemoryDurableMutationJournal();
        journal.Append("budget", AiDurableMutationType.BudgetCreated, Now,
            new AiBudgetMutationPayload { Budget = BudgetSnapshot("budget") });
        journal.Append("reservation", AiDurableMutationType.ReservationCreated, Now.AddSeconds(1),
            new AiReservationMutationPayload
            {
                Reservation = ReservationSnapshot("reservation", AiReservationState.Reserved)
            });
        journal.Append("reservation-release", AiDurableMutationType.ReservationReleased, Now.AddSeconds(2),
            new AiReservationMutationPayload
            {
                Reservation = ReservationSnapshot("reservation", AiReservationState.Released)
            });
        journal.Append("idempotency", AiDurableMutationType.IdempotencyStored, Now.AddSeconds(3),
            new AiIdempotencyStoredPayload { Record = IdempotencyRecord() });
        journal.Append("audit", AiDurableMutationType.AuditRecorded, Now.AddSeconds(4),
            new AiAuditMutationPayload { Entry = AuditEntry() });

        var snapshot = journal.CreateSnapshot(Now.AddMinutes(1));
        Assert.Empty(snapshot.Tasks);
        Assert.Empty(snapshot.Executions);
        Assert.Single(snapshot.Budgets);
        Assert.Equal(AiReservationState.Released, Assert.Single(snapshot.Reservations).State);
        Assert.Single(snapshot.IdempotencyRecords);
        Assert.Single(snapshot.AuditEntries);
    }

    [Fact]
    public async Task ParallelAppends_AssignUniqueGaplessSequences()
    {
        var journal = new AiInMemoryDurableMutationJournal();
        var envelopes = new ConcurrentBag<AiDurableMutationEnvelope>();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(client => Task.Run(() =>
        {
            for (var index = 0; index < 20; index++)
            {
                var id = $"task-{client:D2}-{index:D2}";
                envelopes.Add(journal.Append(id, AiDurableMutationType.TaskCreated, Now,
                    new AiTaskMutationPayload { Task = TaskSnapshot(id) }));
            }
        })));

        var sequences = envelopes.Select(item => item.Sequence).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, 320).Select(value => (long)value), sequences);
        Assert.Equal(320, journal.LastSequence);
        Assert.Equal(320, journal.CreateSnapshot(Now).Tasks.Count);
    }

    [Fact]
    public void DeltaEvent_IsSmallerThanEquivalentLargeOrchestrationSnapshot()
    {
        var tasks = Enumerable.Range(0, 100).Select(index => TaskSnapshot($"task-{index:D3}")).ToArray();
        var fullSnapshot = new AiDurableOrchestrationSnapshot { CreatedAtUtc = Now, Tasks = tasks };
        var delta = Envelope(1, "new-task", AiDurableMutationType.TaskCreated,
            new AiTaskMutationPayload { Task = TaskSnapshot("task-new") });
        var journalBytes = AiDurableMutationCodec.ToJournalEntry(delta).Payload;

        Assert.True(journalBytes.Length < JsonSerializer.SerializeToUtf8Bytes(fullSnapshot).Length / 10);
        Assert.DoesNotContain("task-099", Encoding.UTF8.GetString(journalBytes), StringComparison.Ordinal);
    }

    private static AiDurableMutationEnvelope Envelope(
        long sequence, string operationId, AiDurableMutationType type, object payload)
        => AiDurableMutationCodec.CreateEnvelope(sequence, operationId, type, Now, payload);

    private static object SamplePayload(Type type)
    {
        if (type == typeof(AiTaskMutationPayload))
            return new AiTaskMutationPayload { Task = TaskSnapshot("task") };
        if (type == typeof(AiExecutionMutationPayload))
            return new AiExecutionMutationPayload { Execution = ExecutionSnapshot("execution", "task", AiExecutionState.Queued) };
        if (type == typeof(AiBudgetMutationPayload))
            return new AiBudgetMutationPayload { Budget = BudgetSnapshot("budget") };
        if (type == typeof(AiReservationMutationPayload))
            return new AiReservationMutationPayload { Reservation = ReservationSnapshot("reservation", AiReservationState.Reserved) };
        if (type == typeof(AiIdempotencyStoredPayload))
            return new AiIdempotencyStoredPayload { Record = IdempotencyRecord() };
        if (type == typeof(AiIdempotencyEvictedPayload))
            return new AiIdempotencyEvictedPayload { ClientFingerprint = "client", Operation = "op", IdempotencyId = "id" };
        if (type == typeof(AiAuditMutationPayload))
            return new AiAuditMutationPayload { Entry = AuditEntry() };
        if (type == typeof(AiRecoveryCheckpointPayload))
            return new AiRecoveryCheckpointPayload { RecoveryRequiredCount = 1 };
        throw new InvalidOperationException($"Kein Sample für {type.Name}.");
    }

    private static AiDurableTaskSnapshot TaskSnapshot(
        string id, AiTaskStatus status = AiTaskStatus.Pending) => new()
    {
        Id = id,
        Title = id,
        Status = status,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
        Steps = Array.Empty<AiDurableTaskStepSnapshot>()
    };

    private static AiDurableExecutionSnapshot ExecutionSnapshot(
        string id, string taskId, AiExecutionState state) => new()
    {
        Id = id,
        TaskId = taskId,
        EnqueuedAtUtc = Now,
        MaxAttempts = 3,
        State = state,
        UpdatedAtUtc = Now
    };

    private static AiDurableBudgetSnapshot BudgetSnapshot(string id) => new()
    {
        Budget = new AiResourceBudget
        {
            Id = id,
            Scope = AiBudgetScope.Runtime,
            CostUnit = "EUR",
            Window = AiBudgetWindow.Day,
            HardLimit = 10,
            WindowStartsAtUtc = Now,
            WindowEndsAtUtc = Now.AddDays(1)
        }
    };

    private static AiDurableReservationSnapshot ReservationSnapshot(
        string id, AiReservationState state) => new()
    {
        Id = id,
        ResourceId = "resource",
        ExecutionRequestId = "execution",
        TaskId = "task",
        State = state,
        CostUnit = "EUR",
        EstimatedCost = 2,
        ReservedAtUtc = Now,
        ExpiresAtUtc = Now.AddMinutes(5),
        SettledAtUtc = state == AiReservationState.Reserved ? null : Now.AddMinutes(1),
        SettlementReasonCode = state == AiReservationState.Released ? "manual" : null,
        BudgetIds = new[] { "budget" }
    };

    private static AiDurableIdempotencyRecord IdempotencyRecord() => new()
    {
        ClientFingerprint = "client",
        Operation = "op",
        IdempotencyId = "id",
        InputFingerprint = new string('A', 64),
        ResultId = "result",
        CreatedAtUtc = Now,
        ExpiresAtUtc = Now.AddHours(24)
    };

    private static AiDurableAuditEntry AuditEntry() => new()
    {
        TimestampUtc = Now,
        Actor = "owner",
        Action = "test",
        Success = true
    };
}
