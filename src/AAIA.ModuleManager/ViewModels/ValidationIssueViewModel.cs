using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel-Wrapper um ValidationAction mit ausführbarem Command.
/// </summary>
public sealed class ValidationActionViewModel
{
    public string        Label          { get; }
    public IRelayCommand ExecuteCommand { get; }

    public ValidationActionViewModel(string label, Action execute)
    {
        Label          = label;
        ExecuteCommand = new RelayCommand(execute);
    }
}

/// <summary>
/// ViewModel-Wrapper um ValidationIssue — mit IBrush-Farben und Commands.
/// </summary>
public sealed class ValidationIssueViewModel
{
    public string  Title        { get; }
    public string  Message      { get; }
    public string  Category     { get; }
    public string  Severity     { get; }  // "Error" | "Warning" | "Info"
    public string  SeverityIcon { get; }
    public bool    HasActions   { get; }
    public List<ValidationActionViewModel> Actions { get; }

    // Avalonia-Brushes
    public IBrush SeverityForeground { get; }
    public IBrush BorderBrushColor   { get; }
    public IBrush BadgeBackground    { get; }
    public string CategoryLabel      { get; }

    public ValidationIssueViewModel(ValidationIssue issue, Action<string> executeAction)
    {
        Title        = issue.Title;
        Message      = issue.Message;
        Category     = issue.Category;
        Severity     = issue.Severity;
        SeverityIcon = issue.SeverityIcon;
        HasActions   = issue.Actions.Count > 0;
        CategoryLabel = issue.Category;

        Actions = issue.Actions
            .Select(a => new ValidationActionViewModel(a.Label, () => executeAction(a.ActionId)))
            .ToList();

        (SeverityForeground, BorderBrushColor, BadgeBackground) = issue.Severity switch
        {
            "Error"   => (
                (IBrush)new SolidColorBrush(Color.Parse("#e05252")),
                (IBrush)new SolidColorBrush(Color.Parse("#4a1a1a")),
                (IBrush)new SolidColorBrush(Color.Parse("#2a1010"))
            ),
            "Warning" => (
                (IBrush)new SolidColorBrush(Color.Parse("#c9a227")),
                (IBrush)new SolidColorBrush(Color.Parse("#3a3010")),
                (IBrush)new SolidColorBrush(Color.Parse("#2a2010"))
            ),
            _ => (
                (IBrush)new SolidColorBrush(Color.Parse("#5865f2")),
                (IBrush)new SolidColorBrush(Color.Parse("#1a1a4a")),
                (IBrush)new SolidColorBrush(Color.Parse("#10102a"))
            )
        };
    }

    public static List<ValidationIssueViewModel> From(
        ValidationResult result, Action<string> executeAction)
        => result.Issues
               .OrderBy(i => i.Severity switch { "Error" => 0, "Warning" => 1, _ => 2 })
               .Select(i => new ValidationIssueViewModel(i, executeAction))
               .ToList();
}
