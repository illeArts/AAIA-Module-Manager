using System.Threading;
using System.Threading.Tasks;

namespace AAIA.Air.Contracts;

/// <summary>
/// Host-Abstraktion. Die AI Runtime kennt KEINE konkrete App (Module Manager, AAIAS,
/// BBK, DUKI …) — nur diese Interfaces. Jede App implementiert die Hosts, die sie
/// anbieten kann, und registriert sie in der <see cref="AiHostRegistry"/>. Dadurch ist
/// die Runtime vollständig wiederverwendbar.
/// </summary>
public interface IAiHost
{
    /// <summary>Anbieter-Kennung des Hosts (z. B. "module-manager", "aaias") — für Audit.</summary>
    string HostId { get; }
}

/// <summary>Adapter-neutrales Host-Ergebnis. Wird vom Tool in ein AiToolResult verpackt.</summary>
public sealed class AiHostResult
{
    public bool Success { get; init; }
    public object? Payload { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static AiHostResult Ok(object payload) => new() { Success = true, Payload = payload };
    public static AiHostResult Fail(string error, string? code = null)
        => new() { Success = false, Error = error, ErrorCode = code };
}

// ── Eingabe-DTOs (host-neutral) ─────────────────────────────────────────────

public sealed class AiProjectCreateInput
{
    public string Idea { get; init; } = "";
    public string ExtensionKind { get; init; } = "Module";
    public string Host { get; init; } = "";
    public string RiskPreference { get; init; } = "Normal";
}

public sealed class AiPatchChange
{
    public string RelativePath { get; init; } = "";
    public string Operation { get; init; } = "create_or_update";
    public string Content { get; init; } = "";
}

public sealed class AiPatchProposalInput
{
    public string ProjectPath { get; init; } = "";
    public System.Collections.Generic.IReadOnlyList<AiPatchChange> Changes { get; init; }
        = System.Array.Empty<AiPatchChange>();
    public string Reason { get; init; } = "";
}

// ── Host-Interfaces je Fähigkeit ────────────────────────────────────────────

/// <summary>Sicherer Status (ohne Secrets) — für aaia.status.get.</summary>
public interface IAiStatusHost : IAiHost
{
    AaiaProjectStatus GetStatus();
}

/// <summary>Projektlebenszyklus: anlegen, sicherer Kontext.</summary>
public interface IAiProjectHost : IAiHost
{
    Task<AiHostResult> CreateProjectAsync(AiProjectCreateInput input, CancellationToken ct);
    Task<AiHostResult> GetContextAsync(string projectPath, CancellationToken ct);
}

/// <summary>Dateien: nur erlaubte Projektdateien, keine Secrets.</summary>
public interface IAiFileHost : IAiHost
{
    Task<AiHostResult> ListAllowedAsync(string projectPath, bool includeContent, CancellationToken ct);
    Task<AiHostResult> ReadAsync(string projectPath, string relativePath, CancellationToken ct);
}

/// <summary>Patch-Vorschläge über den Approval-Workflow der App.</summary>
public interface IAiPatchHost : IAiHost
{
    Task<AiHostResult> ProposeAsync(AiPatchProposalInput input, CancellationToken ct);
}

public interface IAiValidationHost : IAiHost
{
    Task<AiHostResult> ValidateAsync(string projectPath, CancellationToken ct);
}

public interface IAiBuildHost : IAiHost
{
    Task<AiHostResult> BuildAsync(string projectPath, bool restoreFirst, CancellationToken ct);
}

public interface IAiPackageHost : IAiHost
{
    Task<AiHostResult> PackageAsync(string projectPath, CancellationToken ct);
}

public interface IAiIdeHost : IAiHost
{
    Task<AiHostResult> OpenAsync(string projectPath, string ide, CancellationToken ct);
}

public interface IAiTerminalHost : IAiHost
{
    Task<AiHostResult> RunSafeAsync(string projectPath, string command, CancellationToken ct);
}

/// <summary>Signieren/Marketplace — in Phase 7.0 nicht verfügbar, nur vorbereitet.</summary>
public interface IAiMarketplaceHost : IAiHost
{
    Task<AiHostResult> NotAvailableAsync(CancellationToken ct);
}
