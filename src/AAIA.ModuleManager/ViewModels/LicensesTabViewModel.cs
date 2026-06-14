using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public partial class LicensesTabViewModel : ObservableObject
{
    private readonly AppConfig              _cfg;
    private readonly AaiasConnectionService _aaiasConn;

    [ObservableProperty] private ObservableCollection<ActivatedLicense> _licenses      = [];
    [ObservableProperty] private ActivatedLicense?                       _selected;
    [ObservableProperty] private string                                  _statusMessage = "";

    /// <summary>True wenn mindestens eine Lizenz vorhanden ist — steuert die Listenansicht.</summary>
    public bool HasLicenses => Licenses.Count > 0;

    public LicensesTabViewModel(AppConfig cfg, AaiasConnectionService aaiasConn)
    {
        _cfg       = cfg;
        _aaiasConn = aaiasConn;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    private async Task LoadAsync()
    {
        var list = await LicenseStore.LoadAsync();
        Licenses      = new ObservableCollection<ActivatedLicense>(list);
        OnPropertyChanged(nameof(HasLicenses));
        StatusMessage = Licenses.Count == 0 ? "Noch keine aktivierten Lizenzen." : "";
    }

    // ── Aktivieren ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenActivateDialog()
    {
        var vm  = new LicenseActivateViewModel(_cfg);
        await vm.TryPrefillDeviceIdAsync(_aaiasConn);

        var dlg = new Views.LicenseActivateWindow(vm);

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lf)
        {
            await dlg.ShowDialog(lf.MainWindow!);
        }

        if (vm.Result is not null)
            await AddOrUpdateAsync(vm.Result);
    }

    // ── Entfernen ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task RemoveLicense()
    {
        if (Selected is null) return;
        Licenses.Remove(Selected);
        Selected = null;
        OnPropertyChanged(nameof(HasLicenses));
        await LicenseStore.SaveAsync([.. Licenses]);
        if (Licenses.Count == 0)
            StatusMessage = "Noch keine aktivierten Lizenzen.";
    }

    private bool HasSelected() => Selected is not null;

    partial void OnSelectedChanged(ActivatedLicense? value)
        => RemoveLicenseCommand.NotifyCanExecuteChanged();

    // ── Interne Helfer ─────────────────────────────────────────────────────────

    private async Task AddOrUpdateAsync(ActivatedLicense entry)
    {
        // Re-Aktivierung: bestehenden Eintrag ersetzen
        var existing = Licenses.FirstOrDefault(l => l.LicenseKey == entry.LicenseKey);
        if (existing is not null)
            Licenses.Remove(existing);

        Licenses.Insert(0, entry);
        OnPropertyChanged(nameof(HasLicenses));
        StatusMessage = "";

        await LicenseStore.SaveAsync([.. Licenses]);
    }
}
