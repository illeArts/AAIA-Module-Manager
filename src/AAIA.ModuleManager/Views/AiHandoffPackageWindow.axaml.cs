using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using AAIA.ModuleManager.Services.AiAdapter;
using AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;
using AAIA.ModuleManager.Services.Help;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class AiHandoffPackageWindow : Window
{
    private AiHandoffPackageViewModel? _vm;

    public AiHandoffPackageWindow()
    {
        InitializeComponent();
    }

    public AiHandoffPackageWindow(AiAdapterRequest request, AiHandoffContext context)
    {
        _vm = new AiHandoffPackageViewModel(request, context);
        DataContext = _vm;
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_vm is not null)
            await _vm.InitAsync();
    }

    // ── Paket-Typ ─────────────────────────────────────────────────────────────

    private void PackageType_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is not ComboBox combo) return;
        var tag = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag is not null && Enum.TryParse<AiHandoffPackageType>(tag, out var t))
            _vm.PackageType = t;
    }

    // ── Rebuild ───────────────────────────────────────────────────────────────

    private async void Rebuild_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            await _vm.InitAsync();
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private async void ExportFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            await _vm.ExportFolderCommand.ExecuteAsync(null);
    }

    private async void ExportZip_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            await _vm.ExportZipCommand.ExecuteAsync(null);
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        => _vm?.OpenFolderCommand.Execute(null);

    // ── Preview kopieren ──────────────────────────────────────────────────────

    private async void CopyPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var text = _vm.PreviewContent;
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }

    // ── Schließen ─────────────────────────────────────────────────────────────

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
