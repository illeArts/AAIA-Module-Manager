using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Zentraler KI-Adapter — einheitlicher Einstiegspunkt für alle KI-Targets.
///
/// Der Adapter abstrahiert:
///   • welche KI (AiTarget)
///   • wie die KI erreicht wird (AiExecutionMode)
///   • welcher Kontext übergeben wird (AiHandoffContextLevel)
///   • welche Sicherheitsregeln gelten (AiSafetyPolicy)
///
/// Der Aufrufer fragt nicht mehr "Nutze Claude" —
/// sondern "Ich brauche Hilfe bei Buildfehlern".
/// </summary>
public interface IAiAdapterService
{
    /// <summary>
    /// Führt eine KI-Anfrage aus. Modus (API vs. Handoff) wird automatisch gewählt
    /// oder aus <see cref="AiAdapterRequest.PreferredMode"/> übernommen.
    /// </summary>
    Task<AiAdapterResult> ExecuteAsync(AiAdapterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gibt die aktuellen Capabilities für ein Target zurück.
    /// Zeigt an ob ein API-Key vorhanden ist und welcher Modus effektiv genutzt wird.
    /// </summary>
    AiAdapterCapabilities GetCapabilities(AiTarget target);

    /// <summary>
    /// Gibt den effektiven Ausführungsmodus für ein Target zurück.
    /// Berücksichtigt: gespeicherte Einstellung + API-Key-Verfügbarkeit + Fallback-Regeln.
    /// </summary>
    AiExecutionMode ResolveMode(AiTarget target, AiExecutionMode? preferred = null);
}
