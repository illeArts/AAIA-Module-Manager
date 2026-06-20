using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public enum WizardStep
{
    IdeaInput,
    IdeaResult,
    ProjectDetails,
    Success
}

public partial class NewProjectWizardViewModel : ObservableObject
{
    // ── Wizard-Navigation ─────────────────────────────────────────────────────

    [ObservableProperty] private WizardStep _currentStep = WizardStep.IdeaInput;

    // ── Step 0: Idee eingeben ─────────────────────────────────────────────────

    [ObservableProperty] private string _ideaText      = "";
    [ObservableProperty] private bool   _isAnalyzing   = false;
    [ObservableProperty] private string _analyzeStatus = "";

    public bool HasApiKey    => AiServiceFactory.Create(_config) is not null;
    public bool HasNoApiKey  => !HasApiKey;

    partial void OnIdeaTextChanged(string value)   => AnalyzeIdeaCommand.NotifyCanExecuteChanged();
    partial void OnIsAnalyzingChanged(bool value)  => AnalyzeIdeaCommand.NotifyCanExecuteChanged();

    // ── Step 1: Analyse-Ergebnis ──────────────────────────────────────────────

    [ObservableProperty] private IdeaAnalysisResult? _analysisResult;
    [ObservableProperty] private bool                 _analysisOffline = false;

    // ── Step 2: Projekttyp & Details ─────────────────────────────────────────

    [ObservableProperty] private NewProjectType _projectType = NewProjectType.ServerModule;

    public List<(NewProjectType Type, string Label, string Description)> ProjectTypes { get; } =
    [
        (NewProjectType.ServerModule, "Server-Modul (AAIAS)",
            "Läuft auf dem AAIAS-Server. Implementiert IAaiaModule → AddServices() + MapRoutes(). Plattformunabhängig."),
        (NewProjectType.ClientPlugin, "Client-Plugin (AAIAC)",
            "Erweitert die AAIAC-UI/Logik. Implementiert IAaiacPlugin aus AAIA.Client.Core."),
        (NewProjectType.HybridModule, "Hybrid-Modul (AAIAS + AAIAC)",
            "Braucht Server- und Client-Teil. Erstellt beide Projekte (Server/ + Client/) im selben Ordner."),
        (NewProjectType.LanguagePack, "Sprachpaket",
            "JSON-basierte Übersetzungen für AAIAS/AAIAC. Kein C#-Code erforderlich."),
    ];

    [ObservableProperty] private string _projectName   = "";
    [ObservableProperty] private string _projectId     = "";
    [ObservableProperty] private string _description   = "";
    [ObservableProperty] private string _outputPath    = "";
    [ObservableProperty] private string _errorMessage  = "";
    [ObservableProperty] private bool   _isBusy        = false;

    partial void OnProjectNameChanged(string value)       { UpdateId(); CreateCommand.NotifyCanExecuteChanged(); }
    partial void OnProjectTypeChanged(NewProjectType value) => UpdateId();
    partial void OnProjectIdChanged(string value)         => CreateCommand.NotifyCanExecuteChanged();
    partial void OnOutputPathChanged(string value)        => CreateCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)              => CreateCommand.NotifyCanExecuteChanged();

    private void UpdateId()
    {
        var prefix = ProjectType switch
        {
            NewProjectType.ServerModule => "aaia.module.",
            NewProjectType.ClientPlugin => "aaia.plugin.",
            NewProjectType.HybridModule => "aaia.hybrid.",
            NewProjectType.LanguagePack => "aaia.langpack.",
            _                           => "aaia."
        };
        var slug = Regex.Replace(ProjectName.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-');
        ProjectId = string.IsNullOrWhiteSpace(slug) ? "" : $"{prefix}{slug}";
    }

    // ── Step 3: Erfolg & nächste Schritte ────────────────────────────────────

    [ObservableProperty] private bool        _isBuildRunning = false;
    [ObservableProperty] private string      _buildOutput    = "";
    [ObservableProperty] private bool        _buildSuccess   = false;
    [ObservableProperty] private bool        _buildFailed    = false;
    [ObservableProperty] private BuildResult? _lastBuildResult;
    [ObservableProperty] private bool        _showRawOutput  = false;

    public ObservableCollection<BuildIssueViewModel> BuildIssues { get; } = new();

    public string RawOutputToggleLabel => ShowRawOutput ? "▲ Details verbergen" : "▶ Details anzeigen";

    partial void OnShowRawOutputChanged(bool value) => OnPropertyChanged(nameof(RawOutputToggleLabel));

    public bool   CanBuild             => ProjectType != NewProjectType.LanguagePack;
    public string? CreatedProjectPath  { get; private set; }

    /// <summary>Pfad zur .csproj (oder HybridModule Server-.csproj) für Restore/Build.</summary>
    private string? ResolvedCsprojPath()
    {
        if (CreatedProjectPath is null) return null;

        var searchDir = ProjectType == NewProjectType.HybridModule
            ? Path.Combine(CreatedProjectPath, "Server")
            : CreatedProjectPath;

        // Suche nach erster .csproj im Verzeichnis
        var files = Directory.GetFiles(searchDir, "*.csproj");
        return files.Length > 0 ? files[0] : null;
    }

    // ── Infrastruktur ─────────────────────────────────────────────────────────

    private readonly AppConfig          _config;
    private readonly IAiProviderService? _provider;

    public IStorageProvider? StorageProvider { get; set; }
    public List<IdeInfo>     InstalledIdes   { get; private set; } = [];
    public string            PublisherId     => _config.DeveloperEtwId ?? _config.DeveloperDisplayName ?? "ETW";

    public NewProjectWizardViewModel(AppConfig config)
    {
        _config       = config;
        _outputPath   = config.NewProjectPath;
        InstalledIdes = IdeDetectionService.Detect();
        _provider     = AiServiceFactory.Create(config);
    }

    // ── Step 0: Idee analysieren ──────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeIdeaAsync(CancellationToken ct)
    {
        if (!CanAnalyze()) return;

        IsAnalyzing   = true;
        AnalyzeStatus = "Idee wird analysiert...";

        IdeaAnalysisResult result;
        if (_provider is not null)
            result = await IdeaAnalyzerService.AnalyzeAsync(_provider, IdeaText, ct);
        else
            result = IdeaAnalyzerService.OfflineFallback();

        IsAnalyzing   = false;
        AnalyzeStatus = "";

        // Idee als Beschreibung vorbelegen (immer sinnvoll)
        if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(IdeaText))
            Description = IdeaText.Length > 200 ? IdeaText[..200] : IdeaText;

        AnalysisResult  = result;
        AnalysisOffline = result.IsOfflineFallback || result.MappedProjectType is null;

        if (!AnalysisOffline)
        {
            // Gültiger Vorschlag: Step 1 zeigen
            CurrentStep = WizardStep.IdeaResult;
        }
        else
        {
            // Kein Vorschlag: direkt zu Step 2
            CurrentStep = WizardStep.ProjectDetails;
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(IdeaText);

    /// <summary>Step 0 überspringen — direkt zu Projektdetails.</summary>
    [RelayCommand]
    private void SkipToManual()
    {
        // Idee als Beschreibung vorbelegen falls vorhanden
        if (string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(IdeaText))
            Description = IdeaText.Length > 200 ? IdeaText[..200] : IdeaText;

        AnalysisResult  = null;
        AnalysisOffline = false;
        CurrentStep     = WizardStep.ProjectDetails;
    }

    // ── Step 1: Vorschlag übernehmen / ablehnen ───────────────────────────────

    /// <summary>Vorschlag der KI übernehmen und zu Projektdetails.</summary>
    [RelayCommand]
    private void UseAnalysisSuggestion()
    {
        if (AnalysisResult?.MappedProjectType is { } type)
            ProjectType = type;
        CurrentStep = WizardStep.ProjectDetails;
    }

    /// <summary>KI-Vorschlag ignorieren, manuell auswählen.</summary>
    [RelayCommand]
    private void GoToManualSelection()
    {
        CurrentStep = WizardStep.ProjectDetails;
    }

    // ── Zurück ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Back()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.IdeaResult    => WizardStep.IdeaInput,
            WizardStep.ProjectDetails => (AnalysisResult is not null && !AnalysisResult.IsOfflineFallback)
                                            ? WizardStep.IdeaResult
                                            : WizardStep.IdeaInput,
            _                        => WizardStep.IdeaInput
        };
    }

    // ── Ordner-Picker ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PickOutputPathAsync()
    {
        if (StorageProvider is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title                  = "Ausgabepfad auswählen",
            AllowMultiple          = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(OutputPath)
        });
        if (folders.Count > 0)
            OutputPath = folders[0].TryGetLocalPath() ?? OutputPath;
    }

    // ── Projekt erstellen ─────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (!CanCreate()) return;

        IsBusy       = true;
        ErrorMessage = "";

        try
        {
            var opts = new ScaffoldOptions(
                Type        : ProjectType,
                Name        : ProjectName.Trim(),
                Id          : ProjectId.Trim(),
                Description : Description.Trim(),
                OutputPath  : OutputPath.Trim(),
                PublisherId : PublisherId,
                SdkPath     : _config.SdkPath
            );

            var projectDir = await ProjectScaffoldingService.ScaffoldAsync(opts);

            _config.NewProjectPath = OutputPath;
            _ = _config.SaveAsync();

            CreatedProjectPath = projectDir;
            CurrentStep        = WizardStep.Success;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreate() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ProjectName) &&
        !string.IsNullOrWhiteSpace(ProjectId)   &&
        !string.IsNullOrWhiteSpace(OutputPath);

    // ── Step 3: Projekt bauen ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task BuildProjectAsync(CancellationToken ct)
    {
        if (!CanBuild) return;
        var csproj = ResolvedCsprojPath();
        if (csproj is null) return;

        IsBuildRunning   = true;
        BuildOutput      = "";
        BuildSuccess     = false;
        BuildFailed      = false;
        LastBuildResult  = null;
        ShowRawOutput    = false;

        var result = await BuildRunnerService.RestoreAndBuildAsync(
            csproj,
            line => BuildOutput = BuildOutput + line + "\n",
            ct);

        // Optional KI-Anreicherung wenn Provider vorhanden (silent fail)
        if (!result.Success && _provider is not null)
            await BuildErrorAnalyzerService.EnrichWithAiAsync(result, _provider, ProjectName, ct);

        // BuildIssues-Collection befüllen
        BuildIssues.Clear();
        foreach (var vm in BuildIssueViewModel.From(result, id => ExecuteBuildActionCommand.Execute(id)))
            BuildIssues.Add(vm);

        LastBuildResult = result;
        BuildSuccess    = result.Success;
        BuildFailed     = !result.Success;
        IsBuildRunning  = false;
    }

    /// <summary>Nur dotnet restore ausführen (Repair-Aktion für NU/CS0246 Fehler).</summary>
    [RelayCommand]
    private async Task RestoreProjectAsync(CancellationToken ct)
    {
        var csproj = ResolvedCsprojPath();
        if (csproj is null) return;

        IsBuildRunning  = true;
        BuildOutput     = "";
        ShowRawOutput   = true; // Restore-Output immer sichtbar

        var result = await BuildRunnerService.RestoreOnlyAsync(
            csproj,
            line => BuildOutput = BuildOutput + line + "\n",
            ct);

        IsBuildRunning = false;

        // Nach erfolgreichen Restore gleich neu bauen
        if (result.Success && !ct.IsCancellationRequested)
            await BuildProjectAsync(ct);
    }

    /// <summary>Zeigt dotnet --info in BuildOutput an.</summary>
    [RelayCommand]
    private async Task ShowSdkInfoAsync(CancellationToken ct)
    {
        IsBuildRunning = true;
        BuildOutput    = "=== dotnet --info ===\n";
        ShowRawOutput  = true;

        var info        = await BuildRunnerService.GetSdkInfoAsync(ct);
        BuildOutput    += info;
        IsBuildRunning  = false;
    }

    /// <summary>Auslöser für Action-Buttons auf BuildIssues.</summary>
    [RelayCommand]
    private async Task ExecuteBuildActionAsync(string actionId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        switch (actionId)
        {
            case "restore":    await RestoreProjectAsync(cts.Token); break;
            case "sdk-info":   await ShowSdkInfoAsync(cts.Token); break;
            case "open-csproj":
                var f = ResolvedCsprojPath();
                if (f is not null)
                    Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
                break;
            case "open-nuget":
                Process.Start(new ProcessStartInfo("https://www.nuget.org") { UseShellExecute = true });
                break;
        }
    }

    [RelayCommand]
    private void ToggleRawOutput() => ShowRawOutput = !ShowRawOutput;

    // ── Im Explorer/Finder öffnen ─────────────────────────────────────────────

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (CreatedProjectPath is null || !Directory.Exists(CreatedProjectPath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo("explorer.exe", CreatedProjectPath) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start(new ProcessStartInfo("open", CreatedProjectPath) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", CreatedProjectPath) { UseShellExecute = true });
    }

    // ── In IDE öffnen ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenInIde(string ideName)
    {
        if (CreatedProjectPath is null) return;
        var ide = InstalledIdes.Find(i => i.Name == ideName);
        if (ide is null || !ide.Installed) return;
        IdeDetectionService.OpenInIde(ide, CreatedProjectPath);
    }
}
