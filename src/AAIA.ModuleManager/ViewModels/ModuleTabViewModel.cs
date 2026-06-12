using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class ModuleTabViewModel : ObservableObject
{
    private readonly AppConfig _cfg;

    [ObservableProperty] private string _projectPath      = "";
    [ObservableProperty] private bool   _projectLoaded    = false;
    [ObservableProperty] private string _projectType      = "";
    [ObservableProperty] private string _projectVersion   = "";
    [ObservableProperty] private string _contractsRef     = "";
    [ObservableProperty] private string _contractsRefColor = "#e0e0e0";
    [ObservableProperty] private bool   _contractsOutdated = false;
    [ObservableProperty] private bool   _isBusy           = false;
    [ObservableProperty] private string _log              = "";

    public ModuleTabViewModel(AppConfig cfg) => _cfg = cfg;

    [RelayCommand]
    private async Task Browse()
    {
        var win = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lf
            ? lf.MainWindow : null;
        if (win == null) return;

        var folders = await win.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Projektordner wählen" });

        if (folders.Count > 0)
        {
            ProjectPath = folders[0].TryGetLocalPath() ?? "";
            await LoadProjectAsync();
        }
    }

    private async Task LoadProjectAsync()
    {
        var dir = ProjectPath;
        if (!Directory.Exists(dir)) return;

        // Erkenne Typ anhand aaia-extension.json
        var extJson = Directory.GetFiles(dir, "aaia-extension.json", SearchOption.AllDirectories)
                               .FirstOrDefault();
        if (extJson == null)
        {
            AppendLog("Keine aaia-extension.json gefunden — kein AAIA-Modul/Plugin?");
            return;
        }

        var json    = await File.ReadAllTextAsync(extJson);
        ProjectType = json.Contains("\"module\"") ? "Modul" :
                      json.Contains("\"plugin\"") ? "Plugin" : "Unbekannt";

        // .csproj finden
        var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        ProjectVersion  = csproj != null ? RegistryService.GetProjectVersion(csproj)  ?? "–" : "–";
        var contractsVer = csproj != null ? RegistryService.GetContractsVersion(csproj) : null;

        var latestNuGet = await NuGetService.GetLatestVersionAsync();

        if (contractsVer == null)
        {
            ContractsRef      = "keine Referenz";
            ContractsRefColor = "#8892a4";
        }
        else if (latestNuGet != null && contractsVer != latestNuGet)
        {
            ContractsRef      = $"{contractsVer} (neu: {latestNuGet})";
            ContractsRefColor = "#f77f00";
            ContractsOutdated = true;
        }
        else
        {
            ContractsRef      = contractsVer;
            ContractsRefColor = "#06d6a0";
            ContractsOutdated = false;
        }

        ProjectLoaded = true;
        AppendLog($"✓ Projekt geladen: {Path.GetFileName(dir)} ({ProjectType}) v{ProjectVersion}");
    }

    [RelayCommand]
    private async Task ScaffoldModule() => await ScaffoldAsync("module");

    [RelayCommand]
    private async Task ScaffoldPlugin() => await ScaffoldAsync("plugin");

    private async Task ScaffoldAsync(string templateType)
    {
        IsBusy = true;
        AppendLog($"▶ Neues {templateType} aus Template scaffolden…");

        var templateRepo = templateType == "module"
            ? "https://github.com/illeArts/aaia-module-template"
            : "https://github.com/illeArts/aaia-plugin-template";

        var win = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lf
            ? lf.MainWindow : null;

        var folders = win != null
            ? await win.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Zielordner wählen" })
            : [];

        if (folders.Count == 0) { IsBusy = false; return; }

        var targetDir = folders[0].TryGetLocalPath() ?? "";

        var exit = await ProcessRunner.GitAsync(
            $"clone {templateRepo}", targetDir, AppendLog);

        if (exit == 0)
        {
            ProjectPath = Path.Combine(targetDir,
                templateType == "module" ? "aaia-module-template" : "aaia-plugin-template");
            await LoadProjectAsync();
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task Pull()
    {
        if (!ProjectLoaded || IsBusy) return;
        IsBusy = true;
        AppendLog("▶ git pull…");
        await ProcessRunner.GitAsync("pull --rebase", ProjectPath, AppendLog);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Build()
    {
        if (!ProjectLoaded || IsBusy) return;
        IsBusy = true;
        AppendLog("▶ dotnet build…");
        await ProcessRunner.DotnetAsync("build -c Release", ProjectPath, AppendLog);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task BumpAndPush()
    {
        if (!ProjectLoaded || IsBusy) return;
        IsBusy = true;
        AppendLog("▶ Version bumpen und pushen…");

        // Version in .csproj hochsetzen (Patch)
        var csproj = Directory.GetFiles(ProjectPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        if (csproj != null)
        {
            var content = await File.ReadAllTextAsync(csproj);
            var match   = System.Text.RegularExpressions.Regex.Match(
                content, @"<Version>(\d+)\.(\d+)\.(\d+)</Version>");
            if (match.Success)
            {
                var patch   = int.Parse(match.Groups[3].Value) + 1;
                var newVer  = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{patch}";
                var updated = content.Replace(match.Value, $"<Version>{newVer}</Version>");
                await File.WriteAllTextAsync(csproj, updated);
                AppendLog($"  Version → {newVer}");
            }
        }

        await ProcessRunner.GitAsync("add -A", ProjectPath, AppendLog);
        await ProcessRunner.GitAsync($"commit -m \"chore: bump version\"", ProjectPath, AppendLog);
        await ProcessRunner.GitAsync("push", ProjectPath, AppendLog);

        await LoadProjectAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task Register()
    {
        if (!ProjectLoaded || IsBusy) return;
        IsBusy = true;
        AppendLog("▶ In Extension Registry eintragen…");

        var csproj = Directory.GetFiles(ProjectPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        var name      = Path.GetFileName(ProjectPath);
        var version   = csproj != null ? RegistryService.GetProjectVersion(csproj) ?? "?" : "?";
        var contracts = csproj != null ? RegistryService.GetContractsVersion(csproj) ?? "?" : "?";

        var entry = new RegistryEntry(
            Name:        name,
            Type:        ProjectType.ToLower(),
            Version:     version,
            Contracts:   contracts,
            Author:      "IleArts",
            Description: $"AAIA {ProjectType} — {name}",
            Repository:  "https://github.com/illeArts/" + name.ToLower()
        );

        var svc = new RegistryService(_cfg.RegistryPath);
        await svc.AddOrUpdateAsync(entry);

        // Registry-Repo committen
        await ProcessRunner.GitAsync("add -A", _cfg.RegistryPath, AppendLog);
        await ProcessRunner.GitAsync(
            $"commit -m \"registry: add/update {name} v{version}\"",
            _cfg.RegistryPath, AppendLog);
        await ProcessRunner.GitAsync("push", _cfg.RegistryPath, AppendLog);

        AppendLog($"✅ {name} v{version} in Registry eingetragen und gepusht.");
        IsBusy = false;
    }

    [RelayCommand]
    private async Task UpdateContracts()
    {
        if (!ProjectLoaded || IsBusy) return;
        IsBusy = true;
        AppendLog("▶ Contracts auf neueste Version updaten…");

        var latest = await NuGetService.GetLatestVersionAsync();
        if (latest == null) { AppendLog("❌ NuGet nicht erreichbar."); IsBusy = false; return; }

        var csproj = Directory.GetFiles(ProjectPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        if (csproj != null)
        {
            var content = await File.ReadAllTextAsync(csproj);
            var updated = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"(Include=""AAIA\.Shared\.Contracts""\s+Version="")[^""]*("")",
                $"${{1}}{latest}${{2}}");
            await File.WriteAllTextAsync(csproj, updated);
            AppendLog($"✓ Contracts → {latest}");
        }

        await LoadProjectAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private void ClearLog() => Log = "";

    private void AppendLog(string line) => Log += line + "\n";
}
