using Avalonia.Controls;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class LicenseActivateWindow : Window
{
    public LicenseActivateWindow()
    {
        InitializeComponent();
    }

    public LicenseActivateWindow(LicenseActivateViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Fenster automatisch schließen sobald Aktivierung erfolgreich
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicenseActivateViewModel.IsSuccess) && vm.IsSuccess)
            {
                // Kurz warten damit der Nutzer den Erfolgs-Status sieht, dann schließen
                _ = System.Threading.Tasks.Task.Delay(1200).ContinueWith(_ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(Close));
            }
        };
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
