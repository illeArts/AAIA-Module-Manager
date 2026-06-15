using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AAIA.ModuleManager.ViewModels;

public sealed partial class TesterTabViewModel : ObservableObject, IDisposable
{
    private readonly AaiasConnectionService _aaias = new();
    private readonly FileWatcherService     _watcher = new();

    /// <summary>Shared AAIAS connection — auch von LicensesTabViewModel für DeviceId genutzt.</summary>
    public AaiasConnectionService AaiasConn => _aaias;
    private CancellationTokenSource?        _logStreamCts;

    // ── Connection ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _aaiasUrl      = "http://localhost:5174";
    [ObservableProperty] private string _aaiasUsername  = "";
    [ObservableProperty] private string _aaiasPassword  = "";
    [ObservableProperty] private string _connectionStatus = "Nicht verbunden";
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private bool   _devModeActive;
    [ObservableProperty] private bool   _isBusy;

    // ── Project ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string  _projectPath     = "";
    [ObservableProperty] private string  _projectName     = "";
    [ObservableProperty] private string  _extensionId     = "";
    [ObservableProperty] private string  _projectVersion  = "";
    [ObservableProperty] private bool    _isWatching;
    [ObservableProperty] private bool    _hasUnsavedChanges;
    [ObservableProperty] private string? _lastChangedFile;
    [ObservableProperty] private string? _publishPath;

    // ── IDEs ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private List<IdeInfo> _detectedIdes = [];
    [ObservableProperty] private IdeInfo?      _selectedIde;

    // ── Installed Extensions ──────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AaiasExtensionInfo> _installedExtensions = [];
    [ObservableProperty] private AaiasExtensionInfo? _selectedExtension;

    // ── WorkOrder Simulator ───────────────────────────────────────────────────

    [ObservableProperty] private string _workOrderJson     = "{\n  \"type\": \"\",\n  \"payload\": {}\n}";
    [ObservableProperty] private string _workOrderResult   = "";
    [ObservableProperty] private bool   _workOrderRunning;

    // ── Log ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool   _logStreaming;

    // ── Build output ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _buildOutput = "";
    [ObservableProperty] private bool   _buildRunning;
    [ObservableProperty] private bool?  _lastBuildSuccess;

    // ── Install/Enable status ─────────────────────────────────────────────────

    [ObservableProperty] private string _installStatus = "";
    [ObservableProperty] private bool?  _lastInstallSuccess;

    // ── Init ───────────────────────────────────────────────────────────────────

    public TesterTabViewModel()
    {
        DetectedIdes = IdeDetectionService.Detect();
        SelectedIde  = DetectedIdes.FirstOrDefault(i => i.Installed);

        _watcher.Changed += OnProjectFileChanged;
    }

    public async Task InitAsync(AppConfig cfg)
    {
        AaiasUrl      = cfg.AaiasUrl;
        AaiasUsername = cfg.AaiasUsername;
        AaiasPassword = cfg.AaiasPassword;
    }

    // ── Connect ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy           = true;
        ConnectionStatus = "Verbinde…";
        LogLine("→ Verbinde mit AAIAS…");

        var err = await _aaias.ConnectAsync(AaiasUrl, AaiasUsername, AaiasPassword);
        if (err != null)
        {
            ConnectionStatus = $"Fehler: {err}";
            IsConnected      = false;
            IsBusy           = false;
            LogLine($"✗ {err}");
            return;
        }

        IsConnected      = true;
        ConnectionStatus = $"Verbunden{(string.IsNullOrEmpty(AaiasUsername) ? "" : $" als {AaiasUsername}")}";
        LogLine("✓ Verbunden mit AAIAS");

        DevModeActive = await _aaias.IsDevModeActiveAsync();
        LogLine(DevModeActive ? "✓ Developer Mode aktiv" : "⚠ Developer Mode nicht aktiv (Hot-Reload, Simulate deaktiviert)");

        await RefreshInstalledAsync();
        IsBusy = false;
    }

    private bool CanConnect() => !IsBusy && !string.IsNullOrWhiteSpace(AaiasUrl);

    [RelayCommand]
    private void Disconnect()
    {
        _aaias.Disconnect();
        IsConnected      = false;
        ConnectionStatus = "Getrennt";
        StopLogStream();
        LogLine("✗ Verbindung getrennt");
    }

    // ── Project ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseProjectAsync()
    {
        // Opens folder picker via Avalonia — handled in code-behind via callback
        // ViewModel raises an event; View provides the result
        BrowseRequested?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public event EventHandler? BrowseRequested;

    public void LoadProject(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        ProjectPath = folderPath;
        ProjectName = Path.GetFileName(folderPath);

        // Read aaia-extension.json for id
        var manifestPath = Path.Combine(folderPath, "aaia-extension.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                ExtensionId = doc.RootElement.GetStringOrEmpty("id");
            }
            catch { ExtensionId = ""; }
        }
        else
        {
            ExtensionId = "";
        }

        // Read version from .csproj
        var csproj = Directory.GetFiles(folderPath, "*.csproj").FirstOrDefault();
        if (csproj != null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(csproj), @"<Version>(.*?)<\/Version>");
            ProjectVersion = match.Success ? match.Groups[1].Value : "";
        }

        // Derive publish output path: bin/Release/net8.0/publish
        var possiblePublish = Path.Combine(folderPath, "bin", "Release", "net8.0", "publish");
        PublishPath = Directory.Exists(possiblePublish) ? possiblePublish : null;

        // Start watching
        _watcher.Watch(folderPath);
        IsWatching        = true;
        HasUnsavedChanges = false;

        LogLine($"📁 Projekt geladen: {ProjectName} (id={ExtensionId}, v{ProjectVersion})");
    }

    private void OnProjectFileChanged(object? sender, string file)
    {
        HasUnsavedChanges = true;
        LastChangedFile   = Path.GetFileName(file);
        // Don't auto-build — just notify
    }

    [RelayCommand]
    private void OpenInIde()
    {
        if (SelectedIde is null || string.IsNullOrEmpty(ProjectPath)) return;
        IdeDetectionService.OpenInIde(SelectedIde, ProjectPath);
        LogLine($"→ Öffne in {SelectedIde.Name}…");
    }

    // ── Build ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        BuildRunning   = true;
        LastBuildSuccess = null;
        BuildOutput    = "";
        LogLine("→ dotnet publish…");

        var output = new System.Text.StringBuilder();
        var outPath = Path.Combine(ProjectPath, "bin", "Release", "net8.0", "publish");

        // publish to bin/Release/net8.0/publish
        var exitCode = await ProcessRunner.DotnetAsync(
            $"publish \"{ProjectPath}\" -c Release -o \"{outPath}\" --nologo",
            ProjectPath,
            line =>
            {
                output.AppendLine(line);
                BuildOutput = output.ToString();
            });

        LastBuildSuccess  = exitCode == 0;
        BuildRunning      = false;
        HasUnsavedChanges = false;

        if (exitCode == 0)
        {
            PublishPath = outPath;
            LogLine($"✓ Build erfolgreich → {PublishPath}");
        }
        else
        {
            LogLine($"✗ Build fehlgeschlagen (exit {exitCode})");
        }
    }

    private bool CanBuild() => !BuildRunning && !string.IsNullOrEmpty(ProjectPath);

    // ── Install + Enable ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAndEnableAsync()
    {
        if (!IsConnected || string.IsNullOrEmpty(PublishPath)) return;
        IsBusy        = true;
        InstallStatus = "Installiere…";
        LogLine($"→ install-from-path: {PublishPath}");

        var (installResult, installErr) = await _aaias.InstallFromPathAsync(PublishPath, overwrite: true);
        if (installErr != null)
        {
            InstallStatus      = $"✗ Install: {installErr}";
            LastInstallSuccess = false;
            IsBusy             = false;
            LogLine($"✗ {installErr}");
            return;
        }

        var id = installResult!.Id;
        ExtensionId = id;
        LogLine($"✓ Installiert: {id} v{installResult.PreviousVersion ?? "?"} → aktuell");

        // Enable
        InstallStatus = "Aktiviere…";
        var (enableResult, enableErr) = await _aaias.EnableAsync(id, allowUnsigned: true);
        if (enableErr != null)
        {
            InstallStatus      = $"✗ Enable: {enableErr}";
            LastInstallSuccess = false;
            IsBusy             = false;
            LogLine($"✗ {enableErr}");
            return;
        }

        LastInstallSuccess = true;
        InstallStatus      = enableResult!.RestartRequired
            ? $"✓ Installiert — Neustart erforderlich"
            : "✓ Aktiv";
        LogLine($"✓ Extension aktiviert (restartRequired={enableResult.RestartRequired})");

        await RefreshInstalledAsync();
        IsBusy = false;
    }

    private bool CanInstall() => IsConnected && !IsBusy && !string.IsNullOrEmpty(PublishPath);

    // ── Hot-Reload (Dev Mode) ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanHotReload))]
    private async Task HotReloadAsync()
    {
        if (!DevModeActive || string.IsNullOrEmpty(ExtensionId)) return;
        IsBusy = true;
        LogLine($"→ Hot-Reload: {ExtensionId}");
        var (ok, err) = await _aaias.HotReloadAsync(ExtensionId);
        IsBusy = false;
        LogLine(ok ? "✓ Hot-Reload erfolgreich" : $"✗ {err}");
        await RefreshInstalledAsync();
    }

    private bool CanHotReload() => DevModeActive && !IsBusy && !string.IsNullOrEmpty(ExtensionId);

    // ── WorkOrder Simulator ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSimulate))]
    private async Task SimulateAsync()
    {
        if (!DevModeActive || string.IsNullOrEmpty(ExtensionId)) return;
        WorkOrderRunning = true;
        WorkOrderResult  = "";
        LogLine($"→ WorkOrder simulate: {ExtensionId}");

        var (resultJson, err) = await _aaias.SimulateWorkOrderAsync(ExtensionId, WorkOrderJson);
        WorkOrderRunning = false;

        if (err != null)
        {
            WorkOrderResult = $"Fehler:\n{err}";
            LogLine($"✗ Simulate: {err}");
        }
        else
        {
            // Pretty-print
            try
            {
                using var doc = JsonDocument.Parse(resultJson!);
                WorkOrderResult = JsonSerializer.Serialize(doc.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch { WorkOrderResult = resultJson ?? ""; }
            LogLine("✓ Simulation abgeschlossen");
        }
    }

    private bool CanSimulate() => DevModeActive && !WorkOrderRunning && !string.IsNullOrEmpty(ExtensionId);

    // ── Live Diagnostics ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartStream))]
    private async Task StartLogStreamAsync()
    {
        if (!DevModeActive) return;
        LogStreaming    = true;
        _logStreamCts   = new CancellationTokenSource();
        LogLine("→ Live-Log gestartet…");

        try
        {
            await foreach (var evt in _aaias.StreamLogsAsync(_logStreamCts.Token))
            {
                var icon = evt.Severity switch
                {
                    "Error"   => "🔴",
                    "Warning" => "🟡",
                    "Trace"   => "⚪",
                    _         => "🔵"
                };
                var hint = string.IsNullOrEmpty(evt.Hint) ? "" : $"\n   💡 {evt.Hint}";
                LogLine($"{icon} [{evt.Source}] {evt.Message}{hint}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogLine($"⚠ Stream-Fehler: {ex.Message}"); }
        finally
        {
            LogStreaming = false;
            LogLine("◼ Live-Log beendet");
        }
    }

    private bool CanStartStream() => DevModeActive && IsConnected && !LogStreaming;

    [RelayCommand]
    private void StopLogStream()
    {
        _logStreamCts?.Cancel();
        _logStreamCts = null;
    }

    // ── Diagnostics Snapshot ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDiagnostics))]
    private async Task ShowDiagnosticsAsync()
    {
        if (!DevModeActive || string.IsNullOrEmpty(ExtensionId)) return;
        var json = await _aaias.GetDiagnosticsJsonAsync(ExtensionId);
        if (json == null)
        {
            LogLine("⚠ Keine Diagnostik-Daten (Dev Mode aktiv?)");
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            BuildOutput = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch { BuildOutput = json; }
        LogLine($"✓ Diagnostik geladen für {ExtensionId}");
    }

    private bool CanDiagnostics() => DevModeActive && IsConnected && !string.IsNullOrEmpty(ExtensionId);

    // ── Refresh installed list ────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshInstalledAsync()
    {
        if (!IsConnected) return;
        try
        {
            var list = await _aaias.GetInstalledAsync();
            InstalledExtensions.Clear();
            foreach (var ext in list) InstalledExtensions.Add(ext);
        }
        catch (Exception ex) { LogLine($"⚠ Refresh fehlgeschlagen: {ex.Message}"); }
    }

    // ── Clear log ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearLog() => Log = "";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void LogLine(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        Log   += $"[{ts}] {line}\n";
    }

    public void Dispose()
    {
        _logStreamCts?.Cancel();
        _watcher.Dispose();
        _aaias.Dispose();
    }
}
