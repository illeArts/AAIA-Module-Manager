using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services.AiAdapter;
using AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für das AI Handoff Package Fenster.
/// Baut das Paket und ermöglicht Export als Ordner oder ZIP.
/// </summary>
public sealed partial class AiHandoffPackageViewModel : ObservableObject
{
    private readonly AiAdapterRequest     _request;
    private readonly AiHandoffContext     _context;
    private          AiHandoffPackage?    _package;

    // ── Datei-Liste für die UI ────────────────────────────────────────────────

    public ObservableCollection<HandoffFileEntry> FileEntries { get; } = [];

    // ── Ausgewählte Datei (Preview) ───────────────────────────────────────────

    [ObservableProperty] private HandoffFileEntry? _selectedFile;
    [ObservableProperty] private string            _previewContent = "";

    partial void OnSelectedFileChanged(HandoffFileEntry? value)
    {
        PreviewContent = value?.Content ?? "";
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isBuilding   = false;
    [ObservableProperty] private bool   _isExporting  = false;
    [ObservableProperty] private string _statusText   = "";
    [ObservableProperty] private bool   _hasStatus    = false;
    [ObservableProperty] private bool   _isError      = false;
    [ObservableProperty] private bool   _isReady      = false;
    [ObservableProperty] private string _packageInfo  = "";
    [ObservableProperty] private string _exportedPath = "";
    [ObservableProperty] private bool   _hasExportedPath = false;

    // ── Paket-Typ Auswahl ─────────────────────────────────────────────────────

    [ObservableProperty] private AiHandoffPackageType _packageType = AiHandoffPackageType.BuildFix;

    partial void OnPackageTypeChanged(AiHandoffPackageType value)
        => _ = RebuildAsync();

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public AiHandoffPackageViewModel(AiAdapterRequest request, AiHandoffContext context)
    {
        _request = request;
        _context = context;
    }

    // ── Laden ─────────────────────────────────────────────────────────────────

    public async Task InitAsync()
    {
        await RebuildAsync();
    }

    private async Task RebuildAsync()
    {
        IsBuilding = true;
        IsReady    = false;
        FileEntries.Clear();
        SetStatus("Paket wird erstellt …", false);

        await Task.Run(() =>
        {
            _package = AiHandoffPackageBuilder.Build(_context, _request, PackageType);
        });

        if (_package is null)
        {
            SetStatus("Fehler beim Erstellen des Pakets.", true);
            IsBuilding = false;
            return;
        }

        // UI befüllen (zurück auf UI-Thread via ObservableCollection)
        foreach (var f in _package.Files)
            FileEntries.Add(new HandoffFileEntry(f.FileName, f.Content, f.Description, f.IsMainPrompt));

        // Hauptprompt vorauswählen
        foreach (var e in FileEntries)
        {
            if (e.IsMainPrompt)
            {
                SelectedFile = e;
                break;
            }
        }

        PackageInfo = $"{_package.Files.Count} Dateien  ·  {_package.Target}  ·  {_package.PackageType}  ·  {_package.ContextLevel}";

        if (_package.SafetyWarnings.Count > 0)
            SetStatus($"⚠️ {_package.SafetyWarnings.Count} Sicherheitshinweis(e). Bitte vor dem Export prüfen.", false);
        else
            HideStatus();

        IsReady    = true;
        IsBuilding = false;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportFolderAsync()
    {
        if (_package is null) return;
        IsExporting = true;
        SetStatus("Exportiere in Ordner …", false);

        try
        {
            var path = await AiHandoffPackageExporter.ExportToDirectoryAsync(_package);
            ExportedPath    = path;
            HasExportedPath = true;
            SetStatus($"✅ Exportiert: {path}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Export fehlgeschlagen: {ex.Message}", true);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportZipAsync()
    {
        if (_package is null) return;
        IsExporting = true;
        SetStatus("Erstelle ZIP …", false);

        try
        {
            var path = await AiHandoffPackageExporter.ExportToZipAsync(_package);
            ExportedPath    = path;
            HasExportedPath = true;
            SetStatus($"✅ ZIP erstellt: {path}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ ZIP-Export fehlgeschlagen: {ex.Message}", true);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(ExportedPath)) return;
        var dir = System.IO.Directory.Exists(ExportedPath)
            ? ExportedPath
            : System.IO.Path.GetDirectoryName(ExportedPath) ?? ExportedPath;
        AiHandoffPackageExporter.OpenInFileManager(dir);
    }

    private bool CanExport()     => IsReady && !IsExporting && _package is not null;
    private bool CanOpenFolder() => HasExportedPath && !string.IsNullOrEmpty(ExportedPath);

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private void SetStatus(string text, bool isError)
    {
        StatusText = text;
        IsError    = isError;
        HasStatus  = true;
    }

    private void HideStatus()
    {
        HasStatus = false;
    }
}

// ── UI-Hilfsmodell ────────────────────────────────────────────────────────────

/// <summary>Repräsentiert eine einzelne Datei im AI Handoff Package (UI-Modell).</summary>
public sealed class HandoffFileEntry(string fileName, string content, string description, bool isMainPrompt)
{
    public string FileName     { get; } = fileName;
    public string Content      { get; } = content;
    public string Description  { get; } = description;
    public bool   IsMainPrompt { get; } = isMainPrompt;
    public string DisplayLabel => IsMainPrompt ? $"⭐ {FileName}" : FileName;
    public int    CharCount    => Content.Length;
    public string CharLabel    => $"{Content.Length:N0} Zeichen";
}
