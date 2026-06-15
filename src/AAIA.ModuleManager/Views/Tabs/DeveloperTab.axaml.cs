using Avalonia.Controls;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views.Tabs;

public partial class DeveloperTab : UserControl
{
    public DeveloperTab()
    {
        InitializeComponent();

        // StorageProvider nach Attachment verfügbar machen (für ZIP-Dateidialog)
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is DeveloperTabViewModel vm)
                vm.StorageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        };
    }
}
