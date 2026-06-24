namespace AAIA.Air.Persistence;

/// <summary>
/// Thread-sichere Referenzimplementierung für Sequenzvergabe, Registry-Codec und Reducer.
/// Sie ersetzt keinen State Store und dient als ausführbares Phase-10.1-Conformance-Modell.
/// </summary>
public sealed class AiInMemoryDurableMutationJournal
{
    private readonly object _gate = new();
    private readonly List<AiRuntimeJournalEntry> _entries = new();
    private readonly AiDurableMutationReducer _reducer;

    public AiInMemoryDurableMutationJournal(
        AiDurableOrchestrationSnapshot? snapshot = null,
        long snapshotSequence = 0)
    {
        _reducer = new AiDurableMutationReducer(snapshot, snapshotSequence);
    }

    public long LastSequence
    {
        get { lock (_gate) return _reducer.LastSequence; }
    }

    public AiDurableMutationEnvelope Append(
        string operationId,
        AiDurableMutationType mutationType,
        DateTime occurredAtUtc,
        object payload,
        string? actorFingerprint = null,
        string? inputFingerprint = null)
    {
        lock (_gate)
        {
            var envelope = AiDurableMutationCodec.CreateEnvelope(
                _reducer.LastSequence + 1,
                operationId,
                mutationType,
                occurredAtUtc,
                payload,
                actorFingerprint,
                inputFingerprint);
            var entry = AiDurableMutationCodec.ToJournalEntry(envelope);
            _reducer.Apply(envelope);
            _entries.Add(Clone(entry));
            return envelope;
        }
    }

    public IReadOnlyList<AiRuntimeJournalEntry> Entries()
    {
        lock (_gate) return _entries.Select(Clone).ToArray();
    }

    public AiDurableOrchestrationSnapshot CreateSnapshot(DateTime createdAtUtc)
    {
        lock (_gate) return _reducer.CreateSnapshot(createdAtUtc);
    }

    public static AiDurableMutationReducer Replay(
        AiDurableOrchestrationSnapshot? snapshot,
        long snapshotSequence,
        IEnumerable<AiRuntimeJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var reducer = new AiDurableMutationReducer(snapshot, snapshotSequence);
        foreach (var entry in entries.OrderBy(item => item.Sequence))
            reducer.Apply(AiDurableMutationCodec.FromJournalEntry(entry));
        return reducer;
    }

    private static AiRuntimeJournalEntry Clone(AiRuntimeJournalEntry entry) => new()
    {
        SchemaVersion = entry.SchemaVersion,
        Sequence = entry.Sequence,
        OperationId = entry.OperationId,
        EventType = entry.EventType,
        OccurredAtUtc = entry.OccurredAtUtc,
        IsProtected = entry.IsProtected,
        Payload = entry.Payload.ToArray(),
        ChecksumSha256 = entry.ChecksumSha256
    };
}
