using Avalonia.Controls;
using Avalonia.Interactivity;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views.Tabs;

public partial class PublishTab : UserControl
{
    public PublishTab()
    {
        InitializeComponent();
    }

    // ── Folder / File Dialogs ─────────────────────────────────────────────────
    // Dialogs müssen vom Code-Behind geöffnet werden (Avalonia StorageProvider).

    private async void OnBrowseProjectClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as PublishTabViewModel;
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title            = "Modul-Projektverzeichnis wählen",
                AllowMultiple    = false,
            });

        if (folders is { Count: > 0 })
            vm.SetProjectPath(folders[0].Path.LocalPath);
    }

    private async void OnBrowsePrivateKeyClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as PublishTabViewModel;
        if (vm is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title         = "Privaten Schlüssel wählen",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("PEM-Schlüssel")
                    {
                        Patterns = ["*.pem", "*.key"]
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("Alle Dateien")
                    {
                        Patterns = ["*"]
                    }
                ]
            });

        if (files is { Count: > 0 })
            vm.SetPrivateKeyPath(files[0].Path.LocalPath);
    }
}
