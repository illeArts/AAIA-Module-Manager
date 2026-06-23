using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime;
using AAIA.ModuleManager.Services.Ai.Runtime.Hosts;

namespace AAIA.ModuleManager.Services.Ai.Integration;

/// <summary>
/// Konkrete Module-Manager-Hosts (Increment 2). Bindet die AIR-Tool-Interfaces an die
/// vorhandenen Phase-6-Services an — KEINE neue Geschäftslogik, nur Übersetzung.
/// Eine Klasse implementiert alle Interfaces und wird über alle registriert.
/// </summary>
public sealed class ModuleManagerHosts :
    IAiStatusHost, IAiProjectHost, IAiFileHost, IAiPatchHost,
    IAiValidationHost, IAiBuildHost, IAiPackageHost, IAiIdeHost, IAiTerminalHost
{
    public string HostId => "module-manager";

    private const long MaxReadBytes = 256 * 1024;
    private const long MaxInlineContentBytes = 32 * 1024;
    private const int  MaxTerminalOutputChars = 20_000;

    private static readonly HashSet<string> ExcludedDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "packages", "node_modules" };

    private static readonly HashSet<string> BlockedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pem", ".pfx", ".key", ".env" };

    private static readonly HashSet<string> BlockedFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".env", ".env.local", ".env.development", "secrets.json", "usersecrets.json",
            "key-info.json", "signature-info.json", "appsettings.Development.json", "appsettings.Local.json"
        };

    // Terminal-Allowlist: exakt erlaubte Kommandos → (exe, args)
    private static readonly Dictionary<string, (string Exe, string Args)> TerminalAllowlist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet --info"]    = ("dotnet", "--info"),
            ["dotnet restore"]   = ("dotnet", "restore"),
            ["dotnet build"]     = ("dotnet", "build"),
            ["dotnet test"]      = ("dotnet", "test"),
            ["git status"]       = ("git", "status"),
            ["git diff"]         = ("git", "diff"),
            ["git log --oneline"]= ("git", "log --oneline"),
        };

    private static readonly string[] HardBlockedTokens =
        { "rm", "del", "format", "sudo", "chmod", "chown", "curl", "wget", "powershell", "-enc", "&&", "|", ";", ">" };

    private readonly IModuleManagerAiBridge _bridge;

    public ModuleManagerHosts(IModuleManagerAiBridge bridge) => _bridge = bridge;

    // ── Status ─────────────────────────────────────────────────────────────────
    public AaiaProjectStatus GetStatus() => _bridge.GetStatus();

    // ── Projekt ──────────────────────────────────────────────────────────────────
    public Task<AiHostResult> CreateProjectAsync(AiProjectCreateInput input, CancellationToken ct)
        => _bridge.CreateProjectAsync(input, ct);

    public Task<AiHostResult> GetContextAsync(string projectPath, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return NoProject();
        // Sicherer, secret-freier Ausschnitt.
        return Task.FromResult(AiHostResult.Ok(new
        {
            projectPath = info.ProjectDir,
            extensionId = info.ExtensionId,
            projectName = info.ProjectName,
            projectType = info.ProjectType.ToString(),
            csproj = info.CsprojPath is null ? null : Path.GetFileName(info.CsprojPath)
        }));
    }

    // ── Patch ────────────────────────────────────────────────────────────────────
    public Task<AiHostResult> ProposeAsync(AiPatchProposalInput input, CancellationToken ct)
        => _bridge.ApproveAndApplyPatchAsync(input, ct);

    // ── Validate ───────────────────────────────────────────────────────────────
    public async Task<AiHostResult> ValidateAsync(string projectPath, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return await NoProject();

        var result = await ProjectValidationOrchestrator.ValidateAsync(
            info.ProjectDir, info.ProjectType, aiProvider: null, projectName: info.ProjectName, ct: ct)
            .ConfigureAwait(false);

        return AiHostResult.Ok(new
        {
            success = !result.HasBlockers,
            overallStatus = result.OverallStatus,
            errors = result.ErrorCount,
            warnings = result.WarningCount,
            issues = result.Issues.Select(i => new
            {
                severity = i.Severity, title = i.Title, message = i.Message, category = i.Category
            }).ToArray()
        });
    }

    // ── Build ────────────────────────────────────────────────────────────────────
    public async Task<AiHostResult> BuildAsync(string projectPath, bool restoreFirst, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return await NoProject();

        var csproj = info.CsprojPath ?? FindCsproj(info.ProjectDir);
        if (csproj is null) return AiHostResult.Fail("Keine .csproj im Projekt gefunden.", "no_csproj");

        var result = await BuildRunnerService.RestoreAndBuildAsync(csproj, onOutput: null, ct: ct)
            .ConfigureAwait(false);

        return AiHostResult.Ok(new
        {
            success = result.Success,
            summary = result.SummaryLabel,
            issues = result.Issues.Select(i => new
            {
                code = i.Code, title = i.Title, file = i.FilePath, line = i.Line,
                explanation = i.HumanMessage, severity = i.Severity
            }).ToArray()
        });
    }

    // ── Package ──────────────────────────────────────────────────────────────────
    public async Task<AiHostResult> PackageAsync(string projectPath, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return await NoProject();

        var result = await ExtensionPackageService.CreatePackageAsync(
            info.ProjectDir, info.ProjectType, packageOutputDir: null, ct: ct).ConfigureAwait(false);

        return AiHostResult.Ok(new
        {
            success = result.Success,
            packageName = result.PackageName,
            packagePath = result.PackagePath,
            sizeBytes = result.SizeBytes,
            packageHash = result.PackageHash,
            includedFiles = result.IncludedFiles.Count,
            issues = result.Issues.Select(i => new { severity = i.Severity, title = i.Title, message = i.Message }).ToArray()
        });
    }

    // ── Files ────────────────────────────────────────────────────────────────────
    public Task<AiHostResult> ListAllowedAsync(string projectPath, bool includeContent, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return NoProject();

        var root = Path.GetFullPath(info.ProjectDir);
        var files = new List<object>();
        foreach (var path in SafeEnumerate(root))
        {
            var rel = Path.GetRelativePath(root, path);
            string? content = null;
            if (includeContent)
            {
                var fi = new FileInfo(path);
                if (fi.Length <= MaxInlineContentBytes && !IsBlocked(path))
                    content = File.ReadAllText(path);
            }
            files.Add(new { relativePath = rel.Replace('\\', '/'), size = new FileInfo(path).Length, content });
        }
        return Task.FromResult(AiHostResult.Ok(new { projectPath = root, files }));
    }

    public Task<AiHostResult> ReadAsync(string projectPath, string relativePath, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return NoProject();

        var root = Path.GetFullPath(info.ProjectDir);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        // Kein Path-Traversal: Zielpfad muss innerhalb des Projektordners liegen.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AiHostResult.Fail("Pfad außerhalb des Projekts.", "path_traversal"));
        if (!File.Exists(full))
            return Task.FromResult(AiHostResult.Fail("Datei nicht gefunden.", "not_found"));
        if (IsBlocked(full))
            return Task.FromResult(AiHostResult.Fail("Datei ist gesperrt (Secret/Key).", "blocked"));
        var fi = new FileInfo(full);
        if (fi.Length > MaxReadBytes)
            return Task.FromResult(AiHostResult.Fail($"Datei zu groß (> {MaxReadBytes / 1024} KB).", "too_large"));

        return Task.FromResult(AiHostResult.Ok(new
        {
            relativePath = relativePath.Replace('\\', '/'),
            size = fi.Length,
            content = File.ReadAllText(full)
        }));
    }

    // ── IDE ──────────────────────────────────────────────────────────────────────
    public Task<AiHostResult> OpenAsync(string projectPath, string ide, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return NoProject();

        var installed = IdeDetectionService.Detect().Where(i => i.Installed).ToList();
        if (installed.Count == 0)
            return Task.FromResult(AiHostResult.Fail("Keine IDE installiert/erkannt.", "no_ide"));

        IdeInfo? target = ide.ToLowerInvariant() switch
        {
            "default" => installed[0],
            "visualstudio" => installed.FirstOrDefault(i => i.Name.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase)),
            "vscode" => installed.FirstOrDefault(i => i.Name.Contains("Code", StringComparison.OrdinalIgnoreCase)),
            "rider" => installed.FirstOrDefault(i => i.Name.Contains("Rider", StringComparison.OrdinalIgnoreCase)),
            "xcode" => installed.FirstOrDefault(i => i.Name.Contains("Xcode", StringComparison.OrdinalIgnoreCase)),
            _ => installed[0]
        };
        if (target is null)
            return Task.FromResult(AiHostResult.Fail($"IDE '{ide}' nicht verfügbar.", "ide_unavailable"));

        IdeDetectionService.OpenInIde(target, info.ProjectDir);
        return Task.FromResult(AiHostResult.Ok(new { opened = target.Name, projectPath = info.ProjectDir }));
    }

    // ── Terminal (Allowlist) ─────────────────────────────────────────────────────
    public async Task<AiHostResult> RunSafeAsync(string projectPath, string command, CancellationToken ct)
    {
        var info = _bridge.ResolveProject(projectPath);
        if (info is null) return await NoProject();

        var normalized = string.Join(' ', (command ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (HardBlockedTokens.Any(t => normalized.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return AiHostResult.Fail("Kommando enthält ein hart blockiertes Element.", "blocked");
        if (!TerminalAllowlist.TryGetValue(normalized, out var cmd))
            return AiHostResult.Fail("Kommando steht nicht auf der Allowlist.", "not_allowlisted");

        var pr = await ProcessRunner.RunCapturedAsync(cmd.Exe, cmd.Args, info.ProjectDir, ct).ConfigureAwait(false);
        var output = pr.Output.Length > MaxTerminalOutputChars
            ? pr.Output[..MaxTerminalOutputChars] + "\n…(gekürzt)"
            : pr.Output;
        return AiHostResult.Ok(new { success = pr.Success, command = normalized, output });
    }

    // ── Helfer ───────────────────────────────────────────────────────────────────
    private static Task<AiHostResult> NoProject()
        => Task.FromResult(AiHostResult.Fail("Kein bekanntes/geladenes Projekt für diesen Pfad.", "no_project"));

    private static string? FindCsproj(string projectDir)
        => Directory.Exists(projectDir)
            ? Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(p => !IsInExcludedDir(p, projectDir))
            : null;

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (IsInExcludedDir(path, root)) continue;
            if (IsBlocked(path)) continue;
            yield return path;
        }
    }

    private static bool IsInExcludedDir(string path, string root)
    {
        var rel = Path.GetRelativePath(root, path);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirs.Contains(p));
    }

    private static bool IsBlocked(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path);
        return BlockedFileNames.Contains(name) || BlockedExtensions.Contains(ext);
    }
}
