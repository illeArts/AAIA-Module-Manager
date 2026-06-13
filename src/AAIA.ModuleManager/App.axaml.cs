using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.ViewModels;
using AAIA.ModuleManager.Views;

namespace AAIA.ModuleManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var config = AppConfig.Load();
            AppConfig.Current = config;

            if (config.Language == "en")
            {
                try { LanguageService.SetLanguage(AppLanguage.En); }
                catch { /* German fallback stays active */ }
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Kein gespeicherter Token → LoginWindow zuerst anzeigen
                bool needsLogin = string.IsNullOrEmpty(config.MarketplaceToken)
                               || string.IsNullOrEmpty(config.DeveloperEtwId);

                if (needsLogin)
                {
                    var api      = new MarketplaceApiClient(config.MarketplaceApiUrl);
                    var loginVm  = new LoginWindowViewModel(config, api);
                    var loginWnd = new LoginWindow(loginVm);

                    loginVm.LoginSucceeded += (_, args) =>
                    {
                        var mainWnd = new MainWindow();
                        // ETW-Identität + Role direkt in die Titelleiste übergeben
                        if (mainWnd.DataContext is MainWindowViewModel mainVm)
                            mainVm.SetDeveloperIdentity(args.EtwId, args.DisplayName, args.Role);

                        desktop.MainWindow = mainWnd;
                        mainWnd.Show();
                        loginWnd.Close();
                    };

                    desktop.MainWindow = loginWnd;
                }
                else
                {
                    // Token vorhanden → direkt ins MainWindow, ETW-Info aus config
                    desktop.MainWindow = new MainWindow();
                }
            }
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AAIAModuleManager", "startup-crash.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"[{DateTime.Now}] Startup exception:\n{ex}");
            }
            catch { /* ignore write failure */ }
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
