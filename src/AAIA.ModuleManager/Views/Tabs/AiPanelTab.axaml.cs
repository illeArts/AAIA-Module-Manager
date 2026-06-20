using Avalonia.Controls;
using Avalonia.Input;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views.Tabs;

public partial class AiPanelTab : UserControl
{
    public AiPanelTab()
    {
        InitializeComponent();
    }

    /// <summary>Strg+Enter sendet die Nachricht.</summary>
    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            DataContext is AiPanelViewModel vm)
        {
            if (vm.SendCommand.CanExecute(null))
                vm.SendCommand.Execute(null);

            e.Handled = true;
        }
    }

    /// <summary>Scrollt nach neuer Nachricht automatisch nach unten.</summary>
    public void ScrollToBottom() => ChatScroll?.ScrollToEnd();
}
