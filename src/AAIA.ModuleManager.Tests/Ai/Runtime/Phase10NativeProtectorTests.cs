using System.Text;
using AAIA.Air.Contracts;
using AAIA.ModuleManager.Services.Ai.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase10NativeProtectorTests
{
    [Fact]
    public async Task NativeProtector_RoundTripsAndUsesKeyIdEnvelope()
    {
        var protector = new AiLocalUserStateProtector(new InMemorySecretStore("test-native"));
        var context = Context("store", "task-step", "task:0");
        var plaintext = Encoding.UTF8.GetBytes("durable input");

        var protectedPayload = await protector.ProtectAsync(plaintext, context);
        var restored = await protector.UnprotectAsync(protectedPayload, context);

        Assert.Equal(plaintext, restored);
        Assert.StartsWith("aaia-native-user-secret-v2:test-native:aaia-air-state-", protectedPayload.ProtectorId);
        Assert.DoesNotContain("store", protectedPayload.ProtectorId, StringComparison.OrdinalIgnoreCase);
        Assert.False(plaintext.SequenceEqual(protectedPayload.Ciphertext));
    }

    [Fact]
    public async Task NativeProtector_BindsCiphertextToStoreRecordAndSchema()
    {
        var protector = new AiLocalUserStateProtector(new InMemorySecretStore("test-native"));
        var protectedPayload = await protector.ProtectAsync(
            Encoding.UTF8.GetBytes("durable input"),
            Context("store", "task-step", "task:0"));

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await protector.UnprotectAsync(protectedPayload, Context("store", "task-step", "task:1")));

        Assert.Equal(AiRuntimeStateReasonCodes.SnapshotCorrupt, error.ReasonCode);
    }

    [Fact]
    public async Task NativeProtector_MissingKeyFailsClosed()
    {
        var original = new AiLocalUserStateProtector(new InMemorySecretStore("test-native"));
        var protectedPayload = await original.ProtectAsync(
            Encoding.UTF8.GetBytes("durable input"),
            Context("store", "task-step", "task:0"));
        var missing = new AiLocalUserStateProtector(new InMemorySecretStore("test-native"));

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await missing.UnprotectAsync(protectedPayload, Context("store", "task-step", "task:0")));

        Assert.Equal(AiRuntimeStateReasonCodes.ProtectorKeyMissing, error.ReasonCode);
    }

    private static AiStateProtectionContext Context(
        string store,
        string type,
        string id,
        int schema = AiRuntimeStateSchema.CurrentVersion)
        => new()
        {
            StoreId = store,
            RecordType = type,
            RecordId = id,
            SchemaVersion = schema
        };

    private sealed class InMemorySecretStore(string platformId) : IAiNativeSecretStore
    {
        private readonly Dictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

        public string PlatformId { get; } = platformId;

        public ValueTask<byte[]?> TryGetSecretAsync(string keyId, CancellationToken ct = default)
            => ValueTask.FromResult(_secrets.TryGetValue(keyId, out var value) ? value.ToArray() : null);

        public ValueTask StoreSecretAsync(
            string keyId,
            ReadOnlyMemory<byte> secret,
            CancellationToken ct = default)
        {
            _secrets[keyId] = secret.ToArray();
            return ValueTask.CompletedTask;
        }
    }
}
