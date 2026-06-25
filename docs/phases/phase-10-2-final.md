# Phase 10.2 — Abschlussdokumentation

> Status: abgeschlossen
> Geprüfter Stand: 2026-06-25
> Verantwortlicher Scope: `AAIA.ModuleManager.Services.Ai.Persistence`

## 1. Was wurde gebaut?

Der lokale AIR-State-Protector unterstützt jetzt native Plattformpfade:

- Windows: bestehende DPAPI-CurrentUser-v1-Payloads bleiben lesbar.
- macOS: Keychain-Backend über den nativen `security`-Client.
- Linux: Secret-Service-Backend über `secret-tool` und aktive DBus-Benutzersitzung.
- Neue native Payloads verwenden AES-GCM mit nicht geheimer Key-ID.

## 2. Warum wurde es gebaut?

Phase 9 war produktiv nur unter Windows aktivierbar. 10.2 erweitert den Protector-Pfad für
macOS und Linux, ohne schwache Klartext- oder Schlüsseldatei-Fallbacks einzuführen.

## 3. Welche Dateien wurden geändert?

- `src/AAIA.ModuleManager/Services/Ai/Persistence/AiLocalUserStateProtector.cs`
- `src/AAIA.Air.Contracts/AiRuntimeStateContracts.cs`
- `src/AAIA.ModuleManager/Services/Ai/Persistence/AiLocalFileRuntimeStateStore.cs`
- `src/AAIA.ModuleManager.Tests/Ai/Runtime/Phase10NativeProtectorTests.cs`
- Phase-10-Spezifikation, AIR-Handoff und diese Abschlussdatei

## 4. Welche Architekturentscheidungen wurden getroffen?

- Der Protector bleibt die Host-Grenze; `AAIA.Air` kennt keine Keychain-, Secret-Service- oder
  Dateipfade.
- Die native Key-ID wird aus Store-ID und Schema abgeleitet, ohne Projektpfade, Task-Titel,
  Actor-Namen oder Tool-Payloads zu speichern.
- Der Ciphertext ist per AES-GCM-AAD an Store-ID, Record-Typ, Record-ID, Schema-Version und Key-ID
  gebunden.
- Linux ohne `DBUS_SESSION_BUS_ADDRESS` startet fail-closed.
- Fehlende native Keys werden mit `state_protector_key_missing` gemeldet.

## 5. Welche Sicherheitsregeln gelten?

- Kein Klartext-Fallback.
- Kein lokaler Schlüsseldatei-Fallback.
- Keine Payload-Metadaten in nativen Secret-Store-Attributen.
- Windows-DPAPI-v1 bleibt nur aus Kompatibilitätsgründen lesbar.
- Entschlüsselung mit falschem Store-/Record-Kontext schlägt fehl.

## 6. Welche Tests müssen grün sein?

- Native Envelope roundtript und enthält eine nicht geheime Key-ID.
- Ciphertext ist an Store/Record/Schema gebunden.
- Fehlender Key schlägt fail-closed mit `state_protector_key_missing` fehl.
- Bestehende Windows-DPAPI- und Phase-9/10-Regression bleibt grün.
- Vollständige Regression: 316/316.

## 7. Was darf nicht verletzt werden?

- Keine automatische Persistenzaktivierung auf bestehenden Installationen.
- Keine neuen MCP-Tools oder Permissions.
- Keine stillen Secret-Service-Prompts als Erfolg vortäuschen.
- Keine Rotation ohne separaten Owner/Admin-Wartungspfad.

## 8. Bekannte Grenzen / offene Punkte

- Key-Rotation und Offline-Rewrap sind vorbereitet, aber noch nicht als Wartungsaktion gebaut.
- Der Linux-Pfad setzt `secret-tool` und eine aktive Secret-Service/DBus-Sitzung voraus.
- Der macOS-Pfad setzt den Systemclient `security` voraus.

## 9. Nächster Schritt

Phase 10.3 — app-neutraler Runtime-Lifecycle und Readiness-Leases.

## 10. Relevanz für Benutzerhandbuch

Keine Bedienänderung. Persistenz bleibt opt-in.

## 11. Relevanz für Entwicklerdokumentation

Hosts verwenden weiterhin `IAiStateProtector`. Native Backends bleiben Host-Implementierung und
werden nicht in AIR-Contracts gezogen.

## 12. Relevanz für Administratorhandbuch

Linux-Installationen benötigen eine aktive Desktop-Secret-Service-Sitzung. Headless Linux bleibt
fail-closed, bis ein expliziter, sicherer Betriebsmodus spezifiziert ist.

## 13. Relevanz für Webseite / öffentliche Hilfe

Linux-Build-/Installationshinweise sind bereits in 10.1.3 ergänzt. Für 10.2 ist später ein
kurzer Persistenzhinweis sinnvoll: Linux Desktop ja, Headless ohne Secret Service nein.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

10.2 ist implementiert. Nicht als Nächstes Rotation bauen, wenn der Phasenplan 10.3 verlangt.
Der nächste definierte Scope ist app-neutraler Lifecycle und Readiness-Gates.
