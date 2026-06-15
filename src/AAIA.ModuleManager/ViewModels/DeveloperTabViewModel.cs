using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Publisher;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.ViewModels;

// ── Item-ViewModel für Modul-Liste ─────────────────────────────────────────────

public sealed class MarketplaceModuleItem
{
    public int     ProductId    { get; init; }
    public string  Title        { get; init; } = "";
    public string  StatusBadge  { get; init; } = "";
    public string  Type         { get; init; } = "";
    public string  Version      { get; init; } = "";
    public int     LicenseCount { get; init; }
    public string  PriceDisplay { get; init; } = "";
    public string  Url          { get; init; } = "";

    public static MarketplaceModuleItem FromDto(MarketplaceModuleDto dto) => new()
    {
        ProductId    = dto.ProductId,
        Title        = dto.Title,
        StatusBadge  = dto.Status switch
        {
            "publish" => "✅ Veröffentlicht",
            "pending" => "⏳ In Prüfung",
            "draft"   => "📝 Entwurf",
            _         => dto.Status
        },
        Type         = dto.Type switch
        {
            "plugin"       => "🔌 Plugin",
            "module"       => "📦 Modul",
            "languagepack" => "🌐 Sprachpaket",
            _              => dto.Type
        },
        Version      = dto.Version,
        LicenseCount = dto.LicenseCount,
        PriceDisplay = dto.Price == 0 ? "Kostenlos" : $"{dto.Price:F2} {dto.Currency}",
        Url          = dto.Url,
    };
}

// ── Haupt-ViewModel ────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel für den Developer-Tab.
/// Deckt ab: ETW-Login/Register, Publisher-Schlüssel verwalten,
///           eigene Module anzeigen, Statistiken, Modul hochladen.
/// </summary>
public sealed partial class DeveloperTabViewModel : ObservableObject
{
    private readonly AppConfig             _config;
    private readonly MarketplaceApiClient  _api;
    private readonly WpMarketplaceClient   _wpApi;
    private readonly PublisherCertService  _certSvc;

    // ── Bindable State: Profil ─────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoggedIn;
    [ObservableProperty] private string _etwId          = "";
    [ObservableProperty] private string _displayName    = "";
    [ObservableProperty] private string _role           = "";
    [ObservableProperty] private float  _reputation;
    [ObservableProperty] private int    _moduleCount;
    [ObservableProperty] private bool   _hasPublisherKey;
    [ObservableProperty] private string _keyId          = "";

    // ── Bindable State: Login / Register ──────────────────────────────────────

    [ObservableProperty] private string _loginEmail    = "";
    [ObservableProperty] private string _loginPassword = "";

    [ObservableProperty] private string _regEmail       = "";
    [ObservableProperty] private string _regPassword    = "";
    [ObservableProperty] private string _regDisplayName = "";
    [ObservableProperty] private string _regGitHub      = "";

    // ── Bindable State: Stats ──────────────────────────────────────────────────

    [ObservableProperty] private int     _statTotalModules;
    [ObservableProperty] private int     _statPublishedModules;
    [ObservableProperty] private int     _statTotalLicenses;
    [ObservableProperty] private int     _statLast30Days;
    [ObservableProperty] private decimal _statRevenueShare;

    // ── Bindable State: Modul-Liste ────────────────────────────────────────────

    public ObservableCollection<MarketplaceModuleItem> Modules { get; } = new();

    [ObservableProperty] private bool _hasModules;
    [ObservableProperty] private bool _isLoadingModules;

    // ── Bindable State: Upload-Formular ────────────────────────────────────────

    [ObservableProperty] private string  _uploadTitle        = "";
    [ObservableProperty] private string  _uploadVersion      = "1.0.0";
    [ObservableProperty] private string  _uploadType         = "plugin";
    [ObservableProperty] private decimal _uploadPrice        = 0m;
    [ObservableProperty] private string  _uploadDescription  = "";
    [ObservableProperty] private string  _uploadMinVersion   = "1.0.0";
    [ObservableProperty] private string  _uploadFilePath     = "";
    [ObservableProperty] private bool    _showUploadForm;

    // ── Status / Log ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool   _isBusy;

    public ObservableCollection<string> Log { get; } = new();
    public bool HasLog => Log.Count > 0;

    /// <summary>Modul-Typen für ComboBox-Binding.</summary>
    public List<(string Key, string Label)> ModuleTypes { get; } = new()
    {
        ("plugin",       "🔌 Plugin"),
        ("module",       "📦 Modul"),
        ("languagepack", "🌐 Sprachpaket"),
    };

    // ── Konstruktor ────────────────────────────────────────────────────────────

    public DeveloperTabViewModel(
        AppConfig            config,
        MarketplaceApiClient api,
        WpMarketplaceClient  wpApi,
        PublisherCertService certSvc)
    {
        _config  = config;
        _api     = api;
        _wpApi   = wpApi;
        _certSvc = certSvc;

        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));

        // Gespeicherten Token wiederherstellen
        if (!string.IsNullOrEmpty(_config.MarketplaceToken) &&
            !string.IsNullOrEmpty(_config.DeveloperEtwId))
        {
            _api.SetBearer(_config.MarketplaceToken);
            _wpApi.SetBearer(_config.MarketplaceToken);
            EtwId       = _config.DeveloperEtwId ?? "";
            DisplayName = _config.DeveloperDisplayName ?? "";
            IsLoggedIn  = true;
        }
    }

    // ── Commands: Login / Register / Logout ───────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Anmelden...";
        try
        {
            var result = await _api.LoginAsync(
                new DeveloperLoginRequest(LoginEmail, LoginPassword), ct);

            _config.MarketplaceToken      = result.AccessToken;
            _config.DeveloperEtwId        = result.EtwId;
            _config.DeveloperDisplayName  = result.DisplayName;
            await _config.SaveAsync();

            _wpApi.SetBearer(result.AccessToken);
            EtwId       = result.EtwId;
            DisplayName = result.DisplayName;
            IsLoggedIn  = true;
            StatusMessage = $"Eingeloggt als {result.EtwId}";

            await LoadProfileAsync(ct);
            await LoadModulesAndStatsAsync(ct);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Login fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanLogin() => !string.IsNullOrWhiteSpace(LoginEmail) &&
                               !string.IsNullOrWhiteSpace(LoginPassword);

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Registriere...";
        try
        {
            var result = await _api.RegisterAsync(
                new DeveloperRegisterRequest(
                    DisplayName:   RegDisplayName,
                    Email:         RegEmail,
                    Password:      RegPassword,
                    GitHubAccount: string.IsNullOrWhiteSpace(RegGitHub) ? null : RegGitHub),
                ct);

            StatusMessage = $"Registriert: {result.EtwId} — {result.Message}";
            Log.Add($"✅ ETW-ID vergeben: {result.EtwId}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Registrierung fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanRegister() =>
        !string.IsNullOrWhiteSpace(RegEmail) &&
        !string.IsNullOrWhiteSpace(RegPassword) &&
        !string.IsNullOrWhiteSpace(RegDisplayName);

    [RelayCommand]
    private void Logout()
    {
        _api.ClearBearer();
        _wpApi.ClearBearer();
        _config.MarketplaceToken     = "";
        _config.DeveloperEtwId       = null;
        _config.DeveloperDisplayName = null;
        _ = _config.SaveAsync();

        IsLoggedIn  = false;
        EtwId       = "";
        DisplayName = "";
        Role        = "";
        Modules.Clear();
        HasModules           = false;
        StatTotalModules     = 0;
        StatPublishedModules = 0;
        StatTotalLicenses    = 0;
        StatLast30Days       = 0;
        StatRevenueShare     = 0m;
        StatusMessage = "Abgemeldet.";
    }

    // ── Command: Publisher-Schlüssel ──────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateAndUploadKeyAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Generiere Publisher-Schlüssel...";
        Log.Clear();
        try
        {
            Log.Add("🔑 Generiere RSA-PSS-SHA256 Schlüsselpaar...");
            var (pubPath, privPath, keyId) = await _certSvc.GenerateKeyPairAsync(DisplayName, ct);

            Log.Add($"✅ Schlüssel generiert: {keyId}");
            Log.Add($"   Public:  {pubPath}");
            Log.Add($"   Private: {privPath}  ← SICHER AUFBEWAHREN!");

            Log.Add("📤 Lade Public Key zur Marketplace API hoch...");
            var resp = await _certSvc.UploadPublicKeyAsync(pubPath, keyId, ct);

            KeyId           = resp.KeyId;
            HasPublisherKey = true;
            _config.PublisherKeyId = resp.KeyId;
            await _config.SaveAsync();
            StatusMessage   = $"Publisher-Key registriert: {keyId}";
            Log.Add($"✅ Key bei Marketplace API registriert: {resp.KeyId}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler: {ex.Message}";
            Log.Add($"❌ {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    // ── Command: Module laden ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadModulesAsync(CancellationToken ct) =>
        await LoadModulesAndStatsAsync(ct);

    private async Task LoadModulesAndStatsAsync(CancellationToken ct)
    {
        IsLoadingModules = true;
        try
        {
            var statsTask   = _wpApi.GetMyStatsAsync(ct);
            var modulesTask = _wpApi.GetMyModulesAsync(ct);
            await Task.WhenAll(statsTask, modulesTask);

            var stats = await statsTask;
            StatTotalModules     = stats.TotalModules;
            StatPublishedModules = stats.PublishedModules;
            StatTotalLicenses    = stats.TotalLicenses;
            StatLast30Days       = stats.Last30DaysLicenses;
            StatRevenueShare     = stats.RevenueShare;
            ModuleCount          = stats.TotalModules;

            var modules = await modulesTask;
            Modules.Clear();
            foreach (var m in modules)
                Modules.Add(MarketplaceModuleItem.FromDto(m));

            HasModules = Modules.Count > 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Laden der Module: {ex.Message}";
        }
        finally { IsLoadingModules = false; }
    }

    // ── Command: Upload-Formular togglen ──────────────────────────────────────

    [RelayCommand]
    private void ToggleUploadForm() => ShowUploadForm = !ShowUploadForm;

    // ── Command: ZIP-Datei auswählen ──────────────────────────────────────────

    /// <summary>Wird vom View-CodeBehind nach Initialisierung gesetzt.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    [RelayCommand]
    private async Task BrowseZipFileAsync(CancellationToken ct)
    {
        if (StorageProvider is null) return;
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "ZIP-Datei auswählen",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ZIP-Archiv") { Patterns = new[] { "*.zip" } },
            }
        });
        if (result is { Count: > 0 })
            UploadFilePath = result[0].Path.LocalPath;
    }

    // ── Command: Modul einreichen ─────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSubmitModule))]
    private async Task SubmitModuleAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Lade Modul hoch...";
        Log.Clear();
        try
        {
            Log.Add($"📦 Sende Modul \"{UploadTitle}\" v{UploadVersion} an Marketplace...");

            var req = new ModuleUploadRequest
            {
                Title          = UploadTitle,
                Version        = UploadVersion,
                Type           = UploadType,
                Price          = UploadPrice,
                Description    = UploadDescription,
                MinAaiaVersion = UploadMinVersion,
                FilePath       = UploadFilePath,
            };

            using var resultDoc = await _wpApi.SubmitModuleAsync(req, ct);
            var root = resultDoc.RootElement;

            var productId = root.TryGetProperty("productId", out var pid) ? pid.GetInt32() : 0;
            Log.Add($"✅ Modul eingereicht! Produkt-ID: {productId}");
            Log.Add("ℹ️  Status: In Prüfung — du wirst benachrichtigt sobald es freigegeben ist.");

            StatusMessage  = $"Modul eingereicht (ID: {productId})";
            ShowUploadForm = false;

            await LoadModulesAndStatsAsync(ct);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload fehlgeschlagen: {ex.Message}";
            Log.Add($"❌ {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private bool CanSubmitModule() =>
        !string.IsNullOrWhiteSpace(UploadTitle) &&
        !string.IsNullOrWhiteSpace(UploadFilePath) &&
        !string.IsNullOrWhiteSpace(UploadVersion);

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task LoadProfileAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(EtwId)) return;
        var profile = await _api.GetProfileAsync(EtwId, ct);
        if (profile is null) return;

        Role            = profile.Role.ToString();
        Reputation      = profile.Reputation;
        ModuleCount     = profile.ModuleCount;
        HasPublisherKey = profile.KeyId is not null;
        KeyId           = profile.KeyId ?? "";
    }
}
