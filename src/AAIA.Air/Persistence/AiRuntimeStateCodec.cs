using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AAIA.Air.Persistence;

/// <summary>
/// Kanonischer, versionierter Binär-Codec für Phase-9-Snapshots und Journal-Einträge.
/// Alle Ganzzahlen sind Big Endian; die SHA-256-Prüfsumme umfasst Header und Payload.
/// </summary>
public static class AiRuntimeStateCodec
{
    private static readonly byte[] SnapshotMagic = "AIRS"u8.ToArray();
    private static readonly byte[] JournalMagic = "AIRJ"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public const ushort FormatVersion = 1;
    public const int ChecksumBytes = 32;
    public const int DefaultMaxPayloadBytes = 1024 * 1024;
    public const int DefaultMaxRecordBytes = DefaultMaxPayloadBytes + 4096;
    public const int MaxIdentifierUtf8Bytes = 512;

    public static AiRuntimeStateSnapshot CreateSnapshot(
        long sequence,
        DateTime createdAtUtc,
        ReadOnlySpan<byte> payload,
        int schemaVersion = AiRuntimeStateSchema.CurrentVersion,
        int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ValidateCommon(schemaVersion, sequence, createdAtUtc, payload.Length, maxPayloadBytes);
        var payloadCopy = payload.ToArray();
        var body = SnapshotBody(schemaVersion, sequence, createdAtUtc, payloadCopy);
        return new AiRuntimeStateSnapshot
        {
            SchemaVersion = schemaVersion,
            Sequence = sequence,
            CreatedAtUtc = createdAtUtc,
            Payload = payloadCopy,
            ChecksumSha256 = HashHex(body)
        };
    }

    public static AiRuntimeJournalEntry CreateJournalEntry(
        long sequence,
        string operationId,
        string eventType,
        DateTime occurredAtUtc,
        bool isProtected,
        ReadOnlySpan<byte> payload,
        int schemaVersion = AiRuntimeStateSchema.CurrentVersion,
        int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ValidateCommon(schemaVersion, sequence, occurredAtUtc, payload.Length, maxPayloadBytes);
        var operationBytes = EncodeIdentifier(operationId, nameof(operationId));
        var eventBytes = EncodeIdentifier(eventType, nameof(eventType));
        var payloadCopy = payload.ToArray();
        var body = JournalBody(schemaVersion, sequence, occurredAtUtc, isProtected,
            operationBytes, eventBytes, payloadCopy);
        return new AiRuntimeJournalEntry
        {
            SchemaVersion = schemaVersion,
            Sequence = sequence,
            OperationId = operationId,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc,
            IsProtected = isProtected,
            Payload = payloadCopy,
            ChecksumSha256 = HashHex(body)
        };
    }

    public static byte[] SerializeSnapshot(
        AiRuntimeStateSnapshot snapshot,
        int maxPayloadBytes = DefaultMaxPayloadBytes,
        int maxRecordBytes = DefaultMaxRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var body = VerifySnapshot(snapshot, maxPayloadBytes);
        return CompleteFrame(body, snapshot.ChecksumSha256, maxRecordBytes);
    }

    public static byte[] SerializeJournalEntry(
        AiRuntimeJournalEntry entry,
        int maxPayloadBytes = DefaultMaxPayloadBytes,
        int maxRecordBytes = DefaultMaxRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var body = VerifyJournalEntry(entry, maxPayloadBytes);
        return CompleteFrame(body, entry.ChecksumSha256, maxRecordBytes);
    }

    public static AiRuntimeStateSnapshot DeserializeSnapshot(
        ReadOnlySpan<byte> frame,
        int maxPayloadBytes = DefaultMaxPayloadBytes,
        int maxRecordBytes = DefaultMaxRecordBytes)
    {
        ValidateLimits(maxPayloadBytes, maxRecordBytes);
        if (frame.Length > maxRecordBytes)
            throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "Snapshot überschreitet das Record-Limit.");

        try
        {
            var reader = new FrameReader(frame);
            reader.Expect(SnapshotMagic);
            ExpectFormatVersion(reader.ReadUInt16());
            var schema = reader.ReadInt32();
            ValidateSchema(schema);
            var sequence = reader.ReadInt64();
            if (sequence < 0) throw new FormatException("Snapshot-Sequenz ist negativ.");
            var created = ReadUtc(reader.ReadInt64());
            var payload = reader.ReadPayload(maxPayloadBytes);
            var checksum = reader.ReadBytes(ChecksumBytes).ToArray();
            reader.ExpectEnd();

            var bodyLength = frame.Length - ChecksumBytes;
            VerifyChecksum(frame[..bodyLength], checksum, AiRuntimeStateReasonCodes.SnapshotCorrupt);
            return new AiRuntimeStateSnapshot
            {
                SchemaVersion = schema,
                Sequence = sequence,
                CreatedAtUtc = created,
                Payload = payload,
                ChecksumSha256 = Convert.ToHexString(checksum).ToLowerInvariant()
            };
        }
        catch (AiStateStoreException) { throw; }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw Error(AiRuntimeStateReasonCodes.SnapshotCorrupt, "Snapshot-Format ist beschädigt.", ex);
        }
    }

    public static AiRuntimeJournalEntry DeserializeJournalEntry(
        ReadOnlySpan<byte> frame,
        IReadOnlySet<string>? knownEventTypes = null,
        int maxPayloadBytes = DefaultMaxPayloadBytes,
        int maxRecordBytes = DefaultMaxRecordBytes)
    {
        ValidateLimits(maxPayloadBytes, maxRecordBytes);
        if (frame.Length > maxRecordBytes)
            throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "Journal-Eintrag überschreitet das Record-Limit.");

        try
        {
            var reader = new FrameReader(frame);
            reader.Expect(JournalMagic);
            ExpectFormatVersion(reader.ReadUInt16());
            var schema = reader.ReadInt32();
            ValidateSchema(schema);
            var sequence = reader.ReadInt64();
            if (sequence < 0) throw new FormatException("Journal-Sequenz ist negativ.");
            var occurred = ReadUtc(reader.ReadInt64());
            var flags = reader.ReadByte();
            if ((flags & ~1) != 0) throw new FormatException("Unbekannte Journal-Flags.");
            var operationId = reader.ReadIdentifier();
            var eventType = reader.ReadIdentifier();
            var payload = reader.ReadPayload(maxPayloadBytes);
            var checksum = reader.ReadBytes(ChecksumBytes).ToArray();
            reader.ExpectEnd();

            var bodyLength = frame.Length - ChecksumBytes;
            VerifyChecksum(frame[..bodyLength], checksum, AiRuntimeStateReasonCodes.JournalChecksumFailed);
            if (knownEventTypes is not null && !knownEventTypes.Contains(eventType))
                throw Error(AiRuntimeStateReasonCodes.JournalEventUnknown,
                    $"Journal-Event-Typ '{eventType}' ist in diesem Schema nicht registriert.");

            return new AiRuntimeJournalEntry
            {
                SchemaVersion = schema,
                Sequence = sequence,
                OperationId = operationId,
                EventType = eventType,
                OccurredAtUtc = occurred,
                IsProtected = (flags & 1) != 0,
                Payload = payload,
                ChecksumSha256 = Convert.ToHexString(checksum).ToLowerInvariant()
            };
        }
        catch (AiStateStoreException) { throw; }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or DecoderFallbackException)
        {
            throw Error(AiRuntimeStateReasonCodes.JournalCorrupt, "Journal-Format ist beschädigt.", ex);
        }
    }

    public static byte[] VerifySnapshot(
        AiRuntimeStateSnapshot snapshot,
        int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = snapshot.Payload ?? throw new ArgumentNullException(nameof(snapshot.Payload));
        ValidateCommon(snapshot.SchemaVersion, snapshot.Sequence, snapshot.CreatedAtUtc,
            payload.Length, maxPayloadBytes);
        var body = SnapshotBody(snapshot.SchemaVersion, snapshot.Sequence, snapshot.CreatedAtUtc, payload);
        VerifyChecksum(body, ParseChecksum(snapshot.ChecksumSha256), AiRuntimeStateReasonCodes.SnapshotCorrupt);
        return body;
    }

    public static byte[] VerifyJournalEntry(
        AiRuntimeJournalEntry entry,
        int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var payload = entry.Payload ?? throw new ArgumentNullException(nameof(entry.Payload));
        ValidateCommon(entry.SchemaVersion, entry.Sequence, entry.OccurredAtUtc,
            payload.Length, maxPayloadBytes);
        var operationBytes = EncodeIdentifier(entry.OperationId, nameof(entry.OperationId));
        var eventBytes = EncodeIdentifier(entry.EventType, nameof(entry.EventType));
        var body = JournalBody(entry.SchemaVersion, entry.Sequence, entry.OccurredAtUtc,
            entry.IsProtected, operationBytes, eventBytes, payload);
        VerifyChecksum(body, ParseChecksum(entry.ChecksumSha256),
            AiRuntimeStateReasonCodes.JournalChecksumFailed);
        return body;
    }

    private static byte[] SnapshotBody(
        int schemaVersion,
        long sequence,
        DateTime createdAtUtc,
        byte[] payload)
    {
        using var stream = new MemoryStream(capacity: 30 + payload.Length);
        stream.Write(SnapshotMagic);
        WriteUInt16(stream, FormatVersion);
        WriteInt32(stream, schemaVersion);
        WriteInt64(stream, sequence);
        WriteInt64(stream, createdAtUtc.Ticks);
        WriteInt32(stream, payload.Length);
        stream.Write(payload);
        return stream.ToArray();
    }

    private static byte[] JournalBody(
        int schemaVersion,
        long sequence,
        DateTime occurredAtUtc,
        bool isProtected,
        byte[] operationId,
        byte[] eventType,
        byte[] payload)
    {
        using var stream = new MemoryStream(capacity: 35 + operationId.Length + eventType.Length + payload.Length);
        stream.Write(JournalMagic);
        WriteUInt16(stream, FormatVersion);
        WriteInt32(stream, schemaVersion);
        WriteInt64(stream, sequence);
        WriteInt64(stream, occurredAtUtc.Ticks);
        stream.WriteByte(isProtected ? (byte)1 : (byte)0);
        WriteUInt16(stream, checked((ushort)operationId.Length));
        stream.Write(operationId);
        WriteUInt16(stream, checked((ushort)eventType.Length));
        stream.Write(eventType);
        WriteInt32(stream, payload.Length);
        stream.Write(payload);
        return stream.ToArray();
    }

    private static byte[] CompleteFrame(byte[] body, string checksumHex, int maxRecordBytes)
    {
        if (maxRecordBytes <= ChecksumBytes) throw new ArgumentOutOfRangeException(nameof(maxRecordBytes));
        var checksum = ParseChecksum(checksumHex);
        if (body.Length + checksum.Length > maxRecordBytes)
            throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "Record überschreitet das Größenlimit.");
        var frame = new byte[body.Length + checksum.Length];
        body.CopyTo(frame, 0);
        checksum.CopyTo(frame, body.Length);
        return frame;
    }

    private static void ValidateCommon(
        int schemaVersion,
        long sequence,
        DateTime timestampUtc,
        int payloadLength,
        int maxPayloadBytes)
    {
        ValidateSchema(schemaVersion);
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (timestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Zeitpunkt muss UTC sein.", nameof(timestampUtc));
        if (maxPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if (payloadLength < 0) throw new ArgumentNullException("payload");
        if (payloadLength > maxPayloadBytes)
            throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "Payload überschreitet das Größenlimit.");
    }

    private static void ValidateLimits(int maxPayloadBytes, int maxRecordBytes)
    {
        if (maxPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if (maxRecordBytes <= ChecksumBytes) throw new ArgumentOutOfRangeException(nameof(maxRecordBytes));
    }

    private static void ValidateSchema(int schemaVersion)
    {
        if (!AiRuntimeStateSchema.IsSupported(schemaVersion))
            throw Error(AiRuntimeStateReasonCodes.SchemaUnsupported,
                $"Schema-Version {schemaVersion} wird nicht unterstützt.");
    }

    private static byte[] EncodeIdentifier(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length > MaxIdentifierUtf8Bytes)
            throw new ArgumentOutOfRangeException(parameter, $"UTF-8-Länge darf {MaxIdentifierUtf8Bytes} Bytes nicht überschreiten.");
        return bytes;
    }

    private static DateTime ReadUtc(long ticks)
    {
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            throw new FormatException("UTC-Ticks sind ungültig.");
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static void ExpectFormatVersion(ushort version)
    {
        if (version != FormatVersion)
            throw Error(AiRuntimeStateReasonCodes.SchemaUnsupported,
                $"Codec-Format-Version {version} wird nicht unterstützt.");
    }

    private static byte[] ParseChecksum(string checksumHex)
    {
        if (checksumHex is null || checksumHex.Length != ChecksumBytes * 2 || checksumHex.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("SHA-256-Prüfsumme muss aus 64 Hex-Zeichen bestehen.", nameof(checksumHex));
        return Convert.FromHexString(checksumHex);
    }

    private static string HashHex(ReadOnlySpan<byte> body)
        => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    private static void VerifyChecksum(ReadOnlySpan<byte> body, ReadOnlySpan<byte> expected, string reasonCode)
    {
        var actual = SHA256.HashData(body);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw Error(reasonCode, "SHA-256-Prüfsumme stimmt nicht überein.");
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static AiStateStoreException Error(string code, string message, Exception? inner = null)
        => inner is null
            ? new AiStateStoreException(code, message)
            : new AiStateStoreException(code, message, inner);

    private ref struct FrameReader
    {
        private readonly ReadOnlySpan<byte> _frame;
        private int _offset;

        public FrameReader(ReadOnlySpan<byte> frame) => _frame = frame;

        public byte ReadByte() => ReadBytes(1)[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(sizeof(ushort)));
        public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(sizeof(int)));
        public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(sizeof(long)));

        public string ReadIdentifier()
        {
            var length = ReadUInt16();
            if (length == 0 || length > MaxIdentifierUtf8Bytes) throw new FormatException("Identifier-Länge ist ungültig.");
            var value = StrictUtf8.GetString(ReadBytes(length));
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("Identifier ist leer.");
            return value;
        }

        public byte[] ReadPayload(int maxPayloadBytes)
        {
            var length = ReadInt32();
            if (length < 0) throw new FormatException("Payload-Länge ist negativ.");
            if (length > maxPayloadBytes)
                throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "Payload überschreitet das Größenlimit.");
            return ReadBytes(length).ToArray();
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > _frame.Length - _offset) throw new FormatException("Record ist abgeschnitten.");
            var result = _frame.Slice(_offset, length);
            _offset += length;
            return result;
        }

        public void Expect(ReadOnlySpan<byte> expected)
        {
            if (!ReadBytes(expected.Length).SequenceEqual(expected)) throw new FormatException("Record-Magic ist ungültig.");
        }

        public void ExpectEnd()
        {
            if (_offset != _frame.Length) throw new FormatException("Record enthält nachlaufende Bytes.");
        }
    }
}
