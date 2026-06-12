using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Publisher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für den Developer-Tab.
/// Deckt ab: ETW-Login/Register, Publisher-Key verwalten, Profil anzeigen.
/// </summary>
public sealed partial class DeveloperTabViewModel : ObservableObject
{
    private readonly AppConfig            _config;
    private readonly MarketplaceApiClient _api;
    private readonly PublisherCertService _certSvc;

    // ── Bindable State ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoggedIn;
    [ObservableProperty] private string _etwId          = "";
    [ObservableProperty] private string _displayName    = "";
    [ObservableProperty] private string _role           = "";
    [ObservableProperty] private float  _reputation;
    [ObservableProperty] private int    _moduleCount;
    [ObservableProperty] private bool   _hasPublisherKey;
    [ObservableProperty] private string _keyId          = "";

    // Login Form
    [ObservableProperty] private string _loginEmail    = "";
    [ObservableProperty] private string _loginPassword = "";

    // Register Form
    [ObservableProperty] private string _regEmail       = "";
    [ObservableProperty] private string _regPassword    = "";
    [ObservableProperty] private string _regDisplayName = "";
    [ObservableProperty] private string _regGitHub      = "";

    [ObservableProperty] private string _statusMessage  = "";
    [ObservableProperty] private bool   _isBusy;

    public ObservableCollection<string> Log { get; } = new();

    /// <summary>True wenn Log mindestens einen Eintrag hat (für AXAML IsVisible-Binding).</summary>
    public bool HasLog => Log.Count > 0;

    public DeveloperTabViewModel(
        AppConfig            config,
        MarketplaceApiClient api,
        PublisherCertService certSvc)
    {
        _config  = config;
        _api     = api;
        _certSvc = certSvc;

        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));

        // Gespeicherten Token wiederherstellen
        if (!string.IsNullOrEmpty(_config.MarketplaceToken) &&
            !string.IsNullOrEmpty(_config.DeveloperEtwId))
        {
            _api.SetBearer(_config.MarketplaceToken);
            EtwId       = _config.DeveloperEtwId ?? "";
            DisplayName = _config.DeveloperDisplayName ?? "";
            IsLoggedIn  = true;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

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

            EtwId       = result.EtwId;
            DisplayName = result.DisplayName;
            IsLoggedIn  = true;
            StatusMessage = $"Eingeloggt als {result.EtwId}";

            await LoadProfileAsync(ct);
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
                    DisplayName:  RegDisplayName,
                    Email:        RegEmail,
                    Password:     RegPassword,
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
        _config.MarketplaceToken     = "";
        _config.DeveloperEtwId       = null;
        _config.DeveloperDisplayName = null;
        _ = _config.SaveAsync();

        IsLoggedIn  = false;
        EtwId       = "";
        DisplayName = "";
        Role        = "";
        StatusMessage = "Abgemeldet.";
    }

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

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task LoadProfileAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(EtwId)) return;

        var profile = await _api.GetProfileAsync(EtwId, ct);
        if (profile is null) return;

        Role        = profile.Role.ToString();
        Reputation  = profile.Reputation;
        ModuleCount = profile.ModuleCount;
        HasPublisherKey = profile.KeyId is not null;
        KeyId       = profile.KeyId ?? "";
    }
}
