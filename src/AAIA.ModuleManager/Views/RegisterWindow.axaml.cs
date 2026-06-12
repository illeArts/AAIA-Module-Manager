using Avalonia.Controls;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Views;

public partial class RegisterWindow : Window
{
    private readonly RegistryService _svc;

    public RegisterWindow() { _svc = new RegistryService(AppConfig.Current?.RegistryPath ?? ""); InitializeComponent(); }

    public RegisterWindow(RegistryService svc)
    {
        _svc = svc;
        InitializeComponent();
    }

    private void Cancel_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = new RegistryEntry(
            Name:        NameBox.Text     ?? "",
            Type:        (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "module",
            Version:     VersionBox.Text  ?? "1.0.0",
            Contracts:   ContractsBox.Text ?? "",
            Author:      AuthorBox.Text   ?? "",
            Description: DescBox.Text     ?? "",
            Repository:  RepoBox.Text     ?? ""
        );
        await _svc.AddOrUpdateAsync(entry);
        Close();
    }
}
