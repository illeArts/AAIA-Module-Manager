using Avalonia.Controls;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // Pre-select based on current language
        RadioDe.IsChecked = LanguageService.Current == AppLanguage.De;
        RadioEn.IsChecked = LanguageService.Current == AppLanguage.En;
    }

    private void Cancel_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lang = RadioEn.IsChecked == true ? AppLanguage.En : AppLanguage.De;
        LanguageService.SetLanguage(lang);

        // Persist to config
        if (AppConfig.Current is { } cfg)
        {
            cfg.Language = lang == AppLanguage.En ? "en" : "de";
            _ = cfg.SaveAsync();
        }

        Close();
    }
}
