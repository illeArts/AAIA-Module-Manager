using Avalonia.Controls;
using Avalonia.Interactivity;
using AAIA.ModuleManager.Services;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class NewProjectWizardWindow : Window
{
    private NewProjectWizardViewModel? _vm;

    public NewProjectWizardWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(NewProjectWizardViewModel vm)
    {
        _vm         = vm;
        DataContext = vm;

        _vm.StorageProvider    = StorageProvider;
        _vm.Clipboard          = TopLevel.GetTopLevel(this)?.Clipboard;
        _vm.OpenHelpRequested  = articleId => OpenHelpForArticle(articleId);

        vm.PropertyChanged += (_, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (e.PropertyName == nameof(NewProjectWizardViewModel.CurrentStep))
                    UpdateStepVisibility();

                if (e.PropertyName == nameof(NewProjectWizardViewModel.ErrorMessage))
                    UpdateErrorDisplay();

                if (e.PropertyName == nameof(NewProjectWizardViewModel.IsBusy))
                    CreateBtnLabel.Text = _vm.IsBusy ? "Wird erstellt..." : "Erstellen";
            });
        };
    }

    // ── Step-Navigation ───────────────────────────────────────────────────────

    private void UpdateStepVisibility()
    {
        if (_vm is null) return;

        var step = _vm.CurrentStep;
        Step0Panel.IsVisible = step == WizardStep.IdeaInput;
        Step1Panel.IsVisible = step == WizardStep.IdeaResult;
        Step2Panel.IsVisible = step == WizardStep.ProjectDetails;
        Step3Panel.IsVisible = step == WizardStep.Success;
        Step4Panel.IsVisible = step == WizardStep.Validation;
        Step5Panel.IsVisible = step == WizardStep.PublishReadiness;
        Step6Panel.IsVisible = step == WizardStep.Signature;

        if (step == WizardStep.ProjectDetails)
            SyncRadioButtons();

        if (step == WizardStep.Success)
            BuildStep3();
    }

    // ── Step 2: Typ-Auswahl ───────────────────────────────────────────────────

    private void RadioServerModule_Click (object? s, RoutedEventArgs e) => SetType(NewProjectType.ServerModule);
    private void RadioClientPlugin_Click (object? s, RoutedEventArgs e) => SetType(NewProjectType.ClientPlugin);
    private void RadioHybridModule_Click (object? s, RoutedEventArgs e) => SetType(NewProjectType.HybridModule);
    private void RadioLanguagePack_Click (object? s, RoutedEventArgs e) => SetType(NewProjectType.LanguagePack);

    private void SetType(NewProjectType type)
    {
        if (_vm is null) return;
        _vm.ProjectType = type;
    }

    /// <summary>
    /// RadioButtons mit dem ViewModel-Typ synchronisieren (z. B. nach KI-Vorschlag).
    /// </summary>
    private void SyncRadioButtons()
    {
        if (_vm is null) return;
        RadioServerModule.IsChecked = _vm.ProjectType == NewProjectType.ServerModule;
        RadioClientPlugin.IsChecked = _vm.ProjectType == NewProjectType.ClientPlugin;
        RadioHybridModule.IsChecked = _vm.ProjectType == NewProjectType.HybridModule;
        RadioLanguagePack.IsChecked = _vm.ProjectType == NewProjectType.LanguagePack;
    }

    // ── Step 2: Fehler-Anzeige ────────────────────────────────────────────────

    private void UpdateErrorDisplay()
    {
        if (_vm is null) return;
        var hasError = !string.IsNullOrWhiteSpace(_vm.ErrorMessage);
        ErrorBorder.IsVisible = hasError;
        ErrorText.Text        = _vm.ErrorMessage;
    }

    // ── Step 3: Erfolg ────────────────────────────────────────────────────────

    private void BuildStep3()
    {
        if (_vm is null) return;

        // Pfad-Label befüllen
        CreatedPathLabel.Text = _vm.CreatedProjectPath ?? "";

        // IDE-Buttons dynamisch erstellen
        // Alle bestehenden außer dem NoIdeText-Platzhalter entfernen
        while (Step3IdePanel.Children.Count > 1)
            Step3IdePanel.Children.RemoveAt(Step3IdePanel.Children.Count - 1);

        var hasIde = false;
        foreach (var ide in _vm.InstalledIdes)
        {
            if (!ide.Installed) continue;
            hasIde = true;
            var btn = new Button
            {
                Content  = ide.Name,
                FontSize = 11,
                Margin   = new Avalonia.Thickness(0, 0, 6, 0)
            };
            btn.Classes.Add("ghost");
            var ideName = ide.Name;
            btn.Click += (_, _) => _vm.OpenInIdeCommand.Execute(ideName);
            Step3IdePanel.Children.Add(btn);
        }

        NoIdeText.IsVisible = !hasIde;
    }

    // ── Step 4: Zurück zu Step 3 ──────────────────────────────────────────────

    private void ValidationBack_Click(object? s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.CurrentStep = WizardStep.Success;
    }

    // ── Step 5: Zurück zu Step 4 ──────────────────────────────────────────────

    private void PublishBack_Click(object? s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.CurrentStep = WizardStep.Success;
    }

    // ── Step 6: Zurück zu Step 5 ──────────────────────────────────────────────

    private void SignatureBack_Click(object? s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.CurrentStep = WizardStep.PublishReadiness;
    }

    // ── Phase 5.0: Marketplace Upload ────────────────────────────────────────

    private void Marketplace_Click(object? s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var ctx = _vm.BuildMarketplaceUploadContext();
        var dlg = new MarketplaceUploadWindow(ctx);
        dlg.ShowDialog(this);
    }

    // ── Phase 4.5: AI Handoff ─────────────────────────────────────────────────

    internal void OpenAiHandoff()
    {
        if (_vm is null) return;
        var ctx = _vm.BuildAiHandoffContext();
        var dlg = new AiHandoffWindow(ctx);
        dlg.ShowDialog(this);
    }

    private void AiHandoff_Click(object? s, RoutedEventArgs e) => OpenAiHandoff();

    // ── Phase 4.5: Kontexthilfe ───────────────────────────────────────────────

    private void Help_Click(object? s, RoutedEventArgs e)
    {
        var dlg = new HelpWindow();
        dlg.ShowDialog(this);
    }

    // ── Fenster schließen ─────────────────────────────────────────────────────

    private void Close_Click(object? s, RoutedEventArgs e) => Close();

    // ── Phase 6.10d: Fehler-Hilfe ─────────────────────────────────────────────

    private void OpenHelpForArticle(string articleId)
    {
        var helpVm  = new HelpCenterViewModel { PendingArticleId = articleId };
        var helpWnd = new HelpCenterWindow { DataContext = helpVm };
        helpWnd.ShowDialog(this);
    }
}
