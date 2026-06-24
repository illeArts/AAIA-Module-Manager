using System.Buffers.Binary;
using System.Text;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9StateCodecTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SnapshotEncoding_IsDeterministicAndRoundTrips()
    {
        var first = AiRuntimeStateCodec.CreateSnapshot(42, Now, new byte[] { 1, 2, 3 });
        var second = AiRuntimeStateCodec.CreateSnapshot(42, Now, new byte[] { 1, 2, 3 });

        var firstFrame = AiRuntimeStateCodec.SerializeSnapshot(first);
        var secondFrame = AiRuntimeStateCodec.SerializeSnapshot(second);
        var restored = AiRuntimeStateCodec.DeserializeSnapshot(firstFrame);

        Assert.Equal(first.ChecksumSha256, second.ChecksumSha256);
        Assert.Equal(firstFrame, secondFrame);
        Assert.Equal(42, restored.Sequence);
        Assert.Equal(Now, restored.CreatedAtUtc);
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.Payload);
        Assert.Equal(first.ChecksumSha256, restored.ChecksumSha256);
    }

    [Fact]
    public void JournalEncoding_IsDeterministicAndPreservesProtectionFlag()
    {
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            7, "operation-1", "execution.queued", Now, true, new byte[] { 4, 5 });

        var frame = AiRuntimeStateCodec.SerializeJournalEntry(entry);
        var restored = AiRuntimeStateCodec.DeserializeJournalEntry(frame,
            new HashSet<string>(StringComparer.Ordinal) { "execution.queued" });

        Assert.Equal(7, restored.Sequence);
        Assert.Equal("operation-1", restored.OperationId);
        Assert.Equal("execution.queued", restored.EventType);
        Assert.True(restored.IsProtected);
        Assert.Equal(new byte[] { 4, 5 }, restored.Payload);
    }

    [Fact]
    public void SnapshotPayloadTampering_IsDetected()
    {
        var frame = AiRuntimeStateCodec.SerializeSnapshot(
            AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[] { 1, 2, 3 }));
        frame[^33] ^= 0x01;

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeSnapshot(frame));

        Assert.Equal(AiRuntimeStateReasonCodes.SnapshotCorrupt, error.ReasonCode);
    }

    [Fact]
    public void JournalHeaderTampering_IsDetected()
    {
        var frame = AiRuntimeStateCodec.SerializeJournalEntry(
            AiRuntimeStateCodec.CreateJournalEntry(1, "op", "test.event", Now, false, Array.Empty<byte>()));
        frame[17] ^= 0x01;

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeJournalEntry(frame));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalChecksumFailed, error.ReasonCode);
    }

    [Fact]
    public void EveryTruncatedSnapshotFrame_IsRejected()
    {
        var frame = AiRuntimeStateCodec.SerializeSnapshot(
            AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[] { 1, 2, 3 }));

        for (var length = 0; length < frame.Length; length++)
            Assert.Throws<AiStateStoreException>(() =>
                AiRuntimeStateCodec.DeserializeSnapshot(frame.AsSpan(0, length)));
    }

    [Fact]
    public void TrailingBytes_AreRejected()
    {
        var frame = AiRuntimeStateCodec.SerializeSnapshot(
            AiRuntimeStateCodec.CreateSnapshot(1, Now, Array.Empty<byte>()));
        Array.Resize(ref frame, frame.Length + 1);

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeSnapshot(frame));

        Assert.Equal(AiRuntimeStateReasonCodes.SnapshotCorrupt, error.ReasonCode);
    }

    [Fact]
    public void DeclaredOversizedPayload_IsRejectedBeforeAllocation()
    {
        var frame = AiRuntimeStateCodec.SerializeSnapshot(
            AiRuntimeStateCodec.CreateSnapshot(1, Now, Array.Empty<byte>()));
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(26, sizeof(int)), int.MaxValue);

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeSnapshot(frame, maxPayloadBytes: 1024));

        Assert.Equal(AiRuntimeStateReasonCodes.QuotaExceeded, error.ReasonCode);
    }

    [Fact]
    public void RecordLimit_IsEnforcedBeforeDecode()
    {
        var frame = AiRuntimeStateCodec.SerializeSnapshot(
            AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[32]));

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeSnapshot(frame, maxRecordBytes: frame.Length - 1));

        Assert.Equal(AiRuntimeStateReasonCodes.QuotaExceeded, error.ReasonCode);
    }

    [Fact]
    public void UnsupportedSchema_IsRejected()
    {
        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.CreateSnapshot(1, Now, Array.Empty<byte>(), schemaVersion: 2));

        Assert.Equal(AiRuntimeStateReasonCodes.SchemaUnsupported, error.ReasonCode);
    }

    [Fact]
    public void UnknownJournalEvent_IsRejectedAfterChecksumValidation()
    {
        var frame = AiRuntimeStateCodec.SerializeJournalEntry(
            AiRuntimeStateCodec.CreateJournalEntry(1, "op", "unknown.event", Now, false, Array.Empty<byte>()));

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeJournalEntry(frame,
                new HashSet<string>(StringComparer.Ordinal) { "known.event" }));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalEventUnknown, error.ReasonCode);
    }

    [Fact]
    public void InvalidUnicodeIdentifier_IsRejected()
    {
        var invalid = "operation-\uD800";

        Assert.Throws<EncoderFallbackException>(() =>
            AiRuntimeStateCodec.CreateJournalEntry(1, invalid, "test.event", Now, false, Array.Empty<byte>()));
    }

    [Fact]
    public void OversizedIdentifier_IsRejected()
    {
        var oversized = new string('x', AiRuntimeStateCodec.MaxIdentifierUtf8Bytes + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AiRuntimeStateCodec.CreateJournalEntry(1, oversized, "test.event", Now, false, Array.Empty<byte>()));
    }

    [Fact]
    public void SnapshotAndJournalMagic_AreNotInterchangeable()
    {
        var journal = AiRuntimeStateCodec.SerializeJournalEntry(
            AiRuntimeStateCodec.CreateJournalEntry(1, "op", "test.event", Now, false, Array.Empty<byte>()));

        var error = Assert.Throws<AiStateStoreException>(() =>
            AiRuntimeStateCodec.DeserializeSnapshot(journal));

        Assert.Equal(AiRuntimeStateReasonCodes.SnapshotCorrupt, error.ReasonCode);
    }

    [Fact]
    public async Task InMemoryStore_RejectsStructurallyValidEntryWithWrongChecksum()
    {
        var valid = AiRuntimeStateCodec.CreateJournalEntry(1, "op", "test.event", Now, false, Array.Empty<byte>());
        var invalid = new AiRuntimeJournalEntry
        {
            Sequence = valid.Sequence,
            OperationId = valid.OperationId,
            EventType = valid.EventType,
            OccurredAtUtc = valid.OccurredAtUtc,
            Payload = valid.Payload,
            ChecksumSha256 = new string('a', 64)
        };
        var store = new AiInMemoryRuntimeStateStore(new AiInMemoryRuntimeStateStoreBackend());
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.AppendJournalAsync(invalid));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalChecksumFailed, error.ReasonCode);
        Assert.Empty(await ReadAll(session));
    }

    [Fact]
    public void ChecksumHex_IsCanonicalLowercase()
    {
        var snapshot = AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[] { 1 });

        Assert.Equal(snapshot.ChecksumSha256.ToLowerInvariant(), snapshot.ChecksumSha256);
        Assert.Equal(64, snapshot.ChecksumSha256.Length);
    }

    private static async Task<IReadOnlyList<AiRuntimeJournalEntry>> ReadAll(
        IAiRuntimeStateStoreSession session)
    {
        var entries = new List<AiRuntimeJournalEntry>();
        await foreach (var entry in session.ReadJournalAsync(0)) entries.Add(entry);
        return entries;
    }
}
