using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using AAIA.ModuleManager.Services.Help;
using AAIA.ModuleManager.Services.Signing;

namespace AAIA.ModuleManager.ViewModels;

public enum WizardStep
{
    IdeaInput,
    IdeaResult,
    ProjectDetails,
    Success,
    Validation,
    PublishReadiness,
    Signature           // Phase 4.0 — Signatur & Vertrauen
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

    /// <summary>Ob der Projekttyp überhaupt bauen kann (nicht LanguagePack).</summary>
    public bool IsBuildableType => ProjectType != NewProjectType.LanguagePack;
    public string? CreatedProjectPath  { get; private set; }

    // ── Pipeline-Gate-Flags ───────────────────────────────────────────────────
    // Jedes Flag wird vom zugehörigen Command gesetzt — Gates verhindern das Überspringen.

    [ObservableProperty] private bool    _isProjectCreated;
    [ObservableProperty] private bool    _isValidated;
    [ObservableProperty] private bool    _hasValidationBlockers;
    [ObservableProperty] private bool    _isBuilt;
    [ObservableProperty] private bool    _buildSucceeded;
    [ObservableProperty] private bool    _isPublishReadinessChecked;
    [ObservableProperty] private bool    _hasPublishBlockers;
    [ObservableProperty] private bool    _isPackaged;
    [ObservableProperty] private string? _packageFilePath;
    [ObservableProperty] private bool    _isInspected;
    [ObservableProperty] private bool    _hasInspectionBlockers;
    [ObservableProperty] private bool    _isReleasePrepared;
    [ObservableProperty] private string? _releaseFolderPath;

    // ── Phase 4.0 Signatur-Flags ──────────────────────────────────────────────
    [ObservableProperty] private bool    _isSignaturePrepared;
    [ObservableProperty] private string  _trustLevel          = "Unsigned";
    [ObservableProperty] private string? _signatureInfoPath;

    // ── Phase 4.1 ETW-Signatur-Flags ─────────────────────────────────────────
    [ObservableProperty] private bool         _isEtwSigned;
    [ObservableProperty] private bool         _isEtwSignatureVerified;
    [ObservableProperty] private bool         _etwKeyExists;
    [ObservableProperty] private EtwKeyInfo?  _currentEtwKeyInfo;

    // Computed Gates — für IsEnabled-Bindings im AXAML
    public bool CanValidate                    => IsProjectCreated;
    public bool CanBuild                       => IsProjectCreated && IsValidated && !HasValidationBlockers;
    public bool CanCheckPublishReadiness       => CanBuild && IsBuilt && BuildSucceeded;
    public bool CanPackage                     => CanCheckPublishReadiness && IsPublishReadinessChecked && !HasPublishBlockers;
    public bool CanInspect                     => IsPackaged && File.Exists(PackageFilePath ?? "");
    public bool CanPrepareRelease              => IsInspected && !HasInspectionBlockers;
    public bool CanOpenReleaseFolder           => IsReleasePrepared && Directory.Exists(ReleaseFolderPath ?? "");
    public bool CanProceedToSignature          => IsReleasePrepared;
    public bool CanPrepareSignature            => IsReleasePrepared && !HasInspectionBlockers;
    public bool CanVerifySignaturePreparation  => IsSignaturePrepared && File.Exists(SignatureInfoPath ?? "");
    // Phase 4.1
    public bool CanGenerateEtwKey       => !string.IsNullOrWhiteSpace(PublisherId)
                                           && PublisherId != "ETW"
                                           && !EtwKeyExists;
    public bool CanCreateEtwSignature   => IsSignaturePrepared
                                           && EtwKeyExists
                                           && !HasInspectionBlockers;
    public bool CanVerifyEtwSignature   => IsEtwSigned
                                           && File.Exists(SignatureInfoPath ?? "");
    public bool CanContinueToMarketplace => IsEtwSignatureVerified
                                            && !HasInspectionBlockers
                                            && Services.Signing.TrustLevels.IsAtLeast(
                                                   TrustLevel,
                                                   Services.Signing.TrustLevels.EtwLocalVerified);

    // Alle Gates bei jeder Flag-Änderung benachrichtigen
    partial void OnIsProjectCreatedChanged(bool value)          => NotifyGates();
    partial void OnIsValidatedChanged(bool value)               => NotifyGates();
    partial void OnHasValidationBlockersChanged(bool value)     => NotifyGates();
    partial void OnIsBuiltChanged(bool value)                   => NotifyGates();
    partial void OnBuildSucceededChanged(bool value)            => NotifyGates();
    partial void OnIsPublishReadinessCheckedChanged(bool value) => NotifyGates();
    partial void OnHasPublishBlockersChanged(bool value)        => NotifyGates();
    partial void OnIsPackagedChanged(bool value)                => NotifyGates();
    partial void OnPackageFilePathChanged(string? value)        => NotifyGates();
    partial void OnIsInspectedChanged(bool value)               => NotifyGates();
    partial void OnHasInspectionBlockersChanged(bool value)     => NotifyGates();
    partial void OnIsReleasePreparedChanged(bool value)         => NotifyGates();
    partial void OnReleaseFolderPathChanged(string? value)      => NotifyGates();
    partial void OnIsSignaturePreparedChanged(bool value)       => NotifyGates();
    partial void OnSignatureInfoPathChanged(string? value)      => NotifyGates();
    partial void OnTrustLevelChanged(string value)              { NotifyGates(); OnPropertyChanged(nameof(SignatureStatusLabel)); }
    partial void OnIsEtwSignedChanged(bool value)               => NotifyGates();
    partial void OnIsEtwSignatureVerifiedChanged(bool value)    => NotifyGates();
    partial void OnEtwKeyExistsChanged(bool value)              => NotifyGates();

    private void NotifyGates()
    {
        OnPropertyChanged(nameof(CanValidate));
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanCheckPublishReadiness));
        OnPropertyChanged(nameof(CanPackage));
        OnPropertyChanged(nameof(CanInspect));
        OnPropertyChanged(nameof(CanPrepareRelease));
        OnPropertyChanged(nameof(CanOpenReleaseFolder));
        OnPropertyChanged(nameof(CanProceedToSignature));
        OnPropertyChanged(nameof(CanPrepareSignature));
        OnPropertyChanged(nameof(CanVerifySignaturePreparation));
        OnPropertyChanged(nameof(CanGenerateEtwKey));
        OnPropertyChanged(nameof(CanCreateEtwSignature));
        OnPropertyChanged(nameof(CanVerifyEtwSignature));
        OnPropertyChanged(nameof(CanContinueToMarketplace));
    }

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

    public IStorageProvider?      StorageProvider { get; set; }
    public Avalonia.Input.Platform.IClipboard? Clipboard { get; set; }

    /// <summary>
    /// Callback vom View: Öffnet das HelpCenter mit dem angegebenen Artikel.
    /// Wird von NewProjectWizardWindow.SetViewModel() gesetzt.
    /// </summary>
    public Action<string>? OpenHelpRequested { get; set; }
    public List<IdeInfo>          InstalledIdes   { get; private set; } = [];
    public string                 PublisherId     => _config.DeveloperEtwId ?? _config.DeveloperDisplayName ?? "ETW";

    public NewProjectWizardViewModel(AppConfig config)
    {
        _config       = config;
        _outputPath   = config.NewProjectPath;
        InstalledIdes = IdeDetectionService.Detect();
        _provider     = AiServiceFactory.Create(config);

        // ETW-Schlüsselstatus einmalig beim Start prüfen
        RefreshEtwKeyStatus();
    }

    private void RefreshEtwKeyStatus()
    {
        EtwKeyExists      = EtwKeyStoreService.HasKey(PublisherId);
        CurrentEtwKeyInfo = EtwKeyExists
            ? EtwKeyStoreService.GetKeyInfo(PublisherId)
            : null;
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
            IsProjectCreated   = true;
            // Reihenfolge erzwingen: Validation vor Build
            CurrentStep = WizardStep.Validation;
            using var valCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await ValidateProjectAsync(valCts.Token);
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
        if (!IsBuildableType) return;
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
        foreach (var vm in BuildIssueViewModel.From(result, id => ExecuteBuildActionCommand.Execute(id), OpenHelpRequested))
            BuildIssues.Add(vm);

        LastBuildResult = result;
        BuildSuccess    = result.Success;
        BuildFailed     = !result.Success;
        IsBuildRunning  = false;
        IsBuilt         = true;
        BuildSucceeded  = result.Success;
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

    // ── Step 4: Projektprüfung ────────────────────────────────────────────────

    [ObservableProperty] private ValidationResult? _validationResult;
    [ObservableProperty] private bool              _isValidating = false;

    public ObservableCollection<ValidationIssueViewModel> ValidationIssues { get; } = new();

    [RelayCommand]
    private async Task ValidateProjectAsync(CancellationToken ct)
    {
        if (CreatedProjectPath is null) return;

        IsValidating     = true;
        ValidationResult = null;
        ValidationIssues.Clear();

        var result = await ProjectValidationOrchestrator.ValidateAsync(
            projectDir:  CreatedProjectPath,
            projectType: ProjectType,
            aiProvider:  _provider,
            projectName: ProjectName,
            ct:          ct);

        foreach (var vm in ValidationIssueViewModel.From(result, id => ExecuteValidationActionCommand.Execute(id), OpenHelpRequested))
            ValidationIssues.Add(vm);

        ValidationResult       = result;
        IsValidating           = false;
        IsValidated            = true;
        HasValidationBlockers  = result.HasBlockers;
        // Step bleibt Validation — Navigation obliegt dem Aufrufer
    }

    /// <summary>Von Validation zu Build wechseln (Weiter: Bauen-Button).</summary>
    [RelayCommand]
    private void ProceedToBuild()
    {
        CurrentStep = WizardStep.Success;
    }

    /// <summary>Aktions-Buttons in den ValidationIssue-Karten.</summary>
    [RelayCommand]
    private async Task ExecuteValidationActionAsync(string actionId)
    {
        if (CreatedProjectPath is null) return;

        switch (actionId)
        {
            case "create-manifest":
            {
                await ManifestValidationService.CreateDefaultManifestAsync(
                    CreatedProjectPath,
                    id:          ProjectId,
                    displayName: ProjectName,
                    host:        ProjectType switch
                    {
                        NewProjectType.ClientPlugin => "AAIAC",
                        NewProjectType.HybridModule => "Hybrid",
                        _                           => "AAIAS"
                    },
                    kind:        ProjectType switch
                    {
                        NewProjectType.ClientPlugin => "Plugin",
                        NewProjectType.LanguagePack => "Connector",
                        _                           => "Module"
                    },
                    pluginClass: ProjectType switch
                    {
                        NewProjectType.ClientPlugin => "ClientPlugin",
                        NewProjectType.HybridModule => "HybridModule",
                        _                           => "ServerModule"
                    },
                    publisherId: PublisherId);
                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await ValidateProjectAsync(cts1.Token);
                break;
            }

            case "add-readme":
            {
                await ExtensionStructureValidator.CreateReadmeAsync(
                    CreatedProjectPath, ProjectName, Description);
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await ValidateProjectAsync(cts2.Token);
                break;
            }

            case "add-license-mit":
            {
                await ExtensionStructureValidator.CreateMitLicenseAsync(
                    CreatedProjectPath, PublisherId);
                using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await ValidateProjectAsync(cts3.Token);
                break;
            }

            case "open-manifest":
                OpenFile(Path.Combine(CreatedProjectPath, "aaia-manifest.json"));
                break;

            case "open-csproj":
                var csproj = ResolvedCsprojPath();
                if (csproj is not null) OpenFile(csproj);
                break;

            case "open-folder":
                OpenInExplorer();
                break;
        }
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ── Step 5: Veröffentlichung vorbereiten ─────────────────────────────────

    [ObservableProperty] private PackageInspectionResult? _inspectionResult;
    public ObservableCollection<PackageFileEntryViewModel> PackageFileEntries { get; } = new();

    [RelayCommand]
    private async Task InspectPackageAsync(CancellationToken ct)
    {
        if (PackageFilePath is null || !File.Exists(PackageFilePath)) return;

        await Task.Run(() =>
        {
            var result = ExtensionPackageInspectorService.Inspect(PackageFilePath);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                PackageFileEntries.Clear();
                foreach (var vm in PackageFileEntryViewModel.From(result))
                    PackageFileEntries.Add(vm);

                InspectionResult        = result;
                IsInspected             = true;
                HasInspectionBlockers   = result.HasBlockers;
            });
        }, ct);
    }

    [ObservableProperty] private ValidationResult? _publishReadinessResult;
    [ObservableProperty] private bool              _isCheckingReadiness = false;
    [ObservableProperty] private PackageResult?    _lastPackageResult;
    [ObservableProperty] private RegistryPreview?  _registryPreview;
    [ObservableProperty] private bool              _showRegistryPreview = false;
    public string RegistryPreviewJson => RegistryPreview?.ToDisplayJson() ?? "";

    partial void OnRegistryPreviewChanged(RegistryPreview? value)
        => OnPropertyChanged(nameof(RegistryPreviewJson));

    public ObservableCollection<ValidationIssueViewModel> PublishIssues { get; } = new();

    public string PublishStatusLabel => PublishReadinessResult switch
    {
        null                     => "Prüfung ausstehend",
        { HasBlockers: true }    => "Nicht veröffentlichbar",
        { WarningCount: > 0 }    => "Veröffentlichbar mit Hinweisen",
        _                        => "Veröffentlichbar ✓"
    };

    public string PublishStatusIcon => PublishReadinessResult switch
    {
        null                  => "⏳",
        { HasBlockers: true } => "🔴",
        { WarningCount: > 0 } => "🟡",
        _                     => "🟢"
    };

    partial void OnPublishReadinessResultChanged(ValidationResult? value)
    {
        OnPropertyChanged(nameof(PublishStatusLabel));
        OnPropertyChanged(nameof(PublishStatusIcon));
    }

    /// <summary>Von Build zu PublishReadiness wechseln und Prüfung starten.</summary>
    [RelayCommand]
    private async Task CheckPublishReadinessAsync(CancellationToken ct)
    {
        if (CreatedProjectPath is null) return;

        IsCheckingReadiness = true;
        PublishReadinessResult = null;
        PublishIssues.Clear();
        LastPackageResult = null;
        RegistryPreview   = null;
        ShowRegistryPreview = false;

        CurrentStep = WizardStep.PublishReadiness;

        var result = PublishReadinessService.Check(
            projectDir:  CreatedProjectPath,
            projectType: ProjectType,
            lastBuild:   LastBuildResult);

        foreach (var vm in ValidationIssueViewModel.From(result, ExecutePublishActionAsync, OpenHelpRequested))
            PublishIssues.Add(vm);

        PublishReadinessResult     = result;
        IsCheckingReadiness        = false;
        IsPublishReadinessChecked  = true;
        HasPublishBlockers         = result.HasBlockers;
    }

    /// <summary>Erstellt das .aaiaext-Paket.</summary>
    [RelayCommand]
    private async Task CreatePackageAsync(CancellationToken ct)
    {
        if (CreatedProjectPath is null) return;

        IsCheckingReadiness = true;
        var pkg = await ExtensionPackageService.CreatePackageAsync(
            CreatedProjectPath, ProjectType, ct: ct);
        LastPackageResult   = pkg;
        IsCheckingReadiness = false;

        if (pkg.Success)
        {
            IsPackaged      = true;
            PackageFilePath = pkg.PackagePath;
            // Automatisch inspizieren
            await InspectPackageAsync(ct);
            // Registry-Preview generieren
            await ShowRegistryPreviewAsync(ct);
        }
        else
        {
            // Paket-Issues in PublishIssues einfügen
            foreach (var issue in pkg.Issues)
                PublishIssues.Insert(0, new ValidationIssueViewModel(issue, ExecutePublishActionAsync, OpenHelpRequested));
        }
    }

    /// <summary>Zeigt die Registry-Vorschau.</summary>
    [RelayCommand]
    private async Task ShowRegistryPreviewAsync(CancellationToken ct)
    {
        if (CreatedProjectPath is null) return;
        RegistryPreview = await ExtensionRegistryPreviewService.GenerateAsync(
            CreatedProjectPath, _config, LastPackageResult, ct);
        ShowRegistryPreview = true;
    }

    /// <summary>Bereitet den Release-Ordner vor (Phase 3.3).</summary>
    [ObservableProperty] private ReleaseResult? _lastReleaseResult;
    [ObservableProperty] private bool           _isPreparingRelease = false;

    [RelayCommand]
    private async Task PrepareReleaseAsync(CancellationToken ct)
    {
        if (CreatedProjectPath is null)   return;
        if (LastPackageResult  is null)   return;
        if (InspectionResult   is null)   return;
        if (!CanPrepareRelease)           return;

        IsPreparingRelease = true;

        var releaseResult = await ReleasePrepareService.PrepareAsync(
            projectDir:       CreatedProjectPath,
            packageResult:    LastPackageResult,
            inspectionResult: InspectionResult,
            developerEtwId:   _config.DeveloperEtwId ?? _config.DeveloperDisplayName ?? "",
            ct:               ct);

        LastReleaseResult = releaseResult;

        if (releaseResult.Success)
        {
            IsReleasePrepared = true;
            ReleaseFolderPath = releaseResult.ReleaseFolderPath;
        }
        else
        {
            foreach (var issue in releaseResult.Issues)
                PublishIssues.Insert(0, new ValidationIssueViewModel(issue, ExecutePublishActionAsync, OpenHelpRequested));
        }

        IsPreparingRelease = false;
    }

    [RelayCommand]
    private void OpenReleaseFolder()
    {
        var dir = ReleaseFolderPath;
        if (dir is null || !Directory.Exists(dir)) return;
        OpenFolder(dir);
    }

    [RelayCommand]
    private void NavigateToSignature()
    {
        RefreshEtwKeyStatus();
        CurrentStep = WizardStep.Signature;
    }

    // ── Phase 4.0: Signaturvorbereitung ──────────────────────────────────────

    [ObservableProperty] private bool              _isPreparingSignature    = false;
    [ObservableProperty] private bool              _isVerifyingSignature    = false;
    [ObservableProperty] private SignatureResult?  _lastSignatureResult;
    [ObservableProperty] private VerificationResult? _lastVerificationResult;

    // ── Phase 4.1: ETW-Signatur ───────────────────────────────────────────────

    [ObservableProperty] private bool                   _isGeneratingEtwKey      = false;
    [ObservableProperty] private bool                   _isCreatingEtwSignature  = false;
    [ObservableProperty] private bool                   _isVerifyingEtwSignature = false;
    [ObservableProperty] private EtwSigningResult?      _lastEtwSigningResult;
    [ObservableProperty] private EtwVerificationResult? _lastEtwVerificationResult;

    public string SignatureStatusLabel => Services.Signing.TrustLevels.ShortLabel(TrustLevel);

    [RelayCommand]
    private async Task PrepareSignatureAsync(CancellationToken ct)
    {
        if (ReleaseFolderPath is null || !Directory.Exists(ReleaseFolderPath)) return;
        if (!CanPrepareSignature) return;

        IsPreparingSignature = true;

        var result = await ReleaseSignatureService.PrepareSignatureAsync(
            releaseDir:     ReleaseFolderPath,
            developerEtwId: _config.DeveloperEtwId ?? _config.DeveloperDisplayName ?? "",
            ct:             ct);

        LastSignatureResult = result;

        if (result.Success)
        {
            IsSignaturePrepared = true;
            TrustLevel          = result.TrustLevel;
            SignatureInfoPath   = result.SignatureInfoPath;
        }

        IsPreparingSignature = false;
    }

    [RelayCommand]
    private async Task VerifySignaturePreparationAsync(CancellationToken ct)
    {
        if (ReleaseFolderPath is null || !Directory.Exists(ReleaseFolderPath)) return;

        IsVerifyingSignature = true;

        var result = await ReleaseSignatureService.VerifySignaturePreparationAsync(
            releaseDir: ReleaseFolderPath, ct: ct);

        LastVerificationResult = result;
        IsVerifyingSignature   = false;
    }

    [RelayCommand]
    private void OpenSignatureInfo()
    {
        if (SignatureInfoPath is not null && File.Exists(SignatureInfoPath))
            OpenFile(SignatureInfoPath);
    }

    // ── Phase 4.1: ETW-Schlüssel erzeugen ────────────────────────────────────

    [RelayCommand]
    private async Task GenerateEtwKeyAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PublisherId) || PublisherId == "ETW") return;

        IsGeneratingEtwKey = true;

        var keyInfo = await EtwKeyStoreService.GenerateKeyAsync(
            PublisherId,
            _config.DeveloperDisplayName ?? PublisherId,
            ct);

        CurrentEtwKeyInfo = keyInfo;
        EtwKeyExists      = keyInfo.HasKey;

        IsGeneratingEtwKey = false;
    }

    // ── Phase 4.1: ETW-Signatur erstellen ─────────────────────────────────────

    [RelayCommand]
    private async Task CreateEtwSignatureAsync(CancellationToken ct)
    {
        if (ReleaseFolderPath is null || !Directory.Exists(ReleaseFolderPath)) return;
        if (!CanCreateEtwSignature) return;

        IsCreatingEtwSignature = true;

        var result = await EtwSigningService.SignAsync(
            releaseDir:          ReleaseFolderPath,
            developerEtwId:      PublisherId,
            developerDisplayName: _config.DeveloperDisplayName ?? PublisherId,
            ct:                  ct);

        LastEtwSigningResult = result;

        if (result.Success)
        {
            IsEtwSigned       = true;
            TrustLevel        = result.TrustLevel;
            SignatureInfoPath  = result.SignatureInfoPath;
        }

        IsCreatingEtwSignature = false;
    }

    // ── Phase 4.1: ETW-Signatur prüfen ────────────────────────────────────────

    [RelayCommand]
    private async Task VerifyEtwSignatureAsync(CancellationToken ct)
    {
        if (ReleaseFolderPath is null || !Directory.Exists(ReleaseFolderPath)) return;

        IsVerifyingEtwSignature = true;

        var result = await EtwSignatureVerificationService.VerifyAsync(
            releaseDir: ReleaseFolderPath,
            ct:         ct);

        LastEtwVerificationResult = result;
        IsEtwSignatureVerified    = result.IsValid;

        // Trust-Level auf EtwLocalVerified hochsetzen wenn Prüfung erfolgreich
        if (result.IsValid)
            TrustLevel = Services.Signing.TrustLevels.EtwLocalVerified;

        IsVerifyingEtwSignature = false;
    }

    // ── Phase 4.2: Public-Key-Export ─────────────────────────────────────────

    /// <summary>Kopiert den Public Key PEM in die Zwischenablage.</summary>
    [RelayCommand]
    private async Task CopyPublicKeyAsync()
    {
        var pem = CurrentEtwKeyInfo?.PublicKeyPem;
        if (Clipboard is null || string.IsNullOrEmpty(pem)) return;
        await Clipboard.SetTextAsync(pem);
    }

    /// <summary>Exportiert den Public Key als .pem-Datei (Datei-Dialog).</summary>
    [RelayCommand]
    private async Task ExportPublicKeyAsync()
    {
        var pem = CurrentEtwKeyInfo?.PublicKeyPem;
        if (StorageProvider is null || string.IsNullOrEmpty(pem)) return;

        var safeName = string.Concat(
            PublisherId.ToLowerInvariant()
                       .Replace('/', '-')
                       .Replace(':', '-')
                       .Split(Path.GetInvalidFileNameChars()));

        var file = await StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title            = "Öffentlichen ETW-Schlüssel exportieren",
                SuggestedFileName = $"{safeName}-public.pem",
                DefaultExtension  = "pem",
                FileTypeChoices   =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("PEM-Schlüssel")
                        { Patterns = ["*.pem"] }
                ]
            });

        if (file?.TryGetLocalPath() is { } path)
            await File.WriteAllTextAsync(path, pem);
    }

    [RelayCommand]
    private async Task IncreasePatchVersionAsync()
    {
        if (CreatedProjectPath is null) return;
        await ManifestVersionService.BumpPatchAsync(CreatedProjectPath);
        // Readiness neu prüfen
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await CheckPublishReadinessAsync(cts.Token);
    }

    [RelayCommand]
    private async Task IncreaseMinorVersionAsync()
    {
        if (CreatedProjectPath is null) return;
        await ManifestVersionService.BumpMinorAsync(CreatedProjectPath);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await CheckPublishReadinessAsync(cts.Token);
    }

    [RelayCommand]
    private void OpenPackageFolder()
    {
        var dir = LastPackageResult?.PackagePath is not null
            ? Path.GetDirectoryName(LastPackageResult.PackagePath)
            : CreatedProjectPath is not null
                ? Path.Combine(CreatedProjectPath, "packages")
                : null;

        if (dir is null || !Directory.Exists(dir)) return;
        OpenFolder(dir);
    }

    private static void OpenFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start(new ProcessStartInfo("open", dir) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", dir) { UseShellExecute = true });
    }

    /// <summary>Aktions-Handler für PublishIssue-Buttons.</summary>
    private async void ExecutePublishActionAsync(string actionId)
    {
        if (CreatedProjectPath is null) return;

        if (actionId.StartsWith("set-version:"))
        {
            var ver = actionId["set-version:".Length..];
            await ManifestVersionService.SetVersionAsync(CreatedProjectPath, ver);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await CheckPublishReadinessAsync(cts.Token);
            return;
        }

        if (actionId.StartsWith("open-file:"))
        {
            OpenFile(actionId["open-file:".Length..]);
            return;
        }

        switch (actionId)
        {
            case "increase-patch":
                await IncreasePatchVersionAsync();
                break;
            case "create-manifest":
                await ExecuteValidationActionCommand.ExecuteAsync("create-manifest");
                break;
            case "add-readme":
                await ExecuteValidationActionCommand.ExecuteAsync("add-readme");
                break;
            case "add-license-mit":
                await ExecuteValidationActionCommand.ExecuteAsync("add-license-mit");
                break;
            case "open-manifest":
                OpenFile(Path.Combine(CreatedProjectPath, "aaia-manifest.json"));
                break;
            case "open-folder":
                OpenInExplorer();
                break;
            case "build-project":
                CurrentStep = WizardStep.Success;
                break;
        }
    }

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

    // ── Phase 4.5: AI Handoff ─────────────────────────────────────────────────

    /// <summary>
    /// Baut den aktuellen Projektzustand als <see cref="AiHandoffContext"/> auf.
    /// Wird vom AiHandoffWindow aufgerufen und enthält KEINE Quelltexte.
    /// </summary>
    /// <summary>
    /// Baut den Upload-Kontext für den Marketplace-Upload-Dialog auf.
    ///
    /// Sicherheitsregel (unveränderlich):
    ///   Enthält NIEMALS Private Key, MarketplaceToken im Klartext im Objekt —
    ///   BearerToken wird aus _config gezogen und nur für den HTTP-Request verwendet.
    /// </summary>
    public Services.Marketplace.MarketplaceSignedUploadContext BuildMarketplaceUploadContext()
    {
        var releaseDir = ReleaseFolderPath ?? "";

        // .aaiaext im Release-Ordner suchen (ReleasePrepareService kopiert es dorthin)
        var packagePath = "";
        if (!string.IsNullOrEmpty(releaseDir) && Directory.Exists(releaseDir))
        {
            packagePath = System.IO.Directory
                .GetFiles(releaseDir, "*.aaiaext", System.IO.SearchOption.TopDirectoryOnly)
                .FirstOrDefault() ?? "";
        }

        return new Services.Marketplace.MarketplaceSignedUploadContext
        {
            ExtensionId      = ProjectId,
            Version          = LastReleaseResult?.Version ?? "",
            DisplayName      = ProjectName,
            DeveloperEtwId   = PublisherId,
            KeyFingerprint   = CurrentEtwKeyInfo?.Fingerprint ?? "",
            TrustLevel       = TrustLevel,
            SignatureVersion  = "etw-signature-v1",

            PackagePath          = packagePath,
            SignatureInfoPath    = SignatureInfoPath ?? "",
            ReleaseInfoPath      = System.IO.Path.Combine(releaseDir, "release-info.json"),
            InspectionReportPath = System.IO.Path.Combine(releaseDir, "inspection-report.json"),

            MarketplaceApiUrl = _config.EtwMarketplaceApiUrl,   // ASP.NET Core API, nicht WP REST
            BearerToken       = _config.MarketplaceToken
        };
    }

    public AiHandoffContext BuildAiHandoffContext()
    {
        // Fehler aus den aktiven Ergebnissen zusammenstellen
        var validationErrors = new System.Collections.Generic.List<string>();
        foreach (var issue in ValidationIssues)
            if (issue.Severity == "Error")
                validationErrors.Add($"[{issue.Category}] {issue.Title}: {issue.Message}");

        var inspectionBlockers = new System.Collections.Generic.List<string>();
        if (InspectionResult is not null)
            foreach (var issue in InspectionResult.Issues)
                if (issue.IsError)
                    inspectionBlockers.Add($"[{issue.Category}] {issue.Title}");

        var signatureErrors = new System.Collections.Generic.List<string>();
        if (LastEtwVerificationResult is not null && !LastEtwVerificationResult.IsValid)
            foreach (var issue in LastEtwVerificationResult.Issues)
                if (issue.IsError)
                    signatureErrors.Add($"[{issue.Category}] {issue.Title}");

        // Nächsten Schritt ableiten
        string? nextStep = null;
        if (!IsProjectCreated)             nextStep = "Projekt erstellen";
        else if (!IsValidated)             nextStep = "Manifest validieren";
        else if (HasValidationBlockers)    nextStep = "Validierungsblocker beheben";
        else if (!IsBuilt)                 nextStep = "Projekt bauen";
        else if (!IsPackaged)              nextStep = ".aaiaext-Paket erstellen";
        else if (!IsInspected)             nextStep = "Paket inspizieren";
        else if (HasInspectionBlockers)    nextStep = "Inspection-Blocker beheben";
        else if (!IsReleasePrepared)       nextStep = "Release vorbereiten";
        else if (!IsSignaturePrepared)     nextStep = "Hash-Vorbereitung (Phase 4.0)";
        else if (!EtwKeyExists)            nextStep = "ETW-Schlüssel erzeugen (Phase 4.1)";
        else if (!IsEtwSigned)             nextStep = "ETW-Signatur erstellen (Phase 4.1)";
        else if (!IsEtwSignatureVerified)  nextStep = "ETW-Signatur prüfen (Phase 4.2)";
        else if (!CanContinueToMarketplace) nextStep = "Marketplace-Freigabe prüfen";
        else                               nextStep = "Phase 5.0 — Marketplace-Upload";

        return new AiHandoffContext
        {
            // Projekt-Identität
            ExtensionId  = ProjectId,
            DisplayName  = ProjectName,
            ProjectType  = ProjectType.ToString(),
            DeveloperEtwId = PublisherId,

            // Pipeline-Flags
            IsProjectCreated         = IsProjectCreated,
            IsValidated              = IsValidated,
            HasValidationBlockers    = HasValidationBlockers,
            IsBuilt                  = IsBuilt,
            IsPackaged               = IsPackaged,
            IsInspected              = IsInspected,
            HasInspectionBlockers    = HasInspectionBlockers,
            IsReleasePrepared        = IsReleasePrepared,
            IsSignaturePrepared      = IsSignaturePrepared,
            EtwKeyExists             = EtwKeyExists,
            IsEtwSigned              = IsEtwSigned,
            IsEtwSignatureVerified   = IsEtwSignatureVerified,
            CanContinueToMarketplace = CanContinueToMarketplace,

            // Trust & Signatur
            TrustLevel      = TrustLevel,
            KeyFingerprint  = CurrentEtwKeyInfo?.Fingerprint,
            KeyAlgorithm    = CurrentEtwKeyInfo?.KeyAlgorithm,
            SignedAtUtc     = LastEtwSigningResult?.SignedAtUtc,

            // Aktueller Schritt
            CurrentStep = CurrentStep.ToString(),
            NextStep    = nextStep,

            // Fehler
            ValidationErrors   = validationErrors,
            InspectionBlockers = inspectionBlockers,
            SignatureErrors     = signatureErrors
        };
    }
}
