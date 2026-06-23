using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>
/// ViewModel für das Patch-Approval-Fenster.
/// Zeigt jeden Patch-Vorschlag mit Vorher/Nachher und lässt den Nutzer entscheiden.
/// </summary>
public sealed partial class PatchApprovalViewModel : ObservableObject
{
    private readonly string          _proposalId;
    private readonly AiPatchRequest  _request;
    private readonly string?         _projectRoot;
    private readonly Action<string, bool> _onDecision; // proposalId, approved

    // ── Navigations-Index ─────────────────────────────────────────────────────

    [ObservableProperty] private int _currentIndex = 0;

    partial void OnCurrentIndexChanged(int value) => LoadCurrentPatch();

    public int  TotalCount   => _request.Patches.Count;
    public bool HasPrevious  => CurrentIndex > 0;
    public bool HasNext      => CurrentIndex < TotalCount - 1;
    public bool IsLast       => CurrentIndex == TotalCount - 1;

    // ── Aktueller Patch ───────────────────────────────────────────────────────

    [ObservableProperty] private string  _targetFile    = "";
    [ObservableProperty] private string  _patchKind     = "";
    [ObservableProperty] private string  _language      = "";
    [ObservableProperty] private string  _description   = "";
    [ObservableProperty] private string  _beforeContent = "";
    [ObservableProperty] private string  _afterContent  = "";
    [ObservableProperty] private string  _rationale     = "";
    [ObservableProperty] private bool    _fileExists    = false;
    [ObservableProperty] private string  _counter       = "";

    // ── Entscheidungs-Tracking ────────────────────────────────────────────────

    private readonly List<PatchDecision> _decisions = [];

    [ObservableProperty] private string _statusText  = "";
    [ObservableProperty] private bool   _hasStatus   = false;
    [ObservableProperty] private bool   _isCompleted = false;
    [ObservableProperty] private int    _approvedCount = 0;
    [ObservableProperty] private int    _rejectedCount = 0;

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public PatchApprovalViewModel(
        string         proposalId,
        AiPatchRequest request,
        string?        projectRoot,
        Action<string, bool> onDecision)
    {
        _proposalId  = proposalId;
        _request     = request;
        _projectRoot = projectRoot;
        _onDecision  = onDecision;
        Rationale    = request.Rationale ?? "";

        LoadCurrentPatch();
    }

    // ── Patch laden ───────────────────────────────────────────────────────────

    private void LoadCurrentPatch()
    {
        if (_request.Patches.Count == 0) return;
        var idx = Math.Clamp(CurrentIndex, 0, _request.Patches.Count - 1);
        var patch = _request.Patches[idx];

        TargetFile  = patch.TargetFile ?? "(Keine Zieldatei angegeben)";
        PatchKind   = patch.Kind;
        Language    = patch.Language;
        Description = patch.Description ?? "";
        AfterContent = patch.Content;
        Counter     = $"Patch {idx + 1} von {TotalCount}";

        // Vorherigen Datei-Inhalt laden
        BeforeContent = LoadCurrentFileContent(patch.TargetFile);
        FileExists    = !string.IsNullOrEmpty(BeforeContent);

        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(IsLast));

        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }

    private string LoadCurrentFileContent(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(_projectRoot))
            return "";

        var fullPath = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return "";

        try { return File.ReadAllText(fullPath, Encoding.UTF8); }
        catch { return ""; }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void Previous()
    {
        if (CurrentIndex > 0)
            CurrentIndex--;
    }

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void Next()
    {
        if (CurrentIndex < TotalCount - 1)
            CurrentIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private async Task ApproveAsync()
    {
        var patch = _request.Patches[CurrentIndex];
        var wrote = await ApplyPatchAsync(patch);

        RecordDecision(CurrentIndex, true, wrote);
        ApprovedCount++;

        if (IsLast)
            await FinishAsync();
        else
            CurrentIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private async Task RejectAsync()
    {
        RecordDecision(CurrentIndex, false, false);
        RejectedCount++;

        if (IsLast)
            await FinishAsync();
        else
            CurrentIndex++;
    }

    [RelayCommand]
    private async Task RejectAllAsync()
    {
        for (int i = CurrentIndex; i < TotalCount; i++)
            RecordDecision(i, false, false);
        RejectedCount += TotalCount - CurrentIndex;
        await FinishAsync();
    }

    private bool CanDecide() => !IsCompleted && !AlreadyDecided(CurrentIndex);

    // ── Patch anwenden ────────────────────────────────────────────────────────

    private async Task<bool> ApplyPatchAsync(AiPatchItem patch)
    {
        if (string.IsNullOrEmpty(patch.TargetFile) || string.IsNullOrEmpty(_projectRoot))
        {
            SetStatus("⚠️ Keine Zieldatei — Patch nicht angewendet.", false);
            return false;
        }

        var fullPath = Path.Combine(
            _projectRoot,
            patch.TargetFile.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            // Backup erstellen wenn Datei existiert
            if (File.Exists(fullPath))
            {
                var backupPath = fullPath + $".bak.{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            // Verzeichnis anlegen falls nötig
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, patch.Content, Encoding.UTF8);
            SetStatus($"✅ Patch angewendet: {patch.TargetFile}", false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Fehler beim Schreiben: {ex.Message}", true);
            return false;
        }
    }

    // ── Abschluss ─────────────────────────────────────────────────────────────

    private async Task FinishAsync()
    {
        IsCompleted = true;
        var anyApproved = ApprovedCount > 0;
        _onDecision(_proposalId, anyApproved);
        SetStatus(
            $"Abgeschlossen: {ApprovedCount} angewendet, {RejectedCount} abgelehnt.",
            false);
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private void RecordDecision(int index, bool approved, bool applied)
    {
        _decisions.RemoveAll(d => d.PatchIndex == index);
        _decisions.Add(new PatchDecision(index, approved, applied));
        ApproveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }

    private bool AlreadyDecided(int index) =>
        _decisions.Exists(d => d.PatchIndex == index);

    private void SetStatus(string text, bool isError)
    {
        StatusText = text;
        HasStatus  = true;
    }
}

// ── Hilfsdatentyp ─────────────────────────────────────────────────────────────

internal sealed record PatchDecision(int PatchIndex, bool Approved, bool Applied);
