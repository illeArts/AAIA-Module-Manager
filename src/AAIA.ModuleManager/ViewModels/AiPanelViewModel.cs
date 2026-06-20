using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.ViewModels;

public sealed record AiChatEntry(string Role, string Content, bool IsUser)
{
    public string DisplayRole => IsUser ? "Du" : "KI-Assistent";
}

public partial class AiPanelViewModel : ObservableObject
{
    private readonly AppConfig          _config;
    private IAiProviderService?         _provider;
    private CancellationTokenSource?    _cts;

    // ── Chat-Verlauf ─────────────────────────────────────────────────────────

    public ObservableCollection<AiChatEntry> Messages { get; } = [];

    [ObservableProperty] private string _inputText  = "";
    [ObservableProperty] private bool   _isSending  = false;
    [ObservableProperty] private string _statusText = "";

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value)   => SendCommand.NotifyCanExecuteChanged();

    // ── Kontext (aus anderen Tabs injiziert) ─────────────────────────────────

    [ObservableProperty] private string? _currentProjectName;
    [ObservableProperty] private string? _currentProjectType;
    [ObservableProperty] private string? _lastError;

    // ── Konstruktor ───────────────────────────────────────────────────────────

    public AiPanelViewModel(AppConfig config)
    {
        _config = config;
        RefreshClient();
    }

    /// <summary>
    /// Wird von SettingsWindow nach dem Speichern aufgerufen,
    /// damit der neue Anbieter sofort wirksam ist.
    /// </summary>
    public void RefreshClient()
    {
        _provider = AiServiceFactory.Create(_config);
        OnPropertyChanged(nameof(HasProvider));
        OnPropertyChanged(nameof(ProviderLabel));
    }

    public bool   HasProvider   => _provider is not null;
    public string ProviderLabel => _provider is not null
        ? _provider.ProviderName
        : "Kein KI-Anbieter";

    // ── Senden ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend()) return;

        var userText = InputText.Trim();
        InputText    = "";
        IsSending    = true;
        StatusText   = $"{ProviderLabel} denkt nach...";

        Messages.Add(new AiChatEntry("user", userText, IsUser: true));

        try
        {
            EnsureProvider();

            _cts?.Dispose();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            // Nur die letzten 20 Nachrichten mitsenden
            var history = new System.Collections.Generic.List<ChatMessage>();
            var start   = Math.Max(0, Messages.Count - 20);
            for (int i = start; i < Messages.Count; i++)
            {
                var m = Messages[i];
                history.Add(new ChatMessage(m.Role, m.Content));
            }

            var system = AiContextBuilder.Build(
                currentProjectName: CurrentProjectName,
                currentProjectType: CurrentProjectType,
                lastError:          LastError);

            var response = await _provider!.SendAsync(
                new AiRequest(history, system), _cts.Token);

            if (response.Success)
                Messages.Add(new AiChatEntry("assistant", response.Text, IsUser: false));
            else
                Messages.Add(new AiChatEntry("assistant", $"Fehler: {response.Error}", IsUser: false));

            StatusText = "";
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new AiChatEntry("assistant", "Anfrage abgebrochen.", IsUser: false));
            StatusText = "";
        }
        catch (Exception ex)
        {
            Messages.Add(new AiChatEntry("assistant", $"Fehler: {ex.Message}", IsUser: false));
            StatusText = "";
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        StatusText = "";
    }

    [RelayCommand]
    private void InsertContext()
    {
        var ctx = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(CurrentProjectName))
            ctx.AppendLine($"Aktuelles Projekt: {CurrentProjectName} ({CurrentProjectType})");
        if (!string.IsNullOrWhiteSpace(LastError))
        {
            ctx.AppendLine("Letzter Fehler:");
            ctx.AppendLine(LastError);
        }

        if (ctx.Length > 0)
            InputText = ctx.ToString().TrimEnd() + "\n\n" + InputText;
    }

    private void EnsureProvider()
    {
        if (_provider is null)
            throw new InvalidOperationException(
                "Kein KI-Anbieter konfiguriert.\n" +
                "Bitte unter Einstellungen → KI-Assistent einen Anbieter und API-Key eintragen.");
    }
}
