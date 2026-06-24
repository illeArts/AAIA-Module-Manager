using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AAIA.Air.Messaging;

namespace AAIA.Air.Persistence;

public sealed record AiDurableMutationRegistration(
    AiDurableMutationType MutationType,
    string EventType,
    Type PayloadType);

/// <summary>Geschlossene Registry aller les- und schreibbaren Delta-Events.</summary>
public static class AiDurableMutationRegistry
{
    public const string EventPrefix = "air.mutation.";

    private static readonly IReadOnlyDictionary<AiDurableMutationType, AiDurableMutationRegistration> ByType =
        Build().ToDictionary(item => item.MutationType);
    private static readonly IReadOnlyDictionary<string, AiDurableMutationRegistration> ByEvent =
        ByType.Values.ToDictionary(item => item.EventType, StringComparer.Ordinal);

    public static IReadOnlyList<AiDurableMutationRegistration> All { get; } =
        ByType.Values.OrderBy(item => item.MutationType).ToArray();

    static AiDurableMutationRegistry()
    {
        var enumValues = Enum.GetValues<AiDurableMutationType>();
        if (ByType.Count != enumValues.Length || enumValues.Any(value => !ByType.ContainsKey(value)))
            throw new InvalidOperationException("Durable Mutation Registry ist unvollständig.");
        if (ByEvent.Count != ByType.Count)
            throw new InvalidOperationException("Durable Mutation Registry enthält doppelte Eventnamen.");
    }

    public static AiDurableMutationRegistration Get(AiDurableMutationType mutationType)
        => ByType.TryGetValue(mutationType, out var registration)
            ? registration
            : throw Unknown($"Unbekannter Mutationstyp: {mutationType}");

    public static AiDurableMutationRegistration Get(string eventType)
        => ByEvent.TryGetValue(eventType, out var registration)
            ? registration
            : throw Unknown($"Unbekannter Eventtyp: {eventType}");

    private static IEnumerable<AiDurableMutationRegistration> Build()
    {
        yield return Task(AiDurableMutationType.TaskCreated, "task.created");
        yield return Task(AiDurableMutationType.TaskClaimed, "task.claimed");
        yield return Task(AiDurableMutationType.TaskClaimReleased, "task.claim_released");
        yield return Task(AiDurableMutationType.TaskStepChanged, "task.step_changed");
        yield return Task(AiDurableMutationType.TaskSettled, "task.settled");
        yield return Execution(AiDurableMutationType.ExecutionQueued, "execution.queued");
        yield return Execution(AiDurableMutationType.ExecutionLeased, "execution.leased");
        yield return Execution(AiDurableMutationType.ExecutionStateChanged, "execution.state_changed");
        yield return Execution(AiDurableMutationType.ExecutionRecoveryResolved, "execution.recovery_resolved");
        yield return Register<AiBudgetMutationPayload>(AiDurableMutationType.BudgetCreated, "budget.created");
        yield return Reservation(AiDurableMutationType.ReservationCreated, "reservation.created");
        yield return Reservation(AiDurableMutationType.ReservationCommitted, "reservation.committed");
        yield return Reservation(AiDurableMutationType.ReservationReleased, "reservation.released");
        yield return Reservation(AiDurableMutationType.ReservationExpired, "reservation.expired");
        yield return Register<AiIdempotencyStoredPayload>(
            AiDurableMutationType.IdempotencyStored, "idempotency.stored");
        yield return Register<AiIdempotencyEvictedPayload>(
            AiDurableMutationType.IdempotencyEvicted, "idempotency.evicted");
        yield return Register<AiAuditMutationPayload>(AiDurableMutationType.AuditRecorded, "audit.recorded");
        yield return Register<AiRecoveryCheckpointPayload>(
            AiDurableMutationType.RuntimeRecoveryCheckpoint, "runtime.recovery_checkpoint");
    }

    private static AiDurableMutationRegistration Task(AiDurableMutationType type, string name)
        => Register<AiTaskMutationPayload>(type, name);

    private static AiDurableMutationRegistration Execution(AiDurableMutationType type, string name)
        => Register<AiExecutionMutationPayload>(type, name);

    private static AiDurableMutationRegistration Reservation(AiDurableMutationType type, string name)
        => Register<AiReservationMutationPayload>(type, name);

    private static AiDurableMutationRegistration Register<T>(AiDurableMutationType type, string name)
        => new(type, EventPrefix + name, typeof(T));

    private static AiStateStoreException Unknown(string message)
        => new(AiRuntimeStateReasonCodes.JournalEventUnknown, message);
}

/// <summary>Kanonische Übersetzung zwischen typisiertem Delta und State-Store-Journal.</summary>
public static class AiDurableMutationCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 64
    };

    public static AiDurableMutationEnvelope CreateEnvelope(
        long sequence,
        string operationId,
        AiDurableMutationType mutationType,
        DateTime occurredAtUtc,
        object payload,
        string? actorFingerprint = null,
        string? inputFingerprint = null,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Mutation-Zeitpunkt muss UTC sein.", nameof(occurredAtUtc));
        ArgumentNullException.ThrowIfNull(payload);
        var registration = AiDurableMutationRegistry.Get(mutationType);
        if (!registration.PayloadType.IsInstanceOfType(payload))
            throw new ArgumentException(
                $"Payload für {mutationType} muss {registration.PayloadType.Name} sein.", nameof(payload));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, registration.PayloadType, JsonOptions);
        if (bytes.Length > maxPayloadBytes)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Delta-Payload überschreitet das Limit.");
        return new AiDurableMutationEnvelope
        {
            Sequence = sequence,
            OperationId = operationId,
            MutationType = mutationType,
            OccurredAtUtc = occurredAtUtc,
            ActorFingerprint = actorFingerprint,
            InputFingerprint = inputFingerprint,
            Payload = bytes,
            PayloadChecksumSha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    public static AiRuntimeJournalEntry ToJournalEntry(
        AiDurableMutationEnvelope envelope,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes)
    {
        ValidateEnvelope(envelope, maxPayloadBytes);
        var registration = AiDurableMutationRegistry.Get(envelope.MutationType);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        return AiRuntimeStateCodec.CreateJournalEntry(
            envelope.Sequence,
            envelope.OperationId,
            registration.EventType,
            envelope.OccurredAtUtc,
            false,
            bytes,
            AiRuntimeStateSchema.CurrentVersion,
            maxPayloadBytes);
    }

    public static AiDurableMutationEnvelope FromJournalEntry(
        AiRuntimeJournalEntry entry,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        AiRuntimeStateCodec.VerifyJournalEntry(entry, maxPayloadBytes);
        var registration = AiDurableMutationRegistry.Get(entry.EventType);
        AiDurableMutationEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<AiDurableMutationEnvelope>(entry.Payload, JsonOptions)
                ?? throw new JsonException("Mutation-Envelope ist leer.");
        }
        catch (JsonException ex)
        {
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalCorrupt,
                "Mutation-Envelope ist ungültig.", ex);
        }
        ValidateEnvelope(envelope, maxPayloadBytes);
        if (envelope.Sequence != entry.Sequence || envelope.OperationId != entry.OperationId ||
            envelope.OccurredAtUtc != entry.OccurredAtUtc ||
            envelope.MutationType != registration.MutationType)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalCorrupt,
                "Journal-Header und Mutation-Envelope stimmen nicht überein.");
        return envelope;
    }

    public static T DeserializePayload<T>(AiDurableMutationEnvelope envelope)
    {
        ValidateEnvelope(envelope, AiRuntimeStateCodec.DefaultMaxPayloadBytes);
        var registration = AiDurableMutationRegistry.Get(envelope.MutationType);
        if (registration.PayloadType != typeof(T))
            throw new InvalidOperationException(
                $"Mutation {envelope.MutationType} verwendet {registration.PayloadType.Name}, nicht {typeof(T).Name}.");
        try
        {
            return (T)(JsonSerializer.Deserialize(envelope.Payload, typeof(T), JsonOptions)
                ?? throw new JsonException("Mutation-Payload ist leer."));
        }
        catch (JsonException ex)
        {
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalCorrupt,
                "Mutation-Payload ist ungültig.", ex);
        }
    }

    public static void ValidateEnvelope(
        AiDurableMutationEnvelope envelope,
        int maxPayloadBytes = AiRuntimeStateCodec.DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.SchemaVersion != AiDurableMutationSchema.CurrentVersion)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.SchemaUnsupported,
                "Mutation-Schema wird nicht unterstützt.");
        if (envelope.Sequence <= 0 || envelope.OccurredAtUtc.Kind != DateTimeKind.Utc)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalCorrupt,
                "Mutation besitzt ungültige Sequenz oder Zeit.");
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.OperationId);
        AiDurableMutationRegistry.Get(envelope.MutationType);
        if (envelope.Payload is null || envelope.Payload.Length > maxPayloadBytes)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.QuotaExceeded,
                "Mutation-Payload überschreitet das Limit.");
        if (AiMessageSafetyPolicy.ContainsSensitiveContent(envelope.ActorFingerprint) ||
            AiMessageSafetyPolicy.ContainsSensitiveContent(envelope.InputFingerprint) ||
            AiMessageSafetyPolicy.ContainsSensitiveContent(Encoding.UTF8.GetString(envelope.Payload)))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.PayloadRejected,
                "Mutation enthält sensible Inhalte und wird nicht persistiert.");
        var expected = Convert.ToHexString(SHA256.HashData(envelope.Payload));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), ParseChecksum(envelope.PayloadChecksumSha256)))
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalChecksumFailed,
                "Mutation-Payload-Prüfsumme ist ungültig.");
    }

    private static byte[] ParseChecksum(string? checksum)
    {
        try
        {
            if (string.IsNullOrEmpty(checksum) || checksum.Length != 64) throw new FormatException();
            return Convert.FromHexString(checksum);
        }
        catch (FormatException ex)
        {
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.JournalChecksumFailed,
                "Mutation-Payload-Prüfsumme ist ungültig.", ex);
        }
    }
}
