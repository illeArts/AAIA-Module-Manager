using System.Collections.Generic;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter;

// ── Anfrage ───────────────────────────────────────────────────────────────────

/// <summary>
/// Einheitliche Anfrage an den zentralen AI-Adapter.
/// Der Adapter entscheidet Ziel, Modus und Kontextstufe selbst —
/// sofern nicht explizit vorgegeben.
/// </summary>
public sealed class AiAdapterRequest
{
    /// <summary>Welche KI soll helfen.</summary>
    public AiTarget Target { get; set; } = AiTarget.Claude;

    /// <summary>Ausführungsmodus — null = Adapter wählt automatisch.</summary>
    public AiExecutionMode? PreferredMode { get; set; }

    /// <summary>Was soll die KI tun.</summary>
    public AiHandoffTarget Task { get; set; } = AiHandoffTarget.ImplementNext;

    /// <summary>Wie viel Kontext bekommt die KI.</summary>
    public AiHandoffContextLevel ContextLevel { get; set; } = AiHandoffContextLevel.Standard;

    /// <summary>Aktueller Projektzustand (befüllt vom ViewModel).</summary>
    public AiHandoffContext ProjectContext { get; set; } = new();

    /// <summary>
    /// Freitext-Ergänzung die am Ende des Prompts angehängt wird.
    /// Wird durch SafetyPolicy gefiltert (keine Secrets).
    /// </summary>
    public string? UserNote { get; set; }
}

// ── Ergebnis ──────────────────────────────────────────────────────────────────

/// <summary>
/// Ergebnis einer Adapter-Anfrage — unabhängig vom gewählten Modus.
/// </summary>
public sealed class AiAdapterResult
{
    public bool             Success        { get; set; }
    public AiTarget         Target         { get; set; }
    public AiExecutionMode  Mode           { get; set; }
    public string           Prompt         { get; set; } = "";
    public string?          ApiResponse    { get; set; }
    public string?          Error          { get; set; }
    public string           Title          { get; set; } = "";
    public int              PromptLength   { get; set; }
    public bool             HasApiResponse => !string.IsNullOrEmpty(ApiResponse);

    /// <summary>Warnungen der SafetyPolicy (kein Fehler, aber der Nutzer sollte informiert werden).</summary>
    public List<string> SafetyWarnings { get; set; } = [];
}

// ── Capabilities ──────────────────────────────────────────────────────────────

/// <summary>
/// Beschreibt was ein bestimmtes Target/Modus-Kombination aktuell kann.
/// Wird für UI-Darstellung genutzt (Buttons aktivieren/deaktivieren, Tooltips).
/// </summary>
public sealed class AiAdapterCapabilities
{
    public AiTarget        Target            { get; set; }
    public AiExecutionMode EffectiveMode     { get; set; }
    public bool            CanHandoff        { get; set; } = true;
    public bool            CanApiDirect      { get; set; }
    public bool            IsApiKeyPresent   { get; set; }
    public string          ModeLabel         { get; set; } = "";
    public string          TargetLabel       { get; set; } = "";
    public string?         ModeHint          { get; set; }
}
