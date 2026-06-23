using System.Collections.Generic;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Baut AiHandoffContext-Objekte aus Rohzuständen.
/// Stellt sicher, dass die Kontextstufe (Compact / Standard / Full / Debug)
/// eingehalten wird und keine nicht freigegebenen Inhalte einfließen.
/// </summary>
public static class AiContextBuilder
{
    // ── Kompakter Kontext (nur Status + Fehler) ───────────────────────────────

    /// <summary>
    /// Baut einen minimalen Kontext — nur Identität + Fehler.
    /// Geeignet für schnelle Handoffs ohne viel Hintergrundinfo.
    /// </summary>
    public static AiHandoffContext BuildCompact(
        string extensionId,
        string displayName,
        string projectType,
        string currentStep,
        IEnumerable<string>? buildErrors = null)
    {
        return new AiHandoffContext
        {
            ExtensionId  = extensionId,
            DisplayName  = displayName,
            ProjectType  = projectType,
            CurrentStep  = currentStep,
            ValidationErrors = buildErrors is not null
                ? [..buildErrors]
                : []
        };
    }

    /// <summary>
    /// Reichert einen bestehenden Kontext mit Build-Fehlern aus dem letzten Build-Lauf an.
    /// Quelltexte bleiben draußen — nur Fehlermeldungen und betroffene Dateipfade.
    /// </summary>
    public static void EnrichWithBuildErrors(
        AiHandoffContext ctx,
        IEnumerable<string> errors,
        string? buildOutput = null)
    {
        ctx.ValidationErrors.Clear();
        ctx.ValidationErrors.AddRange(errors);

        // Build-Output wird NICHT automatisch einbezogen — zu groß und zu viel Quelltext.
        // Der Nutzer kann ihn manuell in UserNote einfügen.
    }

    /// <summary>
    /// Fügt Inspection-Blocker hinzu ohne andere Felder zu überschreiben.
    /// </summary>
    public static void EnrichWithInspectionBlockers(
        AiHandoffContext ctx,
        IEnumerable<string> blockers)
    {
        ctx.InspectionBlockers.Clear();
        ctx.InspectionBlockers.AddRange(blockers);
        ctx.HasInspectionBlockers = ctx.InspectionBlockers.Count > 0;
    }

    /// <summary>
    /// Fügt Signatur-Fehler hinzu.
    /// </summary>
    public static void EnrichWithSignatureErrors(
        AiHandoffContext ctx,
        IEnumerable<string> errors)
    {
        ctx.SignatureErrors.Clear();
        ctx.SignatureErrors.AddRange(errors);
    }

    // ── Angepasste Kontextstufen ──────────────────────────────────────────────

    /// <summary>
    /// Erzeugt eine Kopie des Kontexts die nur die für die angegebene Stufe
    /// relevanten Felder enthält. Verhindert versehentliches Durchreichen
    /// von Daten die für eine niedrigere Stufe nicht gedacht sind.
    /// </summary>
    public static AiHandoffContext ApplyContextLevel(
        AiHandoffContext ctx,
        AiHandoffContextLevel level)
    {
        // Compact: nur Identität + Fehler + aktueller Schritt
        if (level == AiHandoffContextLevel.Compact)
        {
            return new AiHandoffContext
            {
                ExtensionId      = ctx.ExtensionId,
                DisplayName      = ctx.DisplayName,
                ProjectType      = ctx.ProjectType,
                CurrentStep      = ctx.CurrentStep,
                ValidationErrors = ctx.ValidationErrors,
                InspectionBlockers = ctx.InspectionBlockers,
                SignatureErrors  = ctx.SignatureErrors,
                NextStep         = ctx.NextStep
            };
        }

        // Standard: zusätzlich Pipeline-Status
        if (level == AiHandoffContextLevel.Standard)
        {
            return new AiHandoffContext
            {
                ExtensionId              = ctx.ExtensionId,
                DisplayName              = ctx.DisplayName,
                ProjectType              = ctx.ProjectType,
                CurrentStep              = ctx.CurrentStep,
                TrustLevel               = ctx.TrustLevel,
                DeveloperEtwId           = ctx.DeveloperEtwId,
                IsProjectCreated         = ctx.IsProjectCreated,
                IsValidated              = ctx.IsValidated,
                HasValidationBlockers    = ctx.HasValidationBlockers,
                IsBuilt                  = ctx.IsBuilt,
                IsPackaged               = ctx.IsPackaged,
                IsInspected              = ctx.IsInspected,
                HasInspectionBlockers    = ctx.HasInspectionBlockers,
                IsReleasePrepared        = ctx.IsReleasePrepared,
                IsSignaturePrepared      = ctx.IsSignaturePrepared,
                EtwKeyExists             = ctx.EtwKeyExists,
                IsEtwSigned              = ctx.IsEtwSigned,
                IsEtwSignatureVerified   = ctx.IsEtwSignatureVerified,
                CanContinueToMarketplace = ctx.CanContinueToMarketplace,
                ValidationErrors         = ctx.ValidationErrors,
                InspectionBlockers       = ctx.InspectionBlockers,
                SignatureErrors          = ctx.SignatureErrors,
                NextStep                 = ctx.NextStep
            };
        }

        // Full / Debug: alles — Sicherheitscheck obliegt dem Aufrufer
        return ctx;
    }
}
