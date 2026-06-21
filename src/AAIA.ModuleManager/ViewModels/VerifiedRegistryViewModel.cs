using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Marketplace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AAIA.ModuleManager.ViewModels;

// Installations-Zustände pro Extension-Kachel (Phase 5.4b)
public enum InstallState
{
    Idle,
    Installing,
    Installed,
    Error
}

// Lizenzstatus-Abfrage-Zustand pro Extension-Kachel (Phase 5.7b)
public enum LicenseQueryState
{
    Idle,
    Checking,          // Anfrage läuft
    Active,            // Lizenz gültig
    NoLicense,         // Keine Lizenz → Kaufen anzeigen
    Expired,           // Abo abgelaufen → Verlängern anzeigen
    Revoked,           // Widerrufen
    AuthRequired,      // Nicht eingeloggt
    Error              // Netzwerk-/API-Fehler
}

// Download-Zustände pro Extension-Kachel
public enum DownloadState
{
    Idle,
    Downloading,
    Done,
    Error,
    LicenseRequired,     // 403: keine Lizenz → Kauflink zeigen
    AuthRequired,        // 401: nicht angemeldet
    SubscriptionExpired, // 403: Abo abgelaufen
    LicenseRevoked       // 403: Lizenz widerrufen
}

// ── Item-ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// Darstellung einer Extension aus dem verifizierten Marketplace-Katalog.
/// Enthält auch Download-Status für die UI.
/// </summary>
public sealed partial class VerifiedExtensionVm : ObservableObject
{
    public string   ExtensionId         { get; init; } = "";
    public string   DisplayName         { get; init; } = "";
    public string   Description         { get; init; } = "";
    public string   Category            { get; init; } = "";
    public string   TypeLabel           { get; init; } = "";
    public string   TypeColor           { get; init; } = "#5865f2";
    public string   LatestVersion       { get; init; } = "";
    public string   PublisherEtwId      { get; init; } = "";
    public string   PublisherDisplay    { get; init; } = "";
    public string   TrustLevelLabel     { get; init; } = "";
    public string   TrustLevelColor     { get; init; } = "#8892a4";
    public string   RiskLabel           { get; init; } = "";
    public string   RiskColor           { get; init; } = "#8892a4";
    public string   LicenseLabel        { get; init; } = "";
    public string   PriceDisplay        { get; init; } = "Kostenlos";
    public string   MinAaiaVersion      { get; init; } = "";
    public string   PublishedDisplay    { get; init; } = "";

    public string? CheckoutUrl { get; init; }

    // ── Lizenzstatus-Zustand (Phase 5.7b) ─────────────────────────────────────

    [ObservableProperty] private LicenseQueryState _licenseQueryState = LicenseQueryState.Idle;
    [ObservableProperty] private string            _licenseStatusText  = "";
    [ObservableProperty] private string            _licenseStatusColor = "#8892a4";

    /// <summary>Sichtbar sobald eine Lizenz-Abfrage erfolgte (Idle = ausgeblendet).</summary>
    public bool LicenseStatusVisible => LicenseQueryState != LicenseQueryState.Idle;

    public string LicenseCheckButtonLabel => LicenseQueryState switch
    {
        LicenseQueryState.Checking => "⏳ Prüfe…",
        _                          => "🔑 Lizenz prüfen"
    };

    public bool IsLicenseChecking => LicenseQueryState == LicenseQueryState.Checking;

    /// <summary>Zeigt "Kaufen"-Button wenn kein gültiger Download möglich und CheckoutUrl gesetzt.</summary>
    public bool ShowBuyButton =>
        !string.IsNullOrWhiteSpace(CheckoutUrl)
        && LicenseQueryState is LicenseQueryState.NoLicense
                             or LicenseQueryState.Expired
                             or LicenseQueryState.Idle;

    partial void OnLicenseQueryStateChanged(LicenseQueryState value)
    {
        OnPropertyChanged(nameof(LicenseStatusVisible));
        OnPropertyChanged(nameof(LicenseCheckButtonLabel));
        OnPropertyChanged(nameof(IsLicenseChecking));
        OnPropertyChanged(nameof(ShowBuyButton));
    }

    // ── Download-Zustand (mutable, Observable) ────────────────────────────────

    [ObservableProperty] private DownloadState _downloadState = DownloadState.Idle;
    [ObservableProperty] private double        _downloadProgress;        // 0..1
    [ObservableProperty] private string        _downloadStatusText = "";
    [ObservableProperty] private string?       _localFilePath;

    // ── Installations-Zustand (mutable, Observable) ───────────────────────────

    [ObservableProperty] private InstallState _installState = InstallState.Idle;
    [ObservableProperty] private string       _installStatusText = "";
    [ObservableProperty] private bool         _restartRequired;

    public string InstallButtonLabel => InstallState switch
    {
        InstallState.Installing => "⏳ Wird installiert…",
        InstallState.Installed  => "✅ Erneut installieren",
        InstallState.Error      => "⚠ Erneut versuchen",
        _                       => "📦 In AAIAS installieren"
    };

    public bool IsInstalling => InstallState == InstallState.Installing;
    public bool CanInstall   => DownloadState == DownloadState.Done && LocalFilePath is not null && !IsInstalling;

    partial void OnInstallStateChanged(InstallState value)
    {
        OnPropertyChanged(nameof(InstallButtonLabel));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(CanInstall));
    }

    partial void OnLocalFilePathChanged(string? value)
        => OnPropertyChanged(nameof(CanInstall));

    public string DownloadButtonLabel => DownloadState switch
    {
        DownloadState.Downloading        => "⏳ Wird geladen…",
        DownloadState.Done               => "✅ Erneut laden",
        DownloadState.Error              => "⚠ Erneut versuchen",
        DownloadState.LicenseRequired    => "💳 Lizenz erforderlich",
        DownloadState.AuthRequired       => "🔒 Anmeldung erforderlich",
        DownloadState.SubscriptionExpired=> "⏰ Abo abgelaufen",
        DownloadState.LicenseRevoked     => "🚫 Lizenz widerrufen",
        _                                => "⬇ Herunterladen"
    };

    public bool IsDownloading       => DownloadState == DownloadState.Downloading;
    public bool IsLicenseBlocked    => DownloadState is
                                           DownloadState.LicenseRequired or
                                           DownloadState.AuthRequired or
                                           DownloadState.SubscriptionExpired or
                                           DownloadState.LicenseRevoked;

    partial void OnDownloadStateChanged(DownloadState value)
    {
        OnPropertyChanged(nameof(DownloadButtonLabel));
        OnPropertyChanged(nameof(IsLicenseBlocked));
    }

    // ──────────────────────────────────────────────────────────────────────────

    public static VerifiedExtensionVm FromDto(RegistryExtensionDto dto) => new()
    {
        ExtensionId      = dto.ExtensionId,
        DisplayName      = dto.DisplayName,
        Description      = dto.Description ?? "",
        Category         = dto.Category    ?? "",
        LatestVersion    = dto.LatestVersion ?? "-",
        PublisherEtwId   = dto.PublisherEtwId,
        PublisherDisplay = dto.PublisherDisplayName,
        MinAaiaVersion   = string.IsNullOrEmpty(dto.MinAaiaVersion) ? "" : $"ab AAIA {dto.MinAaiaVersion}",
        PublishedDisplay = dto.LatestPublishedAt.HasValue
            ? dto.LatestPublishedAt.Value.LocalDateTime.ToString("dd.MM.yyyy")
            : "",

        TypeLabel = dto.ExtensionType switch
        {
            "Module"       => "📦 Modul",
            "Plugin"       => "🔌 Plugin",
            "LanguagePack" => "🌐 Sprachpaket",
            _              => dto.ExtensionType
        },
        TypeColor = dto.ExtensionType switch
        {
            "Module"       => "#06d6a0",
            "Plugin"       => "#5865f2",
            "LanguagePack" => "#f4a261",
            _              => "#8892a4"
        },

        TrustLevelLabel = dto.LatestTrustLevel switch
        {
            "MarketplaceVerified" => "✓ Marketplace-Zertifiziert",
            "EtwLocalVerified"    => "🔑 ETW-Signiert",
            _                     => "⚠ Unbekannt"
        },
        TrustLevelColor = dto.LatestTrustLevel switch
        {
            "MarketplaceVerified" => "#06d6a0",
            "EtwLocalVerified"    => "#5865f2",
            _                     => "#f4a261"
        },

        RiskLabel = dto.LatestRiskLevel switch
        {
            "Low"    => "🟢 Niedriges Risiko",
            "Medium" => "🟡 Mittleres Risiko",
            "High"   => "🔴 Hohes Risiko",
            _        => "⬜ Risiko unbekannt"
        },
        RiskColor = dto.LatestRiskLevel switch
        {
            "Low"    => "#06d6a0",
            "Medium" => "#f4a261",
            "High"   => "#ef4444",
            _        => "#8892a4"
        },

        CheckoutUrl = dto.CheckoutUrl,
        LicenseLabel = dto.LicenseModel switch
        {
            "Free"         => "Kostenlos",
            "Paid"         => "Kostenpflichtig",
            "Subscription" => "Abonnement",
            "Enterprise"   => "Enterprise",
            _              => dto.LicenseModel
        },
        PriceDisplay = dto.Price is null or 0
            ? "Kostenlos"
            : $"{dto.Price:F2} {dto.Currency ?? "EUR"}"
    };
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

/// <summary>
/// Marketplace-Katalog aus verifizierten, signierten Releases der marketplace-api.
/// Ersetzt/ergänzt den WooCommerce-basierten BrowseTab für Phase 5.2.
/// </summary>
public sealed partial class VerifiedRegistryViewModel : ObservableObject
{
    private readonly RegistryApiClient        _client;
    private readonly ExtensionDownloadService _downloader;
    private readonly AaiasConnectionService?  _aaiasConn;
    private List<VerifiedExtensionVm> _all = [];

    [ObservableProperty] private ObservableCollection<VerifiedExtensionVm> _extensions = [];
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _searchText    = "";
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private VerifiedExtensionVm? _selectedExtension;

    public VerifiedRegistryViewModel(
        RegistryApiClient        client,
        ExtensionDownloadService downloader,
        AaiasConnectionService?  aaiasConn = null)
    {
        _client     = client;
        _downloader = downloader;
        _aaiasConn  = aaiasConn;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading     = true;
        StatusMessage = "";
        try
        {
            var response = await _client.GetExtensionsAsync(page: 1, pageSize: 100, ct: ct);
            _all       = response.Items.Select(VerifiedExtensionVm.FromDto).ToList();
            TotalCount = response.TotalCount;
            ApplyFilter();

            if (TotalCount == 0)
                StatusMessage = "Noch keine verifizierten Extensions im Marketplace.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Laden: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenDetails(VerifiedExtensionVm? item)
    {
        if (item is null) return;
        SelectedExtension = item;
    }

    [RelayCommand]
    private async Task DownloadAsync(VerifiedExtensionVm? item, CancellationToken ct = default)
    {
        if (item is null || item.IsDownloading) return;

        if (item.LatestVersion is "-" or "" or null)
        {
            item.DownloadState      = DownloadState.Error;
            item.DownloadStatusText = "Keine Version verfügbar.";
            return;
        }

        item.DownloadState       = DownloadState.Downloading;
        item.DownloadProgress    = 0;
        item.DownloadStatusText  = "Verbindung wird hergestellt…";
        item.LocalFilePath       = null;

        var progress = new Progress<double>(p =>
        {
            item.DownloadProgress   = p;
            item.DownloadStatusText = $"{p:P0} heruntergeladen…";
        });

        try
        {
            var result = await _downloader.DownloadAsync(
                item.ExtensionId, item.LatestVersion, progress, ct);

            if (result.Success)
            {
                item.DownloadState      = DownloadState.Done;
                item.LocalFilePath      = result.LocalPath;
                item.DownloadStatusText = result.HashVerified
                    ? $"✅ {FormatBytes(result.FileSizeBytes)} — Hash verifiziert"
                    : $"✅ {FormatBytes(result.FileSizeBytes)} — kein Hash im Server-Header";
            }
            else
            {
                // Lizenz-spezifische Zustände statt generischer Error
                item.DownloadState = result.DeniedReason switch
                {
                    AAIA.Shared.Contracts.Marketplace.DownloadDeniedReason.AuthRequired
                        => DownloadState.AuthRequired,
                    AAIA.Shared.Contracts.Marketplace.DownloadDeniedReason.LicenseRequired
                        => DownloadState.LicenseRequired,
                    AAIA.Shared.Contracts.Marketplace.DownloadDeniedReason.SubscriptionExpired
                        => DownloadState.SubscriptionExpired,
                    AAIA.Shared.Contracts.Marketplace.DownloadDeniedReason.LicenseRevoked
                        => DownloadState.LicenseRevoked,
                    _ => DownloadState.Error
                };
                item.DownloadStatusText = result.ErrorMessage ?? "Unbekannter Fehler.";
            }
        }
        catch (OperationCanceledException)
        {
            item.DownloadState      = DownloadState.Idle;
            item.DownloadStatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            item.DownloadState      = DownloadState.Error;
            item.DownloadStatusText = $"Fehler: {ex.Message}";
        }
    }

    [RelayCommand]
    private static void OpenLocalFile(VerifiedExtensionVm? item)
    {
        if (item?.LocalFilePath is null || !File.Exists(item.LocalFilePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = Path.GetDirectoryName(item.LocalFilePath)!,
                UseShellExecute = true
            });
        }
        catch { /* Ignorieren */ }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes / 1024.0 / 1024.0:F1} MB"
    };

    [RelayCommand]
    private static void OpenPublisherUrl(VerifiedExtensionVm? item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://aaiagent.de/developer/{item.PublisherEtwId}",
                UseShellExecute = true
            });
        }
        catch { /* Ignorieren */ }
    }

    // ── Lizenz prüfen + Kaufen (Phase 5.7b) ──────────────────────────────────

    /// <summary>
    /// Ruft GET /api/marketplace/extensions/{extensionId}/license-status auf
    /// und aktualisiert den LicenseQueryState der Kachel.
    ///
    /// Voraussetzung: Bearer-Token in _client muss gesetzt sein (= Nutzer ist angemeldet).
    /// Wenn nicht angemeldet: AuthRequired-Zustand → UI zeigt entsprechenden Hinweis.
    /// </summary>
    [RelayCommand]
    private async Task CheckLicenseAsync(VerifiedExtensionVm? item, CancellationToken ct = default)
    {
        if (item is null || item.IsLicenseChecking) return;

        item.LicenseQueryState  = LicenseQueryState.Checking;
        item.LicenseStatusText  = "Prüfe Lizenzstatus…";
        item.LicenseStatusColor = "#8892a4";

        try
        {
            var dto = await _client.GetLicenseStatusAsync(item.ExtensionId, ct);

            if (dto is null)
            {
                item.LicenseQueryState  = LicenseQueryState.Error;
                item.LicenseStatusText  = "Extension nicht gefunden.";
                item.LicenseStatusColor = "#ef4444";
                return;
            }

            if (dto.CanDownload)
            {
                item.LicenseQueryState  = LicenseQueryState.Active;
                var expiry = dto.ExpiresAt.HasValue
                    ? $" (gültig bis {dto.ExpiresAt.Value.LocalDateTime:dd.MM.yyyy})"
                    : "";
                item.LicenseStatusText  = $"✅ Lizenz aktiv{expiry}";
                item.LicenseStatusColor = "#06d6a0";
            }
            else if (!dto.HasLicense)
            {
                item.LicenseQueryState  = LicenseQueryState.NoLicense;
                item.LicenseStatusText  = "Keine Lizenz gefunden. Bitte Modul kaufen.";
                item.LicenseStatusColor = "#f4a261";
            }
            else
            {
                item.LicenseQueryState = dto.Status switch
                {
                    "Expired"   => LicenseQueryState.Expired,
                    "Revoked"   => LicenseQueryState.Revoked,
                    "Suspended" => LicenseQueryState.Revoked,
                    _            => LicenseQueryState.Error
                };
                item.LicenseStatusText = dto.Status switch
                {
                    "Expired"   => $"⏰ Lizenz abgelaufen am {dto.ExpiresAt?.LocalDateTime:dd.MM.yyyy}. Bitte Abo verlängern.",
                    "Revoked"   => "🚫 Lizenz widerrufen. Bitte Support kontaktieren.",
                    "Suspended" => "🚫 Lizenz gesperrt. Bitte Support kontaktieren.",
                    _            => $"Unbekannter Lizenzstatus: {dto.Status}"
                };
                item.LicenseStatusColor = "#ef4444";
            }
        }
        catch (UnauthorizedAccessException)
        {
            item.LicenseQueryState  = LicenseQueryState.AuthRequired;
            item.LicenseStatusText  = "Nicht angemeldet. Bitte im Marketplace-Tab einloggen.";
            item.LicenseStatusColor = "#f4a261";
        }
        catch (OperationCanceledException)
        {
            item.LicenseQueryState  = LicenseQueryState.Idle;
            item.LicenseStatusText  = "";
        }
        catch (Exception ex)
        {
            item.LicenseQueryState  = LicenseQueryState.Error;
            item.LicenseStatusText  = $"Fehler: {ex.Message}";
            item.LicenseStatusColor = "#ef4444";
        }
    }

    /// <summary>
    /// Öffnet die CheckoutUrl der Extension im Standard-Browser.
    /// Nur aktiv wenn CheckoutUrl gesetzt und keine gültige Lizenz vorhanden.
    /// </summary>
    [RelayCommand]
    private static void BuyExtension(VerifiedExtensionVm? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.CheckoutUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = item.CheckoutUrl,
                UseShellExecute = true
            });
        }
        catch { /* Ignorieren */ }
    }

    /// <summary>
    /// Installiert das heruntergeladene .aaiaext-Paket in eine verbundene AAIAS-Instanz.
    ///
    /// Voraussetzungen:
    ///   - Download erfolgreich (DownloadState == Done, LocalFilePath != null)
    ///   - AAIAS verbunden (_aaiasConn?.IsConnected == true)
    ///
    /// AAIAS prüft das Paket selbst (Hash, Manifest, Signatur) — der Module Manager
    /// ist nur Transporteur und übergibt lediglich den Dateipfad.
    /// </summary>
    [RelayCommand]
    private async Task InstallAsync(VerifiedExtensionVm? item, CancellationToken ct = default)
    {
        if (item is null || item.IsInstalling) return;
        if (item.LocalFilePath is null || !File.Exists(item.LocalFilePath))
        {
            item.InstallState      = InstallState.Error;
            item.InstallStatusText = "Paketdatei nicht gefunden. Bitte zuerst herunterladen.";
            return;
        }

        if (_aaiasConn is null || !_aaiasConn.IsConnected)
        {
            item.InstallState      = InstallState.Error;
            item.InstallStatusText = "AAIAS nicht verbunden. Bitte unter 'Tester' verbinden.";
            return;
        }

        item.InstallState      = InstallState.Installing;
        item.InstallStatusText = "Wird an AAIAS übergeben…";
        item.RestartRequired   = false;

        try
        {
            var (result, error) = await _aaiasConn.InstallFromAaiaextAsync(
                item.LocalFilePath, overwrite: true, allowDowngrade: false);

            if (error is not null)
            {
                item.InstallState      = InstallState.Error;
                item.InstallStatusText = $"Fehler: {error}";
                return;
            }

            if (result is null)
            {
                item.InstallState      = InstallState.Error;
                item.InstallStatusText = "AAIAS antwortete ohne Ergebnis.";
                return;
            }

            item.InstallState    = InstallState.Installed;
            item.RestartRequired = result.RestartRequired;

            var action = result.Updated ? "aktualisiert" : "installiert";
            var restart = result.RestartRequired ? " — AAIAS-Neustart empfohlen" : "";
            item.InstallStatusText = $"✅ Extension {action} (TrustStatus: {result.TrustStatus}){restart}";
        }
        catch (OperationCanceledException)
        {
            item.InstallState      = InstallState.Idle;
            item.InstallStatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            item.InstallState      = InstallState.Error;
            item.InstallStatusText = $"Fehler: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(m =>
                m.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)  ||
                m.ExtensionId.Contains(q, StringComparison.OrdinalIgnoreCase)  ||
                m.Category.Contains(q,    StringComparison.OrdinalIgnoreCase)  ||
                m.PublisherDisplay.Contains(q, StringComparison.OrdinalIgnoreCase))
              .ToList();

        Extensions.Clear();
        foreach (var e in filtered)
            Extensions.Add(e);

        if (filtered.Count == 0 && TotalCount > 0)
            StatusMessage = "Keine Extensions gefunden.";
        else if (string.IsNullOrEmpty(StatusMessage) || StatusMessage == "Keine Extensions gefunden.")
            StatusMessage = "";
    }
}
