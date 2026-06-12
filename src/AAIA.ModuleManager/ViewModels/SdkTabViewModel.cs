using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class SdkTabViewModel : ObservableObject
{
    private readonly AppConfig _cfg;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _localVersion  = "–";
    [ObservableProperty] private string _nuGetVersion  = "–";
    [ObservableProperty] private string _nuGetVersionColor = "#e0e0e0";
    [ObservableProperty] private string _statusText    = "Wird geladen…";
    [ObservableProperty] private string _statusColor   = "#8892a4";
    [ObservableProperty] private string _log           = "";
    [ObservableProperty] private bool   _isBusy        = false;
    [ObservableProperty] private bool   _nuGetPolling  = false;
    [ObservableProperty] private double _progress      = 0;
    [ObservableProperty] private string _progressLabel = "";

    public SdkTabViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        LocalVersion = ReadLocalVersion();

        var nuget = await NuGetService.GetLatestVersionAsync();
        if (nuget == null)
        {
            NuGetVersion = "–";
            StatusText   = "NuGet nicht erreichbar";
            StatusColor  = "#f77f00";
        }
        else
        {
            NuGetVersion = nuget;
            if (LocalVersion == nuget)
            {
                StatusText  = "✓ Aktuell";
                StatusColor = "#06d6a0";
                NuGetVersionColor = "#06d6a0";
            }
            else
            {
                StatusText  = "↑ Update verfügbar";
                StatusColor = "#f77f00";
                NuGetVersionColor = "#f77f00";
            }
        }
    }

    private string ReadLocalVersion()
    {
        var csproj = Path.Combine(_cfg.SdkPath,
            "src", "AAIA.Shared.Contracts", "AAIA.Shared.Contracts.csproj");
        if (!File.Exists(csproj)) return "nicht gefunden";
        var m = Regex.Match(File.ReadAllText(csproj), @"<Version>([^<]+)</Version>");
        return m.Success ? m.Groups[1].Value : "–";
    }

    [RelayCommand] private Task ReleasePatch() => ReleaseAsync("patch");
    [RelayCommand] private Task ReleaseMinor() => ReleaseAsync("minor");
    [RelayCommand] private Task ReleaseMajor() => ReleaseAsync("major");

    private async Task ReleaseAsync(string bumpType)
    {
        if (IsBusy) return;
        IsBusy       = true;
        NuGetPolling = false;
        Progress     = 0;
        Log          = "";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var script = Path.Combine(_cfg.SdkPath, "release-contracts.ps1");
            if (!File.Exists(script))
            {
                AppendLog($"❌ release-contracts.ps1 nicht gefunden: {script}");
                return;
            }

            AppendLog($"▶ Release ({bumpType}) gestartet…");
            ProgressLabel = "Bumpe Version und pushe…";
            Progress = 10;

            var exit = await ProcessRunner.RunPsAsync(
                script, $"-Bump {bumpType}",
                _cfg.SdkPath,
                line =>
                {
                    AppendLog(line);
                    if (line.Contains("Warte auf NuGet"))
                    {
                        NuGetPolling  = true;
                        ProgressLabel = line.Trim();
                    }
                    else if (line.Contains("NuGet hat"))
                    {
                        NuGetPolling  = false;
                        Progress      = 85;
                        ProgressLabel = line.Trim();
                    }
                    else if (line.Contains("✅"))
                    {
                        Progress      = 100;
                        ProgressLabel = "Fertig!";
                    }
                },
                ct);

            if (exit == 0)
            {
                AppendLog("✅ Release erfolgreich abgeschlossen.");
                await RefreshAsync();
            }
            else
                AppendLog($"⚠ Prozess beendet mit Exit-Code {exit}");
        }
        catch (OperationCanceledException)
        {
            AppendLog("⚡ Abgebrochen.");
        }
        finally
        {
            IsBusy       = false;
            NuGetPolling = false;
        }
    }

    [RelayCommand]
    private void ClearLog() => Log = "";

    private void AppendLog(string line)
    {
        Log += line + "\n";
    }
}
