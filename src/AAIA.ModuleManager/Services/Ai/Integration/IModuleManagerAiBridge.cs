using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime;
using AAIA.ModuleManager.Services.Ai.Runtime.Hosts;

namespace AAIA.ModuleManager.Services.Ai.Integration;

/// <summary>
/// Projektinfos, die die host-gebundenen Tools brauchen. Werden von der App (ViewModel)
/// aus dem aktuell geladenen Projekt aufgelöst.
/// </summary>
public sealed class ModuleManagerProjectInfo
{
    public required string ProjectDir { get; init; }
    public string? CsprojPath { get; init; }
    public NewProjectType ProjectType { get; init; } = NewProjectType.ServerModule;
    public string? ProjectName { get; init; }
    public string? ExtensionId { get; init; }
}

/// <summary>
/// Brücke der AIR zum Module Manager. Bündelt die Stellen, an denen die Hosts echten
/// App-/UI-Zustand brauchen: Status, Projektauflösung, Patch-Approval (UI) und Projekt-
/// erstellung (Wizard). Die übrigen Hosts arbeiten direkt auf den statischen Phase-6-
/// Services und brauchen die Bridge nur zur Projektauflösung.
///
/// Implementiert wird das vom ConnectorTab-ViewModel (kennt das geladene Projekt + UI).
/// </summary>
public interface IModuleManagerAiBridge
{
    /// <summary>Sicherer Status ohne Secrets (für aaia.status.get).</summary>
    AaiaProjectStatus GetStatus();

    /// <summary>
    /// Löst aus einem projectPath die Projektinfos auf (csproj, Typ, Name). Null, wenn
    /// der Pfad kein bekanntes/geladenes Projekt ist.
    /// </summary>
    ModuleManagerProjectInfo? ResolveProject(string projectPath);

    /// <summary>
    /// Öffnet den vorhandenen Patch-Approval-Workflow (Diff), wartet auf die Entscheidung
    /// des Nutzers und wendet bei Zustimmung an. Die AIR reimplementiert das NICHT.
    /// </summary>
    Task<AiHostResult> ApproveAndApplyPatchAsync(AiPatchProposalInput input, CancellationToken ct);

    /// <summary>Erstellt ein Projekt über den vorhandenen Wizard/Scaffold (nach Approval).</summary>
    Task<AiHostResult> CreateProjectAsync(AiProjectCreateInput input, CancellationToken ct);
}
