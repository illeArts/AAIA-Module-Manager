using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class SetupTabViewModel : ObservableObject
{
    private readonly AppConfig _cfg;

    [ObservableProperty] private string _gitStatusIcon  = "⏳";
    [ObservableProperty] private string _gitStatusText  = "git — wird geprüft…";
    [ObservableProperty] private string _ghStatusIcon   = "⏳";
    [ObservableProperty] private string _ghStatusText   = "gh CLI — wird geprüft…";
    [ObservableProperty] private string _loginStatusIcon = "⏳";
    [ObservableProperty] private string _loginStatusText = "GitHub Login — wird geprüft…";
    [ObservableProperty] private string _dotnetStatusIcon = "⏳";
    [ObservableProperty] private string _dotnetStatusText = ".NET SDK — wird geprüft…";

    [ObservableProperty] private string _sdkPath      = "";
    [ObservableProperty] private string _monorepoPath = "";
    [ObservableProperty] private string _registryPath = "";
    [ObservableProperty] private string _log          = "";

    // V2 — AAIAS
    [ObservableProperty] private string _aaiasUrl      = "http://localhost:5174";
    [ObservableProperty] private string _aaiasUsername  = "";
    [ObservableProperty] private string _aaiasPassword  = "";

    public SetupTabViewModel(AppConfig cfg)
    {
        _cfg           = cfg;
        SdkPath        = cfg.SdkPath;
        MonorepoPath   = cfg.MonorepoPath;
        RegistryPath   = cfg.RegistryPath;
        AaiasUrl       = cfg.AaiasUrl;
        AaiasUsername  = cfg.AaiasUsername;
        AaiasPassword  = cfg.AaiasPassword;
        _ = CheckAllAsync();
    }

    [RelayCommand]
    private async Task CheckAll() => await CheckAllAsync();

    private async Task CheckAllAsync()
    {
        await CheckToolAsync("git",    "--version",
            ok  => { GitStatusIcon = "✅"; GitStatusText  = $"git  —  {ok.Trim()}"; },
            err => { GitStatusIcon = "❌"; GitStatusText  = "git nicht gefunden"; });

        await CheckToolAsync("gh",     "--version",
            ok  => { GhStatusIcon  = "✅"; GhStatusText   = $"gh CLI  —  {ok.Split('\n')[0].Trim()}"; },
            err => { GhStatusIcon  = "❌"; GhStatusText   = "gh CLI nicht installiert"; });

        await CheckToolAsync("gh",     "auth status",
            ok  => { LoginStatusIcon = "✅"; LoginStatusText = "Eingeloggt bei GitHub"; },
            err => { LoginStatusIcon = "⚠"; LoginStatusText = "Nicht eingeloggt — gh auth login ausführen"; });

        await CheckToolAsync("dotnet", "--version",
            ok  => { DotnetStatusIcon = "✅"; DotnetStatusText = $".NET SDK  —  {ok.Trim()}"; },
            err => { DotnetStatusIcon = "❌"; DotnetStatusText = ".NET SDK nicht gefunden"; });
    }

    private static async Task CheckToolAsync(
        string exe, string args,
        System.Action<string> onOk,
        System.Action<string> onFail)
    {
        var output = "";
        var exit = await ProcessRunner.RunAsync(exe, args, ".", line => output += line + "\n");
        if (exit == 0) onOk(output);
        else           onFail(output);
    }

    [RelayCommand]
    private async Task InstallGhCli()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            AppendLog("▶ Installiere GitHub CLI via Homebrew…");
            AppendLog("  (Homebrew muss bereits installiert sein: https://brew.sh)");
            await ProcessRunner.RunAsync("brew", "install gh", ".", AppendLog);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                     System.Runtime.InteropServices.OSPlatform.Windows))
        {
            AppendLog("▶ Installiere GitHub CLI via winget…");
            await ProcessRunner.RunAsync("winget", "install --id GitHub.cli -e", ".", AppendLog);
        }
        else
        {
            AppendLog("⚠ Automatische Installation nicht unterstützt.");
            AppendLog("  Bitte manuell installieren: https://cli.github.com");
        }
        await CheckAllAsync();
    }

    [RelayCommand]
    private async Task GhLogin()
    {
        AppendLog("▶ gh auth login (Browser öffnet sich)…");
        await ProcessRunner.GhAsync("auth login --web", ".", AppendLog);
        await CheckAllAsync();
    }

    [RelayCommand]
    private async Task CheckPermissions()
    {
        AppendLog("▶ GitHub Rechte prüfen…");
        await ProcessRunner.GhAsync("auth status --show-token", ".", AppendLog);
    }

    [RelayCommand]
    private async Task FixCredentials()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            AppendLog("▶ GitHub Credentials aus macOS Keychain entfernen…");
            // gh auth logout entfernt alle gespeicherten Tokens sauber
            await ProcessRunner.GhAsync("auth logout --hostname github.com", ".", AppendLog);
            AppendLog("  Credentials entfernt. Bitte danach gh auth login ausführen.");
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                     System.Runtime.InteropServices.OSPlatform.Windows))
        {
            AppendLog("▶ Git Credential Manager zurücksetzen…");
            await ProcessRunner.GitAsync("credential reject", ".", AppendLog);
            await ProcessRunner.RunAsync(
                "cmdkey", "/delete:LegacyGeneric:target=git:https://github.com", ".", AppendLog);
            AppendLog("  Bitte danach erneut gh auth login ausführen.");
        }
        else
        {
            AppendLog("▶ GitHub Credentials zurücksetzen…");
            await ProcessRunner.GhAsync("auth logout --hostname github.com", ".", AppendLog);
            AppendLog("  Bitte danach erneut gh auth login ausführen.");
        }
    }

    // ── Pfad-Picker ──────────────────────────────────────────────────────────

    [RelayCommand] private async Task BrowseSdk()      => SdkPath      = await PickFolderAsync() ?? SdkPath;
    [RelayCommand] private async Task BrowseMonorepo() => MonorepoPath = await PickFolderAsync() ?? MonorepoPath;
    [RelayCommand] private async Task BrowseRegistry() => RegistryPath = await PickFolderAsync() ?? RegistryPath;

    private static async Task<string?> PickFolderAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lf)
            return null;

        var result = await lf.MainWindow!.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Ordner wählen" });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    [RelayCommand]
    private async Task SaveConfig()
    {
        _cfg.SdkPath       = SdkPath;
        _cfg.MonorepoPath  = MonorepoPath;
        _cfg.RegistryPath  = RegistryPath;
        _cfg.AaiasUrl      = AaiasUrl;
        _cfg.AaiasUsername = AaiasUsername;
        _cfg.AaiasPassword = AaiasPassword;
        await _cfg.SaveAsync();
        AppendLog("✅ Konfiguration gespeichert.");
    }

    [RelayCommand]
    private void ClearLog() => Log = "";

    private void AppendLog(string line) => Log += line + "\n";
}
