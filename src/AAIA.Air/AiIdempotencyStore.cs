using System.Security.Cryptography;
using System.Text;

namespace AAIA.Air;

/// <summary>
/// Begrenzter Idempotenzspeicher auf Basis einer stabilen Client-Kennung. Im Speicher
/// bleibt das vollständige Ergebnis nur bis zum Prozessende; dauerhaft wird ausschließlich
/// seine stabile Resultat-ID zusammen mit einem SHA-256-Input-Fingerprint gehalten.
/// </summary>
public sealed class AiIdempotencyStore
{
    private sealed class Entry
    {
        public required string Key { get; init; }
        public required string ClientFingerprint { get; init; }
        public required string Operation { get; init; }
        public required string IdempotencyId { get; init; }
        public required string InputFingerprint { get; init; }
        public string? ResultId { get; init; }
        public AiToolResult? Result { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;

    public AiIdempotencyStore(
        int capacity = 10_000,
        TimeSpan? ttl = null,
        TimeProvider? timeProvider = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ttl = ttl ?? TimeSpan.FromHours(24);
        if (_ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        _capacity = capacity;
        _time = timeProvider ?? TimeProvider.System;
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public AiToolResult Execute(
        string clientFingerprint,
        string operation,
        string idempotencyId,
        string input,
        Func<AiToolResult> factory,
        Func<AiToolResult, string?>? resultIdSelector = null,
        Func<string, AiToolResult>? restoredReplayFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyId);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(factory);

        var key = Key(clientFingerprint, operation, idempotencyId);
        var inputFingerprint = Fingerprint(input);
        lock (_gate)
        {
            var now = UtcNow;
            PurgeExpiredLocked(now);
            if (_entries.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
                    return AiToolResult.Fail(
                        "Idempotency-ID wurde bereits mit anderen Eingaben verwendet.",
                        AiPhase8ErrorCodes.IdempotencyConflict);
                if (existing.Result is not null) return existing.Result;
                return existing.ResultId is not null
                    ? restoredReplayFactory?.Invoke(existing.ResultId) ??
                      AiToolResult.Ok(new { resultId = existing.ResultId, replayed = true })
                    : AiToolResult.Fail("Idempotenzresultat ist nicht wiederherstellbar.", "idempotency_result_unavailable");
            }

            var result = factory();
            var resultId = result.Success ? resultIdSelector?.Invoke(result) : null;
            _entries[key] = new Entry
            {
                Key = key,
                ClientFingerprint = clientFingerprint,
                Operation = operation,
                IdempotencyId = idempotencyId,
                InputFingerprint = inputFingerprint,
                ResultId = string.IsNullOrWhiteSpace(resultId) ? null : resultId,
                Result = result,
                CreatedAtUtc = now,
                ExpiresAtUtc = now + _ttl
            };
            TrimLocked();
            return result;
        }
    }

    internal IReadOnlyList<AiDurableIdempotencyRecord> CaptureDurableRecords(DateTime nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        lock (_gate)
        {
            PurgeExpiredLocked(nowUtc);
            return _entries.Values
                .Where(entry => entry.ResultId is not null)
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new AiDurableIdempotencyRecord
                {
                    ClientFingerprint = entry.ClientFingerprint,
                    Operation = entry.Operation,
                    IdempotencyId = entry.IdempotencyId,
                    InputFingerprint = entry.InputFingerprint,
                    ResultId = entry.ResultId!,
                    CreatedAtUtc = entry.CreatedAtUtc,
                    ExpiresAtUtc = entry.ExpiresAtUtc
                }).ToArray();
        }
    }

    internal int RestoreDurableRecords(IEnumerable<AiDurableIdempotencyRecord> records, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(records);
        EnsureUtc(nowUtc, nameof(nowUtc));
        var restored = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.ClientFingerprint);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.Operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.IdempotencyId);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.ResultId);
            if (record.InputFingerprint.Length != 64 ||
                record.InputFingerprint.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException("Idempotenz-Input-Fingerprint ist ungültig.");
            EnsureUtc(record.CreatedAtUtc, nameof(record.CreatedAtUtc));
            EnsureUtc(record.ExpiresAtUtc, nameof(record.ExpiresAtUtc));
            if (record.ExpiresAtUtc <= record.CreatedAtUtc)
                throw new InvalidOperationException("Idempotenz-Ablaufzeit ist ungültig.");
            if (record.ExpiresAtUtc <= nowUtc) continue;
            var key = Key(record.ClientFingerprint, record.Operation, record.IdempotencyId);
            if (!restored.TryAdd(key, new Entry
                {
                    Key = key,
                    ClientFingerprint = record.ClientFingerprint,
                    Operation = record.Operation,
                    IdempotencyId = record.IdempotencyId,
                    InputFingerprint = record.InputFingerprint.ToUpperInvariant(),
                    ResultId = record.ResultId,
                    Result = null,
                    CreatedAtUtc = record.CreatedAtUtc,
                    ExpiresAtUtc = record.ExpiresAtUtc
                }))
                throw new InvalidOperationException("Doppelter Idempotenzdatensatz im Snapshot.");
        }

        lock (_gate)
        {
            if (_entries.Count != 0)
                throw new InvalidOperationException("Idempotenz kann nur in einen leeren Store wiederhergestellt werden.");
            foreach (var entry in restored.Values
                         .OrderByDescending(entry => entry.CreatedAtUtc)
                         .ThenByDescending(entry => entry.Key, StringComparer.Ordinal)
                         .Take(_capacity))
                _entries.Add(entry.Key, entry);
            return _entries.Count;
        }
    }

    internal void ClearDurableRestore()
    {
        lock (_gate) _entries.Clear();
    }

    private void PurgeExpiredLocked(DateTime nowUtc)
    {
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAtUtc <= nowUtc)
                     .Select(pair => pair.Key).ToArray())
            _entries.Remove(key);
    }

    private void TrimLocked()
    {
        foreach (var entry in _entries.Values
                     .OrderBy(entry => entry.CreatedAtUtc)
                     .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                     .Take(Math.Max(0, _entries.Count - _capacity)).ToArray())
            _entries.Remove(entry.Key);
    }

    private static string Key(string clientFingerprint, string operation, string idempotencyId)
        => $"{clientFingerprint.Length}:{clientFingerprint}{operation.Length}:{operation}{idempotencyId.Length}:{idempotencyId}";

    private static string Fingerprint(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static void EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Zeitpunkt muss UTC sein.", field);
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
}
