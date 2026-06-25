using System;
using Avalonia.Controls;
using AAIA.ModuleManager.Services.AiAdapter.Connector;
using AAIA.ModuleManager.ViewModels;

namespace AAIA.ModuleManager.Views;

public partial class PatchApprovalWindow : Window
{
    private PatchApprovalViewModel? _vm;

    public PatchApprovalWindow()
    {
        InitializeComponent();
    }

    public PatchApprovalWindow(
        string         proposalId,
        AiPatchRequest request,
        string?        projectRoot,
        Action<string, bool> onDecision)
    {
        _vm = new PatchApprovalViewModel(proposalId, request, projectRoot, (id, approved) =>
        {
            onDecision(id, approved);
            Close();
        });
        DataContext = _vm;
        InitializeComponent();
    }
}
