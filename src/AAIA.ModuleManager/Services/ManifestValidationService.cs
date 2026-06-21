using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Prüft die aaia-manifest.json eines AAIA-Projekts.
/// Schlägt fehl wenn Pflichtfelder fehlen oder ungültig sind.
/// Bietet Auto-Fix: Manifest mit Standardwerten erzeugen.
/// </summary>
public static class ManifestValidationService
{
    private const string ManifestFileName = "aaia-manifest.json";

    private static readonly HashSet<string> ValidHosts =
        new(StringComparer.OrdinalIgnoreCase) { "AAIAS", "AAIAC", "Hybrid" };

    private static readonly HashSet<string> ValidKinds =
        new(StringComparer.OrdinalIgnoreCase) { "Module", "Plugin", "Connector", "LanguagePack" };

    // ── Hauptprüfung ──────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft die aaia-manifest.json im angegebenen Projektordner.
    /// projectDir: Wurzel des Projekts (wo aaia-manifest.json liegt).
    /// </summary>
    public static List<ValidationIssue> Validate(string projectDir)
    {
        var issues  = new List<ValidationIssue>();
        var path    = Path.Combine(projectDir, ManifestFileName);

        // ── 1. Existenz ───────────────────────────────────────────────────────

        if (!File.Exists(path))
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Manifest",
                Title    = "Manifest fehlt",
                Message  =
                    "Jedes AAIA-Modul braucht eine aaia-manifest.json im Projektwurzel-Ordner.\n" +
                    "Ohne Manifest kann das Modul nicht von AAIAS geladen, geprüft oder veröffentlicht werden.",
                Actions  =
                [
                    new() { Label = "Manifest erzeugen", ActionId = "create-manifest", IsAutomatic = true },
                    new() { Label = "Ordner öffnen",     ActionId = "open-folder" }
                ]
            });
            return issues; // restliche Prüfungen sinnlos ohne Datei
        }

        // ── 2. JSON parsen ────────────────────────────────────────────────────

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Error",
                Category = "Manifest",
                Title    = "Manifest ist kein gültiges JSON",
                Message  = "Die aaia-manifest.json enthält einen Syntaxfehler und kann nicht gelesen werden.\n" +
                           "Öffne die Datei und prüfe auf fehlende Kommas, Anführungszeichen oder Klammern.",
                Actions  =
                [
                    new() { Label = "Manifest öffnen", ActionId = "open-manifest", IsAutomatic = true }
                ]
            });
            return issues;
        }

        if (root is null)
        {
            issues.Add(Error("Manifest", "Manifest ist leer", "Die aaia-manifest.json ist leer."));
            return issues;
        }

        // ── 3. Pflichtfelder ─────────────────────────────────────────────────

        RequireString(root, "id",          "Modul-ID fehlt",
            "Das Feld 'id' ist die eindeutige Kennung des Moduls (z. B. 'aaia.module.mein-modul').\n" +
            "Ohne ID kann das Modul im Marketplace nicht identifiziert werden.",
            "open-manifest", issues);

        RequireString(root, "displayName", "Anzeigename fehlt",
            "Das Feld 'displayName' ist der Name der im Marketplace und in AAIAS angezeigt wird.",
            "open-manifest", issues);

        RequireString(root, "version",     "Versionsangabe fehlt",
            "Das Feld 'version' muss eine SemVer-Version enthalten (z. B. '1.0.0').",
            "open-manifest", issues);

        var host = StringField(root, "host");
        if (string.IsNullOrWhiteSpace(host))
        {
            issues.Add(Error("Manifest", "Host-Angabe fehlt",
                "Das Feld 'host' gibt an wo das Modul läuft: AAIAS (Server), AAIAC (Client) oder Hybrid.\n" +
                "Ohne Host-Angabe weiß AAIAS nicht, wo das Modul geladen werden soll."));
        }
        else if (!ValidHosts.Contains(host))
        {
            issues.Add(Error("Manifest", $"Ungültiger Host: '{host}'",
                $"'{host}' ist kein erlaubter Host-Wert. Erlaubt: AAIAS, AAIAC, Hybrid."));
        }

        var kind = StringField(root, "kind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            issues.Add(Error("Manifest", "Erweiterungstyp fehlt",
                "Das Feld 'kind' muss angeben was für eine Erweiterung das ist: Module, Plugin, Connector oder LanguagePack."));
        }
        else if (!ValidKinds.Contains(kind))
        {
            issues.Add(Error("Manifest", $"Ungültiger Typ: '{kind}'",
                $"'{kind}' ist kein erlaubter kind-Wert. Erlaubt: Module, Plugin, Connector, LanguagePack."));
        }

        RequireString(root, "pluginClass", "Einstiegspunkt (pluginClass) fehlt",
            "Das Feld 'pluginClass' gibt den C#-Klassenname an, den AAIAS als Einstiegspunkt lädt.\n" +
            "Ohne diesen Wert kann das Modul nicht aktiviert werden.",
            "open-manifest", issues, isWarning: kind == "Connector");

        // ── 4. Empfehlungen ───────────────────────────────────────────────────

        var description = StringField(root, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            issues.Add(Warning("Manifest", "Beschreibung fehlt",
                "Eine Beschreibung hilft ETW zu verstehen was dieses Modul tut.\n" +
                "Sie wird im Marketplace und in AAIAS angezeigt.",
                "open-manifest"));
        }

        var publisherId = StringField(root, "publisherId");
        if (string.IsNullOrWhiteSpace(publisherId))
        {
            issues.Add(Warning("Manifest", "Publisher-ID fehlt",
                "Das Feld 'publisherId' identifiziert den ETW als Autor des Moduls.\n" +
                "Ohne diese Angabe kann das Modul später nicht korrekt im Marketplace zugeordnet werden.",
                "open-manifest"));
        }

        // ── 5. Marketplace-Vorbereitung (Info) ───────────────────────────────

        var nuGetId = StringField(root, "nuGetPackageId");
        if (string.IsNullOrWhiteSpace(nuGetId))
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Info",
                Category = "Manifest",
                Title    = "NuGet-Paket-ID noch nicht gesetzt",
                Message  = "Das Feld 'nuGetPackageId' wird beim Paketieren benötigt.\n" +
                           "Kann jetzt leer bleiben — muss vor dem Upload gesetzt werden.",
                Actions  = [new() { Label = "Manifest öffnen", ActionId = "open-manifest", IsAutomatic = true }]
            });
        }

        return issues;
    }

    // ── Auto-Fix: Manifest erzeugen ───────────────────────────────────────────

    /// <summary>
    /// Erzeugt eine minimale aaia-manifest.json wenn keine vorhanden ist.
    /// </summary>
    public static async Task CreateDefaultManifestAsync(
        string projectDir,
        string id,
        string displayName,
        string host,
        string kind,
        string pluginClass,
        string publisherId = "")
    {
        var path     = Path.Combine(projectDir, ManifestFileName);
        var manifest = new
        {
            id,
            displayName,
            version            = "1.0.0",
            host,
            kind,
            pluginClass,
            publisherId,
            description        = "",
            supportedPlatforms = new[] { "all" },
            permissions        = Array.Empty<object>(),
            routes             = Array.Empty<object>(),
            requiredSecrets    = Array.Empty<string>(),
            networkTargets     = Array.Empty<string>(),
            licenseModel       = "Free"
        };

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, opts));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? StringField(JsonNode root, string key)
        => root[key]?.GetValue<string>();

    private static void RequireString(
        JsonNode root, string key, string title, string message,
        string actionId, List<ValidationIssue> issues,
        bool isWarning = false)
    {
        var val = StringField(root, key);
        if (string.IsNullOrWhiteSpace(val))
        {
            issues.Add(isWarning
                ? Warning("Manifest", title, message, actionId)
                : new ValidationIssue
                {
                    Severity = "Error",
                    Category = "Manifest",
                    Title    = title,
                    Message  = message,
                    Actions  = [new() { Label = "Manifest öffnen", ActionId = actionId, IsAutomatic = true }]
                });
        }
    }

    private static ValidationIssue Error(string category, string title, string message) =>
        new() { Severity = "Error", Category = category, Title = title, Message = message };

    private static ValidationIssue Warning(
        string category, string title, string message, string? actionId = null) =>
        new()
        {
            Severity = "Warning",
            Category = category,
            Title    = title,
            Message  = message,
            Actions  = actionId is null ? [] :
                       [new() { Label = "Öffnen", ActionId = actionId, IsAutomatic = true }]
        };
}
