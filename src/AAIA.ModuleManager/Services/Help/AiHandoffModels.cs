using System.Collections.Generic;

namespace AAIA.ModuleManager.Services.Help;

// ── Kontext — wird vom ViewModel befüllt ──────────────────────────────────────

/// <summary>
/// Aktueller Projektzustand für den AI Handoff.
/// Enthält KEINE Quelltexte — der Entwickler entscheidet selbst, was die KI sieht.
/// </summary>
public sealed class AiHandoffContext
{
    // Projekt-Identität
    public string ExtensionId  { get; set; } = "";
    public string DisplayName  { get; set; } = "";
    public string ProjectType  { get; set; } = "";

    // Pipeline-Flags
    public bool IsProjectCreated         { get; set; }
    public bool IsValidated              { get; set; }
    public bool HasValidationBlockers    { get; set; }
    public bool IsBuilt                  { get; set; }
    public bool IsPackaged               { get; set; }
    public bool IsInspected              { get; set; }
    public bool HasInspectionBlockers    { get; set; }
    public bool IsReleasePrepared        { get; set; }
    public bool IsSignaturePrepared      { get; set; }
    public bool IsEtwSigned              { get; set; }
    public bool IsEtwSignatureVerified   { get; set; }
    public bool CanContinueToMarketplace { get; set; }

    // Trust & Signatur
    public string  TrustLevel      { get; set; } = "Unsigned";
    public string? DeveloperEtwId  { get; set; }
    public string? KeyFingerprint  { get; set; }
    public string? SignedAtUtc     { get; set; }
    public string? KeyAlgorithm    { get; set; }
    public bool    EtwKeyExists    { get; set; }

    // Aktueller Schritt
    public string CurrentStep { get; set; } = "";

    // Fehler & Blocker (keine Quellcode-Inhalte)
    public List<string> ValidationErrors   { get; set; } = [];
    public List<string> InspectionBlockers { get; set; } = [];
    public List<string> SignatureErrors    { get; set; } = [];

    // Nächster logischer Schritt
    public string? NextStep { get; set; }
}

// ── Anfrage ───────────────────────────────────────────────────────────────────

public enum AiHandoffContextLevel
{
    Compact,   // Nur Status + Aufgabe (klein, schnell)
    Standard,  // Pipeline + Fehler + Aufgabe
    Full,      // Alles inkl. Trust-Modell und Signatur-Details
    Debug      // Alles + technische Details für Fehlersuche
}

public enum AiHandoffTarget
{
    ImplementNext,         // Nächste Phase implementieren lassen
    FixBuildError,         // Build-Fehler analysieren und beheben
    FixValidationError,    // Validierungsfehler erklären/beheben
    FixInspectionBlocker,  // Inspection-Blocker beheben
    DebugSignature,        // ETW-Signaturproblem debuggen
    ArchitectureReview,    // Architektur und Designentscheidungen reviewen
    PlanMarketplace,       // Phase 5 (Marketplace-Upload) planen
    FullProjectContext     // Vollständiger Kontext ohne konkrete Aufgabe
}

public enum AiHandoffProfile
{
    ChatGpt,  // ChatGPT / GPT-4o — gut für Planung und Architektur
    Claude,   // Claude — gut für große Implementierungen
    Codex,    // GitHub Copilot / Codex — gut für konkrete Code-Patches
    Gemini    // Google Gemini — gut für Reviews und Alternativen
}

public sealed class AiHandoffRequest
{
    public AiHandoffTarget       Target       { get; set; } = AiHandoffTarget.ImplementNext;
    public AiHandoffProfile      Profile      { get; set; } = AiHandoffProfile.Claude;
    public AiHandoffContextLevel ContextLevel { get; set; } = AiHandoffContextLevel.Standard;
}

// ── Ergebnis ─────────────────────────────────────────────────────────────────

public sealed class AiHandoffResult
{
    public bool                  Success      { get; set; }
    public string                Prompt       { get; set; } = "";
    public string                Title        { get; set; } = "";
    public AiHandoffProfile      Profile      { get; set; }
    public AiHandoffContextLevel ContextLevel { get; set; }
    public int                   CharCount    { get; set; }
    public string?               Error        { get; set; }
}
