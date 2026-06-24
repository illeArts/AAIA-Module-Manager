using System;
using System.Collections.Generic;

namespace AAIA.Air;

/// <summary>
/// Begrenzter, sessiongebundener Idempotenzspeicher für mutierende Adapteraufrufe.
/// Er liegt im AIR-Kern, damit Adapter keinen eigenen Fachzustand halten.
/// </summary>
public sealed class AiIdempotencyStore
{
    private sealed record Entry(string Fingerprint, AiToolResult Result);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();
    private readonly int _capacity;

    public AiIdempotencyStore(int capacity = 2048)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public AiToolResult Execute(
        string sessionId,
        string operation,
        string idempotencyId,
        string fingerprint,
        Func<AiToolResult> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyId);
        ArgumentNullException.ThrowIfNull(factory);

        var key = $"{sessionId}\n{operation}\n{idempotencyId}";
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
                return string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal)
                    ? existing.Result
                    : AiToolResult.Fail(
                        "Idempotency-ID wurde bereits mit anderen Eingaben verwendet.",
                        AiPhase8ErrorCodes.IdempotencyConflict);

            var result = factory();
            _entries[key] = new Entry(fingerprint, result);
            _insertionOrder.Enqueue(key);
            while (_entries.Count > _capacity)
                _entries.Remove(_insertionOrder.Dequeue());
            return result;
        }
    }
}
