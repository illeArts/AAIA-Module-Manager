using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.Shared.Contracts.Publisher;

namespace AAIA.ModuleManager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public SdkTabViewModel            SdkTab       { get; }
    public ModuleTabViewModel         ModuleTab    { get; }
    public RegistryTabViewModel       RegistryTab  { get; }
    public TesterTabViewModel         TesterTab    { get; }
    public SetupTabViewModel          SetupTab     { get; }
    public DeveloperTabViewModel      DeveloperTab { get; }
    public PublishTabViewModel        PublishTab   { get; }
    public LicensesTabViewModel       LicensesTab  { get; }
    public MarketplaceBrowseViewModel BrowseTab    { get; }
    public AdminTabViewModel          AdminTab     { get; }

    // ── Developer-Identität (Titelleiste) ─────────────────────────────────────

    /// <summary>True wenn ein ETW-Account angemeldet ist.</summary>
    [ObservableProperty] private bool          _isLoggedIn;
    /// <summary>Anzeigename in der Titelleiste, z.B. "Max Mustermann".</summary>
    [ObservableProperty] private string        _developerDisplayName = "";
    /// <summary>ETW-ID in der Titelleiste, z.B. "ETW-000042".</summary>
    [ObservableProperty] private string        _developerEtwId       = "";
    /// <summary>Rolle des eingeloggten Entwicklers.</summary>
    [ObservableProperty] private DeveloperRole _developerRole        = DeveloperRole.Community;

    /// <summary>Nur Owner sieht Admin-Tabs (SDK/Contracts, NuGet, …).</summary>
    public bool IsOwner => DeveloperRole == DeveloperRole.Owner;
    /// <summary>Alle ETW-Tabs für Owner + alle anderen Rollen.</summary>
    public bool IsEtw   => IsLoggedIn;

    // ── Konstruktor ────────────────────────────────────────────────────────────

    public MainWindowViewModel()
    {
        var config = AppConfig.Current ?? AppConfig.Load();
        AppConfig.Current ??= config;

        var marketplaceClient = new MarketplaceApiClient(config.MarketplaceBackendApiUrl);
        var wpMarketplace     = new WpMarketplaceClient(config.MarketplaceApiUrl);
        var certSvc           = new PublisherCertService(marketplaceClient);
        var publishSvc        = new PublishService(certSvc, marketplaceClient, config);

        SdkTab       = new SdkTabViewModel(config);
        ModuleTab    = new ModuleTabViewModel(config);
        RegistryTab  = new RegistryTabViewModel(config);
        TesterTab    = new TesterTabViewModel();
        SetupTab     = new SetupTabViewModel(config);
        DeveloperTab = new DeveloperTabViewModel(config, marketplaceClient, wpMarketplace, certSvc);
        PublishTab   = new PublishTabViewModel(config, publishSvc, marketplaceClient);
        LicensesTab  = new LicensesTabViewModel(config, TesterTab.AaiasConn);
        BrowseTab    = new MarketplaceBrowseViewModel(wpMarketplace);
        AdminTab     = new AdminTabViewModel(wpMarketplace);

        _ = TesterTab.InitAsync(config);

        // Gespeicherte Identität in Titelleiste laden
        if (!string.IsNullOrEmpty(config.DeveloperEtwId))
        {
            DeveloperEtwId       = config.DeveloperEtwId ?? "";
            DeveloperDisplayName = config.DeveloperDisplayName ?? "";
            DeveloperRole        = config.DeveloperRole;
            IsLoggedIn           = true;

            // Bearer-Token wiederherstellen (beide Clients)
            if (!string.IsNullOrEmpty(config.MarketplaceToken))
            {
                marketplaceClient.SetBearer(config.MarketplaceToken);
                wpMarketplace.SetBearer(config.MarketplaceToken);
            }
        }
    }

    partial void OnDeveloperRoleChanged(DeveloperRole value)
    {
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(IsEtw));
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEtw));
    }

    /// <summary>
    /// Wird von App.axaml.cs aufgerufen nachdem LoginWindow erfolgreich abgeschlossen wurde.
    /// Aktualisiert Titelleiste sofort ohne Neustart.
    /// </summary>
    public void SetDeveloperIdentity(string etwId, string displayName, DeveloperRole role = DeveloperRole.Community)
    {
        DeveloperEtwId       = etwId;
        DeveloperDisplayName = displayName;
        DeveloperRole        = role;
        IsLoggedIn           = true;
    }
}
