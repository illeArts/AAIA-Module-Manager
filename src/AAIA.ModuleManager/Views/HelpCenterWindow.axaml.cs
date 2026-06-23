using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class HelpCenterWindow : Window
{
    private HelpCenterViewModel? _vm;
    private ItemsControl?        _markdownPanel;

    // Markdig-Pipeline (einmalig erstellen)
    private static readonly MarkdownPipeline MdPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public HelpCenterWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as HelpCenterViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _markdownPanel = this.FindControl<ItemsControl>("MarkdownPanel");

        if (_vm is not null)
            await _vm.LoadAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpCenterViewModel.CurrentMarkdown))
        {
            Dispatcher.UIThread.Post(RenderMarkdown);
        }
    }

    private void RenderMarkdown()
    {
        if (_markdownPanel is null || _vm is null) return;

        var markdown = _vm.CurrentMarkdown;
        var items    = new List<Control>();

        if (string.IsNullOrEmpty(markdown))
        {
            _markdownPanel.ItemsSource = items;
            return;
        }

        try
        {
            var doc = Markdown.Parse(markdown, MdPipeline);
            foreach (var block in doc)
                items.Add(RenderBlock(block));
        }
        catch
        {
            // Fallback: Plain-Text anzeigen
            items.Add(new SelectableTextBlock
            {
                Text        = markdown,
                TextWrapping = TextWrapping.Wrap,
                Foreground  = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xdd)),
                FontSize    = 13,
            });
        }

        _markdownPanel.ItemsSource = items;
    }

    // ── Block-Renderer ────────────────────────────────────────────────────────

    private static Control RenderBlock(Block block) => block switch
    {
        HeadingBlock h      => RenderHeading(h),
        ParagraphBlock p    => RenderParagraph(p),
        FencedCodeBlock c   => RenderCodeBlock(c),
        CodeBlock c         => RenderCodeBlock(c),
        ListBlock l         => RenderList(l),
        ThematicBreakBlock  => RenderDivider(),
        QuoteBlock q        => RenderQuote(q),
        _                   => RenderFallbackBlock(block),
    };

    private static Control RenderHeading(HeadingBlock h)
    {
        var (size, color, weight) = h.Level switch
        {
            1 => (22.0, Color.FromRgb(0xe8, 0xe8, 0xff), FontWeight.Bold),
            2 => (17.0, Color.FromRgb(0xb0, 0xb0, 0xee), FontWeight.SemiBold),
            3 => (14.0, Color.FromRgb(0x90, 0x90, 0xcc), FontWeight.SemiBold),
            _ => (13.0, Color.FromRgb(0x80, 0x80, 0xbb), FontWeight.Medium),
        };

        var margin = h.Level switch
        {
            1 => new Thickness(0, 20, 0, 12),
            2 => new Thickness(0, 16, 0, 8),
            _ => new Thickness(0, 12, 0, 6),
        };

        return new TextBlock
        {
            Text        = ExtractInlineText(h.Inline),
            FontSize    = size,
            FontWeight  = weight,
            Foreground  = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin      = margin,
        };
    }

    private static Control RenderParagraph(ParagraphBlock p)
        => new SelectableTextBlock
        {
            Text         = ExtractInlineText(p.Inline),
            TextWrapping = TextWrapping.Wrap,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xdd)),
            FontSize     = 13,
            LineHeight   = 22,
            Margin       = new Thickness(0, 0, 0, 10),
        };

    private static Control RenderCodeBlock(LeafBlock c)
    {
        var content = c.Lines.ToString().TrimEnd();
        return new Border
        {
            Background    = new SolidColorBrush(Color.FromRgb(0x0e, 0x0e, 0x22)),
            CornerRadius  = new CornerRadius(6),
            Padding       = new Thickness(14, 10),
            Margin        = new Thickness(0, 4, 0, 12),
            BorderBrush   = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x55)),
            BorderThickness = new Thickness(1),
            Child = new SelectableTextBlock
            {
                Text         = content,
                FontFamily   = new FontFamily("Consolas,Courier New,monospace"),
                FontSize     = 12,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x88, 0xdd, 0xaa)),
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    private static Control RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10) };
        int index = 1;

        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;

            var bullet = list.IsOrdered ? $"{index++}." : "•";
            var text   = ExtractBlockText(li);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var bulletLabel = new TextBlock
            {
                Text       = bullet,
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0xcc)),
                FontSize   = 13,
                Margin     = new Thickness(8, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(bulletLabel, 0);

            var textLabel = new SelectableTextBlock
            {
                Text         = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xdd)),
                FontSize     = 13,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(textLabel, 1);

            row.Children.Add(bulletLabel);
            row.Children.Add(textLabel);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control RenderDivider()
        => new Border
        {
            Height      = 1,
            Background  = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x55)),
            Margin      = new Thickness(0, 12, 0, 12),
        };

    private static Control RenderQuote(QuoteBlock q)
    {
        var text = ExtractBlockText(q);
        return new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x88)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding         = new Thickness(12, 6),
            Margin          = new Thickness(0, 4, 0, 10),
            Background      = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x35)),
            CornerRadius    = new CornerRadius(0, 4, 4, 0),
            Child = new SelectableTextBlock
            {
                Text         = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xcc)),
                FontSize     = 13,
                FontStyle    = FontStyle.Italic,
            },
        };
    }

    private static Control RenderFallbackBlock(Block block)
        => new SelectableTextBlock
        {
            Text         = block.ToString() ?? "",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)),
            FontSize     = 12,
            Margin       = new Thickness(0, 0, 0, 6),
        };

    // ── Text-Extraktion aus Markdig-AST ──────────────────────────────────────

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var child in inline)
            sb.Append(ExtractInline(child));
        return sb.ToString();
    }

    private static string ExtractInline(Inline inline) => inline switch
    {
        LiteralInline lit           => lit.Content.ToString(),
        EmphasisInline em           => ExtractInlineText(em),
        CodeInline code             => code.Content,
        LineBreakInline             => "\n",
        LinkInline link             => ExtractInlineText(link),
        AutolinkInline al           => al.Url,
        HtmlInline html             => "",  // HTML-Tags überspringen
        _                           => "",
    };

    private static string ExtractBlockText(ContainerBlock container)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in container)
        {
            sb.Append(block switch
            {
                ParagraphBlock p => ExtractInlineText(p.Inline),
                _                => "",
            });
        }
        return sb.ToString().Trim();
    }
}
