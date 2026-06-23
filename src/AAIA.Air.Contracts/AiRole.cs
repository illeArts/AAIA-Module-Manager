namespace AAIA.Air.Contracts;

/// <summary>
/// Rollen/Verantwortlichkeiten einer KI im Team. Nicht modellspezifisch — jede KI kann
/// jede Rolle einnehmen. Der Collaboration Manager verteilt Aufgaben nach Verantwortung,
/// nicht nur nach Verfügbarkeit.
/// </summary>
public enum AiRole
{
    Architect,    // entwirft die Struktur
    Developer,    // implementiert den Code
    Reviewer,     // prüft Änderungen und Sicherheit
    Researcher,   // beschafft Informationen
    Tester,       // validiert und erstellt Testfälle
    Installer,    // Build, Paketierung, Installation
    Documenter,   // Dokumentation
    Administrator // Verwaltung (z. B. AAIAS)
}
