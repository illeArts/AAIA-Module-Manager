using Avalonia.Controls;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        RadioDe.IsChecked = LanguageService.Current == AppLanguage.De;
        RadioEn.IsChecked = LanguageService.Current == AppLanguage.En;

        // Gespeicherte API-URL vorladen
        if (AppConfig.Current is { } cfg)
            ApiUrlBox.Text = cfg.MarketplaceApiUrl;
    }

    private void Cancel_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lang = RadioEn.IsChecked == true ? AppLanguage.En : AppLanguage.De;
        LanguageService.SetLanguage(lang);

        if (AppConfig.Current is { } cfg)
        {
            cfg.Language          = lang == AppLanguage.En ? "en" : "de";
            cfg.MarketplaceApiUrl = ApiUrlBox.Text?.Trim().TrimEnd('/') ?? cfg.MarketplaceApiUrl;
            _ = cfg.SaveAsync();
        }

        Close();
    }
}
