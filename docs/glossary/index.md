# AAIA-Glossar

> Stand: Foundation. Status und Beleglage historischer Begriffe stehen in
> [`term-status.md`](term-status.md). „Definition ausstehend“ bedeutet, dass Langform oder
> Produktgrenze noch nicht verbindlich belegt ist.

Jeder Eintrag nennt Kurzbeschreibung, Zielgruppe, Beispiel und Querverweise. Technische
Sicherheitsentscheidungen werden durch das Glossar nicht ersetzt.

## AAIA

- **Kurzbeschreibung:** AI-Assisted Integration Architecture; das Gesamtsystem zum Erstellen,
  Prüfen, Paketieren, Veröffentlichen und Betreiben von Erweiterungen.
- **Relevant für:** alle.
- **Beispiel:** Der Module Manager begleitet einen ETW vom Projekt bis zum Marketplace-Upload.
- **Siehe auch:** AAIAS, AAIAC, AAIAM, AIR, ETW.

## AAIAS

- **Kurzbeschreibung:** AAIA Server; installiert und betreibt serverseitige AAIA-Module.
- **Relevant für:** Anwender, Administratoren, ETWs.
- **Beispiel:** Der Module Manager überträgt ein geprüftes `.aaiaext`-Paket an AAIAS; AAIAS
  validiert es erneut vor der Installation.
- **Siehe auch:** AAIAC, AAIAEXT, Connector.

## AAIAC

- **Kurzbeschreibung:** AAIA Client; Ziel für Client-Plugins und Client-/UI-Logik.
- **Relevant für:** Anwender und Plugin-Entwickler.
- **Beispiel:** Ein Hybrid-Modul enthält einen AAIAS- und einen AAIAC-Anteil.
- **Siehe auch:** AAIAS, Hybrid-Modul, Manifest.

## AAIAM

- **Kurzbeschreibung:** Kurzform für den AAIA Module Manager. Diese Schreibweise ist im
  aktuellen Code nicht durchgehend etabliert und muss vor öffentlicher Nutzung bestätigt werden.
- **Relevant für:** Dokumentation und Produktkommunikation.
- **Beispiel:** Der AAIA Module Manager erstellt und validiert ein Erweiterungspaket.
- **Siehe auch:** AAIA Module Manager, ETW, Release.

## AAIA Module Manager

- **Kurzbeschreibung:** Desktop-Werkzeug zum Erstellen, Validieren, Bauen, Signieren, Testen
  und Veröffentlichen von AAIA-Modulen und -Plugins.
- **Relevant für:** ETWs, Prüfer und Administratoren.
- **Beispiel:** Ein ETW prüft ein Release lokal bis `EtwLocalVerified`.
- **Siehe auch:** AAIAM, ETW, Trust-Level, Marketplace.

## AAIAModuleManager

- **Kurzbeschreibung:** Technische bzw. kompakte Schreibweise für den AAIA Module Manager;
  in öffentlichen Texten wird die ausgeschriebene Produktbezeichnung bevorzugt.
- **Relevant für:** Quellcode-, Repository- und Dateinamen sowie technische Suche.
- **Beispiel:** Das Repository kann einen kompakten Namen verwenden, während die Oberfläche
  „AAIA Module Manager“ anzeigt.
- **Siehe auch:** AAIAM, AAIA Module Manager.

## AIR

- **Kurzbeschreibung:** AAIA Intelligence Runtime; app-neutraler Laufzeitkern für Sessions,
  Rollen, Tools, Messaging, Scheduling, Ressourcen und durable Zustände.
- **Relevant für:** AIR-Entwickler, Host-Entwickler und Administratoren.
- **Beispiel:** Der Module Manager ist ein Host/Nutzer von AIR; AIR kennt den Module Manager nicht.
- **Siehe auch:** Runtime, Scheduler, Resource Manager, MCP Bridge, Durable State.

## ETW

- **Kurzbeschreibung:** Im Projekt verwendete Rolle für Entwickler von AAIA-Erweiterungen.
  Die ausgeschriebene Langform ist in diesem Repository noch verbindlich zu bestätigen.
- **Relevant für:** Entwickler, Marketplace-Prüfer und Dokumentation.
- **Beispiel:** Ein ETW erstellt ein Modul, signiert das Release und reicht es zur Prüfung ein.
- **Siehe auch:** ETW-ID, AAIAEXT, Trust-Level.

## ETW-ID

- **Kurzbeschreibung:** Entwicklerkennung, die Signatur- und Release-Metadaten einem ETW zuordnet.
- **Relevant für:** ETWs und Trust-/Marketplace-Prozesse.
- **Beispiel:** Eine fiktive ID kann dem Format `beispiel.max` folgen.
- **Siehe auch:** ETW, Signatur, Fingerprint.

## DUKI

- **Kurzbeschreibung:** Historische Bezeichnung für den operativen AAIA-Operator für
  kontrollierte Desktop- und Systemaktionen. Langform und aktueller Produktstatus sind offen.
- **Relevant für:** Architektur und Produktplanung.
- **Beispiel:** Historisch führt DUKI freigegebene Aktionen aus, während BBK Wissen liefert.
- **Siehe auch:** AIR, Host, BBK.

## BBK

- **Kurzbeschreibung:** Historische Bezeichnung für den Bibliotheks-/Wissensagenten von AAIA;
  die Langform und der aktuelle Produktstatus sind offen.
- **Relevant für:** Architektur und Produktplanung.
- **Beispiel:** Historische Trennung: BBK liefert Wissen, DUKI führt aus.
- **Siehe auch:** AIR, Host, DUKI.

## Prompti

- **Kurzbeschreibung:** Historische Kommunikations- und Aufgabenübersetzungsschicht, die nach
  einer Sicherheitsprüfung Benutzersprache in strukturierte WorkOrders oder Vorschläge überführt.
- **Relevant für:** Produktplanung und Glossarpflege.
- **Beispiel:** Prompti ist historisch nicht der Gatekeeper, sondern verarbeitet bereits
  sicherheitsgeprüfte Eingaben.
- **Siehe auch:** AAIA, Runtime.

## Lector

- **Kurzbeschreibung:** Historische Voice-/Sprachschicht für Mikrofon, Wake Word, STT,
  Sicherheitsübergabe und TTS/Antwort; aktueller Implementierungsstatus offen.
- **Relevant für:** Produktplanung und Glossarpflege.
- **Beispiel:** Lector übergibt erkannte Sprache nach der Sicherheitsgrenze an Prompti/AAIA.
- **Siehe auch:** AAIA, Runtime.

## VSI

- **Kurzbeschreibung:** Visual System Intelligence; historische visuelle Analyse-, Layout-,
  Animations- und Design-Unterstützungsschicht. Aktueller Implementierungsstatus offen.
- **Relevant für:** Produktplanung und Glossarpflege.
- **Beispiel:** Historische VSI-Texte beschreiben visuelle Prüfung ohne direkte Lösch- oder
  Shell-Cleanup-Befugnis.
- **Siehe auch:** AAIA.

## Marketplace

- **Kurzbeschreibung:** Veröffentlichungs- und Vertriebsbereich für geprüfte AAIA-Erweiterungen.
- **Relevant für:** Anwender, ETWs, Administratoren und Marketplace-Betrieb.
- **Beispiel:** Nur der Marketplace-Server darf `MarketplaceVerified` setzen.
- **Siehe auch:** MoR, Trust-Level, MarketplaceVerified, MarketplacePublished.

## MoR

- **Kurzbeschreibung:** Merchant of Record; externer Vertragspartner für Zahlungsabwicklung
  und zugehörige Händlerpflichten. Der konkrete Anbieter ist keine Glossar-Zusage.
- **Relevant für:** ETWs mit kostenpflichtigen Angeboten und Marketplace-Administration.
- **Beispiel:** Ein kostenpflichtiges Angebot benötigt vor Veröffentlichung eine gültige MoR-Zuordnung.
- **Siehe auch:** Marketplace, Lizenz, Release.

## Trust-Level

- **Kurzbeschreibung:** Geprüfte Vertrauensstufe eines Releases. Die Stufen steigen nur über
  die jeweils autorisierte lokale oder serverseitige Prüfung.
- **Relevant für:** ETWs, Nutzer, Marketplace und Sicherheit.
- **Beispiel:** `EtwLocalVerified` ist die lokale Upload-Voraussetzung; es ist nicht gleich
  `MarketplaceVerified`.
- **Siehe auch:** EtwLocalSigned, EtwLocalVerified, MarketplaceVerified, MarketplacePublished.

## EtwLocalSigned

- **Kurzbeschreibung:** Lokale ETW-Signatur wurde erstellt; die Signatur ist damit noch nicht
  automatisch erfolgreich verifiziert.
- **Relevant für:** ETWs und Release-Prüfung.
- **Beispiel:** Nach der Signatur folgt eine getrennte lokale Verifikation.
- **Siehe auch:** Signatur, EtwLocalVerified.

## EtwLocalVerified

- **Kurzbeschreibung:** Lokale ETW-Signatur wurde erfolgreich geprüft; derzeitige lokale
  Mindeststufe für einen Marketplace-Upload.
- **Relevant für:** ETWs und Marketplace-Upload.
- **Beispiel:** Der Module Manager gibt den Upload erst nach erfolgreicher lokaler Prüfung frei.
- **Siehe auch:** EtwLocalSigned, MarketplaceVerified.

## MarketplaceVerified

- **Kurzbeschreibung:** Serverseitige Marketplace-Prüfung war erfolgreich. Dieser Status darf
  niemals lokal gesetzt oder vorgetäuscht werden.
- **Relevant für:** Marketplace-Betrieb, ETWs und Sicherheit.
- **Beispiel:** Der Module Manager zeigt den vom Server gelieferten Status nur an.
- **Siehe auch:** EtwLocalVerified, MarketplacePublished.

## MarketplacePublished

- **Kurzbeschreibung:** Das geprüfte Release ist im Marketplace veröffentlicht.
- **Relevant für:** Nutzer, ETWs und Marketplace-Betrieb.
- **Beispiel:** Erst diese Stufe beschreibt eine tatsächliche öffentliche Veröffentlichung.
- **Siehe auch:** MarketplaceVerified, Release.

## AAIAEXT / `.aaiaext`

- **Kurzbeschreibung:** Paketformat für AAIA-Erweiterungen; ein ZIP-Archiv mit Manifest,
  Paketmetadaten und Binaries.
- **Relevant für:** ETWs, AAIAS, AAIAC und Marketplace.
- **Beispiel:** AAIAS validiert Hash, Manifest und Signatur eines Pakets erneut.
- **Siehe auch:** Manifest, Release, Signatur.

## Release

- **Kurzbeschreibung:** Versionierter, reproduzierbarer Stand einer Erweiterung samt Paket,
  Prüfnachweisen und Metadaten.
- **Relevant für:** ETWs, Prüfer, Marketplace und Nutzer.
- **Beispiel:** Das erneute Packen nach der Signatur verändert den Hash und macht die Prüfung ungültig.
- **Siehe auch:** AAIAEXT, Manifest, Trust-Level.

## Signatur

- **Kurzbeschreibung:** Kryptografischer Nachweis über Integrität und Unterzeichner eines
  definierten kanonischen Release-Inhalts.
- **Relevant für:** ETWs, Marketplace, AAIAS und Sicherheit.
- **Beispiel:** Private Schlüssel werden niemals in Paket, Dokumentation oder Log übernommen.
- **Siehe auch:** Fingerprint, EtwLocalSigned, EtwLocalVerified.

## Manifest

- **Kurzbeschreibung:** Maschinenlesbare Beschreibung einer Erweiterung, unter anderem mit
  Identität, Version, Ziel und angeforderten Fähigkeiten.
- **Relevant für:** ETWs, Validatoren, AAIAS und AAIAC.
- **Beispiel:** Ohne gültiges `aaia-extension.json` darf ein Paket nicht geladen werden.
- **Siehe auch:** AAIAEXT, Release, Permission.

## Runtime

- **Kurzbeschreibung:** Laufzeitumgebung, die registrierte Funktionen unter festgelegten
  Sicherheits-, Rollen- und Lifecycle-Regeln ausführt.
- **Relevant für:** Entwickler und Administratoren.
- **Beispiel:** AIR ist die app-neutrale Intelligence Runtime der AAIA-Plattform.
- **Siehe auch:** AIR, Host, Durable State.

## Durable State

- **Kurzbeschreibung:** Persistenter Runtime-Zustand, der Neustart und definierte Crash-Recovery
  übersteht, ohne Sicherheitsentscheidungen still zu umgehen.
- **Relevant für:** Runtime-Entwickler und Administratoren.
- **Beispiel:** AIR gibt mutierende Adapter erst nach erfolgreichem Recovery und Readiness frei.
- **Siehe auch:** Runtime, Journal, Recovery.

## Scheduler

- **Kurzbeschreibung:** AIR-Komponente, die entscheidet, wer welchen Task wann übernimmt.
- **Relevant für:** AIR-Entwickler und Betreiber.
- **Beispiel:** Priorität, FIFO/Aging und Capability-Filter beeinflussen die Zuteilung.
- **Siehe auch:** Resource Manager, Execution Queue.

## Resource Manager

- **Kurzbeschreibung:** AIR-Komponente, die entscheidet, wo für eine bereits geplante Execution
  Kapazität und Budget reserviert werden dürfen.
- **Relevant für:** AIR-Entwickler und Betreiber.
- **Beispiel:** Er führt keine Tools aus und ersetzt weder Scheduler noch Permission-Prüfung.
- **Siehe auch:** Scheduler, Reservation, Budget.

## Connector

- **Kurzbeschreibung:** Integrationskomponente zwischen getrennten Anwendungen oder Prozessen;
  Authentisierung, Ziel und Vertrauensgrenze müssen explizit sein.
- **Relevant für:** Entwickler, Administratoren und Sicherheit.
- **Beispiel:** Der Module Manager verbindet sich lokal mit AAIAS über dessen API.
- **Siehe auch:** AAIAS, MCP Bridge, Host.

## MCP Bridge

- **Kurzbeschreibung:** Abgesicherter Adapter, der freigegebene AIR-Funktionen über MCP anbietet;
  er ist nicht der AIR-Kern und darf Sicherheitsprüfungen nicht umgehen.
- **Relevant für:** Integrationsentwickler und Administratoren.
- **Beispiel:** Mutierende Aufrufe bleiben bis AIR-Readiness gesperrt.
- **Siehe auch:** AIR, Connector, Permission.

## Handoff Package

- **Kurzbeschreibung:** Redigiertes, strukturiertes Übergabepaket mit Projektstand, Grenzen,
  Tests und nächstem Schritt für Menschen oder KI-Assistenten.
- **Relevant für:** Maintainer und unterstützende KI-Systeme.
- **Beispiel:** Es enthält keine Secrets und ersetzt keine kanonische Spezifikation.
- **Siehe auch:** Phasenabschluss, AIR.
