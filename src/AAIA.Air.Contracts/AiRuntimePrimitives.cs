using System;

namespace AAIA.Air.Contracts;

/// <summary>
/// Risikostufen für AI-Runtime-Tools. Black-Tools werden nicht implementiert.
/// </summary>
public enum AiRiskLevel
{
    Green,  // nur lesen
    Yellow, // Projektdateien ändern, nur per Patch Approval
    Orange, // Build, Terminal, Git, IDE öffnen
    Red,    // Signieren, Marketplace Upload, Veröffentlichung
    Black   // existiert nicht — Secrets/Private Keys/Systempfade/Löschen außerhalb Projekt
}

/// <summary>
/// Per-Client-Berechtigungen. Flags, damit pro Session ein Set vergeben werden kann.
/// Default für neue Sessions: nur <see cref="Read"/>.
/// </summary>
[Flags]
public enum AiPermission
{
    None            = 0,
    Read            = 1 << 0,
    CreateProject   = 1 << 1,
    ProposePatch    = 1 << 2,
    Build           = 1 << 3,
    Validate        = 1 << 4,
    Package         = 1 << 5,
    OpenIde         = 1 << 6,
    RunSafeTerminal = 1 << 7,
    Sign            = 1 << 8,   // nicht über AIR freigegeben
    Marketplace     = 1 << 9,   // nicht über AIR freigegeben
    Collaborate     = 1 << 10,
    Schedule        = 1 << 11,
    ManageResources = 1 << 12
}

/// <summary>Geltungsbereich eines Workspace-Locks.</summary>
public enum AiLockScope
{
    Project,
    Folder,
    File
}

/// <summary>Ereignistypen des Runtime Event Bus (Push statt Polling).</summary>
public enum AiRuntimeEventType
{
    ToolCalled,
    ProjectCreated,
    PatchProposed,
    PatchApproved,
    PatchRejected,
    BuildStarted,
    BuildFinished,
    ValidationFinished,
    PackageCreated,
    MessageSent,
    MessageDelivered,
    MessageAcknowledged,
    ExecutionQueued,
    ExecutionLeased,
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    ExecutionRecovered,
    ExecutionRecoveryRequired,
    ExecutionRecoveryResolved,
    ResourceReserved,
    ResourceCommitted,
    ResourceReleased,
    ResourceDenied,
    ErrorOccurred
}

/// <summary>
/// Bekannte Client-Capabilities (Capability Negotiation).
/// Tools können Capabilities verlangen; die Runtime bietet sie nur passenden Clients an.
/// Modellneutral — die Runtime fragt Fähigkeiten ab, statt Modelle hart zu unterscheiden.
/// </summary>
public static class AiCapabilities
{
    public const string ToolCalling   = "tool-calling";
    public const string Mcp           = "mcp";
    public const string LargeContext  = "large-context";
    public const string Streaming     = "streaming";
    public const string Events        = "events";
    public const string Reasoning     = "reasoning";
    public const string Vision        = "vision";
    public const string Files         = "files";
    public const string Terminal      = "terminal";
}
