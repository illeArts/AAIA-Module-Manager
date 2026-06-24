using System.Text;
using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using AAIA.ModuleManager.Services.Ai.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9MaintenanceTests
{
    [Fact]
    public async Task AuditSnapshot_IsSessionFreeRedactedAndRestored()
    {
        var source = Runtime();
        source.Audit.RecordAdministrative(
            "owner", "air.state.backup", true,
            "token=super-secret password=hunter2 Bearer abcdefghijklmnopqrstuvwxyz");
        var snapshot = await Persistence(source).CaptureAsync(1, DateTime.UtcNow);
        var json = Encoding.UTF8.GetString(snapshot.Payload);
        var durable = JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(snapshot.Payload)!;

        Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", json, StringComparison.Ordinal);
        Assert.DoesNotContain("local-admin", json, StringComparison.Ordinal);
        var persisted = Assert.Single(durable.AuditEntries);
        Assert.Contains("***", persisted.Detail);

        var target = Runtime();
        var report = await Persistence(target).RestoreAsync(snapshot);
        var restored = Assert.Single(target.Audit.Recent());
        Assert.Equal(1, report.AuditEntryCount);
        Assert.Null(restored.SessionId);
        Assert.Contains("***", restored.Detail);
    }

    [Fact]
    public async Task MaintenanceWithoutAuthorizationOrConfirmation_IsBlockedAndAudited()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var audit = new AiAuditService();
        var service = new AiRuntimeStateMaintenanceService(
            new AiInMemoryRuntimeStateStore(backend), audit,
            new FixedMaintenanceAuthorizer(false), "runtime");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await service.BackupAsync("user", "diagnosis", confirmed: false));

        Assert.Equal(AiRuntimeStateReasonCodes.RecoveryForbidden, error.ReasonCode);
        var entry = Assert.Single(audit.Recent());
        Assert.False(entry.Success);
        Assert.Equal("air.state.backup", entry.Tool);
    }

    [Fact]
    public async Task BackupAndCompaction_AreAuthorizedBackedUpAndAudited()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        await SeedSnapshotAndJournal(store);
        var audit = new AiAuditService();
        var service = new AiRuntimeStateMaintenanceService(
            store, audit, new FixedMaintenanceAuthorizer(true), "maintenance");

        var result = await service.CompactAsync("owner", "scheduled maintenance", confirmed: true);

        Assert.True(result.Success);
        Assert.StartsWith("backup-", result.BackupId);
        Assert.Equal(0, backend.JournalCount);
        Assert.Contains(audit.Recent(), entry => entry.Tool == "air.state.compact" && entry.Success);
    }

    [Fact]
    public async Task Repair_CreatesBackupAndClearsQuarantine()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        await using (var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer"))
            await writer.QuarantineAsync("token=hidden checksum failure");
        var audit = new AiAuditService();
        var service = new AiRuntimeStateMaintenanceService(
            store, audit, new FixedMaintenanceAuthorizer(true), "maintenance");

        var before = await service.GetDiagnosticsAsync();
        var result = await service.RepairAsync("owner", "validated local repair", confirmed: true);
        var after = await service.GetDiagnosticsAsync();

        Assert.Equal(AiRuntimeRecoveryStatus.Quarantined, before.Status);
        Assert.DoesNotContain("hidden", before.RedactedMessage, StringComparison.Ordinal);
        Assert.True(result.Success);
        Assert.NotNull(result.BackupId);
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, after.Status);
    }

    [Fact]
    public async Task LocalFileBackupAndRepair_PreserveBackupAndRestoreWriterAccess()
    {
        using var temp = new TempDirectory();
        var store = FileStore(temp.Path);
        await using (var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer"))
            await writer.QuarantineAsync("manual quarantine");
        var service = new AiRuntimeStateMaintenanceService(
            store, new AiAuditService(), new FixedMaintenanceAuthorizer(true), "maintenance");

        var result = await service.RepairAsync("owner", "verified files", confirmed: true);

        Assert.True(result.Success);
        var backupPath = Path.Combine(temp.Path, "backups", result.BackupId!);
        Assert.True(File.Exists(Path.Combine(backupPath, "backup.json")));
        Assert.True(File.Exists(Path.Combine(backupPath, "quarantine.json")));
        await using var writerAfterRepair = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer-after");
        Assert.False(writerAfterRepair.IsQuarantined);
    }

    [Fact]
    public async Task LocalFileRepair_WithCorruptJournalKeepsQuarantineAndOriginalFiles()
    {
        using var temp = new TempDirectory();
        var store = FileStore(temp.Path);
        await using (var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer"))
            await writer.QuarantineAsync("journal corrupt");
        var journalPath = Path.Combine(temp.Path, "journal.bin");
        File.WriteAllBytes(journalPath, new byte[] { 0, 0, 0, 1, 1 });
        var original = File.ReadAllBytes(journalPath);
        var service = new AiRuntimeStateMaintenanceService(
            store, new AiAuditService(), new FixedMaintenanceAuthorizer(true), "maintenance");

        await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await service.RepairAsync("owner", "inspect corruption", confirmed: true));

        Assert.True(File.Exists(Path.Combine(temp.Path, "quarantine.json")));
        Assert.Equal(original, File.ReadAllBytes(journalPath));
        Assert.NotEmpty(Directory.EnumerateDirectories(Path.Combine(temp.Path, "backups")));
    }

    [Fact]
    public async Task Diagnostics_DoesNotRequireWriterAndReportsStoreMetadata()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("diagnostic-store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        await SeedSnapshotAndJournal(store);
        var service = new AiRuntimeStateMaintenanceService(
            store, new AiAuditService(), new FixedMaintenanceAuthorizer(true), "diagnostics");

        var diagnostics = await service.GetDiagnosticsAsync();

        Assert.Equal("diagnostic-store", diagnostics.StoreId);
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, diagnostics.Status);
        Assert.Equal(1, diagnostics.LastSequence);
        Assert.Equal(1, diagnostics.SnapshotSequence);
    }

    private static async Task SeedSnapshotAndJournal(IAiRuntimeStateStore store)
    {
        var now = DateTime.UtcNow;
        await using var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "seed");
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            1, "operation", "test.event", now, false, Encoding.UTF8.GetBytes("{}"));
        await writer.AppendJournalAsync(entry);
        var snapshot = AiRuntimeStateCodec.CreateSnapshot(1, now, Encoding.UTF8.GetBytes("{}"));
        await writer.WriteSnapshotAsync(snapshot);
        await writer.FlushAsync();
    }

    private static AiLocalFileRuntimeStateStore FileStore(string path) => new(
        path,
        "test",
        new AiRuntimePersistenceOptions { Enabled = true });

    private static AiRuntimeService Runtime() => new(
        new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
        new AiPermissionEngine(), new AiWorkspaceLockService(),
        new AiRuntimeEventBus(), new AiAuditService());

    private static AiOrchestrationPersistenceService Persistence(AiRuntimeService runtime)
        => new(runtime, new PassthroughProtector(), "store");

    private sealed class FixedMaintenanceAuthorizer(bool allowed) : IAiStateMaintenanceAuthorizer
    {
        public bool IsAuthorized(string actorId, string action, bool confirmed, out string? denialReason)
        {
            var success = allowed && confirmed;
            denialReason = success ? null : "Owner/Admin und Bestätigung erforderlich.";
            return success;
        }
    }

    private sealed class PassthroughProtector : IAiStateProtector
    {
        public string ProtectorId => "test";
        public ValueTask<AiProtectedStatePayload> ProtectAsync(
            ReadOnlyMemory<byte> plaintext, AiStateProtectionContext context, CancellationToken ct = default)
            => ValueTask.FromResult(new AiProtectedStatePayload
            {
                ProtectorId = ProtectorId,
                Ciphertext = plaintext.ToArray()
            });
        public ValueTask<byte[]> UnprotectAsync(
            AiProtectedStatePayload protectedPayload, AiStateProtectionContext context, CancellationToken ct = default)
            => ValueTask.FromResult(protectedPayload.Ciphertext.ToArray());
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "aaia-phase9-maintenance", Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
