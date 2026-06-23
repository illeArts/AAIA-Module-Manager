# Phase 6 — KI-Integration: Abschlussdokumentation

> **Status:** Abgeschlossen (Phase 6.0 – 6.9)
> **Datum:** 2026-06-23
> **Projekt:** AAIA Module Manager (`aaia-module-manager`)
> **Stack:** Avalonia 11, C# 12, net8.0 / net8.0-windows, xUnit 2.9

---

## 1. Übersicht der Sub-Phasen

| Phase | Titel | Status |
|-------|-------|--------|
| 6.0 | Zentraler AI Adapter (`AiAdapterService`, `IAiAdapterService`) | ✅ |
| 6.1 | Handoff-Package (Builder, ZIP, Approval-Window) | ✅ |
| 6.2 | AI Connector Protocol + lokaler HTTP-Server | ✅ |
| 6.3 | Connector-Tab (ViewModel + AXAML) | ✅ |
| 6.4 | Handoff-Generator-Service (`AiHandoffGeneratorService`, 480 Zeilen) | ✅ (Teil von 6.1/6.3) |
| 6.5 | AAIAS als KI-Target (`AaiasAiProvider`) | ✅ |
| 6.6 | Connector Hardening & Local-Only Security | ✅ |
| 6.7 | Patch/Diff Test Suite (Unit-Tests) | ✅ |
| 6.8 | Connector SDK / Beispiel-Client (`AAIA.Connector.Client`) | ✅ |
| 6.9 | Finalisierung: Status-Modell, Memory-Leak, Integration-Tests, Doku | ✅ |

---

## 2. Architektur

```
src/
├── AAIA.ModuleManager/
│   └── Services/
│       └── AiAdapter/
│           ├── AiAdapterService.cs          # Zentraler Adapter (Phase 6.0)
│           ├── IAiAdapterService.cs
│           ├── AiAdapterModels.cs
│           ├── AiAdapterSettings.cs
│           ├── AiContextBuilder.cs
│           ├── AiSafetyPolicy.cs            # Sicherheitsfilter
│           ├── AaiasAiProvider.cs           # AAIAS-Target (Phase 6.5)
│           ├── Connector/
│           │   ├── AiConnectorProtocol.cs   # Konstanten, Ports, Endpunkte
│           │   ├── AiConnectorServer.cs     # HttpListener, Request-Handling
│           │   ├── AiConnectorPermission.cs # [Flags] Enum
│           │   ├── AiConnectorRegistry.cs   # Bekannte Connector-IDs
│           │   ├── AiConnectorServerSettings.cs
│           │   ├── AiPatchRequest.cs        # DTOs: Request, Response, Status
│           │   └── ConnectorHardening.cs    # Rate-Limit, localhost-check, Validation
│           └── HandoffPackage/
│               ├── AiHandoffPackageBuilder.cs
│               ├── AiHandoffPackageZipExporter.cs
│               └── PackageFileEntry.cs
│   └── Services/Help/
│       ├── AiHandoffGeneratorService.cs     # Prompt-Generierung (Phase 6.4)
│       └── AiHandoffModels.cs
├── AAIA.ModuleManager.Tests/
│   ├── AiSafetyPolicyTests.cs
│   └── Connector/
│       ├── ConnectorHardeningTests.cs
│       ├── PermissionCheckerTests.cs
│       ├── PatchRequestTests.cs
│       └── AiConnectorServerIntegrationTests.cs  # Phase 6.9
└── AAIA.Connector.Client/
    ├── AaiaConnectorClient.cs               # Typed HTTP-Client SDK
    ├── AaiaConnectorModels.cs               # DTOs für Client
    └── Program.cs                           # Demo-Programm
```

---

## 3. AI Connector Protocol (aaia-connector-v1)

### Endpunkte

| Method | Pfad | Beschreibung | Permission |
|--------|------|-------------|------------|
| GET | `/aaia/v1/capabilities` | Server-Info, erlaubte Rechte | – |
| GET | `/aaia/v1/context/current` | Kompakter Projektzustand (kein Quellcode) | ReadContext |
| GET | `/aaia/v1/context/project` | Vollständiger Pipeline-State als JSON | ReadContext |
| GET | `/aaia/v1/handoff/latest` | Letzte Handoff-Package-Info | ReadContext |
| POST | `/aaia/v1/patch/propose` | Patch-Vorschlag einreichen (202 + Polling) | ProposePatch |
| GET | `/aaia/v1/patch/{id}/status` | Approval-Status abfragen | ReadContext |

### Patch-Status-Modell

| Status | Bedeutung |
|--------|-----------|
| `pending` | Wartet auf Nutzer-Entscheidung |
| `approved` | Vom Nutzer genehmigt |
| `rejected` | Vom Nutzer abgelehnt |
| `expired` | Server gestoppt, Proposal hinfällig |
| `not_found` | ID unbekannt oder aus Cache entfernt (>10 min) |

### Port und Binding

- **Port:** 39157 (Produktiv) / 39197 (Tests)
- **Binding:** `http://localhost:{port}/aaia/v1/`
- **Extern gesperrt:** Alle Requests von Nicht-localhost → HTTP 403

---

## 4. Sicherheitsregeln (unveränderlich)

Diese Regeln gelten systemweit und dürfen nicht durch Code-Änderungen aufgeweicht werden:

```
KRITISCH — NIEMALS VERLETZEN:
• Private Keys (PEM, Hex > 40 Zeichen) → NIEMALS in Prompt
• API-Keys, Tokens                      → NIEMALS in Prompt
• Marketplace-Tokens                    → NIEMALS in Prompt
• .env, .pem, .pfx, .p12, .key         → NIEMALS als Patch-Ziel
• Quellcode                             → NICHT automatisch einbeziehen
• ETW-Signatur                          → NIEMALS durch externe KI auslösen
• MarketplaceVerified                   → NUR vom Server gesetzt, nie lokal
• marketplaceReady                      → Lokal immer false
• KI                                    → NIEMALS direkt ins Projekt schreiben
```

Implementiert in: `AiSafetyPolicy.cs`, `ConnectorHardening.cs` (ForbiddenPathPatterns)

---

## 5. Connector Hardening

### Schutzziele (implementiert in `ConnectorHardening.cs`)

| Schutz | Mechanismus | Konfiguration |
|--------|-------------|--------------|
| Localhost-only | `IsLocalhost(IPAddress)` — IPv4, IPv6, mapped | Hardcoded |
| Rate-Limiting | Sliding-Window, 60 req/min pro Connector-ID | `MaxRequestsPerMinute = 60` |
| Body-Size | `ContentLength64 > MaxBodyBytes` → 400 | `MaxBodyBytes = 512 KB` |
| Patch-Target | Forbidden patterns + Path-Traversal + absolute Pfade | `ForbiddenPathPatterns[]` |
| CORS | `Access-Control-Allow-Origin: http://localhost` | Hardcoded (kein Wildcard) |

### Memory-Leak-Schutz

- **Rate-Windows:** Inaktive Connector-IDs werden nach 5 Minuten lazy aus `ConcurrentDictionary` entfernt (Purge alle 60 Sekunden)
- **Resolved Proposals:** Nach 10 Minuten aus `_resolved`-Dictionary entfernt (via `PurgeOldResolved()`)

---

## 6. Test-Suite

### Unit-Tests (`AAIA.ModuleManager.Tests/`)

| Datei | Tests | Abdeckung |
|-------|-------|-----------|
| `ConnectorHardeningTests.cs` | ~15 | IsLocalhost, IsBodyTooLarge, IsRateLimited, ValidatePatchTarget, ValidatePatchRequest |
| `PermissionCheckerTests.cs` | ~10 | RequiredFor, CreateSession, ReadConnectorHeaders, HasPermission |
| `PatchRequestTests.cs` | ~8 | Deserialisierung, ToPatchProposal, RawBlock-Format |
| `AiSafetyPolicyTests.cs` | ~6 | PrivateKey-Erkennung, API-Key, Längen-Warnung, SanitizeUserNote |

### Integration-Tests (`AiConnectorServerIntegrationTests.cs`)

Starten echten `HttpListener` auf Port 39197 via `IAsyncLifetime`.

| Test | Was wird geprüft |
|------|-----------------|
| `Server_IsRunning_AfterStart` | Port konfigurierbar |
| `GetCapabilities_Returns200WithProtocolVersion` | Protokoll-Version korrekt |
| `GetContextCurrent_Returns404_WhenNoContextSet` | Kein aktives Projekt → 404 |
| `GetContextCurrent_Returns200_WhenContextSet` | Kontext korrekt serialisiert |
| `ProposePatch_ValidPatch_Returns202WithProposalId` | Happy-Path Proposal |
| `ProposePatch_ForbiddenTarget_Returns400` | `.env` geblockt |
| `ProposePatch_PathTraversal_Returns400` | `../../etc/passwd` geblockt |
| `ProposePatch_BodyTooLarge_Returns400` | Content > 512 KB geblockt |
| `PatchStatus_UnknownId_Returns404` | Unbekannte ID |
| `PatchStatus_PendingProposal_ReturnsPending` | Vor Entscheidung |
| `PatchStatus_AfterApprove_ReturnsApproved` | Nach ApprovePatch() |
| `PatchStatus_AfterReject_ReturnsRejected` | Nach RejectPatch() |
| `UnknownEndpoint_Returns404` | Routing |

**Hinweis:** Auf Windows benötigt `HttpListener` für nicht-39157-Ports ggf. eine URL-Reservation via:
```
netsh http add urlacl url=http://localhost:39197/aaia/v1/ user=EVERYONE
```
Alternativ Tests als Admin ausführen.

---

## 7. Connector Client SDK (`AAIA.Connector.Client`)

Eigenständiges .NET 8-Projekt (kein Verweis auf Haupt-App).

```csharp
using var client = new AaiaConnectorClient("my-agent", "Mein Agent");

var caps = await client.GetCapabilitiesAsync();
var ctx  = await client.GetContextSummaryAsync();

if (ctx?.HasErrors == true)
{
    var proposal = await client.ProposePatchAsync(new PatchRequest { ... });
    var result   = await client.PollPatchStatusAsync(proposal.ProposalId);
    // result.Status == "approved" | "rejected" | "expired"
}
```

### Methoden

| Methode | HTTP | Beschreibung |
|---------|------|-------------|
| `IsServerRunningAsync()` | GET /capabilities | Health-Check |
| `GetCapabilitiesAsync()` | GET /capabilities | Server-Info |
| `GetContextSummaryAsync()` | GET /context/current | Projektzustand |
| `GetFullContextAsync()` | GET /context/project | Vollständiger State |
| `ProposePatchAsync(request)` | POST /patch/propose | Patch einreichen (wirft `ConnectorException` bei 4xx) |
| `GetPatchStatusAsync(id)` | GET /patch/{id}/status | Einzelabfrage |
| `PollPatchStatusAsync(id, ...)` | GET /patch/{id}/status | Polling bis approved/rejected |

---

## 8. Threading-Modell

```
UI-Thread                    Background-Thread (Server)
    │                               │
    │   ←── PatchProposalReceived ──┤  (via event, cross-thread-safe)
    │                               │
    ├── ApprovePatch(id) ──────────→│  (schreibt in ConcurrentDictionary)
    │                               │
    ├── UpdateContext(ctx) ─────────→│ (volatile write)
    │
    └── Dispatcher.UIThread.Post()  (für UI-Updates aus Event-Handler)
```

- `_currentContext` und `_projectRoot`: `volatile` — race-condition-sicher für single-write, multi-read
- `_pending`, `_resolved`, `_rateWindows`: `ConcurrentDictionary` — lock-free reads
- Rate-Window-Timestamps: `lock(window)` — minimaler Lock-Scope

---

## 9. Bekannte Einschränkungen

| Einschränkung | Details |
|--------------|---------|
| HttpListener URL-Reservation | Auf Windows mit nicht-privilegiertem Account ggf. `netsh http add urlacl` nötig |
| Kein TLS | Server läuft über HTTP (localhost-only, kein TLS nötig aber kein Schutz gegen localhost-Sniffer) |
| Keine Authentifizierung | Jeder Prozess auf localhost kann sich verbinden — kein Token/Secret |
| Kein NuGet-Paket | `AAIA.Connector.Client` ist publish-ready aber noch nicht auf NuGet veröffentlicht |
| Keine Integration-Tests für non-localhost | HttpListenerContext.RemoteEndPoint kann in Tests nicht gefälscht werden |
| Rate-Window-Purge ist lazy | Kein aktiver Timer — Bereinigung nur bei neuem Request |

---

## 10. Offene Punkte für Phase 7+

- [ ] `AAIA.Connector.Client` als NuGet-Paket veröffentlichen
- [ ] Optionale Token-Authentifizierung für Connector-Sessions
- [ ] Websocket-basierter Push statt HTTP-Polling für Patch-Status
- [ ] CI-Integration-Test-Job mit `netsh`-Reservation oder Admin-Rechten
- [ ] Rate-Limit konfigurierbar machen (aktuell hardcoded 60 req/min)
- [ ] Audit-Log für alle Connector-Requests (welcher Agent hat was vorgeschlagen)
