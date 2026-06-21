using System;
using System.Collections.Generic;
using System.Text;

namespace AAIA.ModuleManager.Services.Help;

/// <summary>
/// Erzeugt strukturierte KI-Handoff-Prompts aus dem aktuellen Pipeline-Zustand.
///
/// SICHERHEITSREGEL (unveränderlich):
///   Quelltexte werden NIEMALS automatisch in den Prompt aufgenommen.
///   Der Entwickler entscheidet selbst, was er der KI zeigt.
///   API-Keys und Private Keys kommen NIEMALS in den Prompt.
/// </summary>
public static class AiHandoffGeneratorService
{
    // ── Öffentliche API ───────────────────────────────────────────────────────

    public static AiHandoffResult Generate(AiHandoffContext ctx, AiHandoffRequest req)
    {
        try
        {
            var sb = new StringBuilder();

            AppendProfileHeader(sb, req.Profile, req.Target);
            sb.AppendLine();

            if (req.ContextLevel >= AiHandoffContextLevel.Standard)
                AppendProjectContext(sb, ctx);

            if (req.ContextLevel >= AiHandoffContextLevel.Standard)
                AppendPipelineState(sb, ctx);

            if (req.ContextLevel >= AiHandoffContextLevel.Full)
                AppendTrustInfo(sb, ctx);

            // Fehler immer einbeziehen — auch in Compact
            if (HasErrors(ctx))
                AppendErrors(sb, ctx);

            AppendGoal(sb, ctx, req);
            AppendRules(sb, req.Profile, req.ContextLevel);

            if (req.ContextLevel == AiHandoffContextLevel.Debug)
                AppendDebugExtras(sb, ctx);

            AppendExpectedOutput(sb, req);

            var prompt = sb.ToString().TrimEnd();
            return new AiHandoffResult
            {
                Success      = true,
                Prompt       = prompt,
                Title        = BuildTitle(req.Target, req.Profile, ctx),
                Profile      = req.Profile,
                ContextLevel = req.ContextLevel,
                CharCount    = prompt.Length
            };
        }
        catch (Exception ex)
        {
            return new AiHandoffResult
            {
                Success = false,
                Error   = ex.Message,
                Prompt  = ""
            };
        }
    }

    // ── Profile-Header ────────────────────────────────────────────────────────

    private static void AppendProfileHeader(StringBuilder sb, AiHandoffProfile profile, AiHandoffTarget target)
    {
        var emoji = TargetEmoji(target);
        var targetLabel = TargetLabel(target);

        sb.AppendLine($"# {emoji} AAIA Module Manager — {targetLabel}");
        sb.AppendLine();

        switch (profile)
        {
            case AiHandoffProfile.Claude:
                sb.AppendLine("Du bist ein erfahrener .NET-Entwickler und kennst Avalonia 11 sowie das MVVM-Pattern (CommunityToolkit.Mvvm). Bitte lese alle Kontext-Abschnitte sorgfältig, bevor du antwortest.");
                break;
            case AiHandoffProfile.ChatGpt:
                sb.AppendLine("You are a senior .NET developer familiar with Avalonia 11 and the MVVM pattern (CommunityToolkit.Mvvm). Please read all context sections before responding.");
                break;
            case AiHandoffProfile.Codex:
                sb.AppendLine("// Context: AAIA Module Manager — Avalonia 11, CommunityToolkit.Mvvm, C# 12");
                sb.AppendLine("// Task: See ## Goal section below");
                break;
            case AiHandoffProfile.Gemini:
                sb.AppendLine("You are reviewing an Avalonia 11 desktop application using the MVVM pattern. The codebase uses CommunityToolkit.Mvvm source generators. Please read all context before answering.");
                break;
        }
    }

    // ── Projektkontext ────────────────────────────────────────────────────────

    private static void AppendProjectContext(StringBuilder sb, AiHandoffContext ctx)
    {
        sb.AppendLine();
        sb.AppendLine("## Projektkontext");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(ctx.ExtensionId))
            sb.AppendLine($"- **Extension-ID:** `{ctx.ExtensionId}`");

        if (!string.IsNullOrEmpty(ctx.DisplayName))
            sb.AppendLine($"- **Name:** {ctx.DisplayName}");

        if (!string.IsNullOrEmpty(ctx.ProjectType))
            sb.AppendLine($"- **Projekttyp:** {ctx.ProjectType}");

        if (!string.IsNullOrEmpty(ctx.DeveloperEtwId))
            sb.AppendLine($"- **ETW-Entwickler-ID:** `{ctx.DeveloperEtwId}`");

        if (!string.IsNullOrEmpty(ctx.CurrentStep))
            sb.AppendLine($"- **Aktueller Schritt:** {ctx.CurrentStep}");

        if (!string.IsNullOrEmpty(ctx.TrustLevel))
            sb.AppendLine($"- **Trust-Level:** `{ctx.TrustLevel}`");
    }

    // ── Pipeline-Zustand ──────────────────────────────────────────────────────

    private static void AppendPipelineState(StringBuilder sb, AiHandoffContext ctx)
    {
        sb.AppendLine();
        sb.AppendLine("## Pipeline-Stand");
        sb.AppendLine();

        sb.AppendLine(Check(ctx.IsProjectCreated)       + " Projekt erstellt");
        sb.AppendLine(Check(ctx.IsValidated)            + " Validierung");
        if (ctx.HasValidationBlockers)
            sb.AppendLine("  ⚠️ Validierungsblocker vorhanden");
        sb.AppendLine(Check(ctx.IsBuilt)                + " Build");
        sb.AppendLine(Check(ctx.IsPackaged)             + " Paket erstellt (.aaiaext)");
        sb.AppendLine(Check(ctx.IsInspected)            + " Paket-Inspektion");
        if (ctx.HasInspectionBlockers)
            sb.AppendLine("  🚫 Inspection-Blocker vorhanden");
        sb.AppendLine(Check(ctx.IsReleasePrepared)      + " Release vorbereitet");
        sb.AppendLine(Check(ctx.IsSignaturePrepared)    + " Hash-Vorbereitung (Phase 4.0)");
        sb.AppendLine(Check(ctx.EtwKeyExists)           + " ETW-Schlüssel vorhanden");
        sb.AppendLine(Check(ctx.IsEtwSigned)            + " ETW-Signatur erstellt (Phase 4.1)");
        sb.AppendLine(Check(ctx.IsEtwSignatureVerified) + " ETW-Signatur geprüft (Phase 4.2)");
        sb.AppendLine(Check(ctx.CanContinueToMarketplace) + " Marketplace-freigegeben");
    }

    // ── Trust & Signatur ──────────────────────────────────────────────────────

    private static void AppendTrustInfo(StringBuilder sb, AiHandoffContext ctx)
    {
        sb.AppendLine();
        sb.AppendLine("## Trust-Modell & Signatur");
        sb.AppendLine();
        sb.AppendLine("### Trust-Level-Hierarchie");
        sb.AppendLine("```");
        sb.AppendLine("Unsigned (0)");
        sb.AppendLine("  → LocalHashPrepared (1)    Phase 4.0");
        sb.AppendLine("  → EtwLocalSigned (2)       Phase 4.1");
        sb.AppendLine("  → EtwLocalVerified (3)     Phase 4.2 ← Minimum für Marketplace");
        sb.AppendLine("  → MarketplaceVerified (4)  NUR vom Server gesetzt");
        sb.AppendLine("  → MarketplacePublished (5)");
        sb.AppendLine("  Blocked (-1)               Dauerhaft gesperrt");
        sb.AppendLine("```");

        if (!string.IsNullOrEmpty(ctx.KeyFingerprint))
        {
            sb.AppendLine();
            sb.AppendLine("### Aktueller Schlüssel");
            sb.AppendLine($"- **Fingerprint:** `{ctx.KeyFingerprint}`");
            if (!string.IsNullOrEmpty(ctx.KeyAlgorithm))
                sb.AppendLine($"- **Algorithmus:** {ctx.KeyAlgorithm}");
            if (!string.IsNullOrEmpty(ctx.SignedAtUtc))
                sb.AppendLine($"- **Signiert am:** {ctx.SignedAtUtc}");
        }

        sb.AppendLine();
        sb.AppendLine("### Unveränderliche Regeln");
        sb.AppendLine("- `MarketplaceVerified` wird NUR vom Marketplace-Server gesetzt — nie lokal.");
        sb.AppendLine("- `marketplaceReady` bleibt lokal immer `false`.");
        sb.AppendLine("- Private Key wird NIEMALS übertragen oder in Git eingecheckt.");
        sb.AppendLine("- Der Marketplace verifiziert immer unabhängig — er vertraut nicht dem lokalen Trust-Level.");
    }

    // ── Fehler & Blocker ──────────────────────────────────────────────────────

    private static void AppendErrors(StringBuilder sb, AiHandoffContext ctx)
    {
        sb.AppendLine();
        sb.AppendLine("## Aktuelle Fehler / Blocker");
        sb.AppendLine();

        bool hasAny = false;

        if (ctx.ValidationErrors.Count > 0)
        {
            sb.AppendLine("### Validierungsfehler");
            foreach (var e in ctx.ValidationErrors)
                sb.AppendLine($"- {e}");
            hasAny = true;
        }

        if (ctx.InspectionBlockers.Count > 0)
        {
            sb.AppendLine("### Inspection-Blocker");
            foreach (var e in ctx.InspectionBlockers)
                sb.AppendLine($"- 🚫 {e}");
            hasAny = true;
        }

        if (ctx.SignatureErrors.Count > 0)
        {
            sb.AppendLine("### Signaturfehler");
            foreach (var e in ctx.SignatureErrors)
                sb.AppendLine($"- ⛔ {e}");
            hasAny = true;
        }

        if (!hasAny)
            sb.AppendLine("_Keine Fehler oder Blocker._");
    }

    // ── Aufgaben-Abschnitt ────────────────────────────────────────────────────

    private static void AppendGoal(StringBuilder sb, AiHandoffContext ctx, AiHandoffRequest req)
    {
        sb.AppendLine();
        sb.AppendLine("## Aufgabe");
        sb.AppendLine();

        switch (req.Target)
        {
            case AiHandoffTarget.ImplementNext:
                var next = DetermineNextPhase(ctx);
                sb.AppendLine($"Implementiere die nächste Phase: **{next}**.");
                sb.AppendLine();
                sb.AppendLine("Vorgehen:");
                sb.AppendLine("1. Analysiere den Pipeline-Stand (oben).");
                sb.AppendLine("2. Identifiziere die nächste fehlende Funktion.");
                sb.AppendLine("3. Schlage eine vollständige Implementierung vor (Services + ViewModel-Erweiterung + AXAML).");
                sb.AppendLine("4. Beachte alle Regeln (unten).");
                break;

            case AiHandoffTarget.FixBuildError:
                sb.AppendLine("Analysiere die oben gelisteten Build-/Validierungsfehler und schlage eine Lösung vor.");
                sb.AppendLine();
                sb.AppendLine("Erwartetes Format:");
                sb.AppendLine("1. Ursache des Fehlers erklären.");
                sb.AppendLine("2. Konkreten Fix als Diff oder kompletten Code-Block angeben.");
                sb.AppendLine("3. Begründen, warum der Fix korrekt ist.");
                break;

            case AiHandoffTarget.FixValidationError:
                sb.AppendLine("Analysiere die Validierungsfehler und erkläre, wie sie behoben werden können.");
                sb.AppendLine("Gib konkrete Änderungen an `aaia-extension.json` oder der Projektstruktur an.");
                break;

            case AiHandoffTarget.FixInspectionBlocker:
                sb.AppendLine("Die Paket-Inspektion hat Blocker gefunden, die den Marketplace-Upload verhindern.");
                sb.AppendLine("Analysiere die Blocker und schlage Korrekturen vor.");
                break;

            case AiHandoffTarget.DebugSignature:
                sb.AppendLine("Die ETW-Signatur schlägt fehl oder das Trust-Level stimmt nicht.");
                sb.AppendLine("Analysiere den Zustand (oben) und identifiziere die Ursache.");
                sb.AppendLine();
                sb.AppendLine("Mögliche Ursachen prüfen:");
                sb.AppendLine("- Schlüssel-ID stimmt nicht mit `developerEtwId` in `signature-info.json` überein.");
                sb.AppendLine("- Paket wurde nach der Signierung neu erstellt.");
                sb.AppendLine("- `signature-info.json`-Felder wurden manuell verändert.");
                sb.AppendLine("- Phase 4.0 (Hash-Vorbereitung) fehlt.");
                break;

            case AiHandoffTarget.ArchitectureReview:
                sb.AppendLine("Reviewe die Architektur des AAIA Module Managers auf Basis des oben beschriebenen Zustands.");
                sb.AppendLine();
                sb.AppendLine("Schwerpunkte:");
                sb.AppendLine("- Pipeline-Gate-Pattern: Ist die Reihenfolge korrekt erzwungen?");
                sb.AppendLine("- Trust-Modell: Sind alle Invarianten eingehalten?");
                sb.AppendLine("- ETW-Signatur: Ist das kryptografische Design solide?");
                sb.AppendLine("- MVVM-Trennung: Keine Logik im Code-behind?");
                break;

            case AiHandoffTarget.PlanMarketplace:
                sb.AppendLine("Plane die Implementierung von **Phase 5.x (Marketplace-Upload)**.");
                sb.AppendLine();
                sb.AppendLine("Voraussetzungen laut Pipeline-Stand (oben):");
                if (ctx.CanContinueToMarketplace)
                    sb.AppendLine("✅ Trust-Level `EtwLocalVerified` erreicht — Upload-Voraussetzungen erfüllt.");
                else
                    sb.AppendLine("⚠️ Trust-Level `EtwLocalVerified` noch nicht erreicht.");
                sb.AppendLine();
                sb.AppendLine("Plane:");
                sb.AppendLine("1. `MarketplaceUploadService` — HTTP multipart upload mit Public Key und signature-info.json.");
                sb.AppendLine("2. Server-seitige Verifikation (beschreibe den erwarteten Endpunkt).");
                sb.AppendLine("3. Status-Polling bis `MarketplaceVerified`.");
                sb.AppendLine("4. UI-Integration in Step 6 (oder neuer Step 7).");
                sb.AppendLine("5. Was darf der Client NICHT tun (Regeln aus unten beachten).");
                break;

            case AiHandoffTarget.FullProjectContext:
                sb.AppendLine("Ich stelle dir den vollständigen Projektzustand zur Verfügung.");
                sb.AppendLine("Analysiere und beantworte Fragen oder gib Handlungsempfehlungen.");
                break;
        }
    }

    // ── Regeln ────────────────────────────────────────────────────────────────

    private static void AppendRules(StringBuilder sb, AiHandoffProfile profile, AiHandoffContextLevel level)
    {
        if (level == AiHandoffContextLevel.Compact) return;

        sb.AppendLine();
        sb.AppendLine("## Regeln (immer einhalten)");
        sb.AppendLine();
        sb.AppendLine("1. **Trust-Level** — `MarketplaceVerified` darf NUR der Server setzen. Lokal nie.");
        sb.AppendLine("2. **Private Key** — NIEMALS in Code, Payload, Upload oder KI-Prompt.");
        sb.AppendLine("3. **Pipeline-Reihenfolge** — Gates dürfen nicht umgangen werden (`CanXxx` prüft immer alle Vorbedingungen).");
        sb.AppendLine("4. **`marketplaceReady`** — lokal immer `false`; nur der Server ändert diesen Wert.");
        sb.AppendLine("5. **`isCryptographicallySigned`** — erst `true` nach Phase 4.1 (echte RSA-Signatur).");
        sb.AppendLine("6. **MVVM** — keine Geschäftslogik im Code-behind; Services sind `static` oder DI-Singleton.");
        sb.AppendLine("7. **Avalonia-Bindings** — `[ObservableProperty]` + `partial void OnXxxChanged` + `NotifyGates()`.");
        sb.AppendLine("8. **release-info.json** — wird von Phase 4.1 modifiziert, daher NICHT als Hash geprüft.");

        if (profile == AiHandoffProfile.Codex)
        {
            sb.AppendLine();
            sb.AppendLine("// Additional: Only output compilable C# / AXAML. No pseudocode.");
        }
    }

    // ── Debug-Extras ──────────────────────────────────────────────────────────

    private static void AppendDebugExtras(StringBuilder sb, AiHandoffContext ctx)
    {
        sb.AppendLine();
        sb.AppendLine("## Debug-Kontext");
        sb.AppendLine();
        sb.AppendLine("### Kanonischer Payload (Schema)");
        sb.AppendLine("```");
        sb.AppendLine("etw-signature-v1");
        sb.AppendLine($"extensionId:{(string.IsNullOrEmpty(ctx.ExtensionId) ? "<id>" : ctx.ExtensionId)}");
        sb.AppendLine("extensionVersion:<version>");
        sb.AppendLine($"developerEtwId:{ctx.DeveloperEtwId ?? "<etwId>"}");
        sb.AppendLine("packageSha256:<sha256-hex>");
        sb.AppendLine("releaseInfoSha256:<sha256-hex>  ← Vor-Signatur-Snapshot");
        sb.AppendLine("inspectionReportSha256:<sha256-hex>");
        sb.AppendLine("signedAtUtc:<ISO8601>");
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("### Wichtige Dateien im Release-Ordner");
        sb.AppendLine("```");
        sb.AppendLine("{releaseFolderPath}/");
        sb.AppendLine("  {name}-{version}.aaiaext    ← Hash wird geprüft");
        sb.AppendLine("  release-info.json           ← NICHT geprüft (wird von Phase 4.1 modifiziert)");
        sb.AppendLine("  inspection-report.json      ← Hash wird geprüft");
        sb.AppendLine("  signature-info.json         ← enthält Signatur + eingebetteten Public Key");
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("### Schlüsselpfad");
        sb.AppendLine("```");
        sb.AppendLine($"%APPDATA%\\AAIA\\Keys\\{ctx.DeveloperEtwId ?? "<etwId>"}-private.pem   ← NIEMALS übertragen");
        sb.AppendLine($"%APPDATA%\\AAIA\\Keys\\{ctx.DeveloperEtwId ?? "<etwId>"}-public.pem    ← darf geteilt werden");
        sb.AppendLine($"%APPDATA%\\AAIA\\Keys\\{ctx.DeveloperEtwId ?? "<etwId>"}-key-info.json ← Fingerprint etc.");
        sb.AppendLine("```");
    }

    // ── Erwartete Ausgabe ─────────────────────────────────────────────────────

    private static void AppendExpectedOutput(StringBuilder sb, AiHandoffRequest req)
    {
        sb.AppendLine();
        sb.AppendLine("## Erwartete Ausgabe");
        sb.AppendLine();

        switch (req.Target)
        {
            case AiHandoffTarget.ImplementNext:
            case AiHandoffTarget.PlanMarketplace:
                sb.AppendLine("Bitte liefere:");
                sb.AppendLine("- Vollständige C#-Dateien (keine Platzhalter, kein Pseudocode).");
                sb.AppendLine("- AXAML-Ergänzungen als vollständige Abschnitte.");
                sb.AppendLine("- Kurze Erklärung der Design-Entscheidungen.");
                break;

            case AiHandoffTarget.FixBuildError:
            case AiHandoffTarget.FixValidationError:
            case AiHandoffTarget.FixInspectionBlocker:
            case AiHandoffTarget.DebugSignature:
                sb.AppendLine("Bitte liefere:");
                sb.AppendLine("- Ursachenanalyse (1–3 Sätze).");
                sb.AppendLine("- Konkreten Fix als vollständigen Code-Block.");
                sb.AppendLine("- Erklärung warum der Fix korrekt ist.");
                break;

            case AiHandoffTarget.ArchitectureReview:
                sb.AppendLine("Strukturiertes Review mit:");
                sb.AppendLine("- Was gut ist (beibehalten).");
                sb.AppendLine("- Was verbessert werden sollte (mit Begründung).");
                sb.AppendLine("- Konkrete Empfehlungen für nächste Schritte.");
                break;

            default:
                sb.AppendLine("Bitte antworte direkt und konkret. Kein Pseudocode.");
                break;
        }
    }

    // ── Hilfsfunktionen ───────────────────────────────────────────────────────

    private static bool HasErrors(AiHandoffContext ctx)
        => ctx.ValidationErrors.Count > 0
        || ctx.InspectionBlockers.Count > 0
        || ctx.SignatureErrors.Count > 0;

    private static string Check(bool done) => done ? "- [x]" : "- [ ]";

    private static string TargetEmoji(AiHandoffTarget target) => target switch
    {
        AiHandoffTarget.ImplementNext         => "🔨",
        AiHandoffTarget.FixBuildError         => "🐛",
        AiHandoffTarget.FixValidationError    => "⚠️",
        AiHandoffTarget.FixInspectionBlocker  => "🚫",
        AiHandoffTarget.DebugSignature        => "🔐",
        AiHandoffTarget.ArchitectureReview    => "🏗️",
        AiHandoffTarget.PlanMarketplace       => "🚀",
        AiHandoffTarget.FullProjectContext    => "📋",
        _                                     => "📝"
    };

    private static string TargetLabel(AiHandoffTarget target) => target switch
    {
        AiHandoffTarget.ImplementNext         => "Nächste Phase implementieren",
        AiHandoffTarget.FixBuildError         => "Build-Fehler beheben",
        AiHandoffTarget.FixValidationError    => "Validierungsfehler analysieren",
        AiHandoffTarget.FixInspectionBlocker  => "Inspection-Blocker beheben",
        AiHandoffTarget.DebugSignature        => "ETW-Signaturproblem debuggen",
        AiHandoffTarget.ArchitectureReview    => "Architektur-Review",
        AiHandoffTarget.PlanMarketplace       => "Phase 5 — Marketplace-Upload planen",
        AiHandoffTarget.FullProjectContext    => "Vollständiger Projektkontext",
        _                                     => "Handoff"
    };

    private static string DetermineNextPhase(AiHandoffContext ctx)
    {
        if (!ctx.IsProjectCreated)        return "Projekt erstellen (Schritt 1)";
        if (!ctx.IsValidated)             return "Manifest validieren (Schritt 4)";
        if (ctx.HasValidationBlockers)    return "Validierungsblocker beheben";
        if (!ctx.IsBuilt)                 return "Projekt bauen (Schritt 3)";
        if (!ctx.IsPackaged)              return ".aaiaext-Paket erstellen (Schritt 5)";
        if (!ctx.IsInspected)             return "Paket inspizieren (Schritt 5)";
        if (ctx.HasInspectionBlockers)    return "Inspection-Blocker beheben";
        if (!ctx.IsReleasePrepared)       return "Release vorbereiten (Schritt 5)";
        if (!ctx.IsSignaturePrepared)     return "Hash-Vorbereitung (Phase 4.0)";
        if (!ctx.EtwKeyExists)            return "ETW-Schlüssel erzeugen (Phase 4.1)";
        if (!ctx.IsEtwSigned)             return "ETW-Signatur erstellen (Phase 4.1)";
        if (!ctx.IsEtwSignatureVerified)  return "ETW-Signatur prüfen (Phase 4.2)";
        if (!ctx.CanContinueToMarketplace) return "Marketplace-Freigabe erhalten";
        return "Phase 5.0 — Marketplace-Upload implementieren";
    }

    private static string BuildTitle(AiHandoffTarget target, AiHandoffProfile profile, AiHandoffContext ctx)
    {
        var profileLabel = profile switch
        {
            AiHandoffProfile.ChatGpt => "ChatGPT",
            AiHandoffProfile.Claude  => "Claude",
            AiHandoffProfile.Codex   => "Codex",
            AiHandoffProfile.Gemini  => "Gemini",
            _                        => profile.ToString()
        };
        return $"{TargetLabel(target)} — {profileLabel}";
    }
}
