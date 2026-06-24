namespace AAIA.Air.Persistence;

/// <summary>
/// Lokale Wartungsprimitive. Jede Mutation benötigt Host-Autorisierung, Bestätigung,
/// Begründung und Audit. Diese API wird nicht als MCP-Werkzeug registriert.
/// </summary>
public sealed class AiRuntimeStateMaintenanceService
{
    private const int MaxReasonLength = 500;
    private readonly IAiRuntimeStateStore _store;
    private readonly IAiRuntimeStateMaintenanceStore? _maintenanceStore;
    private readonly AiAuditService _audit;
    private readonly IAiStateMaintenanceAuthorizer _authorizer;
    private readonly string _runtimeInstanceId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AiRuntimeStateMaintenanceService(
        IAiRuntimeStateStore store,
        AiAuditService audit,
        IAiStateMaintenanceAuthorizer authorizer,
        string runtimeInstanceId)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _maintenanceStore = store as IAiRuntimeStateMaintenanceStore;
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        _runtimeInstanceId = runtimeInstanceId;
    }

    public async ValueTask<AiRuntimeStateDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        if (_maintenanceStore is null)
            return new AiRuntimeStateDiagnostics
            {
                StoreId = _store.StoreId,
                Status = AiRuntimeRecoveryStatus.Disabled,
                ReasonCode = AiRuntimeStateReasonCodes.Disabled,
                RedactedMessage = "Wartungsdiagnose ist für diesen Store nicht verfügbar."
            };
        try
        {
            return await _maintenanceStore.GetDiagnosticsAsync(ct).ConfigureAwait(false);
        }
        catch (AiStateStoreException ex)
        {
            return FailedDiagnostics(ex.ReasonCode, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return FailedDiagnostics(AiRuntimeStateReasonCodes.SnapshotCorrupt, ex.Message);
        }
    }

    public ValueTask<AiStateMaintenanceOperationResult> BackupAsync(
        string actorId, string reason, bool confirmed, CancellationToken ct = default)
        => ExecuteAsync("backup", actorId, reason, confirmed, async token =>
        {
            var backup = await RequiredMaintenanceStore().CreateBackupAsync(_runtimeInstanceId, token)
                .ConfigureAwait(false);
            return new AiStateMaintenanceOperationResult
            {
                Action = "backup",
                Success = true,
                BackupId = backup.BackupId
            };
        }, ct);

    public ValueTask<AiStateMaintenanceOperationResult> CompactAsync(
        string actorId, string reason, bool confirmed, CancellationToken ct = default)
        => ExecuteAsync("compact", actorId, reason, confirmed, async token =>
        {
            var backup = await RequiredMaintenanceStore().CreateBackupAsync(_runtimeInstanceId, token)
                .ConfigureAwait(false);
            await using var session = await _store.OpenAsync(
                AiStateStoreOpenMode.ReadWrite, _runtimeInstanceId, token).ConfigureAwait(false);
            var snapshot = await session.LoadSnapshotAsync(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Kompaktierung benötigt einen bestätigten Snapshot.");
            await session.CompactAsync(snapshot.Sequence, token).ConfigureAwait(false);
            await session.FlushAsync(token).ConfigureAwait(false);
            return new AiStateMaintenanceOperationResult
            {
                Action = "compact",
                Success = true,
                BackupId = backup.BackupId
            };
        }, ct);

    public ValueTask<AiStateMaintenanceOperationResult> RepairAsync(
        string actorId, string reason, bool confirmed, CancellationToken ct = default)
        => ExecuteAsync("repair", actorId, reason, confirmed, async token =>
        {
            var maintenance = RequiredMaintenanceStore();
            var backup = await maintenance.CreateBackupAsync(_runtimeInstanceId, token).ConfigureAwait(false);
            var repair = await maintenance.RepairAsync(_runtimeInstanceId, backup.BackupId, token)
                .ConfigureAwait(false);
            return new AiStateMaintenanceOperationResult
            {
                Action = "repair",
                Success = repair.Repaired,
                BackupId = backup.BackupId,
                ReasonCode = repair.ReasonCode
            };
        }, ct);

    private async ValueTask<AiStateMaintenanceOperationResult> ExecuteAsync(
        string action,
        string actorId,
        string reason,
        bool confirmed,
        Func<CancellationToken, ValueTask<AiStateMaintenanceOperationResult>> operation,
        CancellationToken ct)
    {
        ValidateRequest(actorId, reason);
        if (!_authorizer.IsAuthorized(actorId, action, confirmed, out var denialReason))
        {
            var message = string.IsNullOrWhiteSpace(denialReason)
                ? "Owner/Admin-Autorisierung und Bestätigung erforderlich."
                : denialReason;
            _audit.RecordAdministrative(actorId, $"air.state.{action}", false, message);
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.RecoveryForbidden, message);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await operation(ct).ConfigureAwait(false);
            _audit.RecordAdministrative(actorId, $"air.state.{action}", result.Success,
                $"reason={reason}; backup={result.BackupId ?? "none"}; code={result.ReasonCode ?? "ok"}");
            return result;
        }
        catch (Exception ex)
        {
            _audit.RecordAdministrative(actorId, $"air.state.{action}", false,
                $"reason={reason}; error={ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IAiRuntimeStateMaintenanceStore RequiredMaintenanceStore()
        => _maintenanceStore ?? throw new AiStateStoreException(
            AiRuntimeStateReasonCodes.Disabled,
            "Der konfigurierte Store unterstützt keine lokale Wartung.");

    private static void ValidateRequest(string actorId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (actorId.Length > 200 || reason.Length > MaxReasonLength)
            throw new ArgumentOutOfRangeException(nameof(reason), "Akteur oder Begründung überschreitet das Limit.");
    }

    private AiRuntimeStateDiagnostics FailedDiagnostics(string reasonCode, string message) => new()
    {
        StoreId = _store.StoreId,
        Status = AiRuntimeRecoveryStatus.RecoveryFailed,
        ReasonCode = reasonCode,
        RedactedMessage = AiAuditService.Redact(message)
    };
}
