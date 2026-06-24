using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.Services.Ai.Integration;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Hosts;
using AAIA.ModuleManager.Services.Ai.Persistence;
using AAIA.ModuleManager.Services.Help;
using AAIA.Shared.Contracts.Publisher;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für den Connector-Server-Tab (Phase 6.3 + 7.0).
/// Steuert den lokalen AiConnectorServer und die AIR MCP-Bridge.
/// Implementiert IModuleManagerAiBridge — die einzige Stelle mit echtem App-/UI-Zustand.
/// </summary>
public sealed partial class ConnectorTabViewModel : ObservableObject, IModuleManagerAiBridge, IDisposable
{
    private readonly AppConfig                _config;
    private readonly AiConnectorServer        _server;
    private readonly AaiasConnectionService?  _aaias;
    private          AiRuntimeConnectorPanel? _airPanel;

    // AIR-Patch-Proposals, die auf UI-Entscheidung warten (proposalId → TCS)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AiHostResult>> _airPending = new();

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isRunning       = false;
    [ObservableProperty] private string _serverUrl       = $"http://localhost:{AiConnectorProtocol.Port}/aaia/v1/";
    [ObservableProperty] private string _statusText      = "Server gestoppt.";
    [ObservableProperty] private bool   _hasError        = false;
    [ObservableProperty] private string _errorText       = "";
    [ObservableProperty] private int    _requestCount    = 0;
    [ObservableProperty] private int    _pendingPatches  = 0;

    /// <summary>True wenn mindestens ein Patch auf Genehmigung wartet.</summary>
    public bool HasPendingPatches  => PendingPatches > 0;
    /// <summary>True wenn Server läuft und Log leer ist.</summary>
    public bool ShowEmptyRunning   => IsRunning && Log.Count == 0;
    /// <summary>True wenn Log mindestens einen Eintrag hat.</summary>
    public bool HasLogEntries      => Log.Count > 0;

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEmptyRunning));
    }

    partial void OnPendingPatchesChanged(int value)
        => OnPropertyChanged(nameof(HasPendingPatches));

    // ── Einstellungen ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _allowPatchProposals;

    partial void OnAllowPatchProposalsChanged(bool value)
    {
        _config.AiConnector.AllowPatchProposals = value;
        _ = _config.SaveAsync();
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    public ObservableCollection<ConnectorLogEntry> Log { get; } = [];

    // ── Projekt-Kontext (von außen gesetzt) ───────────────────────────────────

    private AiHandoffContext? _currentContext;
    private string?           _projectRoot;

    // ── AIR / MCP-Bridge ─────────────────────────────────────────────────────

    [ObservableProperty] private bool   _airIsRunning  = false;
    [ObservableProperty] private string _airStatusText = "MCP-Bridge gestoppt.";
    [ObservableProperty] private string _airUrl        = "";
    [ObservableProperty] private string _airLastEvent  = "";
    [ObservableProperty] private bool   _hasMcpBridge  = false;
    [ObservableProperty] private bool   _airAllowCollaboration;
    [ObservableProperty] private bool   _airAllowScheduling;
    [ObservableProperty] private bool   _airAllowResourceRead;
    [ObservableProperty] private string _airAdminExecutionId = "";
    [ObservableProperty] private string _airAdminResourceId = "";
    [ObservableProperty] private bool   _airAdminResourceEnabled = true;
    [ObservableProperty] private string _airAdminReason = "";
    [ObservableProperty] private string _airBudgetCostUnit = "EUR";
    [ObservableProperty] private decimal _airBudgetHardLimit;
    [ObservableProperty] private string _airAdminStatus = "";
    [ObservableProperty] private string _airStateStatus = "Persistenzdiagnose wird geladen …";

    public bool IsAirAdmin => _config.DeveloperRole is DeveloperRole.Owner or DeveloperRole.Admin;

    public ObservableCollection<string> AirSessions { get; } = [];
    public ObservableCollection<string> AirLocks    { get; } = [];
    public ObservableCollection<string> AirTools    { get; } = [];
    public ObservableCollection<string> AirAudit    { get; } = [];
    public ObservableCollection<string> AirMessages { get; } = [];
    public ObservableCollection<string> AirExecutions { get; } = [];
    public ObservableCollection<string> AirResources { get; } = [];

    partial void OnAirIsRunningChanged(bool value)
    {
        StartAirCommand.NotifyCanExecuteChanged();
        StopAirCommand.NotifyCanExecuteChanged();
    }

    partial void OnAirAllowCollaborationChanged(bool value) => PersistPhase8McpAccess();
    partial void OnAirAllowSchedulingChanged(bool value) => PersistPhase8McpAccess();
    partial void OnAirAllowResourceReadChanged(bool value) => PersistPhase8McpAccess();

    // ── Patch-Approval-Callback ───────────────────────────────────────────────

    /// <summary>
    /// Wird vom View (MainWindow) gesetzt — öffnet das PatchApprovalWindow.
    /// Parameter: proposalId, patchRequest.
    /// </summary>
    public Action<string, AiPatchRequest>? ShowPatchApproval { get; set; }

    /// <summary>Lokaler Bestätigungsdialog für AIR-Admin-Aktionen.</summary>
    public Func<string, string, Task<bool>>? ConfirmAirAdminAction { get; set; }

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public ConnectorTabViewModel(AppConfig config, AaiasConnectionService? aaias = null)
    {
        _config               = config;
        _aaias                = aaias;
        _allowPatchProposals  = config.AiConnector.AllowPatchProposals;
        _airAllowCollaboration = config.McpBridge.AllowCollaboration;
        _airAllowScheduling = config.McpBridge.AllowScheduling;
        _airAllowResourceRead = config.McpBridge.AllowResourceRead;

        _server = new AiConnectorServer(config);
        _server.ConnectorConnected    += OnConnectorConnected;
        _server.PatchProposalReceived += OnPatchProposalReceived;

        // AIR MCP-Bridge initialisieren (deaktiviert bis StartAirAsync)
        HasMcpBridge = true;
        _airPanel    = new AiRuntimeConnectorPanel(config.McpBridge, this);
        var stateStore = new AiLocalFileRuntimeStateStore(
            AiLocalFileRuntimeStateStore.GetDefaultRootPath("module-manager"),
            "module-manager",
            new AiRuntimePersistenceOptions { Enabled = false });
        var stateAuthorizer = new LocalStateMaintenanceAuthorizer(() => IsAirAdmin);
        _airPanel.ConfigureStateMaintenance(
            stateStore,
            stateAuthorizer,
            $"module-manager-{Environment.ProcessId}");
        _airPanel.ConfigureRecoveryDecisions(stateAuthorizer);
        _airPanel.Log += msg => Dispatcher.UIThread.Post(() => AppendLog($"[AIR] {msg}", ConnectorLogLevel.Info));
        _airPanel.Runtime.Events.EventPublished += e =>
        {
            var text = $"{e.TimestampUtc:HH:mm:ss} {e.Type} {e.Tool} ({e.ClientName})";
            Dispatcher.UIThread.Post(() =>
            {
                AirLastEvent = text;
                RefreshAirLists();
            });
        };

        if (config.AiConnector.AutoStart)
            _ = StartAsync();
    }

    // ── Kontext aktualisieren ─────────────────────────────────────────────────

    /// <summary>Vom ModuleTab aufgerufen wenn sich das Projekt ändert.</summary>
    public void UpdateProjectContext(AiHandoffContext ctx, string? projectRoot = null)
    {
        _currentContext = ctx;
        _projectRoot    = projectRoot;
        _server.UpdateContext(ctx, projectRoot);
    }

    // ── Commands — Connector-Server ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        HasError = false;
        try
        {
            await _server.StartAsync();
            IsRunning  = true;
            StatusText = $"Server läuft auf Port {AiConnectorProtocol.Port}.";
            AppendLog("Server gestartet.", ConnectorLogLevel.Info);
        }
        catch (Exception ex)
        {
            HasError  = true;
            ErrorText = ex.Message;
            StatusText = "Fehler beim Starten.";
            AppendLog($"Start fehlgeschlagen: {ex.Message}", ConnectorLogLevel.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await _server.StopAsync();
        IsRunning  = false;
        StatusText = "Server gestoppt.";
        AppendLog("Server gestoppt.", ConnectorLogLevel.Info);
    }

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        OnPropertyChanged(nameof(ShowEmptyRunning));
        OnPropertyChanged(nameof(HasLogEntries));
    }

    [RelayCommand]
    private void CopyUrl()
    {
        // Clipboard-Zugriff muss über UI erfolgen — Signal via StatusText
        StatusText = $"URL: {ServerUrl}";
    }

    private bool CanStart()    => !IsRunning;
    private bool CanStop()     =>  IsRunning;
    private bool CanStartAir() => !AirIsRunning && HasMcpBridge;
    private bool CanStopAir()  =>  AirIsRunning  && HasMcpBridge;

    // ── Commands — AIR / MCP-Bridge ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartAir))]
    private async Task StartAirAsync()
    {
        if (_airPanel is null) return;
        try
        {
            await _airPanel.StartAsync();
            AirIsRunning  = true;
            AirUrl        = _airPanel.Url;
            AirStatusText = $"MCP-Bridge läuft auf Port {_airPanel.Port}.";
            AppendLog("MCP-Bridge gestartet.", ConnectorLogLevel.Success);
            RefreshAirLists();
        }
        catch (Exception ex)
        {
            AirStatusText = $"Fehler: {ex.Message}";
            AppendLog($"MCP-Bridge Start fehlgeschlagen: {ex.Message}", ConnectorLogLevel.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopAir))]
    private async Task StopAirAsync()
    {
        if (_airPanel is null) return;
        await _airPanel.StopAsync();
        AirIsRunning  = false;
        AirStatusText = "MCP-Bridge gestoppt.";
        AppendLog("MCP-Bridge gestoppt.", ConnectorLogLevel.Info);
        RefreshAirLists();
    }

    [RelayCommand]
    private void RotateAirToken()
    {
        if (_airPanel is null) return;
        _airPanel.RotateToken();
        AppendLog("MCP-Bridge: Bridge-Token rotiert. Clients müssen neu konfiguriert werden.", ConnectorLogLevel.Warning);
    }

    [RelayCommand]
    private void CopyClaudeConfig()
    {
        if (_airPanel is null) return;
        CopyToClipboard(_airPanel.ClaudeDesktopConfig());
        AppendLog("Claude Desktop-Config in Zwischenablage kopiert.", ConnectorLogLevel.Info);
    }

    [RelayCommand]
    private void CopyCodexConfig()
    {
        if (_airPanel is null) return;
        CopyToClipboard(_airPanel.CodexConfig());
        AppendLog("Codex-Config in Zwischenablage kopiert.", ConnectorLogLevel.Info);
    }

    [RelayCommand]
    private async Task CancelAirExecutionAsync()
    {
        if (_airPanel is null) return;
        if (!ValidateAirAdminInput(AirAdminExecutionId, out var validation))
        {
            AirAdminStatus = validation;
            return;
        }
        if (!await ConfirmAirAdminAsync("Execution abbrechen",
                $"Execution {AirAdminExecutionId} wirklich abbrechen?")) return;

        var success = _airPanel.TryCancelExecution(
            AirAdminExecutionId.Trim(), AirAdminActor(), AirAdminReason.Trim(),
            IsAirAdmin, true, out var error);
        CompleteAirAdminAction(success, error, "Execution wurde abgebrochen.");
    }

    [RelayCommand]
    private async Task SetAirResourceEnabledAsync()
    {
        if (_airPanel is null) return;
        if (!ValidateAirAdminInput(AirAdminResourceId, out var validation))
        {
            AirAdminStatus = validation;
            return;
        }
        var state = AirAdminResourceEnabled ? "aktivieren" : "deaktivieren";
        if (!await ConfirmAirAdminAsync("Ressourcenstatus ändern",
                $"Ressource {AirAdminResourceId} wirklich {state}?")) return;

        var success = _airPanel.TrySetResourceEnabled(
            AirAdminResourceId.Trim(), AirAdminResourceEnabled, AirAdminActor(), AirAdminReason.Trim(),
            IsAirAdmin, true, out var error);
        CompleteAirAdminAction(success, error, $"Ressource wurde {state}.");
    }

    [RelayCommand]
    private async Task CreateAirBudgetAsync()
    {
        if (_airPanel is null) return;
        if (!ValidateAirAdminInput(AirBudgetCostUnit, out var validation) || AirBudgetHardLimit < 0)
        {
            AirAdminStatus = AirBudgetHardLimit < 0 ? "Budgetlimit darf nicht negativ sein." : validation;
            return;
        }
        if (!await ConfirmAirAdminAsync("Runtime-Budget setzen",
                $"Neues 24h-Runtime-Budget über {AirBudgetHardLimit} {AirBudgetCostUnit} anlegen?")) return;

        var now = DateTime.UtcNow;
        var budget = new AiResourceBudget
        {
            Scope = AiBudgetScope.Runtime,
            CostUnit = AirBudgetCostUnit.Trim(),
            Window = AiBudgetWindow.Day,
            HardLimit = AirBudgetHardLimit,
            WindowStartsAtUtc = now,
            WindowEndsAtUtc = now.AddDays(1)
        };
        var success = _airPanel.TryCreateBudget(
            budget, AirAdminActor(), AirAdminReason.Trim(), IsAirAdmin, true, out var error);
        CompleteAirAdminAction(success, error, "Runtime-Budget wurde angelegt.");
    }

    [RelayCommand]
    private async Task FailAirRecoveryAsync()
    {
        if (_airPanel is null) return;
        if (!ValidateAirAdminInput(AirAdminExecutionId, out var validation))
        {
            AirAdminStatus = validation;
            return;
        }
        if (!await ConfirmAirAdminAsync("Recovery abschließen",
                $"Execution {AirAdminExecutionId} nach Sichtprüfung als fehlgeschlagen abschließen?")) return;
        try
        {
            var success = _airPanel.ResolveRecoveryAsFailed(
                AirAdminExecutionId.Trim(), AirAdminActor(), AirAdminReason.Trim());
            CompleteAirAdminAction(success,
                success ? null : "Execution ist nicht recovery-pflichtig.",
                "Recovery wurde als fehlgeschlagen abgeschlossen.");
        }
        catch (Exception ex) when (ex is AiStateStoreException or InvalidOperationException)
        {
            CompleteAirAdminAction(false, ex.Message, "");
        }
    }

    [RelayCommand]
    private async Task RetryAirRecoveryAsync()
    {
        if (_airPanel is null) return;
        if (!ValidateAirAdminInput(AirAdminExecutionId, out var validation))
        {
            AirAdminStatus = validation;
            return;
        }
        if (!await ConfirmAirAdminAsync("Recovery-Retry erzeugen",
                $"Für Execution {AirAdminExecutionId} neue Task-/Execution-IDs erzeugen?")) return;
        try
        {
            var retry = _airPanel.CreateRecoveryRetry(
                AirAdminExecutionId.Trim(), AirAdminActor(), AirAdminReason.Trim());
            CompleteAirAdminAction(true, null,
                $"Recovery-Retry {retry.Request.Id} wurde erzeugt und nicht automatisch gestartet.");
        }
        catch (Exception ex) when (ex is AiStateStoreException or InvalidOperationException)
        {
            CompleteAirAdminAction(false, ex.Message, "");
        }
    }

    [RelayCommand]
    private async Task BackupAirStateAsync()
        => await ExecuteAirStateMaintenanceAsync(
            "State Store sichern", "Lokales AIR-State-Backup erstellen?",
            (actor, reason) => _airPanel!.BackupStateAsync(actor, reason, true));

    [RelayCommand]
    private async Task CompactAirStateAsync()
        => await ExecuteAirStateMaintenanceAsync(
            "State Store kompaktieren", "Backup erstellen und bestätigtes Journal kompaktieren?",
            (actor, reason) => _airPanel!.CompactStateAsync(actor, reason, true));

    [RelayCommand]
    private async Task RepairAirStateAsync()
        => await ExecuteAirStateMaintenanceAsync(
            "State Store reparieren", "Unveränderliches Backup erstellen und quarantänisierten Store prüfen/reparieren?",
            (actor, reason) => _airPanel!.RepairStateAsync(actor, reason, true));

    private async Task ExecuteAirStateMaintenanceAsync(
        string title,
        string confirmation,
        Func<string, string, ValueTask<AiStateMaintenanceOperationResult>> operation)
    {
        if (_airPanel is null) return;
        if (!ValidateAirMaintenanceInput(out var validation))
        {
            AirAdminStatus = validation;
            return;
        }
        if (!await ConfirmAirAdminAsync(title, confirmation)) return;
        try
        {
            var result = await operation(AirAdminActor(), AirAdminReason.Trim());
            AirAdminStatus = result.Success
                ? $"{title} abgeschlossen. Backup: {result.BackupId ?? "—"}"
                : $"{title} ohne Änderung: {result.ReasonCode ?? "unbekannt"}";
        }
        catch (Exception ex) when (ex is AiStateStoreException or InvalidOperationException or IOException)
        {
            AirAdminStatus = $"{title} fehlgeschlagen: {ex.Message}";
        }
        AppendLog(AirAdminStatus, AirAdminStatus.Contains("fehlgeschlagen", StringComparison.Ordinal)
            ? ConnectorLogLevel.Error : ConnectorLogLevel.Info);
        await RefreshAirStateStatusAsync();
        RefreshAirLists();
    }

    private void RefreshAirLists()
    {
        if (_airPanel is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            var sessions = _airPanel.ActiveSessions();
            AirSessions.Clear(); foreach (var s in sessions) AirSessions.Add(s);

            var locks = _airPanel.ActiveLocks();
            AirLocks.Clear(); foreach (var l in locks) AirLocks.Add(l);

            var tools = _airPanel.ActiveTools();
            AirTools.Clear(); foreach (var t in tools) AirTools.Add(t);

            var audit = _airPanel.RecentAudit(20);
            AirAudit.Clear(); foreach (var a in audit) AirAudit.Add(a);

            var messages = _airPanel.MessageInboxes();
            AirMessages.Clear(); foreach (var message in messages) AirMessages.Add(message);

            var executions = _airPanel.Executions();
            AirExecutions.Clear(); foreach (var execution in executions) AirExecutions.Add(execution);

            var resources = _airPanel.Resources();
            AirResources.Clear(); foreach (var resource in resources) AirResources.Add(resource);
        });
        _ = RefreshAirStateStatusAsync();
    }

    private async Task RefreshAirStateStatusAsync()
    {
        if (_airPanel is null) return;
        var diagnostics = await _airPanel.StateDiagnosticsAsync();
        var size = diagnostics.StoreSizeBytes / 1024d;
        Dispatcher.UIThread.Post(() => AirStateStatus =
            $"{diagnostics.Status} · Schema {diagnostics.SchemaVersion?.ToString() ?? "—"} · " +
            $"Sequenz {diagnostics.LastSequence} · Snapshot {diagnostics.SnapshotSequence} · {size:F1} KiB" +
            (string.IsNullOrWhiteSpace(diagnostics.RedactedMessage)
                ? ""
                : $" · {diagnostics.RedactedMessage}"));
    }

    private void PersistPhase8McpAccess()
    {
        if (_airPanel is null) return;
        _airPanel.SetPhase8McpAccess(AirAllowCollaboration, AirAllowScheduling, AirAllowResourceRead);
        _ = _config.SaveAsync();
        RefreshAirLists();
    }

    private bool ValidateAirAdminInput(string value, out string error)
    {
        if (!IsAirAdmin)
        {
            error = "Lokale Administratorrechte erforderlich.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(AirAdminReason))
        {
            error = "Technische ID/Wert und Begründung sind erforderlich.";
            return false;
        }
        if (AirAdminReason.Length > 500)
        {
            error = "Begründung darf maximal 500 Zeichen enthalten.";
            return false;
        }
        error = "";
        return true;
    }

    private bool ValidateAirMaintenanceInput(out string error)
    {
        if (!IsAirAdmin)
        {
            error = "Lokale Administratorrechte erforderlich.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(AirAdminReason) || AirAdminReason.Length > 500)
        {
            error = "Begründung (maximal 500 Zeichen) ist erforderlich.";
            return false;
        }
        error = "";
        return true;
    }

    private async Task<bool> ConfirmAirAdminAsync(string title, string message)
    {
        if (ConfirmAirAdminAction is null)
        {
            AirAdminStatus = "Bestätigungsdialog ist nicht verfügbar.";
            return false;
        }
        var confirmed = await ConfirmAirAdminAction(title, message);
        if (!confirmed) AirAdminStatus = "Aktion wurde nicht bestätigt.";
        return confirmed;
    }

    private string AirAdminActor()
        => _config.DeveloperDisplayName ?? _config.DeveloperEtwId ?? "local-admin";

    private void CompleteAirAdminAction(bool success, string? error, string successMessage)
    {
        AirAdminStatus = success ? successMessage : error ?? "Admin-Aktion fehlgeschlagen.";
        AppendLog(AirAdminStatus, success ? ConnectorLogLevel.Success : ConnectorLogLevel.Error);
        RefreshAirLists();
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                _ = desktop.MainWindow?.Clipboard?.SetTextAsync(text);
        }
        catch { /* Clipboard-Fehler ignorieren */ }
    }

    // ── Connector-Events (kommen aus Background-Thread) ───────────────────────

    private void OnConnectorConnected(string connectorLabel)
    {
        RequestCount++;
        Dispatcher.UIThread.Post(() =>
        {
            AppendLog($"Connector verbunden: {connectorLabel}", ConnectorLogLevel.Info);
        });
    }

    private void OnPatchProposalReceived(string proposalId, AiPatchRequest request)
    {
        PendingPatches++;
        Dispatcher.UIThread.Post(() =>
        {
            AppendLog(
                $"Patch-Vorschlag eingegangen: {request.Patches.Count} Patch(es) von Connector.",
                ConnectorLogLevel.Warning);

            // Patch-Approval-Fenster öffnen
            ShowPatchApproval?.Invoke(proposalId, request);
        });
    }

    /// <summary>Wird vom PatchApprovalWindow nach User-Entscheidung aufgerufen.</summary>
    public void OnPatchDecision(string proposalId, bool approved)
    {
        // AIR-Pfad: Proposal kam von ApproveAndApplyPatchAsync
        if (_airPending.TryRemove(proposalId, out var tcs))
        {
            PendingPatches = Math.Max(0, PendingPatches - 1);
            var result = approved
                ? AiHostResult.Ok(new { message = "Patch genehmigt und angewendet." })
                : AiHostResult.Fail("Patch vom Nutzer abgelehnt.", "rejected");
            tcs.TrySetResult(result);
            AppendLog(
                approved ? $"AIR-Patch {proposalId}: Genehmigt." : $"AIR-Patch {proposalId}: Abgelehnt.",
                approved ? ConnectorLogLevel.Success : ConnectorLogLevel.Info);
            return;
        }

        // Connector-Pfad: klassischer HTTP-Connector
        if (approved)
            _server.ApprovePatch(proposalId);
        else
            _server.RejectPatch(proposalId);

        PendingPatches = Math.Max(0, PendingPatches - 1);
        AppendLog(
            approved ? $"Patch {proposalId}: Genehmigt." : $"Patch {proposalId}: Abgelehnt.",
            approved ? ConnectorLogLevel.Success : ConnectorLogLevel.Info);
    }

    // ── IModuleManagerAiBridge ────────────────────────────────────────────────

    /// <inheritdoc/>
    public AaiaProjectStatus GetStatus() => new()
    {
        Version          = System.Reflection.Assembly.GetExecutingAssembly()
                               .GetName().Version?.ToString(3) ?? "0.0.0",
        ConnectorRunning = _server.IsRunning,
        McpBridgeRunning = _airPanel?.IsRunning ?? false,
        CurrentProject   = string.IsNullOrEmpty(_currentContext?.DisplayName)
                               ? _currentContext?.ExtensionId
                               : _currentContext.DisplayName,
        PipelineStep     = _currentContext?.CurrentStep,
        TrustLevel       = _currentContext?.TrustLevel ?? "Unsigned",
        AaiasConnected   = _aaias?.IsConnected ?? false,
    };

    /// <inheritdoc/>
    public ModuleManagerProjectInfo? ResolveProject(string projectPath)
    {
        if (_projectRoot is null || _currentContext is null) return null;

        var root = Path.GetFullPath(_projectRoot);
        string normPath;
        try   { normPath = string.IsNullOrEmpty(projectPath) ? root : Path.GetFullPath(projectPath); }
        catch { return null; }

        if (!normPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !normPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        var projectType = _currentContext.ProjectType switch
        {
            "ClientPlugin" => NewProjectType.ClientPlugin,
            "HybridModule" => NewProjectType.HybridModule,
            "LanguagePack" => NewProjectType.LanguagePack,
            _              => NewProjectType.ServerModule,
        };

        var csproj = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(p =>
                    !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                    !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            : null;

        return new ModuleManagerProjectInfo
        {
            ProjectDir  = root,
            CsprojPath  = csproj,
            ProjectType = projectType,
            ProjectName = _currentContext.DisplayName,
            ExtensionId = _currentContext.ExtensionId,
        };
    }

    /// <inheritdoc/>
    public async Task<AiHostResult> ApproveAndApplyPatchAsync(AiPatchProposalInput input, CancellationToken ct)
    {
        if (ShowPatchApproval is null)
            return AiHostResult.Fail("Patch-Approval-UI nicht verfügbar (ShowPatchApproval nicht gesetzt).", "no_ui");

        // AiPatchProposalInput → AiPatchRequest konvertieren
        var patchReq = new AiPatchRequest
        {
            ProtocolVersion = AiConnectorProtocol.ProtocolVersion,
            Rationale       = input.Reason,
            Patches         = input.Changes.Select(c => new AiPatchItem
            {
                Kind        = "FullFileReplacement",
                TargetFile  = c.RelativePath,
                Content     = c.Content,
                Language    = Path.GetExtension(c.RelativePath).TrimStart('.'),
                Description = $"Operation: {c.Operation}",
            }).ToList(),
        };

        var proposalId = "air-" + Guid.NewGuid().ToString("N")[..12];
        var tcs = new TaskCompletionSource<AiHostResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _airPending[proposalId] = tcs;

        using (ct.Register(() =>
        {
            if (_airPending.TryRemove(proposalId, out var pending))
                pending.TrySetCanceled(ct);
        }))
        {
            Dispatcher.UIThread.Post(() =>
            {
                PendingPatches++;
                AppendLog($"AIR-Patch eingegangen: {patchReq.Patches.Count} Datei(en). Warte auf Genehmigung.", ConnectorLogLevel.Warning);
                ShowPatchApproval?.Invoke(proposalId, patchReq);
            });

            return await tcs.Task.ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<AiHostResult> CreateProjectAsync(AiProjectCreateInput input, CancellationToken ct)
    {
        try
        {
            var projectType = input.ExtensionKind switch
            {
                "Plugin" or "ClientPlugin" => NewProjectType.ClientPlugin,
                "Hybrid" or "HybridModule" => NewProjectType.HybridModule,
                "LanguagePack"             => NewProjectType.LanguagePack,
                _                          => NewProjectType.ServerModule,
            };

            var raw      = string.IsNullOrWhiteSpace(input.Idea) ? "NewExtension" : input.Idea;
            var safeName = Regex.Replace(raw, @"[^a-zA-Z0-9 _-]", "").Trim().Replace(' ', '_');
            if (safeName.Length > 40) safeName = safeName[..40];
            if (string.IsNullOrEmpty(safeName)) safeName = "NewExtension";

            var opts = new ScaffoldOptions(
                Type:        projectType,
                Name:        safeName,
                Id:          safeName.ToLowerInvariant().Replace('_', '-'),
                Description: input.Idea,
                OutputPath:  _config.NewProjectPath,
                PublisherId: _config.DeveloperEtwId ?? "",
                SdkPath:     _config.SdkPath
            );

            var projectPath = await ProjectScaffoldingService.ScaffoldAsync(opts).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
                AppendLog($"AIR: Projekt erstellt — {projectPath}", ConnectorLogLevel.Success));

            return AiHostResult.Ok(new { projectPath, projectType = projectType.ToString(), name = safeName });
        }
        catch (OperationCanceledException)
        {
            return AiHostResult.Fail("Projekt-Erstellung abgebrochen.", "cancelled");
        }
        catch (Exception ex)
        {
            return AiHostResult.Fail($"Projekt-Erstellung fehlgeschlagen: {ex.Message}", "scaffold_error");
        }
    }

    // ── Log-Hilfsmethode ──────────────────────────────────────────────────────

    private void AppendLog(string message, ConnectorLogLevel level)
    {
        var entry = new ConnectorLogEntry(DateTime.Now, message, level);
        Log.Insert(0, entry);     // neueste oben
        while (Log.Count > 200)   // max. 200 Einträge
            Log.RemoveAt(Log.Count - 1);
        OnPropertyChanged(nameof(ShowEmptyRunning));
        OnPropertyChanged(nameof(HasLogEntries));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _server.ConnectorConnected    -= OnConnectorConnected;
        _server.PatchProposalReceived -= OnPatchProposalReceived;
        _ = _server.StopAsync();
        _server.Dispose();

        if (_airPanel is not null)
            _ = _airPanel.StopAsync();

        // Alle wartenden AIR-Proposals abbrechen
        foreach (var (_, tcs) in _airPending)
            tcs.TrySetCanceled();
        _airPending.Clear();
    }
}

// ── Log-Modell ────────────────────────────────────────────────────────────────

public enum ConnectorLogLevel { Info, Warning, Error, Success }

public sealed class ConnectorLogEntry(DateTime time, string message, ConnectorLogLevel level)
{
    public string Time    { get; } = time.ToString("HH:mm:ss");
    public string Message { get; } = message;
    public ConnectorLogLevel Level { get; } = level;

    public string LevelLabel => Level switch
    {
        ConnectorLogLevel.Warning => "⚠",
        ConnectorLogLevel.Error   => "✗",
        ConnectorLogLevel.Success => "✓",
        _                         => "·"
    };

    public string LevelColor => Level switch
    {
        ConnectorLogLevel.Warning => "#f59e0b",
        ConnectorLogLevel.Error   => "#f87171",
        ConnectorLogLevel.Success => "#6fcf97",
        _                         => "#8892a4"
    };
}
