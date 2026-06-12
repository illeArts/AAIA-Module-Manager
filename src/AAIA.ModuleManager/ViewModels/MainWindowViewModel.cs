using CommunityToolkit.Mvvm.ComponentModel;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public SdkTabViewModel       SdkTab       { get; }
    public ModuleTabViewModel    ModuleTab    { get; }
    public RegistryTabViewModel  RegistryTab  { get; }
    public TesterTabViewModel    TesterTab    { get; }
    public SetupTabViewModel     SetupTab     { get; }
    public DeveloperTabViewModel DeveloperTab { get; }
    public PublishTabViewModel   PublishTab   { get; }

    public MainWindowViewModel()
    {
        // Use config loaded by App.axaml.cs; fall back to synchronous load if not yet set
        var config = AppConfig.Current ?? AppConfig.Load();
        AppConfig.Current ??= config;

        var marketplaceClient = new MarketplaceApiClient(config.MarketplaceApiUrl);
        var certSvc           = new PublisherCertService(marketplaceClient);
        var publishSvc        = new PublishService(certSvc, marketplaceClient, config);

        SdkTab       = new SdkTabViewModel(config);
        ModuleTab    = new ModuleTabViewModel(config);
        RegistryTab  = new RegistryTabViewModel(config);
        TesterTab    = new TesterTabViewModel();
        SetupTab     = new SetupTabViewModel(config);
        DeveloperTab = new DeveloperTabViewModel(config, marketplaceClient, certSvc);
        PublishTab   = new PublishTabViewModel(config, publishSvc, marketplaceClient);

        // V2: pre-fill AAIAS connection from config
        _ = TesterTab.InitAsync(config);
    }
}
