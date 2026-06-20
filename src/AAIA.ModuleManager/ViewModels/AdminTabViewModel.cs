using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für den Owner-only Admin-Tab.
/// Zeigt Echtzeit-Diagnose vom Live-Server:
///   - System-Info (PHP, WP, WC, Speicher)
///   - DB-Tabellen mit Zeilenzahlen
///   - WP-Debug-Log (letzte N Zeilen)
///   - Letzter PHP-Fatal (Transient)
/// </summary>
public sealed partial class AdminTabViewModel(WpMarketplaceClient wpApi) : ObservableObject
{
    private readonly WpMarketplaceClient _wpApi = wpApi;

    // ── Status ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMessage = "Bereit — 'Alles laden' klicken.";

    // ── System-Info ────────────────────────────────────────────────────────────

    [ObservableProperty] private string _systemInfo = "";
    [ObservableProperty] private bool   _hasSystemInfo;

    // ── DB-Tabellen ────────────────────────────────────────────────────────────

    public ObservableCollection<DbTableItem> Tables { get; } = new();
    [ObservableProperty] private bool _hasTables;

    // ── Logs ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _wpLogContent  = "";
    [ObservableProperty] private string _phpLogContent = "";
    [ObservableProperty] private string _wpLogPath     = "";
    [ObservableProperty] private long   _wpLogSizeBytes;
    [ObservableProperty] private bool   _hasLogs;
    [ObservableProperty] private int    _logLines = 200;

    // ── Letzter Fatal ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _lastFatalJson = "";
    [ObservableProperty] private bool   _hasFatal;
    [ObservableProperty] private bool   _fatalFound;

    // ── Collapse-State ─────────────────────────────────────────────────────────
    // false = eingeklappt; wird beim Laden automatisch auf true gesetzt wenn Inhalt vorhanden

    [ObservableProperty] private bool _isWpLogExpanded     = false;
    [ObservableProperty] private bool _isPhpLogExpanded    = false;
    [ObservableProperty] private bool _isLastFatalExpanded = false;

    [RelayCommand]
    private void ToggleSection(string section)
    {
        switch (section)
        {
            case "wplog":  IsWpLogExpanded     = !IsWpLogExpanded;     break;
            case "phplog": IsPhpLogExpanded    = !IsPhpLogExpanded;    break;
            case "fatal":  IsLastFatalExpanded = !IsLastFatalExpanded; break;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    /// <summary>Lädt System-Info, DB-Tabellen, Logs und letzten Fatal parallel.</summary>
    [RelayCommand]
    private async Task LoadAllAsync(CancellationToken ct)
    {
        IsBusy        = true;
        StatusMessage = "Lade Diagnose-Daten vom Server...";
        try
        {
            await Task.WhenAll(
                LoadSystemInfoAsync(ct),
                LoadTablesAsync(ct),
                LoadLogsAsync(ct),
                LoadLastFatalAsync(ct));

            StatusMessage = $"Geladen um {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Lädt nur die Logs (z.B. nach Log-Clear).</summary>
    [RelayCommand]
    private async Task RefreshLogsAsync(CancellationToken ct)
    {
        IsBusy        = true;
        StatusMessage = "Lade Logs...";
        try
        {
            await LoadLogsAsync(ct);
            StatusMessage = $"Logs neu geladen um {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Log-Laden: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Leert das WP-Debug-Log und aktualisiert die Anzeige.</summary>
    [RelayCommand]
    private async Task ClearLogsAsync(CancellationToken ct)
    {
        IsBusy        = true;
        StatusMessage = "Leere debug.log...";
        try
        {
            var msg = await _wpApi.ClearDebugLogsAsync(ct);
            StatusMessage = msg;
            await LoadLogsAsync(ct);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler beim Leeren: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Ruft den letzten PHP-Fatal-Transient ab (löscht ihn dabei).</summary>
    [RelayCommand]
    private async Task CheckLastFatalAsync(CancellationToken ct)
    {
        IsBusy        = true;
        StatusMessage = "Prüfe letzten PHP-Fatal...";
        try
        {
            await LoadLastFatalAsync(ct);
            StatusMessage = FatalFound
                ? "⚠️  Fatal-Error gefunden!"
                : "✅ Kein Fatal-Error aufgezeichnet.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    // ── Private Loader ─────────────────────────────────────────────────────────

    private async Task LoadSystemInfoAsync(CancellationToken ct)
    {
        var info = await _wpApi.GetDebugInfoAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"PHP          {info.PhpVersion}");
        sb.AppendLine($"WordPress    {info.WpVersion}");
        sb.AppendLine($"WooCommerce  {info.WcVersion}");
        sb.AppendLine($"AAIA Plugin  {info.AaiaPluginVersion}");
        sb.AppendLine($"DB Schema    {info.AaiaDbVersion}  (erwartet: {info.AaiaDbExpected})");
        sb.AppendLine();
        sb.AppendLine($"PHP Memory   {info.PhpMemoryLimit}  (Peak: {info.MemoryPeakMb:F1} MB)");
        sb.AppendLine($"Upload Max   {info.UploadMaxFilesize}   Post Max: {info.PostMaxSize}");
        sb.AppendLine($"Max Exec     {info.MaxExecutionTime}s");
        sb.AppendLine();
        sb.AppendLine($"WP_DEBUG         {(info.WpDebug ? "✅ AN" : "❌ AUS")}");
        sb.AppendLine($"WP_DEBUG_LOG     {(info.WpDebugLog ? "✅ AN" : "❌ AUS")}");
        sb.AppendLine($"WP_DEBUG_DISPLAY {(info.WpDebugDisplay ? "⚠️  AN" : "✅ AUS")}");
        sb.AppendLine();
        sb.AppendLine($"REST Base    {info.RestUrl}");
        sb.AppendLine($"Home URL     {info.HomeUrl}");
        sb.AppendLine($"Server Zeit  {info.TimeUtc} UTC  ({info.Timezone})");
        sb.AppendLine();
        sb.AppendLine($"Aktive Plugins ({info.ActivePlugins.Count}):");
        foreach (var p in info.ActivePlugins)
            sb.AppendLine($"  • {p.Name}  v{p.Version}");

        SystemInfo    = sb.ToString();
        HasSystemInfo = true;
    }

    private async Task LoadTablesAsync(CancellationToken ct)
    {
        var list = await _wpApi.GetDebugTablesAsync(ct);
        Tables.Clear();
        foreach (var t in list)
            Tables.Add(new DbTableItem(t.Name, t.Rows, t.DataBytes + t.IndexBytes, t.Engine));
        HasTables = Tables.Count > 0;
    }

    private async Task LoadLogsAsync(CancellationToken ct)
    {
        var logs = await _wpApi.GetDebugLogsAsync(LogLines, ct);

        WpLogPath     = logs.WpDebugLog?.Path ?? "";
        WpLogSizeBytes = logs.WpDebugLog?.Size ?? 0;
        WpLogContent  = logs.WpDebugLog is { } wp
            ? string.Join("\n", wp.Lines)
            : "(nicht verfügbar)";

        PhpLogContent = logs.PhpErrorLog is { } php
            ? string.Join("\n", php.Lines)
            : "(nicht verfügbar)";

        HasLogs = true;

        // Auto-expand wenn Inhalt vorhanden
        bool wpHasContent  = !string.IsNullOrWhiteSpace(WpLogContent)  && WpLogContent  != "(nicht verfügbar)";
        bool phpHasContent = !string.IsNullOrWhiteSpace(PhpLogContent) && PhpLogContent != "(nicht verfügbar)";
        if (wpHasContent)  IsWpLogExpanded  = true;
        if (phpHasContent) IsPhpLogExpanded = true;
    }

    private async Task LoadLastFatalAsync(CancellationToken ct)
    {
        var json = await _wpApi.GetLastRestFatalAsync(ct);
        LastFatalJson = json;
        HasFatal      = true;

        // Einfacher Heuristik-Check: "found": true im JSON
        FatalFound = json.Contains("\"found\": true", StringComparison.OrdinalIgnoreCase)
                  || json.Contains("\"found\":true",  StringComparison.OrdinalIgnoreCase);

        // Auto-expand wenn Fatal vorhanden
        if (FatalFound) IsLastFatalExpanded = true;
    }
}

// ── Display-Item ──────────────────────────────────────────────────────────────

public sealed class DbTableItem
{
    public string Name        { get; }
    public int    Rows        { get; }
    public string SizeDisplay { get; }
    public string Engine      { get; }

    public DbTableItem(string name, int rows, long totalBytes, string engine)
    {
        Name  = name;
        Rows  = rows;
        Engine = engine;
        SizeDisplay = totalBytes switch
        {
            > 1_048_576 => $"{totalBytes / 1_048_576.0:F1} MB",
            > 1_024     => $"{totalBytes / 1_024.0:F1} KB",
            _           => $"{totalBytes} B",
        };
    }
}
