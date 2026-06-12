using Avalonia.Controls;
using Avalonia.Input;
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
}
