using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Finale Checkliste vor dem Marketplace-Upload.
/// Gibt ValidationResult zurück — passt direkt in die bestehende Issue-Karten-UI.
///
/// Prüft: Manifest, Build-Output, Struktur, Sicherheit, Risiko, Versions-Hygiene.
/// </summary>
public static class PublishReadinessService
{
    public static ValidationResult Check(
        string        projectDir,
        NewProjectType projectType,
        BuildResult?  lastBuild = null)
    {
        var result = new ValidationResult();

        // ── 1. Manifest vollständig? ──────────────────────────────────────────
        // (Pflichtfelder + version + publisherId)
        CheckManifestForPublish(projectDir, result);

        // ── 2. Build-Status ───────────────────────────────────────────────────
        CheckBuildStatus(projectDir, lastBuild, result);

        // ── 3. Struktur ───────────────────────────────────────────────────────
        CheckStructure(projectDir, result);

        // ── 4. Secret-Scan ────────────────────────────────────────────────────
        result.Issues.AddRange(SecretScanService.Scan(projectDir));

        // ── 5. Risiko-Plausibilität ───────────────────────────────────────────
        CheckRiskPlausibility(projectDir, result);

        // ── 6. Versions-Hygiene ───────────────────────────────────────────────
        CheckVersionHygiene(projectDir, result);

        return result;
    }

    // ── 1. Manifest ───────────────────────────────────────────────────────────

    private static void CheckManifestForPublish(string projectDir, ValidationResult result)
    {
        var path = Path.Combine(projectDir, "aaia-manifest.json");
        if (!File.Exists(path))
        {
            result.Issues.Add(Error("Manifest", "Manifest fehlt",
                "aaia-manifest.json muss vorhanden und vollständig sein."));
            return;
        }

        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(path)); }
        catch
        {
            result.Issues.Add(Error("Manifest", "Manifest nicht parsierbar", "JSON-Syntaxfehler."));
            return;
        }

        RequireField(root, "publisherId", result,
            "Publisher-ID fehlt",
            "Das Feld 'publisherId' muss gesetzt sein bevor ein Modul veröffentlicht werden kann.");

        RequireField(root, "id", result, "Modul-ID fehlt", "Das Feld 'id' ist Pflicht.");
        RequireField(root, "displayName", result, "Anzeigename fehlt", "Das Feld 'displayName' ist Pflicht.");
        RequireField(root, "version", result, "Version fehlt", "Das Feld 'version' muss gesetzt sein.");
        RequireField(root, "description", result, "Beschreibung fehlt",
            "Eine Beschreibung ist für den Marketplace empfohlen.", isWarning: true);

        // ID-Stabilität: keine Leerzeichen, kein Uppercase
        var id = root?["id"]?.GetValue<string>() ?? "";
        if (!string.IsNullOrEmpty(id) && (id != id.ToLower() || id.Contains(' ')))
        {
            result.Issues.Add(Error("Manifest", "Modul-ID nicht stabil",
                $"Die ID '{id}' enthält Großbuchstaben oder Leerzeichen.\n" +
                "IDs müssen lowercase und bindestrich-separiert sein (z. B. 'aaia.module.mein-modul')."));
        }

        // Kind/Host-Kompatibilität
        var kind = root?["kind"]?.GetValue<string>() ?? "";
        var host = root?["host"]?.GetValue<string>() ?? "";
        if (kind == "Module" && host == "AAIAC")
        {
            result.Issues.Add(Warning("Manifest", "Kind/Host-Kombination ungewöhnlich",
                "Ein 'Module' läuft normalerweise auf AAIAS, nicht AAIAC.\n" +
                "Für Client-Erweiterungen verwende kind='Plugin'."));
        }
    }

    // ── 2. Build-Status ───────────────────────────────────────────────────────

    private static void CheckBuildStatus(string projectDir, BuildResult? lastBuild, ValidationResult result)
    {
        if (lastBuild is null)
        {
            // Prüfen ob Build-Output vorhanden (ggf. vorheriger Build)
            var hasBinaries = Directory.Exists(Path.Combine(projectDir, "bin")) &&
                              Directory.GetFiles(Path.Combine(projectDir, "bin"), "*.dll", SearchOption.AllDirectories).Length > 0;

            if (!hasBinaries)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = "Error",
                    Category = "Build",
                    Title    = "Build-Artefakte fehlen",
                    Message  = "Es wurden keine kompilierten Dateien in bin/ gefunden.\n" +
                               "Das Projekt muss erfolgreich gebaut worden sein.",
                    Actions  = [new() { Label = "Jetzt bauen", ActionId = "build-project", IsAutomatic = true }]
                });
            }
            else
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = "Info",
                    Category = "Build",
                    Title    = "Build nicht in dieser Sitzung ausgeführt",
                    Message  = "Build-Artefakte wurden gefunden, aber der Build wurde nicht in dieser Sitzung ausgeführt.\n" +
                               "Stelle sicher, dass die Binaries aktuell sind."
                });
            }
        }
        else if (!lastBuild.Success)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Build",
                Title    = "Build fehlgeschlagen",
                Message  = $"Der letzte Build hatte {lastBuild.ErrorCount} Fehler.\n" +
                           "Das Projekt muss fehlerfrei bauen bevor es veröffentlicht werden kann.",
                Actions  = [new() { Label = "Jetzt bauen", ActionId = "build-project", IsAutomatic = true }]
            });
        }
        else
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Info",
                Category = "Build",
                Title    = "Build erfolgreich",
                Message  = "Das Projekt hat erfolgreich gebaut."
            });
        }
    }

    // ── 3. Struktur ───────────────────────────────────────────────────────────

    private static void CheckStructure(string projectDir, ValidationResult result)
    {
        if (!File.Exists(Path.Combine(projectDir, "README.md")) &&
            !File.Exists(Path.Combine(projectDir, "README.txt")))
        {
            result.Issues.Add(Warning("Struktur", "README fehlt",
                "Ein README ist für den Marketplace empfohlen.",
                "add-readme"));
        }

        var hasLicense = File.Exists(Path.Combine(projectDir, "LICENSE")) ||
                         File.Exists(Path.Combine(projectDir, "LICENSE.md")) ||
                         File.Exists(Path.Combine(projectDir, "LICENSE.txt"));
        if (!hasLicense)
        {
            result.Issues.Add(Error("Struktur", "Keine Lizenzdatei",
                "Ohne Lizenz kann das Modul nicht im Marketplace veröffentlicht werden.",
                "add-license-mit"));
        }

        var hasIcon = File.Exists(Path.Combine(projectDir, "icon.png")) ||
                      File.Exists(Path.Combine(projectDir, "icon.svg")) ||
                      File.Exists(Path.Combine(projectDir, "Icon.png"));
        if (!hasIcon)
        {
            result.Issues.Add(Warning("Struktur", "Kein Icon",
                "Module ohne Icon werden mit Platzhalter im Marketplace angezeigt."));
        }
    }

    // ── 5. Risiko ─────────────────────────────────────────────────────────────

    private static void CheckRiskPlausibility(string projectDir, ValidationResult result)
    {
        var path = Path.Combine(projectDir, "aaia-manifest.json");
        if (!File.Exists(path)) return;

        try
        {
            var root        = JsonNode.Parse(File.ReadAllText(path));
            var permissions = root?["permissions"]?.AsArray();
            var network     = root?["networkTargets"]?.AsArray();

            // NetworkAccess deklariert aber keine networkTargets → Warnung
            if (permissions is not null &&
                permissions.ToString().Contains("NetworkAccess", System.StringComparison.OrdinalIgnoreCase) &&
                (network is null || network.Count == 0))
            {
                result.Issues.Add(Warning("Risiko", "NetworkAccess ohne Ziele",
                    "Die Berechtigung 'NetworkAccess' ist deklariert, aber 'networkTargets' ist leer.\n" +
                    "Füge die erlaubten Domains zu networkTargets hinzu (z. B. 'api.example.com')."));
            }
        }
        catch { /* Manifest-Fehler wird bereits von CheckManifestForPublish gemeldet */ }
    }

    // ── 6. Versions-Hygiene ───────────────────────────────────────────────────

    private static void CheckVersionHygiene(string projectDir, ValidationResult result)
    {
        var version = ManifestVersionService.GetVersion(projectDir);

        if (version.Contains("-beta") || version.Contains("-alpha") || version.Contains("-dev"))
        {
            result.Issues.Add(Warning("Version", $"Pre-Release-Version: {version}",
                $"Die Version '{version}' enthält einen Pre-Release-Suffix.\n" +
                "Für eine Marketplace-Veröffentlichung sollte die Version stabil sein (z. B. 1.0.0)."));
        }

        if (version.StartsWith("0."))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = "Info",
                Category = "Version",
                Title    = $"Version {version} — noch in Entwicklung",
                Message  = "Versionen unter 1.0.0 gelten als instabil.\n" +
                           "Das ist beim ersten Release okay — denk daran, auf 1.0.0 zu wechseln wenn du bereit bist.",
                Actions  =
                [
                    new() { Label = "Patch erhöhen (→ " + ManifestVersionService.SemVer.Default + ")", ActionId = "increase-patch" },
                    new() { Label = "Auf 1.0.0 setzen", ActionId = "set-version:1.0.0" }
                ]
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RequireField(
        JsonNode? root, string key, ValidationResult result,
        string title, string message, bool isWarning = false)
    {
        var val = root?[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(val))
            result.Issues.Add(isWarning
                ? Warning("Manifest", title, message, "open-manifest")
                : Error("Manifest", title, message, "open-manifest"));
    }

    private static ValidationIssue Error(string category, string title, string message, string? actionId = null) =>
        new()
        {
            Severity = "Error", Category = category, Title = title, Message = message,
            Actions  = actionId is null ? [] :
                       [new() { Label = "Öffnen", ActionId = actionId, IsAutomatic = true }]
        };

    private static ValidationIssue Warning(string category, string title, string message, string? actionId = null) =>
        new()
        {
            Severity = "Warning", Category = category, Title = title, Message = message,
            Actions  = actionId is null ? [] :
                       [new() { Label = "Öffnen", ActionId = actionId, IsAutomatic = true }]
        };
}
