using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AAIA.ModuleManager.Views;

public partial class LogWindow : Window
{
    public LogWindow() => InitializeComponent();

    public LogWindow(string title, string initialText)
    {
        InitializeComponent();
        Title = title;
        AppendText(initialText);
    }

    public void AppendText(string text)
    {
        if (LogBox is null) return;
        LogBox.Text = text;
        // Auto-scroll to end
        LogBox.CaretIndex = text.Length;
        if (StatusBar is not null)
            StatusBar.Text = $"{text.Split('\n').Length} Zeilen";
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        if (LogBox is not null) LogBox.Text = "";
        if (StatusBar is not null) StatusBar.Text = "Geleert";
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard is null || LogBox?.Text is null) return;
        await clipboard.SetTextAsync(LogBox.Text);
        if (StatusBar is not null) StatusBar.Text = "In Zwischenablage kopiert ✓";
    }
}
