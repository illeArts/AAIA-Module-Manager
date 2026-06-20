using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

// ── Item-ViewModel ────────────────────────────────────────────────────────────

public sealed class ModuleFeedItemVm
{
    public int     ProductId     { get; init; }
    public string  Code          { get; init; } = "";
    public string  Name          { get; init; } = "";
    public string  TypeLabel     { get; init; } = "";
    public string  TypeColor     { get; init; } = "#5865f2";
    public string  Version       { get; init; } = "";
    public string  PriceDisplay  { get; init; } = "";
    public string  Url           { get; init; } = "";

    /// <summary>
    /// Approval Token vom Marketplace-Feed — opakes HMAC-Token, kein Sicherheitsdetail.
    /// Null wenn das Modul noch nicht vom Admin freigegeben wurde.
    /// </summary>
    public string? ApprovalToken { get; init; }

    /// <summary>
    /// Wurde das Modul vom AAIA-Inspector geprüft und vom Admin freigegeben?
    /// true  = Parkausweis vorhanden → Installation empfohlen
    /// false = kein Token (Legacy oder noch in Prüfung) → Warnung anzeigen
    /// </summary>
    public bool    IsApproved    => !string.IsNullOrWhiteSpace(ApprovalToken);

    /// <summary>Badge-Text für die UI.</summary>
    public string  ApprovalBadge => IsApproved ? "✓ AAIA Geprüft" : "⚠ Nicht geprüft";

    /// <summary>Badge-Farbe.</summary>
    public string  ApprovalColor => IsApproved ? "#06d6a0" : "#f4a261";

    public static ModuleFeedItemVm FromDto(ModuleFeedItem dto) => new()
    {
        ProductId     = dto.ProductId,
        Code          = dto.Code,
        Name          = dto.Name,
        Version       = dto.Version,
        Url           = dto.Url,
        ApprovalToken = dto.ApprovalToken,
        TypeLabel     = dto.Type switch
        {
            "plugin"       => "🔌 Plugin",
            "module"       => "📦 Modul",
            "languagepack" => "🌐 Sprachpaket",
            _              => dto.Type
        },
        TypeColor    = dto.Type switch
        {
            "plugin"       => "#5865f2",
            "module"       => "#06d6a0",
            "languagepack" => "#f4a261",
            _              => "#8892a4"
        },
        PriceDisplay = dto.Price == 0
            ? "Kostenlos"
            : $"{dto.Price:F2} {dto.Currency}",
    };
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public sealed partial class MarketplaceBrowseViewModel : ObservableObject
{
    private readonly WpMarketplaceClient _wpApi;

    private List<ModuleFeedItemVm> _allModules = [];

    [ObservableProperty] private ObservableCollection<ModuleFeedItemVm> _modules = [];
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _searchText    = "";
    [ObservableProperty] private int    _totalCount;

    public MarketplaceBrowseViewModel(WpMarketplaceClient wpApi)
    {
        _wpApi = wpApi;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading     = true;
        StatusMessage = "";
        try
        {
            var feed = await _wpApi.GetModulesFeedAsync(ct);
            _allModules = feed.Items.Select(ModuleFeedItemVm.FromDto).ToList();
            TotalCount  = _allModules.Count;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Laden: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync(ModuleFeedItemVm? item, CancellationToken ct = default)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Url)) return;

        // Parkausweis prüfen: Token aus Feed serverseitig verifizieren
        if (item.IsApproved)
        {
            var verified = await _wpApi.VerifyApprovalAsync(item.ProductId, item.ApprovalToken!, ct);
            if (!verified)
            {
                StatusMessage = $"⚠ Warnung: Parkausweis für '{item.Name}' konnte nicht verifiziert werden. Bitte nicht installieren.";
                return;
            }
        }
        else
        {
            // Kein Token: Warnung anzeigen, aber dennoch Öffnen erlauben (Legacy-Module)
            StatusMessage = $"ℹ '{item.Name}' hat noch keinen AAIA-Prüfstempel (möglicherweise noch in Prüfung).";
        }

        try { Process.Start(new ProcessStartInfo { FileName = item.Url, UseShellExecute = true }); }
        catch { }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allModules
            : _allModules.Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Modules.Clear();
        foreach (var m in filtered)
            Modules.Add(m);

        StatusMessage = Modules.Count == 0 && TotalCount > 0
            ? "Keine Module gefunden."
            : Modules.Count == 0
                ? "Noch keine Module im Marketplace."
                : "";
    }
}
