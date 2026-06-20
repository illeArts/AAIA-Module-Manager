using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Führt dotnet restore + build aus und gibt ein BuildResult zurück.
/// Nutzt ProcessRunner.DotnetAsync (mit live output callback).
/// </summary>
public static class BuildRunnerService
{
    /// <summary>
    /// Restore + Build in einem Durchlauf.
    /// onOutput wird bei jeder Ausgabezeile aufgerufen (für Live-Anzeige).
    /// </summary>
    public static async Task<BuildResult> RestoreAndBuildAsync(
        string            csprojPath,
        Action<string>?   onOutput    = null,
        CancellationToken ct          = default)
    {
        var outputSb = new StringBuilder();

        void Collect(string line)
        {
            outputSb.AppendLine(line);
            onOutput?.Invoke(line);
        }

        var workDir = Path.GetDirectoryName(csprojPath) ?? "";

        // ── 1. Restore ───────────────────────────────────────────────────────────

        Collect("=== dotnet restore ===");
        var restoreExit = await ProcessRunner.DotnetAsync(
            $"restore \"{csprojPath}\"", workDir, Collect, ct);

        if (ct.IsCancellationRequested)
            return Cancelled(outputSb.ToString());

        // ── 2. Build ─────────────────────────────────────────────────────────────

        Collect("");
        Collect("=== dotnet build ===");
        var buildExit = await ProcessRunner.DotnetAsync(
            $"build \"{csprojPath}\" --no-restore", workDir, Collect, ct);

        var rawOutput = outputSb.ToString();
        var success   = buildExit == 0;
        var issues    = BuildErrorAnalyzerService.Analyze(rawOutput);

        // Wenn Issues gefunden aber ExitCode 0 → trotzdem Fehler vorhanden (seltener Fall)
        if (issues.Exists(i => i.IsError) && success)
            success = false;

        return new BuildResult
        {
            Success   = success,
            RawOutput = rawOutput,
            Issues    = issues
        };
    }

    /// <summary>
    /// Nur restore (ohne Build).
    /// </summary>
    public static async Task<BuildResult> RestoreOnlyAsync(
        string            csprojPath,
        Action<string>?   onOutput    = null,
        CancellationToken ct          = default)
    {
        var outputSb = new StringBuilder();

        void Collect(string line)
        {
            outputSb.AppendLine(line);
            onOutput?.Invoke(line);
        }

        var workDir = Path.GetDirectoryName(csprojPath) ?? "";
        var exitCode = await ProcessRunner.DotnetAsync(
            $"restore \"{csprojPath}\"", workDir, Collect, ct);

        var rawOutput = outputSb.ToString();
        var issues    = BuildErrorAnalyzerService.Analyze(rawOutput);

        return new BuildResult
        {
            Success   = exitCode == 0,
            RawOutput = rawOutput,
            Issues    = issues
        };
    }

    /// <summary>
    /// Holt dotnet SDK-Informationen (dotnet --info).
    /// Gibt den Roh-Output zurück.
    /// </summary>
    public static async Task<string> GetSdkInfoAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await ProcessRunner.DotnetAsync("--info", "", line => sb.AppendLine(line), ct);
        return sb.ToString();
    }

    private static BuildResult Cancelled(string rawOutput) => new()
    {
        Success   = false,
        RawOutput = rawOutput,
        Issues    =
        [
            new BuildIssue
            {
                Code           = "CANCELLED",
                Severity       = "Error",
                Title          = "Build abgebrochen",
                HumanMessage   = "Der Build-Vorgang wurde abgebrochen.",
                TechnicalDetails = "Operation cancelled by user."
            }
        ]
    };
}
