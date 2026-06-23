using System;
using System.Collections.Generic;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

// ── Package-Typ ───────────────────────────────────────────────────────────────

/// <summary>
/// Welches Problem soll das Paket lösen.
/// Steuert welche Dateien generiert werden und was der Prompt-Fokus ist.
/// </summary>
public enum AiHandoffPackageType
{
    BuildFix,
    ValidationFix,
    ManifestCreation,
    SignatureIssue,
    MarketplaceUploadIssue,
    NewExtensionPlanning,
    CodeReview,
    SecurityReview,
    DocumentationGeneration
}

// ── Einzelne Paket-Datei ──────────────────────────────────────────────────────

/// <summary>
/// Eine Datei innerhalb eines Handoff-Pakets.
/// Content ist bereits fertig serialisiert (UTF-8 String).
/// </summary>
public sealed class AiHandoffPackageFile
{
    /// <summary>Dateiname mit Endung, z. B. "handoff.md".</summary>
    public string FileName    { get; init; } = "";
    /// <summary>Vollständiger Inhalt als Text.</summary>
    public string Content     { get; init; } = "";
    /// <summary>Kurze Beschreibung für die UI-Vorschau.</summary>
    public string Description { get; init; } = "";
    /// <summary>True wenn diese Datei der KI-Hauptprompt ist.</summary>
    public bool   IsMainPrompt { get; init; }
}

// ── Vollständiges Paket ───────────────────────────────────────────────────────

/// <summary>
/// Ein strukturiertes AI Handoff Package — enthält alle Dateien die eine KI
/// braucht um eine bestimmte Aufgabe zu lösen.
/// </summary>
public sealed class AiHandoffPackage
{
    /// <summary>Eindeutige Paket-ID (Timestamp-basiert).</summary>
    public string PackageId    { get; init; } = "";
    /// <summary>Extension für die das Paket erstellt wurde.</summary>
    public string ExtensionId  { get; init; } = "";
    /// <summary>Zeitpunkt der Erstellung (UTC).</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    /// <summary>Paket-Version für spätere Migrations-Kompatibilität.</summary>
    public string SchemaVersion { get; init; } = "ai-handoff-v1";
    /// <summary>Ziel-KI.</summary>
    public AiTarget Target      { get; init; } = AiTarget.Claude;
    /// <summary>Aufgabentyp.</summary>
    public AiHandoffPackageType PackageType { get; init; } = AiHandoffPackageType.BuildFix;
    /// <summary>Kontext-Stufe mit der das Paket gebaut wurde.</summary>
    public AiHandoffContextLevel ContextLevel { get; init; } = AiHandoffContextLevel.Standard;
    /// <summary>Alle Dateien im Paket.</summary>
    public List<AiHandoffPackageFile> Files { get; init; } = [];
    /// <summary>Sicherheitswarnungen (keine Blocker, aber Info für den Nutzer).</summary>
    public List<string> SafetyWarnings { get; init; } = [];

    /// <summary>Schnellzugriff auf handoff.md.</summary>
    public AiHandoffPackageFile? MainPromptFile
        => Files.Find(f => f.IsMainPrompt);

    /// <summary>Menschenlesbarer Ordnername für Export.</summary>
    public string SuggestedFolderName =>
        $"{CreatedAtUtc:yyyy-MM-dd-HHmm}-{Target.ToString().ToLowerInvariant()}-{PackageType.ToString().ToLowerInvariant()}";
}

// ── Manifest-Modell (handoff.json) ────────────────────────────────────────────

/// <summary>
/// Maschinenlesbares Manifest — wird als handoff.json in das Paket geschrieben.
/// Dient als Metadaten-Einstiegspunkt für Connectors (Phase 6.2).
/// </summary>
public sealed class AiHandoffManifest
{
    public string PackageVersion    { get; init; } = "ai-handoff-v1";
    public string Target            { get; init; } = "";
    public string ContextLevel      { get; init; } = "";
    public string PackageType       { get; init; } = "";
    public string ExtensionId       { get; init; } = "";
    public string CreatedAtUtc      { get; init; } = "";
    public bool   ContainsSourceCode { get; init; } = false;
    public bool   ContainsSecrets   { get; init; } = false;
    public List<string> AllowedActions { get; init; } = [];
    public List<string> Files       { get; init; } = [];
}
