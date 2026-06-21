using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Prüft die Dateistruktur eines AAIA-Projekts:
/// README, LICENSE, Icons, HybridModule-Subdirs.
/// </summary>
public static class ExtensionStructureValidator
{
    private static readonly string[] ReadmeNames   = ["README.md", "README.txt", "README"];
    private static readonly string[] LicenseNames  = ["LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE", "LICENCE.md"];
    private static readonly string[] IconNames     = ["icon.png", "icon.svg", "icon.jpg", "Icon.png", "Icon.svg"];

    public static List<ValidationIssue> Validate(string projectDir, NewProjectType projectType)
    {
        var issues = new List<ValidationIssue>();

        // ── README ────────────────────────────────────────────────────────────

        if (!AnyExists(projectDir, ReadmeNames))
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Warning",
                Category = "Struktur",
                Title    = "README fehlt",
                Message  =
                    "Ein README.md erklärt was das Modul tut, wie es installiert wird und wofür es gedacht ist.\n" +
                    "Ohne README werden andere ETW (und Marketplace-Besucher) wenig über dein Modul erfahren.",
                Actions  =
                [
                    new() { Label = "README erzeugen", ActionId = "add-readme", IsAutomatic = true },
                    new() { Label = "Ordner öffnen",   ActionId = "open-folder" }
                ]
            });
        }

        // ── LICENSE ───────────────────────────────────────────────────────────

        if (!AnyExists(projectDir, LicenseNames))
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Warning",
                Category = "Struktur",
                Title    = "Keine Lizenzdatei",
                Message  =
                    "Ohne Lizenz ist die Nutzung des Moduls rechtlich unklar.\n" +
                    "Für kostenlose Module empfiehlt sich MIT. Marketplace erfordert eine Lizenzangabe.",
                Actions  =
                [
                    new() { Label = "MIT-Lizenz hinzufügen", ActionId = "add-license-mit", IsAutomatic = true },
                    new() { Label = "Ordner öffnen",          ActionId = "open-folder" }
                ]
            });
        }

        // ── Icon ──────────────────────────────────────────────────────────────

        if (!AnyExists(projectDir, IconNames))
        {
            issues.Add(new ValidationIssue
            {
                Severity = "Info",
                Category = "Struktur",
                Title    = "Kein Icon vorhanden",
                Message  =
                    "Module ohne Icon werden im Marketplace mit einem Platzhalter angezeigt.\n" +
                    "Empfohlen: icon.png, 256×256 Pixel.",
                Actions  = [new() { Label = "Ordner öffnen", ActionId = "open-folder" }]
            });
        }

        // ── HybridModule: Server/ + Client/ prüfen ────────────────────────────

        if (projectType == NewProjectType.HybridModule)
        {
            var serverDir = Path.Combine(projectDir, "Server");
            var clientDir = Path.Combine(projectDir, "Client");

            if (!Directory.Exists(serverDir))
                issues.Add(Error("Struktur", "Server/-Ordner fehlt",
                    "Ein HybridModule braucht einen Server/-Unterordner mit dem AAIAS-Teil des Moduls."));

            if (!Directory.Exists(clientDir))
                issues.Add(Error("Struktur", "Client/-Ordner fehlt",
                    "Ein HybridModule braucht einen Client/-Unterordner mit dem AAIAC-Plugin."));

            // Beide .csproj vorhanden?
            if (Directory.Exists(serverDir) && !Directory.GetFiles(serverDir, "*.csproj").Any())
                issues.Add(Error("Struktur", "Keine Server-.csproj gefunden",
                    "Im Server/-Ordner wurde keine .csproj-Datei gefunden. Das Projekt kann nicht gebaut werden."));

            if (Directory.Exists(clientDir) && !Directory.GetFiles(clientDir, "*.csproj").Any())
                issues.Add(Error("Struktur", "Keine Client-.csproj gefunden",
                    "Im Client/-Ordner wurde keine .csproj-Datei gefunden. Das Projekt kann nicht gebaut werden."));
        }
        else
        {
            // Reguläres Projekt: .csproj im Wurzelordner?
            if (projectType != NewProjectType.LanguagePack &&
                !Directory.GetFiles(projectDir, "*.csproj").Any())
            {
                issues.Add(Error("Struktur", "Keine .csproj-Datei gefunden",
                    "Im Projektordner wurde keine .csproj-Datei gefunden.\n" +
                    "Prüfe ob das Projekt korrekt angelegt wurde."));
            }
        }

        return issues;
    }

    // ── Auto-Fix: README + LICENSE ────────────────────────────────────────────

    public static async Task CreateReadmeAsync(
        string projectDir, string moduleName, string description = "")
    {
        var path    = Path.Combine(projectDir, "README.md");
        var content = $"""
            # {moduleName}

            {(string.IsNullOrWhiteSpace(description) ? "Beschreibe hier dein AAIA-Modul." : description)}

            ## Installation

            Dieses Modul kann über den AAIA Marketplace oder manuell über AAIAS installiert werden.

            ## Entwicklung

            Erstellt mit dem AAIA Module Manager.

            ## Lizenz

            Siehe [LICENSE](LICENSE).
            """;

        await File.WriteAllTextAsync(path, content);
    }

    public static async Task CreateMitLicenseAsync(string projectDir, string authorName = "")
    {
        var path    = Path.Combine(projectDir, "LICENSE");
        var year    = System.DateTime.UtcNow.Year;
        var author  = string.IsNullOrWhiteSpace(authorName) ? "AAIA Extension Developer" : authorName;

        var content = $"""
            MIT License

            Copyright (c) {year} {author}

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.
            """;

        await File.WriteAllTextAsync(path, content);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AnyExists(string dir, string[] names)
        => names.Any(n => File.Exists(Path.Combine(dir, n)));

    private static ValidationIssue Error(string category, string title, string message) =>
        new() { Severity = "Error", Category = category, Title = title, Message = message };
}
