# Was ist AAIA?

> Zielgruppe: neue Anwender
> Stand: Foundation; Produktdetails werden schrittweise ergänzt

AAIA steht für **AI-Assisted Integration Architecture**. Es verbindet Werkzeuge und
Laufzeitkomponenten, mit denen Erweiterungen erstellt, geprüft, paketiert, veröffentlicht
und betrieben werden können.

## Die wichtigsten Bestandteile

- **AAIA Module Manager:** Desktop-Werkzeug für Entwicklung, Prüfung und Veröffentlichung.
- **AAIAS:** Server, der serverseitige Erweiterungen installiert und betreibt.
- **AAIAC:** Client-Ziel für Client-Plugins und Benutzeroberflächen.
- **AIR:** app-neutraler Intelligence-Runtime-Kern für kontrollierte KI-Orchestrierung.
- **Marketplace:** Veröffentlichung und Bezug geprüfter Erweiterungen.

## Typischer Ablauf

1. Ein ETW erstellt ein Modul oder Plugin im Module Manager.
2. Struktur, Manifest und Code werden validiert und getestet.
3. Das Release wird als `.aaiaext` paketiert, signiert und lokal verifiziert.
4. Der Marketplace prüft einen Upload getrennt und serverseitig.
5. AAIAS oder AAIAC validieren das bezogene Paket erneut für ihr Ziel.

Keine lokale Schaltfläche darf eine serverseitige Trust-Stufe vortäuschen. Begriffe und
Sicherheitsstufen erklärt das [Glossar](../glossary/index.md).

## Als Nächstes

Die Installations- und Verbindungsanleitungen folgen nach einem Abgleich der tatsächlich
veröffentlichten Produkte und unterstützten Plattformen. Bis dahin gelten bestehende
projektspezifische Runbooks; Zugangsdaten gehören niemals in Support-Nachrichten.
