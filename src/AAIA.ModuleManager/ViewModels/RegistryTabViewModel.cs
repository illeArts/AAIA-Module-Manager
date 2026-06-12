using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class RegistryTabViewModel : ObservableObject
{
    private readonly RegistryService _svc;

    [ObservableProperty] private ObservableCollection<RegistryEntry> _entries = [];
    [ObservableProperty] private RegistryEntry? _selectedEntry;
    [ObservableProperty] private string _searchText = "";

    public RegistryTabViewModel(AppConfig cfg)
    {
        _svc = new RegistryService(cfg.RegistryPath);
        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    private async Task LoadAsync()
    {
        var list = await _svc.SearchAsync(SearchText);
        Entries  = new ObservableCollection<RegistryEntry>(list);
    }

    [RelayCommand]
    private void RegisterNew()
    {
        // öffnet ein kleines Dialog-Fenster (wird in RegisterWindow implementiert)
        var dlg = new Views.RegisterWindow(_svc);
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lf)
        {
            dlg.ShowDialog(lf.MainWindow!);
            dlg.Closed += async (_, _) => await LoadAsync();
        }
    }
}
