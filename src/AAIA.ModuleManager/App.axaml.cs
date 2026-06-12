using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.Views;

namespace AAIA.ModuleManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            // Synchronous load — avoids SynchronizationContext deadlock on UI thread startup
            var config = AppConfig.Load();
            AppConfig.Current = config;

            // App.axaml already includes Strings.de.axaml as the default.
            // Only swap if the user has chosen English.
            if (config.Language == "en")
            {
                try { LanguageService.SetLanguage(AppLanguage.En); }
                catch { /* silently ignore — German fallback stays active */ }
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
        }
        catch (Exception ex)
        {
            // Write crash info so we can diagnose startup failures
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AAIAModuleManager", "startup-crash.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"[{DateTime.Now}] Startup exception:\n{ex}");
            }
            catch { /* ignore write failure */ }

            throw; // still propagate so VS shows the exception
        }

        base.OnFrameworkInitializationCompleted();
    }
}
