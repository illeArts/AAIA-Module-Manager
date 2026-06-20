using AAIA.ModuleManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für den Publish-Tab.
/// Orchestriert Build → Pack → Sign → Marketplace-Publish für ein Modul.
///
/// Voraussetzungen:
///   - Entwickler muss im Developer-Tab eingeloggt sein (MarketplaceToken vorhanden)
///   - Publisher-Schlüssel muss registriert sein
/// </summary>
public sealed partial class PublishTabViewModel : ObservableObject
{
    private readonly AppConfig            _config;
    private readonly PublishService       _publishSvc;
    private readonly MarketplaceApiClient _api;

    // ── Formularfelder ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _projectPath    = "";
    [ObservableProperty] private string _version        = "1.0.0";
    [ObservableProperty] private string _changelog      = "";
    [ObservableProperty] private string _privateKeyPath = "";

    /// <summary>"module" | "plugin" | "languagepack"</summary>
    [ObservableProperty] private string _extensionType  = "module";

    /// <summary>Nur relevant wenn ExtensionType == "languagepack", z.B. "de-DE".</summary>
    [ObservableProperty] private string _targetLocale   = "";

    /// <summary>Steuert Sichtbarkeit des TargetLocale-Feldes in der UI.</summary>
    public bool IsLanguagePack => ExtensionType == "languagepack";

    /// <summary>Auswahloptionen für den ExtensionType-ComboBox.</summary>
    public IReadOnlyList<ExtensionTypeOption> ExtensionTypeOptions { get; } =
    [
        new("module",       "📦 Modul (Server-Extension)"),
        new("plugin",       "🔌 Plugin (Client-Extension)"),
        new("languagepack", "🌐 Sprachpaket (.aalang)"),
    ];

    /// <summary>
    /// Gewählte Option in der ComboBox. Synchronisiert <see cref="ExtensionType"/>.
    /// </summary>
    public ExtensionTypeOption SelectedExtensionTypeOption
    {
        get => ExtensionTypeOptions.FirstOrDefault(o => o.Value == ExtensionType)
               ?? ExtensionTypeOptions[0];
        set
        {
            ExtensionType = value.Value;
            OnPropertyChanged();
        }
    }

    // Optionale Schritte
    [ObservableProperty] private bool   _publishNuGet       = false;
    [ObservableProperty] private bool   _createGitHub       = false;
    [ObservableProperty] private bool   _publishMarketplace = true;

    // ── Status ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private bool   _isSuccess;
    [ObservableProperty] private string _marketplaceUrl = "";

    public ObservableCollection<string> Log { get; } = new();
    public bool HasLog => Log.Count > 0;

    // ── Auth-State (abgeleitet aus Config) ─────────────────────────────────────

    public bool IsLoggedIn => !string.IsNullOrEmpty(_config.MarketplaceToken) &&
                              !string.IsNullOrEmpty(_config.DeveloperEtwId);

    public string EtwId => _config.DeveloperEtwId ?? "";

    public PublishTabViewModel(
        AppConfig            config,
        PublishService       publishSvc,
        MarketplaceApiClient api)
    {
        _config     = config;
        _publishSvc = publishSvc;
        _api        = api;

        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));

        // Private-Key-Pfad aus Config vorbelegen falls vorhanden
        PrivateKeyPath = config.PublisherPrivateKeyPath ?? "";
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    partial void OnExtensionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLanguagePack));
        OnPropertyChanged(nameof(SelectedExtensionTypeOption));
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync(CancellationToken ct)
    {
        IsBusy        = true;
        IsSuccess     = false;
        MarketplaceUrl = "";
        StatusMessage = "Publish läuft...";
        Log.Clear();

        try
        {
            var opts = new PublishService.PublishOptions(
                ProjectPath:          ProjectPath,
                Version:              Version,
                Changelog:            Changelog,
                PrivateKeyPath:       string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath,
                KeyId:                _config.PublisherKeyId,
                PublishNuGet:         PublishNuGet,
                CreateGitHub:         CreateGitHub,
                PublishToMarketplace: PublishMarketplace,
                ExtensionType:        ExtensionType,
                TargetLocale:         string.IsNullOrWhiteSpace(TargetLocale) ? null : TargetLocale.Trim());

            var progress = new Progress<string>(msg =>
            {
                Log.Add(msg);
                StatusMessage = msg;
            });

            var result = await _publishSvc.PublishAsync(opts, progress, ct);

            if (result.Success)
            {
                IsSuccess      = true;
                MarketplaceUrl = result.MarketplaceUrl ?? "";
                StatusMessage  = $"✅ Veröffentlicht: {result.ModuleId}";
                Log.Add($"✅ Fertig!");
                if (result.MarketplaceUrl is not null)
                    Log.Add($"🔗 Marketplace: {result.MarketplaceUrl}");
                if (result.NuGetPackageUrl is not null)
                    Log.Add($"📦 NuGet: {result.NuGetPackageUrl}");
                if (result.GitHubReleaseUrl is not null)
                    Log.Add($"🐙 GitHub: {result.GitHubReleaseUrl}");
            }
            else
            {
                StatusMessage = $"❌ {result.Error}";
                Log.Add($"❌ Fehler: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Abgebrochen.";
            Log.Add("⚠️ Abgebrochen.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unerwarteter Fehler: {ex.Message}";
            Log.Add($"❌ {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private bool CanPublish() =>
        !IsBusy &&
        IsLoggedIn &&
        !string.IsNullOrWhiteSpace(ProjectPath) &&
        !string.IsNullOrWhiteSpace(Version) &&
        Directory.Exists(ProjectPath);

    [RelayCommand]
    private void BrowseProject()
    {
        // Platzhalter — tatsächliche Folder-Dialog-Interaktion erfolgt via Code-Behind
        // (Avalonia StorageProvider muss vom View aufgerufen werden)
    }

    [RelayCommand]
    private void BrowsePrivateKey()
    {
        // Platzhalter — File-Dialog im Code-Behind
    }

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        StatusMessage = "";
        IsSuccess     = false;
        MarketplaceUrl = "";
    }

    // ── Hilfsmethoden ──────────────────────────────────────────────────────────

    /// <summary>Setzt den Projektpfad (wird vom Code-Behind nach Folder-Dialog aufgerufen).</summary>
    public void SetProjectPath(string path)
    {
        ProjectPath = path;
        PublishCommand.NotifyCanExecuteChanged();

        // Version, ExtensionType und TargetLocale aus aaia-extension.json vorausfüllen
        var manifestPath = Path.Combine(path, "aaia-extension.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc  = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                if (root.TryGetProperty("version", out var v))
                    Version = v.GetString() ?? Version;

                if (root.TryGetProperty("extensionType", out var et) && et.GetString() is { } etStr)
                    ExtensionType = etStr.ToLowerInvariant();

                if (root.TryGetProperty("targetLocale", out var tl) && tl.GetString() is { } tlStr)
                    TargetLocale = tlStr;
            }
            catch { /* ignorieren */ }
        }
    }

    /// <summary>Setzt den Private-Key-Pfad (wird vom Code-Behind nach File-Dialog aufgerufen).</summary>
    public void SetPrivateKeyPath(string path)
    {
        PrivateKeyPath = path;
        _config.PublisherPrivateKeyPath = path;
        _ = _config.SaveAsync();
    }
}

/// <summary>Eintrag für den ExtensionType-ComboBox im Publish-Tab.</summary>
public sealed record ExtensionTypeOption(string Value, string Label)
{
    public override string ToString() => Label;
}
