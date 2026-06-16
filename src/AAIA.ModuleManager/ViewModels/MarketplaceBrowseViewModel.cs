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
    public int     ProductId    { get; init; }
    public string  Code         { get; init; } = "";
    public string  Name         { get; init; } = "";
    public string  TypeLabel    { get; init; } = "";
    public string  TypeColor    { get; init; } = "#5865f2";
    public string  Version      { get; init; } = "";
    public string  PriceDisplay { get; init; } = "";
    public string  Url          { get; init; } = "";

    public static ModuleFeedItemVm FromDto(ModuleFeedItem dto) => new()
    {
        ProductId    = dto.ProductId,
        Code         = dto.Code,
        Name         = dto.Name,
        Version      = dto.Version,
        Url          = dto.Url,
        TypeLabel    = dto.Type switch
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
    private void OpenInBrowser(ModuleFeedItemVm? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Url)) return;
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
