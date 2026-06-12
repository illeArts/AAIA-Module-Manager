using Avalonia.Controls;
using Avalonia.Interactivity;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views.Tabs;

public partial class SetupTab : UserControl
{
    public SetupTab() => InitializeComponent();

    private void PopOutLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupTabViewModel vm) return;

        var win = new LogWindow("Setup-Ausgabe", vm.Log);
        win.Show();

        // Keep log window in sync as new lines arrive
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Log))
                win.AppendText(vm.Log);
        };
    }
}
