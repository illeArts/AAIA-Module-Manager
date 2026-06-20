using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

        var cfg = AppConfig.Current ?? AppConfig.Load();
        ApiUrlBox.Text        = cfg.MarketplaceApiUrl;
        BackendApiUrlBox.Text = cfg.MarketplaceBackendApiUrl;
        RefreshStoredConnectionStatus(cfg);

        // KI-Assistent-Tab laden
        LoadAiSettings(cfg);

        // Setup/Umgebung-Tab: eigene ViewModel-Instanz
        EmbeddedSetupTab.DataContext = new ViewModels.SetupTabViewModel(cfg);

        _ = RefreshGithubStatusAsync();
    }

    // ── Allgemein ─────────────────────────────────────────────────────────────

    private void Cancel_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var lang = RadioEn.IsChecked == true ? AppLanguage.En : AppLanguage.De;
        LanguageService.SetLanguage(lang);

        if (AppConfig.Current is { } cfg)
        {
            cfg.Language                 = lang == AppLanguage.En ? "en" : "de";
            cfg.MarketplaceApiUrl        = ApiUrlBox.Text?.Trim().TrimEnd('/')        ?? cfg.MarketplaceApiUrl;
            cfg.MarketplaceBackendApiUrl = BackendApiUrlBox.Text?.Trim().TrimEnd('/') ?? cfg.MarketplaceBackendApiUrl;
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

    // ── GitHub-Status ─────────────────────────────────────────────────────────

    private async Task RefreshGithubStatusAsync()
    {
        GithubStatusText.Text = "wird geprueft...";

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

    // ── KI-Assistent ─────────────────────────────────────────────────────────

    private void LoadAiSettings(AppConfig cfg)
    {
        // Provider-RadioButton
        RadioProviderClaude.IsChecked  = cfg.AiProvider != "OpenAI" && cfg.AiProvider != "Gemini";
        RadioProviderOpenAi.IsChecked  = cfg.AiProvider == "OpenAI";
        RadioProviderGemini.IsChecked  = cfg.AiProvider == "Gemini";

        // Keys & Modelle
        ClaudeApiKeyBox.Text = cfg.ClaudeApiKey;
        SelectModel(ClaudeModelBox, _claudeModels, cfg.ClaudeModel);

        OpenAiApiKeyBox.Text = cfg.OpenAiApiKey;
        SelectModel(OpenAiModelBox, _openAiModels, cfg.OpenAiModel);

        GeminiApiKeyBox.Text = cfg.GeminiApiKey;
        SelectModel(GeminiModelBox, _geminiModels, cfg.GeminiModel);

        // AAIAS-Kontext
        AaiasUrlLabel.Text = string.IsNullOrWhiteSpace(cfg.AaiasUrl)
            ? "(keine AAIAS-URL konfiguriert — siehe Tester-Tab)"
            : cfg.AaiasUrl;

        CbSendProject.IsChecked     = cfg.AiContextSendProject;
        CbSendBuildErrors.IsChecked = cfg.AiContextSendBuildErrors;
        CbSendManifest.IsChecked    = cfg.AiContextSendManifest;
        CbSendAaiasStatus.IsChecked = cfg.AiContextSendAaiasStatus;
        CbSendSourceFiles.IsChecked = cfg.AiContextSendSourceFiles;

        // Sichtbarkeit Provider-Sektionen
        UpdateProviderSections();
    }

    private void RadioProvider_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
        => UpdateProviderSections();

    private void UpdateProviderSections()
    {
        ClaudeSection.IsVisible  = RadioProviderClaude.IsChecked == true;
        OpenAiSection.IsVisible  = RadioProviderOpenAi.IsChecked == true;
        GeminiSection.IsVisible  = RadioProviderGemini.IsChecked == true;
        AiTestResult.Text        = "";
    }

    private void SaveAi_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AppConfig.Current is not { } cfg) { Close(); return; }

        // Provider
        cfg.AiProvider   = RadioProviderOpenAi.IsChecked == true ? "OpenAI"
                         : RadioProviderGemini.IsChecked == true ? "Gemini"
                         : "Claude";

        // Keys + Modelle (immer alle speichern, nicht nur den aktiven)
        cfg.ClaudeApiKey = ClaudeApiKeyBox.Text?.Trim() ?? "";
        cfg.ClaudeModel  = SelectedModel(ClaudeModelBox, _claudeModels, "claude-haiku-4-5-20251001");

        cfg.OpenAiApiKey = OpenAiApiKeyBox.Text?.Trim() ?? "";
        cfg.OpenAiModel  = SelectedModel(OpenAiModelBox, _openAiModels, "gpt-4o-mini");

        cfg.GeminiApiKey = GeminiApiKeyBox.Text?.Trim() ?? "";
        cfg.GeminiModel  = SelectedModel(GeminiModelBox, _geminiModels, "gemini-2.0-flash");

        // Kontext-Flags
        cfg.AiContextSendProject     = CbSendProject.IsChecked     == true;
        cfg.AiContextSendBuildErrors = CbSendBuildErrors.IsChecked  == true;
        cfg.AiContextSendManifest    = CbSendManifest.IsChecked     == true;
        cfg.AiContextSendAaiasStatus = CbSendAaiasStatus.IsChecked  == true;
        cfg.AiContextSendSourceFiles = CbSendSourceFiles.IsChecked  == true;

        _ = cfg.SaveAsync();

        // AiPanelViewModel sofort aktualisieren
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is ViewModels.MainWindowViewModel mainVm)
        {
            mainVm.AiPanel.RefreshClient();
        }

        Close();
    }

    // ── Verbindungstests ─────────────────────────────────────────────────────

    private async void AiTestBtn_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AiTestResult.Text     = "Teste...";
        AiTestResult.Foreground = Avalonia.Media.Brushes.Gray;
        AiTestBtn.IsEnabled   = false;

        try
        {
            // Temporaere Provider-Instanz aus aktuellen Formularwerten
            IAiProviderService? tempProvider = null;

            if (RadioProviderClaude.IsChecked == true)
            {
                var key = ClaudeApiKeyBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    tempProvider = new ClaudeAiProvider(key,
                        SelectedModel(ClaudeModelBox, _claudeModels, "claude-haiku-4-5-20251001"));
            }
            else if (RadioProviderOpenAi.IsChecked == true)
            {
                var key = OpenAiApiKeyBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    tempProvider = new OpenAiProvider(key,
                        SelectedModel(OpenAiModelBox, _openAiModels, "gpt-4o-mini"));
            }
            else if (RadioProviderGemini.IsChecked == true)
            {
                var key = GeminiApiKeyBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    tempProvider = new GeminiAiProvider(key,
                        SelectedModel(GeminiModelBox, _geminiModels, "gemini-2.0-flash"));
            }

            if (tempProvider is null)
            {
                AiTestResult.Text       = "Kein API Key eingetragen.";
                AiTestResult.Foreground = Avalonia.Media.Brushes.OrangeRed;
                return;
            }

            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var success    = await tempProvider.TestConnectionAsync(cts.Token);

            AiTestResult.Text       = success ? "Verbindung erfolgreich." : "Verbindung fehlgeschlagen.";
            AiTestResult.Foreground = success
                ? Avalonia.Media.Brushes.LightGreen
                : Avalonia.Media.Brushes.OrangeRed;
        }
        catch (Exception ex)
        {
            AiTestResult.Text       = $"Fehler: {ex.Message}";
            AiTestResult.Foreground = Avalonia.Media.Brushes.OrangeRed;
        }
        finally
        {
            AiTestBtn.IsEnabled = true;
        }
    }

    private async void AaiasTestBtn_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AaiasTestResult.Text       = "Teste...";
        AaiasTestResult.Foreground = Avalonia.Media.Brushes.Gray;
        AaiasTestBtn.IsEnabled     = false;

        try
        {
            var cfg = AppConfig.Current ?? AppConfig.Load();
            var url = cfg.AaiasUrl?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(url))
            {
                AaiasTestResult.Text       = "Keine AAIAS-URL konfiguriert (Tester-Tab).";
                AaiasTestResult.Foreground = Avalonia.Media.Brushes.OrangeRed;
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp       = await http.GetAsync($"{url}/api/server/status", cts.Token);

            if (resp.IsSuccessStatusCode)
            {
                AaiasTestResult.Text       = "AAIAS erreichbar.";
                AaiasTestResult.Foreground = Avalonia.Media.Brushes.LightGreen;
            }
            else
            {
                AaiasTestResult.Text       = $"Fehler HTTP {(int)resp.StatusCode}";
                AaiasTestResult.Foreground = Avalonia.Media.Brushes.OrangeRed;
            }
        }
        catch (Exception ex)
        {
            AaiasTestResult.Text       = $"Nicht erreichbar: {ex.Message}";
            AaiasTestResult.Foreground = Avalonia.Media.Brushes.OrangeRed;
        }
        finally
        {
            AaiasTestBtn.IsEnabled = true;
        }
    }

    // ── Modell-Mapping ────────────────────────────────────────────────────────

    private static readonly string[] _claudeModels =
    [
        "claude-haiku-4-5-20251001",
        "claude-sonnet-4-6",
        "claude-opus-4-8"
    ];

    private static readonly string[] _openAiModels =
    [
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-4-turbo",
        "o1-mini"
    ];

    private static readonly string[] _geminiModels =
    [
        "gemini-2.0-flash",
        "gemini-2.5-pro",
        "gemini-1.5-flash",
        "gemini-1.5-pro"
    ];

    private static void SelectModel(ComboBox box, string[] models, string model)
    {
        var idx = Array.IndexOf(models, model);
        box.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string SelectedModel(ComboBox box, string[] models, string fallback)
    {
        var idx = box.SelectedIndex;
        return idx >= 0 && idx < models.Length ? models[idx] : fallback;
    }
}
