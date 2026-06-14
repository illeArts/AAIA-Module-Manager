using Avalonia.Controls;
using AAIA.ModuleManager.Services;
using System;
using System.Threading.Tasks;

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
        {
            ApiUrlBox.Text        = cfg.MarketplaceApiUrl;
            BackendApiUrlBox.Text = cfg.MarketplaceBackendApiUrl;
            RefreshStoredConnectionStatus(cfg);
        }

        _ = RefreshGithubStatusAsync();
    }

    private void Cancel_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lang = RadioEn.IsChecked == true ? AppLanguage.En : AppLanguage.De;
        LanguageService.SetLanguage(lang);

        if (AppConfig.Current is { } cfg)
        {
            cfg.Language                   = lang == AppLanguage.En ? "en" : "de";
            cfg.MarketplaceApiUrl          = ApiUrlBox.Text?.Trim().TrimEnd('/')        ?? cfg.MarketplaceApiUrl;
            cfg.MarketplaceBackendApiUrl   = BackendApiUrlBox.Text?.Trim().TrimEnd('/') ?? cfg.MarketplaceBackendApiUrl;
            _ = cfg.SaveAsync();
        }

        Close();
    }

    private void RefreshStoredConnectionStatus(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.MarketplaceToken) &&
            !string.IsNullOrWhiteSpace(cfg.DeveloperEtwId))
        {
            var name = string.IsNullOrWhiteSpace(cfg.DeveloperDisplayName)
                ? cfg.DeveloperEtwId
                : $"{cfg.DeveloperDisplayName} ({cfg.DeveloperEtwId})";
            MarketplaceStatusText.Text = $"Verbunden als {name}";
        }
        else
        {
            MarketplaceStatusText.Text = "Nicht verbunden";
        }

        AaiasStatusText.Text = string.IsNullOrWhiteSpace(cfg.AaiasUrl)
            ? "Keine Server-URL gespeichert"
            : $"Konfiguriert: {cfg.AaiasUrl.TrimEnd('/')}";
    }

    private async Task RefreshGithubStatusAsync()
    {
        GithubStatusText.Text = "wird geprüft...";

        var result = await ProcessRunner.RunCapturedAsync("gh", "auth status --hostname github.com");
        if (result.Success)
        {
            GithubStatusText.Text = "Verbunden mit GitHub";
            return;
        }

        var output = result.Output.Trim();
        GithubStatusText.Text = output.Contains("konnte nicht gestartet", StringComparison.OrdinalIgnoreCase)
            ? "GitHub CLI nicht installiert"
            : "Nicht verbunden";
    }
}
