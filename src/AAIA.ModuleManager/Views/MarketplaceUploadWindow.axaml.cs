using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AAIA.ModuleManager.Services.Marketplace;

namespace AAIA.ModuleManager.Views;

/// <summary>
/// Upload-Dialog für ein ETW-signiertes Release an den Marketplace.
///
/// Sicherheitsregel (unveränderlich):
///   Dieser Dialog überträgt NIEMALS Private Keys, Quellcode oder Secrets.
///   Alle Security-Invarianten werden durch <see cref="MarketplaceSignedUploadService"/> erzwungen.
///   MarketplaceVerified wird NICHT lokal gesetzt — nur der Server darf das.
/// </summary>
public partial class MarketplaceUploadWindow : Window
{
    private readonly MarketplaceSignedUploadContext _ctx;
    private MarketplaceUploadResult?                _lastResult;
    private CancellationTokenSource?                _cts;

    public MarketplaceUploadWindow(MarketplaceSignedUploadContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PopulatePreview();
    }

    // ── Preview befüllen ──────────────────────────────────────────────────────

    private void PopulatePreview()
    {
        // Header
        HeaderSubLabel.Text = string.IsNullOrEmpty(_ctx.DisplayName)
            ? _ctx.ExtensionId
            : $"{_ctx.DisplayName}  ·  {_ctx.Version}";

        // Metadaten
        ExtIdLabel.Text       = _ctx.ExtensionId.Length > 0 ? _ctx.ExtensionId : "–";
        VersionLabel.Text     = _ctx.Version.Length    > 0 ? _ctx.Version      : "–";
        EtwIdLabel.Text       = _ctx.DeveloperEtwId.Length > 0 ? _ctx.DeveloperEtwId : "–";
        FingerprintLabel.Text = _ctx.KeyFingerprint.Length > 0 ? _ctx.KeyFingerprint : "–";
        TrustLevelLabel.Text  = _ctx.TrustLevel.Length > 0 ? _ctx.TrustLevel : "–";

        // Dateiliste
        AddFileRow(_ctx.PackageFileName,         _ctx.PackagePath,          _ctx.PackageExists);
        AddFileRow("signature-info.json",         _ctx.SignatureInfoPath,    _ctx.SignatureExists);
        AddFileRow("release-info.json",           _ctx.ReleaseInfoPath,      _ctx.ReleaseInfoExists);
        AddFileRow("inspection-report.json",      _ctx.InspectionReportPath, _ctx.InspectionExists);

        // Warnungen
        if (!_ctx.IsLoggedIn) LoginWarning.IsVisible = true;

        // Upload möglich?
        if (_ctx.IsReadyToUpload)
        {
            StatusFooter.Text    = "Bereit für den Upload.";
            UploadBtn.IsEnabled  = true;
        }
        else
        {
            NotReadyBanner.IsVisible = true;
            NotReadyText.Text        = BuildNotReadyMessage();
            UploadBtn.IsEnabled      = false;
        }
    }

    private void AddFileRow(string name, string path, bool exists)
    {
        var icon  = exists ? "✅" : "❌";
        var color = exists ? Color.Parse("#4ade80") : Color.Parse("#e05252");
        var size  = exists ? GetSizeLabel(path) : "nicht gefunden";

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6
        };

        row.Children.Add(new TextBlock
        {
            Text       = $"{icon}  {name}",
            Foreground = new SolidColorBrush(color),
            FontSize   = 12
        });

        row.Children.Add(new TextBlock
        {
            Text       = $"({size})",
            Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
            FontSize   = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        FileListPanel.Children.Add(row);
    }

    private static string GetSizeLabel(string path)
    {
        try
        {
            if (!File.Exists(path)) return "nicht gefunden";
            var bytes = new FileInfo(path).Length;
            return bytes switch
            {
                < 1024    => $"{bytes} B",
                < 1048576 => $"{bytes / 1024.0:F1} KB",
                _         => $"{bytes / 1048576.0:F2} MB"
            };
        }
        catch { return "?"; }
    }

    private string BuildNotReadyMessage()
    {
        var reasons = new System.Collections.Generic.List<string>();
        if (!_ctx.PackageExists)    reasons.Add("Paketdatei nicht gefunden");
        if (!_ctx.SignatureExists)  reasons.Add("signature-info.json nicht gefunden");
        if (!_ctx.IsApiConfigured)  reasons.Add("Marketplace-API-URL fehlt");
        if (!_ctx.IsLoggedIn)       reasons.Add("Nicht angemeldet");

        return reasons.Count > 0
            ? "Upload nicht möglich: " + string.Join(" · ", reasons) + "."
            : "Upload-Voraussetzungen nicht erfüllt.";
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    private async void Upload_Click(object? sender, RoutedEventArgs e)
    {
        UploadBtn.IsEnabled      = false;
        PreviewPanel.IsVisible   = false;
        ProgressPanel.IsVisible  = true;
        StatusFooter.Text        = "Upload läuft …";

        _cts = new CancellationTokenSource();

        var progress = new Progress<int>(pct =>
            Dispatcher.UIThread.Post(() =>
                ProgressLabel.Text = pct switch
                {
                    < 25  => "Verbindung wird aufgebaut …",
                    < 80  => $"Dateien werden übertragen … ({pct}%)",
                    < 100 => "Serverantwort wird verarbeitet …",
                    _     => "Upload abgeschlossen."
                }));

        try
        {
            _lastResult = await MarketplaceSignedUploadService.UploadAsync(_ctx, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _lastResult = new MarketplaceUploadResult
            {
                Success = false,
                Status  = MarketplaceUploadStatus.Unknown,
                Message = "Upload abgebrochen."
            };
        }
        catch (Exception ex)
        {
            _lastResult = new MarketplaceUploadResult
            {
                Success     = false,
                Status      = MarketplaceUploadStatus.ServerError,
                Message     = "Unerwarteter Fehler beim Upload.",
                ErrorDetail = ex.Message
            };
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }

        ProgressPanel.IsVisible = false;
        ResultPanel.IsVisible   = true;
        ShowResult(_lastResult);
    }

    // ── Ergebnis anzeigen ─────────────────────────────────────────────────────

    private void ShowResult(MarketplaceUploadResult result)
    {
        ResultStatusLabel.Text   = result.StatusLabel;
        ResultMessage.Text       = result.Message ?? "";

        // Farbe des Status-Labels anpassen
        ResultStatusLabel.Foreground = new SolidColorBrush(Color.Parse(result.StatusColor));

        // Submission-ID
        if (!string.IsNullOrWhiteSpace(result.SubmissionId))
        {
            SubmissionIdRow.IsVisible = true;
            SubmissionIdLabel.Text    = result.SubmissionId;
        }

        // Nächster Schritt
        if (!string.IsNullOrWhiteSpace(result.NextStep))
        {
            NextStepRow.IsVisible = true;
            NextStepLabel.Text    = result.NextStep;
        }

        // Fehlerdetail
        if (!string.IsNullOrWhiteSpace(result.ErrorDetail))
        {
            ErrorDetailRow.IsVisible = true;
            ErrorDetailLabel.Text    = result.ErrorDetail;
        }

        // Marketplace-URL öffnen
        if (!string.IsNullOrWhiteSpace(result.MarketplaceUrl))
            OpenMarketplaceBtn.IsVisible = true;

        StatusFooter.Text = result.Success
            ? "Upload erfolgreich abgeschlossen."
            : "Upload fehlgeschlagen.";
    }

    // ── Footer-Buttons ────────────────────────────────────────────────────────

    private void OpenMarketplace_Click(object? sender, RoutedEventArgs e)
    {
        var url = _lastResult?.MarketplaceUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* Wenn Browser nicht geöffnet werden kann — still ignorieren */ }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
