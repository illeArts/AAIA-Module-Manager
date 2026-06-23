using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter.HandoffPackage;

/// <summary>
/// Baut ein vollständiges AI Handoff Package aus Kontext + Anfrage.
///
/// Erzeugte Dateien:
///   handoff.md           — Hauptprompt für direkte KI-Nutzung
///   handoff.json         — Maschinenlesbares Manifest
///   project-summary.json — Projekt-Identität und Zustand
///   pipeline-state.json  — Pipeline-Flags (welche Schritte sind erledigt)
///   validation-report.json — Fehler und Blocker
///   allowed-files.txt    — Welche Dateien die KI sehen darf (leer = keine)
///
/// SICHERHEITSREGEL: Quelltexte, Keys und Secrets kommen NIEMALS in ein Paket.
/// </summary>
public static class AiHandoffPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Öffentliche API ───────────────────────────────────────────────────────

    public static AiHandoffPackage Build(
        AiHandoffContext       ctx,
        AiAdapterRequest       req,
        AiHandoffPackageType   packageType)
    {
        var packageId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{req.Target.ToString().ToLowerInvariant()}";

        // Kontext auf gewünschte Stufe reduzieren
        var safeCtx = AiContextBuilder.ApplyContextLevel(ctx, req.ContextLevel);

        // Hauptprompt via bestehendem Generator
        var handoffReq = new AiHandoffRequest
        {
            Target       = req.Task,
            Profile      = MapTarget(req.Target),
            ContextLevel = req.ContextLevel,
        };
        var promptResult = AiHandoffGeneratorService.Generate(safeCtx, handoffReq);
        var mainPrompt   = promptResult.Success ? promptResult.Prompt : $"# Fehler\n{promptResult.Error}";

        // Dateien zusammenbauen
        var files = new List<AiHandoffPackageFile>
        {
            BuildHandoffMd(mainPrompt),
            BuildHandoffJson(safeCtx, req, packageType, packageId),
            BuildProjectSummaryJson(safeCtx),
            BuildPipelineStateJson(safeCtx),
            BuildValidationReportJson(safeCtx),
            BuildAllowedFilesTxt(req)
        };

        // Safety-Check
        var combinedContent = mainPrompt;
        var warnings = AiSafetyPolicy.Validate(combinedContent, out bool isCritical);
        if (isCritical)
            warnings.Insert(0, "⛔ SafetyPolicy: Paket enthält möglicherweise verbotene Inhalte.");

        return new AiHandoffPackage
        {
            PackageId    = packageId,
            ExtensionId  = ctx.ExtensionId,
            CreatedAtUtc = DateTime.UtcNow,
            Target       = req.Target,
            PackageType  = packageType,
            ContextLevel = req.ContextLevel,
            Files        = files,
            SafetyWarnings = warnings
        };
    }

    // ── handoff.md ────────────────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildHandoffMd(string prompt) =>
        new()
        {
            FileName    = "handoff.md",
            Content     = prompt,
            Description = "Hauptprompt — direkt in die KI einfügen",
            IsMainPrompt = true
        };

    // ── handoff.json ──────────────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildHandoffJson(
        AiHandoffContext     ctx,
        AiAdapterRequest     req,
        AiHandoffPackageType packageType,
        string               packageId)
    {
        var manifest = new AiHandoffManifest
        {
            PackageVersion = "ai-handoff-v1",
            Target         = req.Target.ToString(),
            ContextLevel   = req.ContextLevel.ToString(),
            PackageType    = packageType.ToString(),
            ExtensionId    = ctx.ExtensionId,
            CreatedAtUtc   = DateTime.UtcNow.ToString("O"),
            ContainsSourceCode = false,
            ContainsSecrets    = false,
            AllowedActions = DetermineAllowedActions(packageType),
            Files =
            [
                "handoff.md",
                "handoff.json",
                "project-summary.json",
                "pipeline-state.json",
                "validation-report.json",
                "allowed-files.txt"
            ]
        };

        return new()
        {
            FileName    = "handoff.json",
            Content     = JsonSerializer.Serialize(manifest, JsonOpts),
            Description = "Maschinenlesbares Manifest (für Connectors/Phase 6.2)"
        };
    }

    // ── project-summary.json ──────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildProjectSummaryJson(AiHandoffContext ctx)
    {
        var summary = new
        {
            extensionId    = ctx.ExtensionId,
            displayName    = ctx.DisplayName,
            projectType    = ctx.ProjectType,
            currentStep    = ctx.CurrentStep,
            nextStep       = ctx.NextStep,
            trustLevel     = ctx.TrustLevel,
            developerEtwId = ctx.DeveloperEtwId,
            etwKeyExists   = ctx.EtwKeyExists,
            canContinueToMarketplace = ctx.CanContinueToMarketplace
        };

        return new()
        {
            FileName    = "project-summary.json",
            Content     = JsonSerializer.Serialize(summary, JsonOpts),
            Description = "Projekt-Identität und Trust-Status"
        };
    }

    // ── pipeline-state.json ───────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildPipelineStateJson(AiHandoffContext ctx)
    {
        var state = new
        {
            isProjectCreated         = ctx.IsProjectCreated,
            isValidated              = ctx.IsValidated,
            hasValidationBlockers    = ctx.HasValidationBlockers,
            isBuilt                  = ctx.IsBuilt,
            isPackaged               = ctx.IsPackaged,
            isInspected              = ctx.IsInspected,
            hasInspectionBlockers    = ctx.HasInspectionBlockers,
            isReleasePrepared        = ctx.IsReleasePrepared,
            isSignaturePrepared      = ctx.IsSignaturePrepared,
            isEtwSigned              = ctx.IsEtwSigned,
            isEtwSignatureVerified   = ctx.IsEtwSignatureVerified,
            canContinueToMarketplace = ctx.CanContinueToMarketplace
        };

        return new()
        {
            FileName    = "pipeline-state.json",
            Content     = JsonSerializer.Serialize(state, JsonOpts),
            Description = "Pipeline-Fortschritt (welche Schritte abgeschlossen)"
        };
    }

    // ── validation-report.json ────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildValidationReportJson(AiHandoffContext ctx)
    {
        var report = new
        {
            validationErrors   = ctx.ValidationErrors,
            inspectionBlockers = ctx.InspectionBlockers,
            signatureErrors    = ctx.SignatureErrors,
            totalIssues        = ctx.ValidationErrors.Count
                               + ctx.InspectionBlockers.Count
                               + ctx.SignatureErrors.Count
        };

        return new()
        {
            FileName    = "validation-report.json",
            Content     = JsonSerializer.Serialize(report, JsonOpts),
            Description = "Fehler und Blocker aus Validierung, Inspektion und Signatur"
        };
    }

    // ── allowed-files.txt ─────────────────────────────────────────────────────

    private static AiHandoffPackageFile BuildAllowedFilesTxt(AiAdapterRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Erlaubte Dateien für KI-Zugriff");
        sb.AppendLine("# Leer = keine Quelltexte freigegeben (Standard)");
        sb.AppendLine("# Format: relativer Pfad zum Projekt-Root, eine Datei pro Zeile");
        sb.AppendLine("#");
        sb.AppendLine("# Beispiele:");
        sb.AppendLine("#   src/MyExtension/aaia-extension.json");
        sb.AppendLine("#   src/MyExtension/MyService.cs");
        sb.AppendLine("#");
        sb.AppendLine("# SICHERHEITSHINWEIS:");
        sb.AppendLine("#   Private Keys (.pem), .env, config.json mit Passwörtern");
        sb.AppendLine("#   dürfen hier NIEMALS eingetragen werden.");
        sb.AppendLine();

        // Für BuildFix/ValidationFix standardmäßig Manifest vorschlagen
        if (req.Task is AiHandoffTarget.FixValidationError or AiHandoffTarget.FixInspectionBlocker)
        {
            sb.AppendLine("# Vorschlag für diesen Aufgabentyp:");
            sb.AppendLine("# src/<ExtensionName>/aaia-extension.json");
        }

        return new()
        {
            FileName    = "allowed-files.txt",
            Content     = sb.ToString(),
            Description = "Vom Entwickler freigegebene Dateien (manuell ausfüllen)"
        };
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    private static List<string> DetermineAllowedActions(AiHandoffPackageType packageType) =>
        packageType switch
        {
            AiHandoffPackageType.BuildFix or
            AiHandoffPackageType.ValidationFix or
            AiHandoffPackageType.SignatureIssue or
            AiHandoffPackageType.MarketplaceUploadIssue =>
                ["analyze", "explain", "suggestPatch"],

            AiHandoffPackageType.ManifestCreation or
            AiHandoffPackageType.NewExtensionPlanning =>
                ["analyze", "explain", "suggestPatch", "generateFiles"],

            AiHandoffPackageType.CodeReview or
            AiHandoffPackageType.SecurityReview =>
                ["analyze", "explain"],

            AiHandoffPackageType.DocumentationGeneration =>
                ["analyze", "explain", "generateFiles"],

            _ => ["analyze", "explain"]
        };

    private static AiHandoffProfile MapTarget(AiTarget target) => target switch
    {
        AiTarget.ChatGPT => AiHandoffProfile.ChatGpt,
        AiTarget.Claude  => AiHandoffProfile.Claude,
        AiTarget.Gemini  => AiHandoffProfile.Gemini,
        AiTarget.Codex   => AiHandoffProfile.Codex,
        _                => AiHandoffProfile.Claude
    };
}
