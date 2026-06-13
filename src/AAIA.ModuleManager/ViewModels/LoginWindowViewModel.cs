using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Publisher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für das Login-/Registrierungs-Fenster.
/// Wird beim ersten Start angezeigt (kein gespeicherter Token).
///
/// Fluss Registrierung:
///   1. RegisterAsync()  → Server liefert EtwId + TotpUri
///   2. LoginWindow zeigt QR-Code (TotpUri)
///   3. User scannt und tippt Code → VerifyTotpAsync()
///   4. Server aktiviert Account, liefert JWT → LoginSucceeded ausgelöst
///
/// Fluss Login:
///   1. LoginAsync() mit E-Mail + Passwort + optionalem TOTP-Code
///   2. LoginSucceeded ausgelöst
/// </summary>
public sealed partial class LoginWindowViewModel : ObservableObject
{
    private readonly AppConfig            _config;
    private readonly MarketplaceApiClient _api;

    // ── UI-Zustand ─────────────────────────────────────────────────────────────

    public enum Screen { Login, Register, TotpSetup }

    [ObservableProperty] private Screen _currentScreen = Screen.Login;

    // Login-Formular
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _loginEmail    = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _loginPassword = "";

    [ObservableProperty] private string _loginTotp     = "";
    [ObservableProperty] private bool   _loginNeedsTotp;

    // Register-Formular
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _regDisplayName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _regEmail       = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _regPassword    = "";

    [ObservableProperty] private string _regPasswordConfirm = "";
    [ObservableProperty] private string _regGitHub      = "";

    // TOTP-Setup (nach Registrierung)
    [ObservableProperty] private string _totpUri        = "";
    [ObservableProperty] private string _totpSecret     = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyTotpCommand))]
    private string _totpCode       = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyTotpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRegistrationCommand))]
    private string _pendingEtwId   = "";

    // Passwort-Sichtbarkeit
    [ObservableProperty] private bool _showLoginPassword;
    [ObservableProperty] private bool _showRegPassword;
    [ObservableProperty] private bool _showRegPasswordConfirm;

    // Allgemein
    [ObservableProperty] private string _statusMessage  = "";
    [ObservableProperty] private bool   _isError;
    [ObservableProperty] private bool   _isBusy;

    // ── Event: Login/Registrierung erfolgreich ─────────────────────────────────

    /// <summary>Wird ausgelöst wenn Auth abgeschlossen ist. App.axaml.cs öffnet dann MainWindow.</summary>
    public event EventHandler<LoginSucceededArgs>? LoginSucceeded;

    public record LoginSucceededArgs(string EtwId, string DisplayName, string AccessToken, DeveloperRole Role);

    // ── Konstruktor ────────────────────────────────────────────────────────────

    public LoginWindowViewModel(AppConfig config, MarketplaceApiClient api)
    {
        _config = config;
        _api    = api;
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowLogin()    => CurrentScreen = Screen.Login;

    [RelayCommand]
    private void ShowRegister() => CurrentScreen = Screen.Register;

    [RelayCommand] private void ToggleLoginPassword()      => ShowLoginPassword      = !ShowLoginPassword;
    [RelayCommand] private void ToggleRegPassword()        => ShowRegPassword        = !ShowRegPassword;
    [RelayCommand] private void ToggleRegPasswordConfirm() => ShowRegPasswordConfirm = !ShowRegPasswordConfirm;

    // ── Login ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken ct)
    {
        IsBusy = true;
        SetStatus("Anmelden...", error: false);
        try
        {
            var result = await _api.LoginAsync(
                new DeveloperLoginRequest(LoginEmail, LoginPassword,
                    string.IsNullOrWhiteSpace(LoginTotp) ? null : LoginTotp.Trim()),
                ct);

            await PersistAndFireAsync(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var serverMsg = ex.Message;
            if (serverMsg.Contains("TOTP", StringComparison.OrdinalIgnoreCase)
                || serverMsg.Contains("totp", StringComparison.OrdinalIgnoreCase))
            {
                // Server fordert TOTP → Eingabefeld einblenden
                LoginNeedsTotp = true;
                SetStatus("Authenticator-Code erforderlich — bitte App öffnen.", error: true);
            }
            else
            {
                // Falsches Passwort, unbekannte E-Mail o.ä. → Server-Meldung direkt zeigen
                LoginNeedsTotp = false;
                SetStatus(serverMsg, error: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Login fehlgeschlagen: {ex.Message}", error: true);
        }
        finally { IsBusy = false; }
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(LoginEmail) &&
        !string.IsNullOrWhiteSpace(LoginPassword);

    // ── Registrierung ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync(CancellationToken ct)
    {
        if (RegPassword != RegPasswordConfirm)
        {
            SetStatus("Passwörter stimmen nicht überein.", error: true);
            return;
        }
        IsBusy = true;
        SetStatus("Account anlegen...", error: false);
        try
        {
            var result = await _api.RegisterAsync(
                new DeveloperRegisterRequest(
                    DisplayName:   RegDisplayName,
                    Email:         RegEmail,
                    Password:      RegPassword,
                    GitHubAccount: string.IsNullOrWhiteSpace(RegGitHub) ? null : RegGitHub),
                ct);

            // ETW-ID merken, in TOTP-Setup wechseln
            PendingEtwId = result.EtwId;
            TotpUri      = result.TotpUri    ?? "";
            TotpSecret   = result.TotpSecret ?? "";
            TotpCode     = "";
            SetStatus($"Account angelegt: {result.EtwId} — Bitte Authenticator-App einrichten.", error: false);
            CurrentScreen = Screen.TotpSetup;
        }
        catch (Exception ex)
        {
            SetStatus($"Registrierung fehlgeschlagen: {ex.Message}", error: true);
        }
        finally { IsBusy = false; }
    }

    private bool CanRegister() =>
        !string.IsNullOrWhiteSpace(RegDisplayName) &&
        !string.IsNullOrWhiteSpace(RegEmail) &&
        !string.IsNullOrWhiteSpace(RegPassword) &&
        RegPassword.Length >= 8;

    // ── Registrierung abbrechen (pending Account löschen) ─────────────────────

    [RelayCommand(CanExecute = nameof(CanCancelRegistration))]
    private async Task CancelRegistrationAsync(CancellationToken ct)
    {
        IsBusy = true;
        SetStatus("Account wird gelöscht...", error: false);
        try
        {
            await _api.DeleteAccountAsync(PendingEtwId, RegEmail, ct);
            // Formularfelder leeren, zurück zur Registrierung
            PendingEtwId = "";
            TotpUri      = "";
            TotpSecret   = "";
            TotpCode     = "";
            RegPassword  = "";
            RegPasswordConfirm = "";
            SetStatus("Registrierung abgebrochen. Du kannst es erneut versuchen.", error: false);
            CurrentScreen = Screen.Register;
        }
        catch (Exception ex)
        {
            SetStatus($"Abbruch fehlgeschlagen: {ex.Message}", error: true);
        }
        finally { IsBusy = false; }
    }

    private bool CanCancelRegistration() => !string.IsNullOrEmpty(PendingEtwId);

    // ── TOTP bestätigen ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanVerifyTotp))]
    private async Task VerifyTotpAsync(CancellationToken ct)
    {
        IsBusy = true;
        SetStatus("Code prüfen...", error: false);
        try
        {
            var result = await _api.VerifyTotpAsync(PendingEtwId, TotpCode.Trim(), ct);
            await PersistAndFireAsync(result);
        }
        catch (Exception ex)
        {
            SetStatus($"Code ungültig: {ex.Message}", error: true);
        }
        finally { IsBusy = false; }
    }

    private bool CanVerifyTotp() =>
        !string.IsNullOrWhiteSpace(PendingEtwId) &&
        TotpCode.Length == 6;

    // ── Hilfe ──────────────────────────────────────────────────────────────────

    private async Task PersistAndFireAsync(DeveloperLoginResponse result)
    {
        _api.SetBearer(result.AccessToken);
        _config.MarketplaceToken     = result.AccessToken;
        _config.DeveloperEtwId       = result.EtwId;
        _config.DeveloperDisplayName = result.DisplayName;
        _config.DeveloperRole        = result.Role;
        await _config.SaveAsync();

        LoginSucceeded?.Invoke(this,
            new LoginSucceededArgs(result.EtwId, result.DisplayName, result.AccessToken, result.Role));
    }

    private void SetStatus(string msg, bool error)
    {
        StatusMessage = msg;
        IsError       = error;
    }
}
