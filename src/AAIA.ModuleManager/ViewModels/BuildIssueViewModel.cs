using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel-Wrapper um BuildAction mit ausführbarem Befehl.
/// </summary>
public sealed class BuildActionViewModel
{
    public string  Label          { get; }
    public IRelayCommand ExecuteCommand { get; }

    public BuildActionViewModel(string label, Action execute)
    {
        Label          = label;
        ExecuteCommand = new RelayCommand(execute);
    }
}

/// <summary>
/// ViewModel-Wrapper um BuildIssue für die XAML-DataTemplate-Bindung.
/// Enthält fertige IBrush-Werte, ausführbare Aktions-Commands und Hilfe-Link.
/// </summary>
public sealed class BuildIssueViewModel
{
    // ── Daten ──────────────────────────────────────────────────────────────────

    public string  Code             { get; }
    public string  Title            { get; }
    public string  HumanMessage     { get; }
    public string  TechnicalDetails { get; }
    public bool    IsError          { get; }
    public bool    IsWarning        { get; }
    public string  SeverityIcon     { get; }

    // ── Dateiinfo ──────────────────────────────────────────────────────────────

    public bool   HasLocation    { get; }
    public string LocationLabel  { get; }

    // ── Aktionen ───────────────────────────────────────────────────────────────

    public bool                        HasActions       { get; }
    public List<BuildActionViewModel>  SuggestedActions { get; }

    // ── Hilfe ──────────────────────────────────────────────────────────────────

    public string?       HelpArticleId  { get; }
    public bool          HasHelp        { get; }
    public IRelayCommand OpenHelpCommand { get; }

    // ── Farben (Avalonia IBrush) ───────────────────────────────────────────────

    public IBrush SeverityForeground { get; }
    public IBrush BorderBrushColor   { get; }
    public IBrush CodeForeground     { get; }
    public IBrush CodeBackground     { get; }

    // ── Konstruktor ────────────────────────────────────────────────────────────

    public BuildIssueViewModel(
        BuildIssue issue,
        Action<string> executeAction,
        Action<string>? openHelp = null)
    {
        Code             = issue.Code;
        Title            = issue.Title;
        HumanMessage     = issue.HumanMessage;
        TechnicalDetails = issue.TechnicalDetails;
        IsError          = issue.IsError;
        IsWarning        = issue.IsWarning;
        SeverityIcon     = issue.SeverityIcon;

        HasLocation   = !string.IsNullOrWhiteSpace(issue.FilePath);
        LocationLabel = HasLocation
            ? (issue.Line.HasValue
                ? $"Datei: {issue.FilePath} (Zeile {issue.Line})"
                : $"Datei: {issue.FilePath}")
            : "";

        HasActions       = issue.SuggestedActions.Count > 0;
        SuggestedActions = issue.SuggestedActions
            .Select(a => new BuildActionViewModel(a.Label, () => executeAction(a.ActionId)))
            .ToList();

        // Hilfe-Artikel: aus Issue-Modell oder per Code-Mapping
        HelpArticleId = issue.HelpArticleId
            ?? ErrorHelpMappingService.GetArticleIdForBuildCode(issue.Code);
        HasHelp = HelpArticleId is not null && openHelp is not null;
        OpenHelpCommand = new RelayCommand(
            () => openHelp?.Invoke(HelpArticleId!),
            () => HasHelp);

        // Farben
        if (IsError)
        {
            SeverityForeground = new SolidColorBrush(Color.Parse("#e05252"));
            BorderBrushColor   = new SolidColorBrush(Color.Parse("#4a1a1a"));
            CodeForeground     = new SolidColorBrush(Color.Parse("#9a5050"));
            CodeBackground     = new SolidColorBrush(Color.Parse("#2a1010"));
        }
        else
        {
            SeverityForeground = new SolidColorBrush(Color.Parse("#c9a227"));
            BorderBrushColor   = new SolidColorBrush(Color.Parse("#3a3010"));
            CodeForeground     = new SolidColorBrush(Color.Parse("#9a8030"));
            CodeBackground     = new SolidColorBrush(Color.Parse("#2a2010"));
        }
    }

    /// <summary>Erstellt ViewModels aus einer BuildResult-Issue-Liste.</summary>
    public static List<BuildIssueViewModel> From(
        BuildResult result,
        Action<string> executeAction,
        Action<string>? openHelp = null)
        => result.Issues
               .Select(i => new BuildIssueViewModel(i, executeAction, openHelp))
               .ToList();
}
