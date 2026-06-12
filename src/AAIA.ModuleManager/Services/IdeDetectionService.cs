using System;
using System.Collections.Generic;
using System.IO;

namespace AAIA.ModuleManager.Services;

public sealed record IdeInfo(string Name, string? ExecutablePath, bool Installed);

/// <summary>
/// Detects locally installed IDEs relevant for AAIA module development.
/// Supports Windows and macOS via well-known path probing + PATH lookup.
/// </summary>
public static class IdeDetectionService
{
    public static List<IdeInfo> Detect()
    {
        var result = new List<IdeInfo>();

        result.Add(DetectVisualStudio());
        result.Add(DetectVsCode());
        result.Add(DetectRider());
        result.Add(DetectXcode()); // macOS only, returns not-installed on Windows

        return result;
    }

    // ── Visual Studio ──────────────────────────────────────────────────────────

    private static IdeInfo DetectVisualStudio()
    {
        if (!OperatingSystem.IsWindows())
            return new IdeInfo("Visual Studio", null, false);

        var candidates = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
            @"C:\Program Files\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe",
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return new IdeInfo("Visual Studio", path, true);
        }

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");
        if (File.Exists(vswhere))
            return new IdeInfo("Visual Studio", vswhere, true);

        return new IdeInfo("Visual Studio", null, false);
    }

    // ── VS Code ────────────────────────────────────────────────────────────────

    private static IdeInfo DetectVsCode()
    {
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Microsoft VS Code\Code.exe"),
                @"C:\Program Files\Microsoft VS Code\Code.exe",
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return new IdeInfo("VS Code", path, true);
            }
            var fromPath = FindOnPath("code.exe");
            if (fromPath != null) return new IdeInfo("VS Code", fromPath, true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Standard .app bundle + CLI-Wrapper via PATH
            var appExe = "/Applications/Visual Studio Code.app/Contents/MacOS/Electron";
            if (File.Exists(appExe)) return new IdeInfo("VS Code", appExe, true);

            var fromPath = FindOnPath("code");
            if (fromPath != null) return new IdeInfo("VS Code", fromPath, true);
        }
        else
        {
            var fromPath = FindOnPath("code");
            if (fromPath != null) return new IdeInfo("VS Code", fromPath, true);
        }

        return new IdeInfo("VS Code", null, false);
    }

    // ── JetBrains Rider ───────────────────────────────────────────────────────

    private static IdeInfo DetectRider()
    {
        if (OperatingSystem.IsWindows())
        {
            var jetbrainsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JetBrains", "Toolbox", "apps", "Rider");

            if (Directory.Exists(jetbrainsDir))
            {
                foreach (var dir in Directory.GetDirectories(jetbrainsDir, "*", SearchOption.AllDirectories))
                {
                    var exe = Path.Combine(dir, "bin", "rider64.exe");
                    if (File.Exists(exe)) return new IdeInfo("Rider", exe, true);
                }
            }

            var riderPath = FindOnPath("rider64.exe") ?? FindOnPath("rider.exe");
            if (riderPath != null) return new IdeInfo("Rider", riderPath, true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // JetBrains Toolbox auf macOS
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var toolboxRider = Path.Combine(home, "Library", "Application Support",
                "JetBrains", "Toolbox", "apps", "Rider");
            if (Directory.Exists(toolboxRider))
            {
                foreach (var dir in Directory.GetDirectories(toolboxRider, "*", SearchOption.AllDirectories))
                {
                    var exe = Path.Combine(dir, "Rider.app", "Contents", "MacOS", "rider");
                    if (File.Exists(exe)) return new IdeInfo("Rider", exe, true);
                }
            }
            // Direkte .app-Installation
            var appExe = "/Applications/Rider.app/Contents/MacOS/rider";
            if (File.Exists(appExe)) return new IdeInfo("Rider", appExe, true);

            var fromPath = FindOnPath("rider");
            if (fromPath != null) return new IdeInfo("Rider", fromPath, true);
        }
        else
        {
            var fromPath = FindOnPath("rider");
            if (fromPath != null) return new IdeInfo("Rider", fromPath, true);
        }

        return new IdeInfo("Rider", null, false);
    }

    // ── Xcode (macOS only) ────────────────────────────────────────────────────

    private static IdeInfo DetectXcode()
    {
        if (!OperatingSystem.IsMacOS()) return new IdeInfo("Xcode", null, false);

        var xcodePath = "/Applications/Xcode.app/Contents/MacOS/Xcode";
        return File.Exists(xcodePath)
            ? new IdeInfo("Xcode", xcodePath, true)
            : new IdeInfo("Xcode", null, false);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir, exe);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Opens the project folder in the given IDE if it is installed.</summary>
    public static void OpenInIde(IdeInfo ide, string projectPath)
    {
        if (!ide.Installed || ide.ExecutablePath is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = ide.ExecutablePath,
                Arguments       = $"\"{projectPath}\"",
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }
    }
}
