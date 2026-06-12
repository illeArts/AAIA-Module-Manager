using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public class ProcessRunner
{
    /// <summary>Führt einen Prozess aus und gibt stdout+stderr live per Callback zurück.</summary>
    public static async Task<int> RunAsync(
        string exe,
        string args,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            WorkingDirectory       = workingDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) onLine("ERR: " + e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }

    /// <summary>
    /// PowerShell-Skript ausführen (Windows) bzw. Shell-Skript via /bin/sh (macOS/Linux).
    /// Auf Windows: powershell.exe mit -File scriptPath.
    /// Auf macOS/Linux: /bin/sh scriptPath (Shell-Skripte müssen .sh sein).
    /// </summary>
    public static Task<int> RunPsAsync(
        string scriptPath,
        string extraArgs,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return RunAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {extraArgs}",
                workingDir, onLine, ct);
        }
        // macOS / Linux — Shell-Skript
        return RunAsync(
            "/bin/sh",
            $"\"{scriptPath}\" {extraArgs}",
            workingDir, onLine, ct);
    }

    /// <summary>
    /// Shell-Inline-Befehl ausführen (macOS/Linux: /bin/sh -c; Windows: cmd /c).
    /// Nützlich für Befehle die Pipes oder Shell-Builtins benötigen.
    /// </summary>
    public static Task<int> RunShellAsync(
        string command,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return RunAsync("cmd.exe", $"/c {command}", workingDir, onLine, ct);
        }
        return RunAsync("/bin/sh", $"-c \"{command}\"", workingDir, onLine, ct);
    }

    /// <summary>git-Befehl ausführen.</summary>
    public static Task<int> GitAsync(
        string gitArgs,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
        => RunAsync("git", gitArgs, workingDir, onLine, ct);

    /// <summary>gh CLI-Befehl ausführen.</summary>
    public static Task<int> GhAsync(
        string ghArgs,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
        => RunAsync("gh", ghArgs, workingDir, onLine, ct);

    /// <summary>dotnet-Befehl ausführen.</summary>
    public static Task<int> DotnetAsync(
        string dotnetArgs,
        string workingDir,
        Action<string> onLine,
        System.Threading.CancellationToken ct = default)
        => RunAsync("dotnet", dotnetArgs, workingDir, onLine, ct);

    // ── Captured-Variante (für Services die nur Success + Output brauchen) ────

    /// <summary>
    /// Führt einen Prozess aus, sammelt die komplette Ausgabe und gibt
    /// (Success, Output) zurück. Für Services die kein Streaming brauchen.
    /// workingDir: leer = aktuelles Verzeichnis.
    /// </summary>
    public static async Task<ProcessResult> RunCapturedAsync(
        string exe,
        string args,
        string workingDir = "",
        System.Threading.CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var wd = string.IsNullOrWhiteSpace(workingDir)
            ? System.IO.Directory.GetCurrentDirectory()
            : workingDir;

        var exit = await RunAsync(exe, args, wd, line => sb.AppendLine(line), ct);
        return new ProcessResult(exit == 0, sb.ToString());
    }
}

/// <summary>Ergebnis eines <see cref="ProcessRunner.RunCapturedAsync"/>-Aufrufs.</summary>
public sealed record ProcessResult(bool Success, string Output);
