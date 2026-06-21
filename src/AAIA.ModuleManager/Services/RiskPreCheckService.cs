using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Wertet die 'permissions'-Liste im Manifest aus und berechnet das Risiko-Level.
/// Gibt ETW-verständliche Warnungen für gefährliche Berechtigungen.
/// </summary>
public static class RiskPreCheckService
{
    private const string ManifestFileName = "aaia-manifest.json";

    // Permission → (Risiko-Level, Titel, Erklärung)
    private static readonly Dictionary<string, (string Level, string Title, string Message)> KnownRisks = new()
    {
        ["FileSystemRead"]    = ("Info",    "Lesezugriff auf Dateisystem",
            "Dieses Modul kann Dateien auf dem Server lesen.\nRisikoLevel: Grün — unkritisch für reine Leseoperationen."),

        ["FileSystemWrite"]   = ("Warning", "Schreibzugriff auf Dateisystem",
            "Dieses Modul kann Dateien auf dem Server erstellen, verändern oder löschen.\n" +
            "Risiko: Gelb — erhöhtes Risiko. Wird im Marketplace entsprechend gekennzeichnet."),

        ["NetworkAccess"]     = ("Warning", "Netzwerkzugriff",
            "Dieses Modul kann HTTP-Anfragen nach außen senden.\n" +
            "Risiko: Gelb — Datenschutz-relevanter Zugriff. Externe Ziele sollten in 'networkTargets' angegeben sein."),

        ["ProcessExecution"]  = ("Warning", "Prozess-Start-Berechtigung",
            "Dieses Modul kann externe Prozesse starten.\n" +
            "Risiko: Orange — kritische Berechtigung. Muss beim Marketplace-Upload begründet werden."),

        ["SystemRegistry"]    = ("Warning", "Registry-Zugriff",
            "Dieses Modul greift auf die Windows-Registry zu.\n" +
            "Risiko: Orange — eingeschränkte Verfügbarkeit (nur Windows-Hosts)."),

        ["DatabaseAccess"]    = ("Warning", "Datenbankzugriff",
            "Dieses Modul greift direkt auf eine Datenbank zu.\n" +
            "Risiko: Gelb — Verbindungsparameter sollten als RequiredSecrets deklariert werden."),

        ["AdminPrivileges"]   = ("Error",   "Administrator-Rechte angefordert",
            "Dieses Modul fordert erhöhte Administrator-Rechte an.\n" +
            "Risiko: Rot — wird beim Marketplace-Upload einer verschärften Prüfung unterzogen.\n" +
            "Nur nutzen wenn absolut notwendig und gut dokumentiert."),

        ["UserImpersonation"] = ("Error",   "User-Impersonation angefordert",
            "Dieses Modul kann im Kontext anderer Benutzer handeln.\n" +
            "Risiko: Rot — sicherheitskritisch. Muss explizit vom Marketplace-Team freigegeben werden."),

        ["CryptographyKeys"]  = ("Warning", "Zugriff auf kryptografische Schlüssel",
            "Dieses Modul verwaltet oder nutzt kryptografische Schlüssel.\n" +
            "Risiko: Orange — Schlüsselmaterial darf nie im Code hardcodiert sein."),
    };

    public static List<ValidationIssue> Validate(string projectDir)
    {
        var issues = new List<ValidationIssue>();
        var path   = Path.Combine(projectDir, ManifestFileName);

        if (!File.Exists(path)) return issues; // ManifestValidationService meldet das bereits

        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(path)); }
        catch { return issues; }

        if (root is null) return issues;

        var permissions = root["permissions"]?.AsArray();
        if (permissions is null || permissions.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Info",
                Category = "Risiko",
                Title    = "Keine Berechtigungen deklariert",
                Message  =
                    "Das Modul fordert keine Sonderrechte an — ideal für einfache Module ohne Systemzugriff.\n" +
                    "Risiko-Level: Grün.",
            });
            return issues;
        }

        foreach (var permNode in permissions)
        {
            var perm = permNode?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(perm)) continue;

            if (KnownRisks.TryGetValue(perm, out var risk))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = risk.Level == "Error" ? "Error" : "Warning",
                    Category = "Risiko",
                    Title    = risk.Title,
                    Message  = risk.Message,
                    Actions  = [new() { Label = "Manifest öffnen", ActionId = "open-manifest", IsAutomatic = true }]
                });
            }
            else
            {
                // Unbekannte Permission → Info
                issues.Add(new ValidationIssue
                {
                    Severity = "Info",
                    Category = "Risiko",
                    Title    = $"Unbekannte Berechtigung: '{perm}'",
                    Message  = $"Die Berechtigung '{perm}' ist nicht im bekannten AAIA-Permission-Set.\n" +
                               "Prüfe ob der Name korrekt ist — unbekannte Permissions werden beim Upload abgelehnt.",
                    Actions  = [new() { Label = "Manifest öffnen", ActionId = "open-manifest", IsAutomatic = true }]
                });
            }
        }

        return issues;
    }
}
