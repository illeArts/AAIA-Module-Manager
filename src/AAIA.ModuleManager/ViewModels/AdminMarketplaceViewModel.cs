using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Marketplace;
using System.Collections.ObjectModel;
using System.Linq;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// Phase 5.10 — Owner/Admin Marketplace Console.
///
/// Technische Betreiber-Sicht: Plattform-Statistiken, Extension-Risk/Trust,
/// Pending-Reviews, Block/Unblock. Kein Finanzsystem.
/// </summary>
public sealed partial class AdminMarketplaceViewModel : ObservableObject
{
    private readonly RegistryApiClient _client;

    // ── Übersicht ─────────────────────────────────────────────────────────────

    [ObservableProperty] private int _totalExtensions;
    [ObservableProperty] private int _publishedExtensions;
    [ObservableProperty] private int _pendingReviewCount;
    [ObservableProperty] private int _blockedReleasesCount;
    [ObservableProperty] private int _highRiskExtensions;
    [ObservableProperty] private int _mediumRiskExtensions;
    [ObservableProperty] private int _totalDevelopers;
    [ObservableProperty] private int _totalLicenses;
    [ObservableProperty] private int _activeLicenses;
    [ObservableProperty] private int _webhookEventsLast24h;
    [ObservableProperty] private int _totalBuyerAccounts;

    // ── State ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int    _selectedTabIndex;

    // ── Extensions ────────────────────────────────────────────────────────────

    public ObservableCollection<AdminExtensionRowVm> Extensions { get; } = [];

    [ObservableProperty] private string _extensionSearch = "";

    partial void OnExtensionSearchChanged(string value)
        => _ = LoadExtensionsAsync(value);

    // ── Pending Reviews ───────────────────────────────────────────────────────

    public ObservableCollection<PendingReviewRowVm> PendingReviews { get; } = [];

    // ── MoR Account Status (Phase 5.11) ───────────────────────────────────────

    public ObservableCollection<AdminMorStatusRowVm> MorAccountItems { get; } = [];

    [ObservableProperty] private int _morTotalEtws;
    [ObservableProperty] private int _morEtwsWithMapping;
    [ObservableProperty] private int _morEtwsWithCheckout;
    [ObservableProperty] private int _morEtwsWithIssues;
    [ObservableProperty] private bool _morStatusLoaded;

    public string MorSummaryLabel =>
        MorStatusLoaded
        ? $"{MorEtwsWithMapping}/{MorTotalEtws} ETWs verbunden · {MorEtwsWithIssues} Problem(e)"
        : "—";

    // ── Konstruktor ────────────────────────────────────────────────────────────

    public AdminMarketplaceViewModel(RegistryApiClient client)
    {
        _client = client;
    }

    // ── Laden ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading     = true;
        StatusMessage = "Lade Marketplace-Übersicht…";
        try
        {
            var overview = await _client.GetMarketplaceOverviewAsync(ct);
            if (overview is not null)
            {
                TotalExtensions      = overview.TotalExtensions;
                PublishedExtensions  = overview.PublishedExtensions;
                PendingReviewCount   = overview.PendingReviewCount;
                BlockedReleasesCount = overview.BlockedReleasesCount;
                HighRiskExtensions   = overview.HighRiskExtensions;
                MediumRiskExtensions = overview.MediumRiskExtensions;
                TotalDevelopers      = overview.TotalDevelopers;
                TotalLicenses        = overview.TotalLicenses;
                ActiveLicenses       = overview.ActiveLicenses;
                WebhookEventsLast24h = overview.WebhookEventsLast24h;
                TotalBuyerAccounts   = overview.TotalBuyerAccounts;
            }

            await Task.WhenAll(
                LoadExtensionsAsync(ExtensionSearch, ct),
                LoadPendingReviewsAsync(ct),
                LoadMorAccountStatusAsync(ct));

            StatusMessage = overview is null ? "⚠ Keine Verbindung zur API." : "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadMorAccountStatusAsync(CancellationToken ct = default)
    {
        var status = await _client.GetAdminMorAccountStatusAsync(ct);
        if (status is null) return;

        MorTotalEtws        = status.TotalEtws;
        MorEtwsWithMapping  = status.EtwsWithMorMapping;
        MorEtwsWithCheckout = status.EtwsWithCheckout;
        MorEtwsWithIssues   = status.EtwsWithWebhookIssues;
        MorStatusLoaded     = true;

        MorAccountItems.Clear();
        foreach (var item in status.Items.OrderBy(i => i.StatusSummary != "OK").ThenBy(i => i.EtwId))
            MorAccountItems.Add(new AdminMorStatusRowVm(item));

        OnPropertyChanged(nameof(MorSummaryLabel));
    }

    private async Task LoadExtensionsAsync(string search = "", CancellationToken ct = default)
    {
        var list = await _client.GetAdminExtensionsAsync(
            search: string.IsNullOrWhiteSpace(search) ? null : search, ct: ct);

        Extensions.Clear();
        foreach (var e in list)
            Extensions.Add(new AdminExtensionRowVm(e, this));
    }

    private async Task LoadPendingReviewsAsync(CancellationToken ct = default)
    {
        var list = await _client.GetPendingReviewsAsync(ct);
        PendingReviews.Clear();
        foreach (var r in list)
            PendingReviews.Add(new PendingReviewRowVm(r, this));
    }

    // ── Block/Unblock ─────────────────────────────────────────────────────────

    internal async Task BlockAsync(int releaseId, string reason)
    {
        var result = await _client.BlockReleaseAsync(releaseId, reason);
        if (result?.Success == true)
        {
            StatusMessage = $"✅ Release {releaseId} gesperrt.";
            await LoadAsync();
        }
        else
        {
            StatusMessage = $"❌ Fehler beim Sperren.";
        }
    }

    internal async Task UnblockAsync(int releaseId)
    {
        var result = await _client.UnblockReleaseAsync(releaseId);
        if (result?.Success == true)
        {
            StatusMessage = $"✅ Release {releaseId} entsperrt.";
            await LoadAsync();
        }
        else
        {
            StatusMessage = $"❌ Fehler beim Entsperren.";
        }
    }
}

// ── Row-ViewModels ─────────────────────────────────────────────────────────────

public sealed partial class AdminExtensionRowVm : ObservableObject
{
    private readonly AdminMarketplaceViewModel _parent;

    public string  ExtensionId    { get; }
    public string  Name           { get; }
    public string  PublisherName  { get; }
    public string  LicenseModel   { get; }
    public bool    IsPublished    { get; }
    public string? LatestVersion  { get; }
    public int     ActiveLicenses { get; }
    public int     TotalLicenses  { get; }
    public int     SalesCount     { get; }
    public string? RiskLevel      { get; }
    public string? TrustLevel     { get; }
    public string? MorProvider    { get; }
    public bool    CheckoutActive { get; }

    public string RiskColor => RiskLevel switch {
        "High"    => "#ef4444",
        "Medium"  => "#f4a261",
        "Low"     => "#06d6a0",
        _         => "#8892a4"
    };

    public string RiskBadge => RiskLevel is not null ? $"⚠ {RiskLevel}" : "—";

    public string StatusBadge => IsPublished ? "✅ Published" : "⬜ Draft";
    public string StatusColor => IsPublished ? "#06d6a0" : "#8892a4";

    public AdminExtensionRowVm(AdminExtensionDto dto, AdminMarketplaceViewModel parent)
    {
        _parent       = parent;
        ExtensionId   = dto.ExtensionId;
        Name          = dto.Name;
        PublisherName = dto.PublisherName;
        LicenseModel  = dto.LicenseModel;
        IsPublished   = dto.IsPublished;
        LatestVersion = dto.LatestVersion;
        ActiveLicenses= dto.ActiveLicenses;
        TotalLicenses = dto.TotalLicenses;
        SalesCount    = dto.SalesFromWebhooks;
        RiskLevel     = dto.RiskLevel;
        TrustLevel    = dto.TrustLevel;
        MorProvider   = dto.MorProvider;
        CheckoutActive= dto.CheckoutActive;
    }
}

public sealed partial class PendingReviewRowVm : ObservableObject
{
    private readonly AdminMarketplaceViewModel _parent;

    public int     ReleaseId     { get; }
    public string  ExtensionId   { get; }
    public string  ExtensionName { get; }
    public string  PublisherName { get; }
    public string  Version       { get; }
    public string? RiskLevel     { get; }
    public string? TrustLevel    { get; }
    public int     DaysPending   { get; }

    public string RiskColor => RiskLevel switch {
        "High"   => "#ef4444",
        "Medium" => "#f4a261",
        "Low"    => "#06d6a0",
        _        => "#8892a4"
    };

    [ObservableProperty] private bool   _isBlocking;
    [ObservableProperty] private string _blockReason = "";

    public PendingReviewRowVm(PendingReviewReleaseDto dto, AdminMarketplaceViewModel parent)
    {
        _parent       = parent;
        ReleaseId     = dto.ReleaseId;
        ExtensionId   = dto.ExtensionId;
        ExtensionName = dto.ExtensionName;
        PublisherName = dto.PublisherName;
        Version       = dto.Version;
        RiskLevel     = dto.RiskLevel;
        TrustLevel    = dto.TrustLevel;
        DaysPending   = dto.DaysPending;
    }

    [RelayCommand]
    private Task BlockAsync() =>
        _parent.BlockAsync(ReleaseId, string.IsNullOrWhiteSpace(BlockReason) ? "Blockiert durch Admin." : BlockReason);
}

/// <summary>
/// Wrapper-ViewModel für AdminMorAccountStatusItemDto.
/// Liefert formatierte Anzeige-Properties für die AXAML-View.
/// </summary>
public sealed class AdminMorStatusRowVm
{
    private readonly AdminMorAccountStatusItemDto _dto;

    public AdminMorStatusRowVm(AdminMorAccountStatusItemDto dto) => _dto = dto;

    public string  EtwId                  => _dto.EtwId;
    public string  DisplayName            => _dto.DisplayName;
    public string? Provider               => _dto.Provider;
    public int     TotalExtensions        => _dto.TotalExtensions;
    public int     ExtensionsWithCheckout => _dto.ExtensionsWithCheckout;
    public int     ActiveMappings         => _dto.ActiveMappings;
    public bool    WebhookHealthy         => _dto.WebhookHealthy;
    public string  StatusSummary          => _dto.StatusSummary;

    public string LastWebhookLabel => _dto.LastWebhookEvent.HasValue
        ? _dto.LastWebhookEvent.Value.ToString("dd.MM.yyyy HH:mm")
        : "Kein Event";

    public string WebhookHealthLabel => _dto.WebhookHealthy ? "✅ Gesund" : "⚠ Problem";

    public string StatusEmoji => _dto.StatusSummary switch
    {
        "OK"           => "✅",
        "NeedsMapping" => "🔌",
        "NoCheckout"   => "🛒",
        "WebhookStale" => "⚠",
        "NoExtensions" => "📭",
        _              => "❓"
    };

    public string StatusLabel => _dto.StatusSummary switch
    {
        "OK"           => "OK",
        "NeedsMapping" => "Kein MoR-Mapping",
        "NoCheckout"   => "Kein Checkout",
        "WebhookStale" => "Webhook veraltet",
        "NoExtensions" => "Keine Extensions",
        _              => _dto.StatusSummary
    };
}
