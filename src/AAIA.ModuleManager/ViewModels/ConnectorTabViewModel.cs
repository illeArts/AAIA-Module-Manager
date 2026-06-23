using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für den Connector-Server-Tab (Phase 6.3).
/// Steuert den lokalen AiConnectorServer und zeigt Status + Log an.
/// Öffnet PatchApprovalWindow wenn ein Connector einen Patch einreicht.
/// </summary>
public sealed partial class ConnectorTabViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig           _config;
    private readonly AiConnectorServer   _server;

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

    // ── Patch-Approval-Callback ───────────────────────────────────────────────

    /// <summary>
    /// Wird vom View (MainWindow) gesetzt — öffnet das PatchApprovalWindow.
    /// Parameter: proposalId, patchRequest.
    /// </summary>
    public Action<string, AiPatchRequest>? ShowPatchApproval { get; set; }

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public ConnectorTabViewModel(AppConfig config)
    {
        _config               = config;
        _allowPatchProposals  = config.AiConnector.AllowPatchProposals;

        _server = new AiConnectorServer(config);
        _server.ConnectorConnected   += OnConnectorConnected;
        _server.PatchProposalReceived += OnPatchProposalReceived;

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

    // ── Commands ──────────────────────────────────────────────────────────────

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

    private bool CanStart() => !IsRunning;
    private bool CanStop()  =>  IsRunning;

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
        if (approved)
            _server.ApprovePatch(proposalId);
        else
            _server.RejectPatch(proposalId);

        PendingPatches = Math.Max(0, PendingPatches - 1);
        AppendLog(
            approved ? $"Patch {proposalId}: Genehmigt." : $"Patch {proposalId}: Abgelehnt.",
            approved ? ConnectorLogLevel.Success : ConnectorLogLevel.Info);
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
        _server.ConnectorConnected   -= OnConnectorConnected;
        _server.PatchProposalReceived -= OnPatchProposalReceived;
        _ = _server.StopAsync();
        _server.Dispose();
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
