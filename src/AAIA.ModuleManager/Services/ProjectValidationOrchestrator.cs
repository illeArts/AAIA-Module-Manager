using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Führt alle Validatoren in der richtigen Reihenfolge aus und gibt ein
/// konsolidiertes ValidationResult zurück.
///
/// Reihenfolge: Manifest → Struktur → Kompatibilität → Risiko
/// Optional: KI-Anreicherung für unbekannte Fehler.
/// </summary>
public static class ProjectValidationOrchestrator
{
    /// <summary>
    /// Vollständige Prüfung eines AAIA-Projekts.
    /// Immer offline-fähig — KI ist optional.
    /// </summary>
    public static async Task<ValidationResult> ValidateAsync(
        string             projectDir,
        NewProjectType     projectType,
        IAiProviderService? aiProvider  = null,
        string?            projectName  = null,
        CancellationToken  ct           = default)
    {
        var result = new ValidationResult();

        // ── 1. Manifest ────────────────────────────────────────────────────────
        result.Issues.AddRange(
            ManifestValidationService.Validate(projectDir));

        // ── 2. Dateistruktur ───────────────────────────────────────────────────
        result.Issues.AddRange(
            ExtensionStructureValidator.Validate(projectDir, projectType));

        // ── 3. .NET-Kompatibilität ─────────────────────────────────────────────
        result.Issues.AddRange(
            CompatibilityCheckService.Validate(projectDir, projectType));

        // ── 4. Risiko-Vorprüfung ───────────────────────────────────────────────
        result.Issues.AddRange(
            RiskPreCheckService.Validate(projectDir));

        // ── 5. KI-Anreicherung (optional, silent fail) ─────────────────────────
        if (aiProvider is not null && result.HasBlockers)
        {
            await EnrichWithAiAsync(result, aiProvider, projectName, ct);
        }

        return result;
    }

    // ── KI-Anreicherung ────────────────────────────────────────────────────────

    private static async Task EnrichWithAiAsync(
        ValidationResult   result,
        IAiProviderService provider,
        string?            projectName,
        CancellationToken  ct)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Du bist AAIA Projekt-Prüfer. Analysiere diese Validierungsfehler.");
            sb.AppendLine("Antworte auf Deutsch, klar und kurz. Maximal 2 Sätze pro Fehler. Kein Markdown.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(projectName))
                sb.AppendLine($"Projekt: {projectName}");
            sb.AppendLine();
            sb.AppendLine("Blocker:");

            int count = 0;
            foreach (var issue in result.Issues)
            {
                if (!issue.IsError) continue;
                if (++count > 3) break;
                sb.AppendLine($"- [{issue.Category}] {issue.Title}: {issue.Message[..System.Math.Min(issue.Message.Length, 120)]}");
            }

            sb.AppendLine();
            sb.AppendLine("Was soll der ETW konkret tun?");

            var resp = await provider.SendAsync(
                new AiRequest([new ChatMessage("user", sb.ToString())],
                    "Du bist ein hilfreicher AAIA Projekt-Assistent.", 400), ct);

            if (resp.Success && !string.IsNullOrWhiteSpace(resp.Text))
            {
                var firstError = result.Issues.Find(i => i.IsError);
                if (firstError is not null)
                    firstError.Message += "\n\n🤖 " + resp.Text.Trim();

                result.WasAiEnriched = true;
            }
        }
        catch { /* KI-Fehler silent schlucken */ }
    }
}
