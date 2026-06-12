using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class TesterTab : UserControl
{
    public TesterTab()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TesterTabViewModel vm)
            vm.BrowseRequested += OnBrowseRequested;
    }

    private async void OnBrowseRequested(object? sender, EventArgs e)
    {
        var toplevel = TopLevel.GetTopLevel(this);
        if (toplevel is null) return;

        var folders = await toplevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Projektordner wählen", AllowMultiple = false });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (path is null) return;

        if (DataContext is TesterTabViewModel vm)
            vm.LoadProject(path);
    }
}
