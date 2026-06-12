using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AAIA.ModuleManager.Services;

namespace AAIA.ModuleManager.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        BuildContent();
    }

    private void Close_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    // ─────────────────────────────────────────────────────────────────────────
    //  Content builder — bilingual, driven by LanguageService.Current
    // ─────────────────────────────────────────────────────────────────────────

    private bool IsDe => LanguageService.Current == AppLanguage.De;

    private void BuildContent()
    {
        var sections = IsDe ? BuildDe() : BuildEn();
        foreach (var s in sections)
            HelpContent.Children.Add(s);
    }

    // ── German ───────────────────────────────────────────────────────────────

    private List<Control> BuildDe() => new()
    {
        Section("🔧  SDK / Contracts",
            "Zeigt alle verfügbaren AAIA SDK-Pakete aus dem konfigurierten SDK-Pfad an.",
            new[] {
                ("NuGet installieren",   "Installiert das gewählte Paket als NuGet-Referenz in dein Projekt."),
                ("Lokale Referenz",      "Fügt das SDK-Projekt direkt als Projektreferenz hinzu (kein NuGet-Feed nötig)."),
                ("Verträge einsehen",    "Zeigt die öffentlichen Interfaces und Typen des SDK-Pakets an.")
            }),

        Section("📦  Mein Modul",
            "Erstellt und verwaltet dein eigenes AAIA-Modul / Plugin.",
            new[] {
                ("Neues Modul erstellen", "Generiert ein vollständiges Modul-Projekt aus einer Vorlage — inkl. aaia-extension.json, .csproj und Beispiel-Handler."),
                ("Build starten",         "Führt dotnet build aus und zeigt Fehler direkt in der Ausgabe an."),
                ("Metadaten bearbeiten",  "Öffnet aaia-extension.json zum Bearbeiten von ID, Name, Version und Kategorie.")
            }),

        Section("🌐  Registry",
            "Verwaltet die lokale Extension-Registry — ein Git-Repo mit allen veröffentlichten Modulen.",
            new[] {
                ("Registry klonen",    "Klont das Registry-Repo in den konfigurierten Pfad."),
                ("Modul eintragen",    "Fügt dein Modul als neuen Eintrag in die Registry ein und erstellt einen Pull Request."),
                ("Status prüfen",      "Zeigt den aktuellen Git-Status der Registry an.")
            }),

        Section("🧪  Tester  (V2-Feature)",
            "Live-Testumgebung: verbindet sich mit einem laufenden AAIAS-Server und ermöglicht vollständiges End-to-End-Testen ohne manuelles Deployen.",
            new[] {
                ("AAIAS verbinden",       "Verbindet sich per HTTP mit dem AAIAS-Server (Standard: http://localhost:5174). Benötigt Benutzername und Passwort. Nach erfolgreichem Login wird Developer Mode geprüft."),
                ("Projekt laden",         "Wähle den Ordner deines Moduls aus. AAIA Module Manager liest aaia-extension.json (ID, Name) und .csproj (Version) automatisch aus. FileSystemWatcher überwacht alle .cs/.csproj/.json/.axaml-Änderungen."),
                ("Build & Publish",       "Führt dotnet publish (Release, self-contained) aus. Die Ausgabe wird live im Build-Ausgabe-Bereich angezeigt."),
                ("Install + Enable",      "Installiert das gebaute Modul per AAIAS-API und aktiviert es automatisch (allowUnsigned für Dev-Builds)."),
                ("Hot-Reload",            "Lädt das aktive Modul im laufenden AAIAS-Server neu ohne Neustart. Nur im Developer Mode verfügbar."),
                ("WorkOrder simulieren",  "Sendet einen beliebigen WorkOrder-JSON-Payload an das Modul und zeigt die Antwort strukturiert an. Ideal zum Testen der AAIAS-Handler-Logik."),
                ("Log-Stream",            "Abonniert den SSE-Endpunkt /api/dev/logs/stream und zeigt Live-Logs des AAIAS-Servers in Echtzeit an."),
                ("IDE öffnen",            "Erkennt installierte IDEs (Visual Studio 2019/2022, VS Code, Rider) und öffnet das Projekt direkt darin.")
            }),

        Section("⚙️  Setup",
            "Konfiguration der Umgebung — Pfade, Abhängigkeiten und AAIAS-Verbindungsdaten.",
            new[] {
                ("Abhängigkeiten prüfen",  "Prüft ob git, gh CLI und .NET SDK verfügbar sind. Zeigt Versions-Info oder Fehlermeldungen."),
                ("gh CLI installieren",    "Installiert die GitHub CLI: Windows via winget, macOS via Homebrew (brew muss vorhanden sein)."),
                ("GitHub Login",           "Führt gh auth login --web aus und öffnet den Browser für die OAuth-Authentifizierung."),
                ("Pfade konfigurieren",    "SDK-Pfad, Monorepo-Pfad und Registry-Pfad können über Ordner-Picker gesetzt werden."),
                ("AAIAS-Verbindung",       "URL, Benutzername und Passwort für den AAIAS-Server. Diese Daten werden in der Konfigurationsdatei gespeichert."),
                ("Konfiguration speichern","Speichert alle Einstellungen. Windows: %AppData%\\AAIAModuleManager\\config.json | macOS: ~/Library/Application Support/AAIAModuleManager/config.json")
            }),

        Note("💡 Tipp: Im Tester-Tab mit Developer Mode und Log-Stream bekommst du das gleiche Erlebnis wie direkt in der AAIAS-Entwicklungsumgebung — ohne die App zu verlassen.")
    };

    // ── English ───────────────────────────────────────────────────────────────

    private List<Control> BuildEn() => new()
    {
        Section("🔧  SDK / Contracts",
            "Shows all available AAIA SDK packages from the configured SDK path.",
            new[] {
                ("Install NuGet",      "Installs the selected package as a NuGet reference into your project."),
                ("Local reference",    "Adds the SDK project directly as a project reference (no NuGet feed required)."),
                ("Browse contracts",   "Shows the public interfaces and types of the SDK package.")
            }),

        Section("📦  My Module",
            "Create and manage your own AAIA module / plugin.",
            new[] {
                ("Create new module",   "Generates a complete module project from a template — including aaia-extension.json, .csproj and sample handler."),
                ("Start build",         "Runs dotnet build and shows errors directly in the output area."),
                ("Edit metadata",       "Opens aaia-extension.json to edit ID, name, version and category.")
            }),

        Section("🌐  Registry",
            "Manages the local extension registry — a Git repo with all published modules.",
            new[] {
                ("Clone registry",  "Clones the registry repo into the configured path."),
                ("Register module", "Adds your module as a new entry to the registry and creates a pull request."),
                ("Check status",    "Shows the current Git status of the registry.")
            }),

        Section("🧪  Tester  (V2 Feature)",
            "Live test environment: connects to a running AAIAS server and enables full end-to-end testing without manual deployment.",
            new[] {
                ("Connect to AAIAS",     "Connects via HTTP to the AAIAS server (default: http://localhost:5174). Requires username and password. After successful login, Developer Mode is checked."),
                ("Load project",         "Select your module's folder. AAIA Module Manager reads aaia-extension.json (ID, name) and .csproj (version) automatically. FileSystemWatcher monitors all .cs/.csproj/.json/.axaml changes."),
                ("Build & Publish",      "Runs dotnet publish (Release, self-contained). Output is shown live in the build output area."),
                ("Install + Enable",     "Installs the built module via the AAIAS API and enables it automatically (allowUnsigned for dev builds)."),
                ("Hot-Reload",           "Reloads the active module in the running AAIAS server without restart. Only available in Developer Mode."),
                ("Simulate WorkOrder",   "Sends an arbitrary WorkOrder JSON payload to the module and displays the response in a structured view. Ideal for testing AAIAS handler logic."),
                ("Log stream",           "Subscribes to the SSE endpoint /api/dev/logs/stream and shows live logs from the AAIAS server in real time."),
                ("Open in IDE",          "Detects installed IDEs (Visual Studio 2019/2022, VS Code, Rider) and opens the project directly in them.")
            }),

        Section("⚙️  Setup",
            "Environment configuration — paths, dependencies and AAIAS connection details.",
            new[] {
                ("Check dependencies",   "Checks whether git, gh CLI and .NET SDK are available. Shows version info or error messages."),
                ("Install gh CLI",       "Installs the GitHub CLI: Windows via winget, macOS via Homebrew (brew must be installed)."),
                ("GitHub login",         "Runs gh auth login --web and opens the browser for OAuth authentication."),
                ("Configure paths",      "SDK path, monorepo path and registry path can be set via folder pickers."),
                ("AAIAS connection",     "URL, username and password for the AAIAS server. Credentials are stored in the local configuration file."),
                ("Save configuration",   "Saves all settings. Windows: %AppData%\\AAIAModuleManager\\config.json | macOS: ~/Library/Application Support/AAIAModuleManager/config.json")
            }),

        Note("💡 Tip: In the Tester tab with Developer Mode and log streaming you get the same experience as working directly inside the AAIAS dev environment — without leaving this app.")
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Control Section(string title, string intro,
        IEnumerable<(string label, string desc)> items)
    {
        var panel = new StackPanel { Spacing = 0, Margin = new(0, 0, 0, 20) };

        // Section header
        var hdr = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1a2040")),
            CornerRadius = new(6, 6, 0, 0),
            Padding = new(14, 8),
            Margin = new(0, 0, 0, 1)
        };
        hdr.Child = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#a0b4d0"))
        };
        panel.Children.Add(hdr);

        // Intro text
        var introBlock = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#111827")),
            Padding = new(14, 8),
            Margin = new(0, 0, 0, 1)
        };
        introBlock.Child = new TextBlock
        {
            Text = intro,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#c0cce0")),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(introBlock);

        // Feature rows
        foreach (var (label, desc) in items)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0d1117")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1e2a3a")),
                BorderThickness = new(0, 0, 0, 1),
                Padding = new(14, 7)
            };

            var rowGrid = new Grid { ColumnDefinitions = new("160,*") };

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#7eb8f7")),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new(0, 0, 12, 0)
            };
            Grid.SetColumn(labelBlock, 0);
            rowGrid.Children.Add(labelBlock);

            var descBlock = new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#8892a4")),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(descBlock, 1);
            rowGrid.Children.Add(descBlock);

            row.Child = rowGrid;
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Note(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1a2520")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a5a3a")),
            BorderThickness = new(0, 0, 0, 0),
            CornerRadius = new(6),
            Padding = new(14, 10),
            Margin = new(0, 0, 0, 20),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#6fcf97")),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }
}
