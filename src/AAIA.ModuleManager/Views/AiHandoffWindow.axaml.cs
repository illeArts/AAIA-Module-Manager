using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Views;

public partial class AiHandoffWindow : Window
{
    private readonly AiHandoffContext _ctx;
    private          AiHandoffResult? _lastResult;

    public AiHandoffWindow(AiHandoffContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
    }

    // ── Auto-generate on open ─────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GeneratePrompt();
    }

    // ── Controls ──────────────────────────────────────────────────────────────

    private void Combo_Changed(object? sender, SelectionChangedEventArgs e)
        => GeneratePrompt();

    private void Generate_Click(object? sender, RoutedEventArgs e)
        => GeneratePrompt();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PromptOutput.Text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(PromptOutput.Text);
        ShowStatus("✅  Prompt wurde in die Zwischenablage kopiert.");
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PromptOutput.Text)) return;

        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var safeName = _lastResult?.Title
            .Replace(" — ", "-")
            .Replace(" ", "-")
            .Replace("/", "-")
            ?? "ai-handoff";

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "AI Handoff exportieren",
            SuggestedFileName = $"{safeName}.md",
            DefaultExtension  = "md",
            FileTypeChoices   =
            [
                new FilePickerFileType("Markdown") { Patterns = ["*.md"] }
            ]
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            await File.WriteAllTextAsync(path, PromptOutput.Text);
            ShowStatus($"✅  Exportiert nach: {Path.GetFileName(path)}");
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    // ── Generator ────────────────────────────────────────────────────────────

    private void GeneratePrompt()
    {
        var req = BuildRequest();
        var result = AiHandoffGeneratorService.Generate(_ctx, req);
        _lastResult = result;

        if (result.Success)
        {
            PromptOutput.Text = result.Prompt;
            CharCountLabel.Text = $"{result.CharCount:N0} Zeichen";
            HideStatus();
        }
        else
        {
            PromptOutput.Text = $"Fehler beim Erzeugen: {result.Error}";
            CharCountLabel.Text = "";
        }
    }

    private AiHandoffRequest BuildRequest()
    {
        var target = GetTag<AiHandoffTarget>(TargetCombo, AiHandoffTarget.ImplementNext);
        var profile = GetTag<AiHandoffProfile>(ProfileCombo, AiHandoffProfile.Claude);
        var level = GetTag<AiHandoffContextLevel>(LevelCombo, AiHandoffContextLevel.Standard);

        return new AiHandoffRequest
        {
            Target       = target,
            Profile      = profile,
            ContextLevel = level
        };
    }

    private static T GetTag<T>(ComboBox combo, T fallback) where T : struct, Enum
    {
        var tag = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag is not null && Enum.TryParse<T>(tag, out var val))
            return val;
        return fallback;
    }

    // ── Status-Leiste ─────────────────────────────────────────────────────────

    private void ShowStatus(string text)
    {
        StatusLabel.Text = text;
        StatusBar.IsVisible = true;
    }

    private void HideStatus()
    {
        StatusBar.IsVisible = false;
    }
}
