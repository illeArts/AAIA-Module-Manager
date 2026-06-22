using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Marketplace;
using System.Collections.ObjectModel;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// Phase 5.9 — ETW Marketplace Dashboard.
///
/// Lädt Verkaufs- und Lizenzsignale für alle Extensions des eingeloggten ETWs.
/// Daten kommen aus der Marketplace-API (aus MoR-Webhooks aggregiert).
/// Kein Ersatz für MoR-Buchhaltung.
/// </summary>
public partial class DeveloperDashboardViewModel : ObservableObject
{
    private readonly RegistryApiClient _api;

    // ── Ladezustand ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusText       = "";
    [ObservableProperty] private bool   _hasError;

    // ── Gesamtübersicht ───────────────────────────────────────────────────────

    [ObservableProperty] private string _developerName    = "";
    [ObservableProperty] private string _etwId            = "";
    [ObservableProperty] private int    _publishedCount;
    [ObservableProperty] private int    _totalActiveLicenses;
    [ObservableProperty] private int    _totalSales;
    [ObservableProperty] private int    _totalRevocations;

    // ── Extensions ────────────────────────────────────────────────────────────

    public ObservableCollection<DeveloperExtensionRowViewModel> Extensions { get; } = new();

    // ── Selektierte Extension (Detailansicht) ─────────────────────────────────

    [ObservableProperty] private DeveloperExtensionRowViewModel? _selectedExtension;

    // ── MoR Status (Phase 5.11) ───────────────────────────────────────────────

    [ObservableProperty] private bool             _morProviderConnected;
    [ObservableProperty] private string?          _morProvider;
    [ObservableProperty] private bool             _morCheckoutActive;
    [ObservableProperty] private bool             _morPayoutSetupComplete;
    [ObservableProperty] private DateTimeOffset?  _morLastEvent;
    [ObservableProperty] private bool             _morWebhookHealthy;
    [ObservableProperty] private int              _morActiveMappings;
    [ObservableProperty] private int              _morModulesWithCheckout;
    [ObservableProperty] private bool             _morStatusLoaded;

    // Formatierte MoR-Statuslabels
    public string MorProviderLabel  => MorProviderConnected
        ? $"{MorProvider ?? "Verbunden"} ({MorActiveMappings} Mapping(s))"
        : "Kein MoR-Provider verbunden";

    public string MorPayoutLabel    => MorPayoutSetupComplete ? "✅ Vollständig" : "⚠ Unvollständig";
    public string MorCheckoutLabel  => MorCheckoutActive      ? "✅ Aktiv"       : "❌ Kein Checkout";
    public string MorWebhookLabel   => !MorProviderConnected  ? "—"
        : MorWebhookHealthy ? "✅ Gesund"
        : MorLastEvent.HasValue ? $"⚠ Letztes Event: {MorLastEvent:dd.MM.yyyy}"
        : "⚠ Kein Event empfangen";

    public DeveloperDashboardViewModel(RegistryApiClient api)
    {
        _api = api;
    }

    /// <summary>Wird aufgerufen wenn ETW einloggt (Token bereits im Client gesetzt).</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading  = true;
        HasError   = false;
        StatusText = "Lade Dashboard…";
        Extensions.Clear();

        try
        {
            var dto = await _api.GetDeveloperDashboardAsync(ct);

            if (dto is null)
            {
                HasError   = true;
                StatusText = "Dashboard konnte nicht geladen werden. Marketplace-API erreichbar?";
                return;
            }

            DeveloperName        = dto.DisplayName;
            EtwId                = dto.EtwId;
            PublishedCount       = dto.PublishedExtensions;
            TotalActiveLicenses  = dto.TotalActiveLicenses;
            TotalSales           = dto.TotalSalesFromWebhooks;
            TotalRevocations     = dto.TotalRevocations;

            foreach (var ext in dto.Extensions)
                Extensions.Add(new DeveloperExtensionRowViewModel(ext));

            StatusText = Extensions.Count == 0
                ? "Noch keine Extensions veröffentlicht."
                : $"{Extensions.Count} Extension(s) geladen.";

            // MoR Status parallel laden
            var morStatus = await _api.GetMorStatusAsync(ct);
            if (morStatus is not null)
            {
                MorProviderConnected    = morStatus.ProviderConnected;
                MorProvider             = morStatus.Provider;
                MorCheckoutActive       = morStatus.CheckoutActive;
                MorPayoutSetupComplete  = morStatus.PayoutSetupComplete;
                MorLastEvent            = morStatus.LastMorEvent;
                MorWebhookHealthy       = morStatus.WebhookHealthy;
                MorActiveMappings       = morStatus.ActiveMappings;
                MorModulesWithCheckout  = morStatus.ModulesWithCheckout;
                MorStatusLoaded         = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "";
        }
        catch (Exception ex)
        {
            HasError   = true;
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        SelectedExtension = null;
        await LoadAsync();
    }
}

/// <summary>
/// Zeile in der Extension-Tabelle des Dashboards.
/// Lazy-lädt Webhook-Events bei Auswahl.
/// </summary>
public partial class DeveloperExtensionRowViewModel : ObservableObject
{
    private readonly DeveloperExtensionSummaryDto _dto;

    // ── Basis-Properties (direkt aus DTO) ─────────────────────────────────────

    public string  ExtensionId          => _dto.ExtensionId;
    public string  Name                 => _dto.Name;
    public string  Description          => _dto.Description;
    public string  LicenseModel         => _dto.LicenseModel;
    public string  Status               => _dto.Status;
    public bool    IsPublished          => _dto.IsPublished;
    public string  TrustLevel           => _dto.TrustLevel;
    public bool    CheckoutActive       => _dto.CheckoutActive;
    public string? MorProvider          => _dto.MorProvider;
    public string? MorExternalProductId => _dto.MorExternalProductId;

    // ── Lizenzstatistiken ─────────────────────────────────────────────────────

    public int LicensesActive    => _dto.LicensesActive;
    public int LicensesExpired   => _dto.LicensesExpired;
    public int LicensesRevoked   => _dto.LicensesRevoked;
    public int LicensesUnclaimed => _dto.LicensesUnclaimed;
    public int LicensesTotal     => _dto.LicensesTotal;
    public int SalesFromWebhooks => _dto.SalesFromWebhooks;

    // ── Formatierte Anzeige ───────────────────────────────────────────────────

    public string StatusBadgeLabel => IsPublished ? "✅ Veröffentlicht" : $"⏳ {Status}";
    public string CheckoutLabel    => CheckoutActive ? "✅ Aktiv" : "❌ Nicht konfiguriert";
    public string MorLabel         => MorProvider is not null
        ? $"{MorProvider} · {MorExternalProductId ?? "—"}"
        : "Kein MoR-Mapping";

    public string LastWebhookLabel => _dto.LastWebhookAt.HasValue
        ? $"{_dto.LastWebhookEventType} · {_dto.LastWebhookAt:dd.MM.yyyy HH:mm}"
        : "—";

    // ── Webhook-Events (lazy) ─────────────────────────────────────────────────

    public ObservableCollection<DeveloperWebhookEventDto> RecentEvents { get; } = new();
    [ObservableProperty] private bool   _eventsLoaded;
    [ObservableProperty] private bool   _eventsLoading;

    public DeveloperExtensionRowViewModel(DeveloperExtensionSummaryDto dto)
    {
        _dto = dto;
    }

    public async Task LoadEventsAsync(RegistryApiClient api, CancellationToken ct = default)
    {
        if (EventsLoaded || EventsLoading) return;
        EventsLoading = true;
        try
        {
            var events = await api.GetWebhookEventsAsync(ExtensionId, ct);
            RecentEvents.Clear();
            foreach (var e in events) RecentEvents.Add(e);
            EventsLoaded = true;
        }
        finally { EventsLoading = false; }
    }
}
