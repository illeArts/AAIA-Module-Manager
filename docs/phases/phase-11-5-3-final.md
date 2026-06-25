# Phase 11.5.3 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`

## 1. Was wurde gebaut?

Die Kernpfade der Benutzer-, Entwickler- und Administratorhandbücher wurden vervollständigt:
Module-Manager-Einstieg, Projektanlage, Validierung/Build/Paketierung, Rollen, Verbindung,
Modul-/Plugin-Entwicklung, Manifest/Permissions, Tests, Release-Signatur,
Entwicklerverantwortung, Signatur/Trust-Level, Marketplace-Upload, KI-Handoff/Connector,
Fehleranalyse/Diagnose, Sicherheit/Laufzeitstatus, Admin-Rollen, Backup/Recovery, Logs/Audit
und Updates.

## 2. Warum wurde es gebaut?

Phase 11.5.2 machte Runtime-Betriebswissen nutzbar. Phase 11.5.3 schließt die größten
Handbuchlücken, damit Benutzer, ETWs und Administratoren den aktuellen Stand ohne Rückgriff
auf ungeprüfte Alttexte verstehen können.

## 3. Welche Dateien wurden geändert?

- `docs/user-manual/`
- `docs/developer-guide/`
- `docs/admin-guide/`
- `docs/documentation-inventory.md`
- `docs/website-help/`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Externe Developer-Docs wurden als Quelle abgeglichen, aber nicht blind übernommen. Deployment-
Pfade, die nicht im aktuellen Repository verbindlich belegt sind, bleiben ergänzende Quelle
und keine öffentliche Produktzusage. Kanonische Handbuchinhalte liegen weiter unter `docs/`.

## 4.1 Welche Doku-Wahrheitsregeln gelten?

- `docs/DOCUMENTATION_TRUTH_RULE.md` ist verbindlich.
- `docs/glossary/term-status.md` bleibt maßgeblich für offene oder historische Begriffe.
- Historische Help- und Handoff-Texte sind Quellen, aber kein alleiniger Implementierungsnachweis.
- Externe Developer-Docs wurden abgeglichen; nicht übernommene Details bleiben externe Quelle.
- Offene Produktpfade werden als geplant, vorbereitet, spezifiziert oder offen markiert.
- ETW wird als Entwickler-/Erstellerrolle beschrieben; die Langform bleibt offen.

## 5. Welche Sicherheitsregeln gelten?

Keine Secrets, Tokens, Passwörter, privaten Schlüssel, realen Zugangsdaten oder privaten
Benutzerpfade. Marketplace-Trust-Stufen bleiben serverseitig. Lokale Admin-, ETW- oder
Recovery-Aktionen dürfen keine Sicherheitsprüfungen umgehen.

## 6. Welche Tests müssen grün sein?

- `git diff --check`
- relative Markdown-Linkprüfung für `docs/`
- Prüfung auf bekannte falsche ETW-Langform
- Prüfung auf abgelöste Developer-Guide-Pfade nach der Kanonisierung von Validierung,
  Manifest und Sicherheit/Laufzeitstatus
- Dublettenprüfung für Signatur-/Trust-Seiten

Produktcode wurde nicht geändert; eine vollständige Code-Regression ist für diese Phase nicht
erforderlich.

## 6.1 Welche Links und Quellen wurden geprüft?

- kanonische Dokumentationsstruktur unter `docs/README.md`
- Quelleninventar unter `docs/documentation-inventory.md`
- historische Help-Seiten unter `docs/help/manual/`
- Troubleshooting-Quellen unter `docs/help/troubleshooting/`
- AI-Handoff-Kontexte unter `docs/ai-handoff/context/`
- AIR-Handoff und Phase-10-Abschlüsse
- externes Repository `aaia-developer-docs` für Modul-, Plugin-, Manifest-, Permission- und
  Konventionsgrundlagen

## 7. Was darf nicht verletzt werden?

- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Features.
- Keine Erfindung offener Begriffslangformen.
- Keine öffentliche Installationsmatrix ohne bestätigte Produkt- und Releasekanäle.
- Keine stille Übernahme historischer Texte als heutige Wahrheit.

## 8. Bekannte Grenzen / offene Punkte

- AIR SDK Detailreferenz bleibt offen.
- Marketplace/MoR/Lizenzbetrieb für Administratoren bleibt offen.
- Öffentliche Website, Suche, PDF und In-App-Hilfe sind weiterhin nicht deployed.
- Automatische Linkprüfung ist noch kein CI-/Build-Schritt.

## 9. Nächster Schritt

Phase 11.5.4 sollte Website-Routen, Suche, Versionierung und Veröffentlichung vorbereiten
oder — falls Website-Deployment noch nicht freigegeben ist — zuerst eine Fehlercode-Referenz
und In-App-Hilfe-Mapping ergänzen.

## 10. Relevanz für Benutzerhandbuch

Benutzerhandbuch enthält jetzt konkrete Einstiege für Module Manager, Projektanlage,
Validierung/Build/Paketierung, Rollen, AAIAS/AAIAC-Verbindung und Sicherheit.

## 11. Relevanz für Entwicklerdokumentation

Entwicklerhandbuch enthält jetzt Modul-/Plugin-Entwicklung, Manifest/Permissions,
Validierung/Tests/Build, Release-Signatur, Rollen/Rechte, Trust-Level,
Marketplace-Upload, KI-Handoff/Connector, Fehleranalyse/Diagnose, Sicherheit/Laufzeitstatus
sowie AIR Runtime und Durability.

## 12. Relevanz für Administratorhandbuch

Administratorhandbuch enthält jetzt Rollen/Betriebsgrenzen, Persistenz/Backup/Recovery,
Logs/Audit/Monitoring, Updates/Release-Betrieb und Runtime-Betrieb.

## 13. Relevanz für Webseite / öffentliche Hilfe

Website-Hilfe kann jetzt deutlich mehr Startkarten aus kanonischen Quellen ableiten. Deployment
und Suche bleiben außerhalb dieses Scopes.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Für weitere Dokuarbeit `docs/documentation-inventory.md`, `docs/DOCUMENTATION_TRUTH_RULE.md`,
`docs/glossary/term-status.md` und die Zielgruppenindizes zuerst lesen. 11.5.3 hat
Kernhandbücher ergänzt, aber keine Produktfunktionen geändert. Externe Developer-Docs sind
abgeglichen, nicht vollständig kanonisiert.
