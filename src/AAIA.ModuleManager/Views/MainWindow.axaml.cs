using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.ViewModels;
using AAIA.ModuleManager.Views;

namespace AAIA.ModuleManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void Settings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new SettingsWindow();
        dlg.ShowDialog(this);
    }

    private void Help_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new HelpWindow();
        dlg.ShowDialog(this);
    }

    private void About_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new AboutWindow();
        dlg.ShowDialog(this);
    }

    private async void Logout_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Config leeren
        var config = AppConfig.Current ?? AppConfig.Load();
        config.MarketplaceToken      = "";
        config.DeveloperEtwId        = null;
        config.DeveloperDisplayName  = null;
        config.DeveloperRole         = AAIA.Shared.Contracts.Publisher.DeveloperRole.Community;
        await config.SaveAsync();

        // LoginWindow öffnen, MainWindow schließen
        var api      = new MarketplaceApiClient(config.MarketplaceApiUrl);
        var loginVm  = new LoginWindowViewModel(config, api);
        var loginWnd = new LoginWindow(loginVm);

        loginVm.LoginSucceeded += (_, args) =>
        {
            var mainWnd = new MainWindow();
            if (mainWnd.DataContext is MainWindowViewModel mainVm)
                mainVm.SetDeveloperIdentity(args.EtwId, args.DisplayName, args.Role);

            if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = mainWnd;

            mainWnd.Show();
            loginWnd.Close();
        };

        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime dt)
            dt.MainWindow = loginWnd;

        loginWnd.Show();
        Close();
    }
}
