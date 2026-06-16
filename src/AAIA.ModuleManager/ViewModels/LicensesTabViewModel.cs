using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
    [ObservableProperty] private bool                                    _isDownloading;

    /// <summary>True wenn mindestens eine Lizenz vorhanden ist — steuert die Listenansicht.</summary>
    public bool HasLicenses => Licenses.Count > 0;

    /// <summary>True wenn ausgewählte Lizenz einen Download hat (LanguagePack mit URL oder beliebige Lizenz zum Browser-Öffnen).</summary>
    public bool CanDownloadSelected => Selected is not null;

    /// <summary>True wenn ausgewählte Lizenz eine direkte Download-URL hat (LanguagePack).</summary>
    private bool HasDirectDownload => Selected?.DownloadUrl is not null && !string.IsNullOrWhiteSpace(Selected.DownloadUrl);

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

    // ── Download ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDownloadSelected))]
    private async Task DownloadLicense(CancellationToken ct)
    {
        if (Selected is null) return;

        if (HasDirectDownload)
        {
            // LanguagePack: direkte HTTP-URL herunterladen und in Downloads speichern
            IsDownloading = true;
            StatusMessage = "Download läuft...";
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AAIA-ModuleManager/1.0");
                var bytes = await http.GetByteArrayAsync(Selected.DownloadUrl!, ct);

                var fileName = Path.GetFileName(new Uri(Selected.DownloadUrl!).LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = $"aaia-{Selected.ModuleId}-{Selected.Locale ?? "lang"}.zip";

                var downloadsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloadsDir);
                var savePath = Path.Combine(downloadsDir, fileName);

                await File.WriteAllBytesAsync(savePath, bytes, ct);
                StatusMessage = $"✅ Gespeichert: {savePath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Download fehlgeschlagen: {ex.Message}";
            }
            finally { IsDownloading = false; }
        }
        else
        {
            // Module/Plugin: Produktseite im Browser öffnen
            var productUrl = $"{_cfg.MarketplaceApiUrl.Split("?")[0].Replace("/index.php", "")}/mein-konto/aaia-licenses/";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = productUrl, UseShellExecute = true });
                StatusMessage = "Browser geöffnet — lade dort dein Modul herunter.";
            }
            catch
            {
                StatusMessage = $"Bitte manuell öffnen: {productUrl}";
            }
        }
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
    {
        RemoveLicenseCommand.NotifyCanExecuteChanged();
        DownloadLicenseCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDownloadSelected));
        OnPropertyChanged(nameof(HasDirectDownload));
    }

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
