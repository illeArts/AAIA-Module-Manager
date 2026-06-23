using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.ViewModels;

// ── Kategorie-Eintrag in der Sidebar ─────────────────────────────────────────

public sealed class HelpCategoryViewModel : ObservableObject
{
    public string                               Name     { get; }
    public ObservableCollection<HelpArticleItemViewModel> Articles { get; } = [];

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public HelpCategoryViewModel(string name) => Name = name;
}

// ── Artikel-Eintrag in der Sidebar ───────────────────────────────────────────

public sealed class HelpArticleItemViewModel : ObservableObject
{
    public HelpArticle Article { get; }
    public string      Title   => Article.Title;
    public string      Id      => Article.Id;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public HelpArticleItemViewModel(HelpArticle article) => Article = article;
}

// ── Suchergebnis-ViewModel ────────────────────────────────────────────────────

public sealed class HelpSearchResultViewModel : ObservableObject
{
    public HelpSearchResult Result  { get; }
    public string           Title   => Result.Article.Title;
    public string           Category => Result.Article.Category;
    public string           Excerpt => Result.Excerpt;
    public bool             IsTopResult => Result.TitleMatch || Result.ErrorMatch;

    public HelpSearchResultViewModel(HelpSearchResult result) => Result = result;
}

// ── Haupt-ViewModel ───────────────────────────────────────────────────────────

public sealed partial class HelpCenterViewModel : ObservableObject
{
    private readonly HelpCenterService  _center;
    private readonly HelpSearchService  _search;
    private readonly AiHelpContextService _aiContext;

    // ── Observable Properties ─────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading = true;
    [ObservableProperty] private string _loadingText = "Hilfezentrum wird geladen…";

    // Sidebar
    public ObservableCollection<HelpCategoryViewModel>   Categories { get; } = [];

    // Suche
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool   _isSearchActive;
    public ObservableCollection<HelpSearchResultViewModel> SearchResults { get; } = [];

    // Aktueller Artikel
    [ObservableProperty] private HelpArticle? _currentArticle;
    [ObservableProperty] private string       _currentMarkdown = "";
    [ObservableProperty] private string       _articleTitle    = "AAIA Hilfezentrum";
    [ObservableProperty] private string       _articleCategory = "";

    // Verwandte Artikel
    public ObservableCollection<HelpArticleItemViewModel> RelatedArticles { get; } = [];

    // KI-Hilfe
    [ObservableProperty] private string _aiContextText = "";
    [ObservableProperty] private bool   _aiContextReady;
    [ObservableProperty] private bool   _showAiPanel;

    // Status
    [ObservableProperty] private string _statusText = "";

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public HelpCenterViewModel()
    {
        var helpRoot = ResolveHelpRoot();
        _center    = new HelpCenterService(helpRoot);
        _search    = new HelpSearchService(_center);
        _aiContext = new AiHelpContextService(_center, _search);
    }

    /// <summary>
    /// Wenn gesetzt, wird nach LoadAsync() direkt dieser Artikel geöffnet.
    /// Kann Artikel-ID oder Fehlercode sein. Gesetzt von Fehlerkarten im UI.
    /// </summary>
    public string? PendingArticleId { get; set; }

    // ── Laden ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        IsLoading   = true;
        LoadingText = "Index wird geladen…";

        await _center.EnsureLoadedAsync();

        LoadingText = "Kategorien werden aufgebaut…";
        BuildCategories();

        IsLoading = false;

        if (PendingArticleId is not null)
        {
            // Direkt zum gewünschten Artikel navigieren
            var article = _center.GetById(PendingArticleId)
                       ?? _center.GetByErrorCode(PendingArticleId);
            if (article is not null)
                await OpenArticleAsync(article);
            else
                await OpenForErrorAsync(PendingArticleId);
        }
        else
        {
            var first = _center.AllArticles.FirstOrDefault();
            if (first is not null)
                await OpenArticleAsync(first);
        }
    }

    // ── Suche ─────────────────────────────────────────────────────────────────

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearchActive = false;
            SearchResults.Clear();
            return;
        }

        IsSearchActive = true;
        var results = _search.Search(value, maxResults: 15);

        SearchResults.Clear();
        foreach (var r in results)
            SearchResults.Add(new HelpSearchResultViewModel(r));
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery    = "";
        IsSearchActive = false;
        SearchResults.Clear();
    }

    [RelayCommand]
    private async Task OpenSearchResultAsync(HelpSearchResultViewModel? result)
    {
        if (result is null) return;
        await OpenArticleAsync(result.Result.Article);
        ClearSearch();
    }

    // ── Artikel öffnen ────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task OpenArticleAsync(HelpArticle? article)
    {
        if (article is null) return;

        CurrentArticle  = article;
        ArticleTitle    = article.Title;
        ArticleCategory = article.Category;
        AiContextReady  = false;
        AiContextText   = "";

        // Sidebar-Selektion aktualisieren
        foreach (var cat in Categories)
            foreach (var item in cat.Articles)
                item.IsSelected = item.Id == article.Id;

        // Markdown laden
        CurrentMarkdown = await _center.LoadMarkdownAsync(article);

        // Verwandte Artikel laden
        RelatedArticles.Clear();
        foreach (var related in _center.GetRelated(article))
            RelatedArticles.Add(new HelpArticleItemViewModel(related));
    }

    [RelayCommand]
    private async Task OpenArticleByIdAsync(string id)
    {
        var article = _center.GetById(id);
        if (article is not null)
            await OpenArticleAsync(article);
    }

    // ── KI-Hilfe ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateAiContextAsync()
    {
        if (CurrentArticle is null) return;

        ShowAiPanel    = true;
        AiContextReady = false;
        AiContextText  = "KI-Kontext wird erzeugt…";

        var ctx = await _aiContext.BuildForArticleAsync(CurrentArticle.Id);
        AiContextText  = ctx;
        AiContextReady = true;
    }

    [RelayCommand]
    private void CopyAiContext()
    {
        if (!AiContextReady) return;
        CopyToClipboard(AiContextText);
        StatusText = "KI-Kontext in Zwischenablage kopiert.";
    }

    [RelayCommand]
    private void CopyMarkdown()
    {
        if (string.IsNullOrEmpty(CurrentMarkdown)) return;
        CopyToClipboard(CurrentMarkdown);
        StatusText = "Artikel in Zwischenablage kopiert.";
    }

    [RelayCommand]
    private void ToggleAiPanel() => ShowAiPanel = !ShowAiPanel;

    // ── Fehler-Artikel öffnen (von Fehlerkarten aus) ──────────────────────────

    /// <summary>
    /// Öffnet das HelpCenter mit dem passenden Artikel für einen Fehlercode.
    /// Wird von Fehlerkarten im UI aufgerufen.
    /// </summary>
    public async Task OpenForErrorAsync(string errorCodeOrArticleId, string? errorMessage = null)
    {
        var article = _center.GetByErrorCode(errorCodeOrArticleId);
        if (article is not null)
        {
            await OpenArticleAsync(article);
        }
        else
        {
            ArticleTitle    = $"Hilfe: {errorCodeOrArticleId}";
            ArticleCategory = "Fehler & Lösungen";
            CurrentMarkdown = $"# Hilfe nicht gefunden\n\n" +
                              $"Für den Fehlercode `{errorCodeOrArticleId}` wurde kein Hilfeartikel gefunden.\n\n" +
                              $"Nutze die **Suche** oben, um einen passenden Artikel zu finden.";
        }

        if (!string.IsNullOrEmpty(errorMessage))
        {
            var aiCtx = await _aiContext.BuildForErrorAsync(errorCodeOrArticleId, errorMessage);
            AiContextText  = aiCtx;
            AiContextReady = true;
            ShowAiPanel    = true;
        }
    }

    // ── Sidebar aufbauen ──────────────────────────────────────────────────────

    private void BuildCategories()
    {
        Categories.Clear();

        var grouped = _center.AllArticles
            .GroupBy(a => a.Category)
            .OrderBy(g => CategoryOrder(g.Key));

        foreach (var group in grouped)
        {
            var cat = new HelpCategoryViewModel(group.Key);
            foreach (var article in group.OrderBy(a => a.Title))
                cat.Articles.Add(new HelpArticleItemViewModel(article));
            Categories.Add(cat);
        }
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Erste Schritte"       => 0,
        "Projekt erstellen"    => 1,
        "Validierung"          => 2,
        "Build & Fehler"       => 3,
        "Paketierung"          => 4,
        "Release"              => 5,
        "Signatur & Vertrauen" => 6,
        "Marketplace"          => 7,
        "Sicherheit"           => 8,
        "AAIAS"                => 9,
        "Fehler & Lösungen"    => 10,
        "FAQ"                  => 11,
        _                      => 99,
    };

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static void CopyToClipboard(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow?.Clipboard
                    : null;
                clipboard?.SetTextAsync(text);
            }
            catch { /* Clipboard-Fehler ignorieren */ }
        });
    }

    private static string? ResolveHelpRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "docs", "help");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            dir = System.IO.Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }
}
