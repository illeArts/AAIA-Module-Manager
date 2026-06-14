using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Marketplace;

namespace AAIA.ModuleManager.ViewModels;

public partial class LicenseActivateViewModel : ObservableObject
{
    private readonly AppConfig _cfg;

    [ObservableProperty] private string _licenseKey    = "";
    [ObservableProperty] private string _email         = "";
    [ObservableProperty] private string _deviceId      = "";
    [ObservableProperty] private string _moduleId      = "";   // optional
    [ObservableProperty] private bool   _isBusy        = false;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool   _isSuccess     = false;
    [ObservableProperty] private bool   _isError       = false;

    /// <summary>Nach erfolgreicher Aktivierung befüllt — Caller liest das aus.</summary>
    public ActivatedLicense? Result { get; private set; }

    public LicenseActivateViewModel(AppConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>
    /// Versucht, die DeviceId automatisch von AAIAS zu holen.
    /// Falls AAIAS nicht verbunden ist, bleibt das Feld leer (manuelle Eingabe).
    /// </summary>
    public async Task TryPrefillDeviceIdAsync(AaiasConnectionService? aaiasConn)
    {
        if (aaiasConn is null || !aaiasConn.IsConnected) return;
        var id = await aaiasConn.GetServerIdAsync();
        if (!string.IsNullOrWhiteSpace(id))
            DeviceId = id;
    }

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task Activate(CancellationToken ct)
    {
        IsBusy        = true;
        IsSuccess     = false;
        IsError       = false;
        StatusMessage = "Lizenz wird aktiviert…";

        try
        {
            var backendUrl = _cfg.MarketplaceBackendApiUrl.TrimEnd('/');

            // Für localhost SSL überspringen (Dev-Umgebung)
            var handler = new HttpClientHandler();
            if (new Uri(backendUrl).Host is "localhost" or "127.0.0.1")
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var http = new HttpClient(handler) { BaseAddress = new Uri(backendUrl + "/") };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/2.0");

            var client = new MarketplaceLicenseClient(http);

            var response = await client.ActivateAsync(new LicenseActivationRequest(
                LicenseKey: LicenseKey.Trim(),
                BuyerEmail: Email.Trim(),
                DeviceId:   DeviceId.Trim(),
                ModuleId:   string.IsNullOrWhiteSpace(ModuleId) ? null : ModuleId.Trim()),
                ct);

            Result = new ActivatedLicense
            {
                LicenseKey    = response.LicenseKey,
                ModuleId      = response.ModuleId,
                ExtensionType = response.ExtensionType,
                BuyerEmail    = Email.Trim(),
                DeviceId      = DeviceId.Trim(),
                LicenseJwt    = response.LicenseJwt,
                DownloadUrl   = response.DownloadUrl,
                Locale        = response.Locale,
                ActivatedAt   = response.IssuedAt,
                ExpiresAt     = response.ExpiresAt,
            };

            StatusMessage = response.ExtensionType == "LanguagePack"
                ? $"✅ Sprachpaket aktiviert. Download-URL erhalten."
                : $"✅ Lizenz aktiviert. JWT erhalten (gültig bis {Result.ExpiresDisplay}).";

            IsSuccess = true;
        }
        catch (LicenseActivationApiException ex) when (ex.IsUserError)
        {
            StatusMessage = $"❌ {ex.Message}";
            IsError       = true;
        }
        catch (LicenseActivationApiException ex) when (ex.IsRateLimited)
        {
            StatusMessage = "⏳ Rate-Limit erreicht — bitte 60 Sekunden warten.";
            IsError       = true;
        }
        catch (LicenseActivationApiException ex)
        {
            StatusMessage = $"❌ Server-Fehler: {ex.Message}";
            IsError       = true;
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"❌ Verbindungsfehler: {ex.Message}";
            IsError       = true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Abgebrochen.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanActivate() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(LicenseKey) &&
        !string.IsNullOrWhiteSpace(Email);

    partial void OnLicenseKeyChanged(string value) => ActivateCommand.NotifyCanExecuteChanged();
    partial void OnEmailChanged(string value)       => ActivateCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)        => ActivateCommand.NotifyCanExecuteChanged();
}
