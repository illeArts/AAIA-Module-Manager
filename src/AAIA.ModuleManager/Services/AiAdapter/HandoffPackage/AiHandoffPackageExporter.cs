using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

/// <summary>
/// Schreibt ein AiHandoffPackage auf Disk (Ordner oder ZIP).
///
/// Standard-Ausgabepfad:
///   %APPDATA%\AAIAModuleManager\ai-handoff\{extensionId}\{timestamp-target-type}\
/// </summary>
public static class AiHandoffPackageExporter
{
    // ── Pfad-Ermittlung ───────────────────────────────────────────────────────

    public static string GetDefaultBasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AAIAModuleManager",
            "ai-handoff");

    public static string GetPackageFolderPath(AiHandoffPackage pkg, string? basePath = null)
    {
        var root = basePath ?? GetDefaultBasePath();
        return Path.Combine(
            root,
            SanitizeName(pkg.ExtensionId),
            pkg.SuggestedFolderName);
    }

    // ── Ordner-Export ─────────────────────────────────────────────────────────

    /// <summary>
    /// Schreibt alle Paket-Dateien in einen Ordner.
    /// Gibt den tatsächlichen Ordnerpfad zurück.
    /// </summary>
    public static async Task<string> ExportToDirectoryAsync(
        AiHandoffPackage pkg,
        string?          basePath = null)
    {
        var folderPath = GetPackageFolderPath(pkg, basePath);
        Directory.CreateDirectory(folderPath);

        foreach (var file in pkg.Files)
        {
            var filePath = Path.Combine(folderPath, file.FileName);
            await File.WriteAllTextAsync(filePath, file.Content, Encoding.UTF8);
        }

        // README mit Kurzinfo
        await File.WriteAllTextAsync(
            Path.Combine(folderPath, "README.txt"),
            BuildReadme(pkg),
            Encoding.UTF8);

        return folderPath;
    }

    // ── ZIP-Export ────────────────────────────────────────────────────────────

    /// <summary>
    /// Exportiert das Paket als ZIP-Datei.
    /// Gibt den ZIP-Pfad zurück.
    /// </summary>
    public static async Task<string> ExportToZipAsync(
        AiHandoffPackage pkg,
        string?          targetDirectory = null)
    {
        var dir     = targetDirectory ?? GetDefaultBasePath();
        Directory.CreateDirectory(dir);

        var zipName = $"{pkg.SuggestedFolderName}.zip";
        var zipPath = Path.Combine(dir, zipName);

        // Existierende ZIP überschreiben
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var file in pkg.Files)
        {
            var entry   = archive.CreateEntry(file.FileName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(file.Content);
        }

        // README in ZIP
        var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        using var readmeStream = readmeEntry.Open();
        using var readmeWriter = new StreamWriter(readmeStream, Encoding.UTF8);
        await readmeWriter.WriteAsync(BuildReadme(pkg));

        return zipPath;
    }

    // ── Ordner öffnen ─────────────────────────────────────────────────────────

    /// <summary>
    /// Öffnet einen Ordner im System-Dateimanager (Explorer / Finder / Nautilus).
    /// </summary>
    public static void OpenInFileManager(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", folderPath);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", folderPath);
            else
                Process.Start("xdg-open", folderPath);
        }
        catch { /* Nicht kritisch — Pfad wurde bereits zurückgegeben */ }
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static string BuildReadme(AiHandoffPackage pkg)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"AAIA Module Manager — AI Handoff Package");
        sb.AppendLine($"========================================");
        sb.AppendLine();
        sb.AppendLine($"Erstellt:     {pkg.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Extension:    {pkg.ExtensionId}");
        sb.AppendLine($"Ziel-KI:      {pkg.Target}");
        sb.AppendLine($"Aufgabe:      {pkg.PackageType}");
        sb.AppendLine($"Kontext:      {pkg.ContextLevel}");
        sb.AppendLine($"Schema:       {pkg.SchemaVersion}");
        sb.AppendLine();
        sb.AppendLine("Enthaltene Dateien:");
        foreach (var f in pkg.Files)
            sb.AppendLine($"  {f.FileName,-32} — {f.Description}");
        sb.AppendLine("  README.txt                       — Diese Datei");
        sb.AppendLine();
        sb.AppendLine("Schnellstart:");
        sb.AppendLine("  1. handoff.md öffnen");
        sb.AppendLine("  2. Inhalt in die Ziel-KI einfügen");
        sb.AppendLine("  3. KI-Antwort in allowed-files.txt und validation-report.json vergleichen");
        sb.AppendLine();
        sb.AppendLine("SICHERHEITSHINWEIS:");
        sb.AppendLine("  Kein Quellcode, keine Keys, keine Secrets in diesem Paket.");
        sb.AppendLine("  Private Keys (.pem) dürfen NIEMALS geteilt werden.");

        if (pkg.SafetyWarnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Sicherheitswarnungen:");
            foreach (var w in pkg.SafetyWarnings)
                sb.AppendLine($"  ⚠ {w}");
        }

        return sb.ToString();
    }

    private static string SanitizeName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "unknown"
            : name.Replace('/', '-').Replace('\\', '-').Replace(':', '-');
}
