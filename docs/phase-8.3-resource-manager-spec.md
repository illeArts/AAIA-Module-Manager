# Phase 8.3 — AIR Resource Manager: Spezifikation

> Status: spezifiziert, nicht implementiert
> Scope: ausschließlich interner AIR-Kern; kein MCP, keine UI, keine Host-Adapter

## 1. Ziel

Der Resource Manager entscheidet, **welche registrierte Ausführungsressource** eine
bereits geplante Execution bedienen kann. Er prüft Fähigkeiten, freie Kapazität,
Kostenbudget und aktuelle Last, reserviert die ausgewählte Ressource atomar und liefert
eine nachvollziehbare Entscheidung.

Er ersetzt weder den Scheduler noch die Sicherheitskette:

- Scheduler: **wer** übernimmt welchen Task und **wann**.
- Resource Manager: **wo** wird dafür Kapazität reserviert.
- Runtime-Sicherheitskette: **darf** der konkrete Tool-Aufruf ausgeführt werden.

## 2. Nicht-Ziele

Phase 8.3 implementiert ausdrücklich nicht:

- MCP-Tools, REST-Endpunkte oder UI
- automatische Änderung von Permissions, Rollen oder Capabilities
- Tool-Ausführung, Task-Claiming oder Scheduler-Priorisierung
- Anbieter-/Modell-Sonderregeln anhand von Namen
- Wechselkursberechnung oder automatische Budgeterhöhung
- persistente Abrechnung oder externes Billing
- Secrets, API-Keys oder Zugangsdaten in Profilen/Entscheidungen
- dynamisches Starten/Stoppen von Infrastruktur

## 3. Ressourcenprofil

Ein Ressourcenprofil beschreibt eine von einem Host registrierte, adressierbare
Ausführungskapazität. Es ist keine Session und kein Benutzerkonto.

### 3.1 Identität

| Feld | Bedeutung |
|---|---|
| `ResourceId` | stabile, eindeutige technische ID |
| `ProviderId` | registrierender Host/Provider; nur Audit, kein Ranking nach Namen |
| `DisplayName` | Anzeige; niemals Auswahlkriterium |
| `Kind` | `Inference`, `Compute` oder `ToolHost` |
| `Enabled` | administrativer Schalter |
| `Locality` | `Local`, `PrivateNetwork`, `Remote` |
| `Capabilities` | herstellerneutrale Tags, z. B. vision/files/terminal/docker |
| `Constraints` | statische harte Grenzen |
| `CostRate` | optionale, explizite Kostensätze |

`ProviderId`, `DisplayName` und freie Metadaten dürfen nicht zur impliziten
Vendor-Erkennung verwendet werden.

### 3.2 Kapazitätsvektor

Kapazitäten sind typisierte, vergleichbare Werte:

| Kapazität | Einheit | Bedeutung |
|---|---|---|
| `MaxConcurrentExecutions` | Slots | harte parallele Reservierungsgrenze |
| `ContextWindowTokens` | Tokens | maximal verarbeitbarer Kontext |
| `RequestsPerMinute` | Requests | optionales Zeitfensterlimit |
| `TokensPerMinute` | Tokens | optionales Durchsatzlimit |
| `WorkUnitsPerMinute` | abstrakte Einheiten | nicht-tokenbasierte Arbeit |
| `MemoryMiB` | MiB | optionaler Speicherrahmen |

Regel für unbekannte Werte: `null` bedeutet unbekannt, niemals unbegrenzt. Fordert eine
Execution einen Mindestwert, wird ein Profil mit unbekanntem Wert abgelehnt. Ohne
Mindestanforderung darf es mit konservativer Bewertung teilnehmen.

## 4. Ressourcenanforderung

Der Scheduler bzw. die Runtime erstellt pro Execution eine `AiResourceRequest`:

- `ExecutionRequestId` und `TaskId`
- erforderlicher `Kind`
- erforderliche Capability-Tags
- Mindestwerte für Kontext, Speicher und Work Units
- geschätzte Input-/Output-Tokens bzw. Work Units
- benötigte Dauer/Lease-Zeit
- Kosten-Einheit und Budget-Scope
- optional `PinnedResourceId`

Ein Pin ist hart: Ist die angegebene Ressource nicht zulässig oder verfügbar, wird die
Anforderung abgelehnt. Es gibt keinen stillen Fallback auf eine andere Ressource.

## 5. Kosten und Budgets

### 5.1 Kostensätze

Kosten werden als `decimal` plus expliziter `CostUnit` dargestellt. Erlaubte Bestandteile:

- fixer Betrag pro Execution
- Betrag pro 1.000 Input-Einheiten
- Betrag pro 1.000 Output-Einheiten
- Betrag pro Work Unit

Der Resource Manager führt keine Währungsumrechnung durch. Profil, Request und Budget
müssen dieselbe `CostUnit` verwenden; andernfalls ist die Ressource nicht wählbar.

### 5.2 Budget-Scope

Budgets können für `Runtime`, `Project`, `Session` oder `Task` gelten. Engere Scopes
ergänzen die übergeordneten Limits; sie ersetzen sie nicht. Jede Ebene besitzt:

- `HardLimit`
- optionale `WarningThreshold`
- `Spent`
- `Reserved`
- Zeitfenster: `Execution`, `Hour`, `Day` oder `Month`

Vor einer Auswahl wird der geschätzte Betrag **atomar reserviert**. Eine Reservation
wird anschließend:

- `Committed`: tatsächliche Kosten werden abgerechnet,
- `Released`: Ausführung fand nicht statt,
- `Expired`: Lease lief vor dem Start ab.

`Spent + Reserved + Estimated` darf kein aktives Hard Limit überschreiten. Der Manager
darf ein Limit niemals selbst erhöhen oder ignorieren.

Kapazitäts- und Budgetreservation bilden eine atomare Einheit: Scheitert eine Seite,
wird die andere im selben kritischen Abschnitt zurückgerollt. Eine Entscheidung darf
nie einen Slot ohne Budget oder Budget ohne Slot hinterlassen. Budgetfenster werden in
UTC bestimmt; Reset-Zeitpunkte sind Bestandteil des jeweiligen Budget-Contracts.

## 6. Lastprofil

Lastdaten sind zeitgestempelte Telemetrie, keine Identität:

- `ObservedAtUtc`
- belegte/gesamte Concurrent Slots
- Request-, Token- und Work-Unit-Auslastung
- Queue-Latenz
- P95-Ausführungslatenz
- Fehlerquote im Messfenster
- `Healthy` und `Throttled`

Telemetrie ist standardmäßig nach zwei Minuten veraltet. Veraltete Werte dürfen keine
freie Kapazität vortäuschen: Bei einer harten Kapazitätsanforderung wird das Profil
abgelehnt, ansonsten stark konservativ bewertet. Eigene aktive Reservationszähler des
Managers gelten unabhängig von externer Telemetrie immer als harte Grenze.

## 7. Auswahlalgorithmus

Die Auswahl erfolgt deterministisch in zwei Phasen.

### 7.1 Harte Filter

Ein Profil wird ausgeschlossen, wenn mindestens eines gilt:

1. deaktiviert, ungesund oder gedrosselt,
2. Kind oder Capability fehlt,
3. Mindestkapazität fehlt oder ist unbekannt,
4. kein Concurrent Slot verfügbar,
5. Telemetrie ist für eine harte Anforderung zu alt,
6. Kosten-Einheit ist inkompatibel,
7. ein Budget-Hard-Limit würde überschritten,
8. ein Resource-Pin zeigt auf ein anderes Profil.

### 7.2 Ranking

Nur verbleibende Profile werden bewertet. Der Score ist vollständig konfigurierbar,
aber die Standardgewichtung ist fest dokumentiert:

| Faktor | Gewicht |
|---|---:|
| freie Kapazität / aktuelle Last | 35 % |
| geschätzte Kosten | 30 % |
| Zuverlässigkeit (Fehlerquote) | 20 % |
| Latenz | 10 % |
| Locality-Präferenz | 5 % |

Alle Teilwerte werden auf `[0,1]` normalisiert. Gleichstände werden über `ResourceId`
ordinal aufgelöst. Namen, Vendor und Modellbezeichnungen sind keine Faktoren.
Fehlt ein optionaler Messwert, erhält der betreffende Teilfaktor konservativ den
schlechtesten Wert; Gewichte werden nicht stillschweigend auf andere Faktoren verteilt.

Die Entscheidung enthält den Score samt Teilwerten und alle Ablehnungsgründe der
nicht gewählten Kandidaten. Dadurch bleibt sie auditierbar.

## 8. Entscheidungs- und Reservationsmodell

Das Ergebnis ist entweder:

- `Selected`: Resource-ID, Reservation-ID, Ablaufzeit, Kostenschätzung, Score-Breakdown,
- `Denied`: stabiler Reason-Code plus Kandidaten-Ablehnungen.

Jede Ablehnung enthält außerdem `Retryable` und optional `RetryAfterUtc`. Temporäre
Kapazität, Drosselung oder veraltete Telemetrie können retryfähig sein. Inkompatible
Capabilities, Cost Units oder überschrittene Hard Budgets sind ohne externe Änderung
nicht retryfähig. Ein harter Pin bleibt auch bei retryfähiger Nichtverfügbarkeit erhalten.

Vorgesehene Reason-Codes:

- `no_matching_resource`
- `capacity_unavailable`
- `telemetry_stale`
- `budget_exceeded`
- `cost_unit_mismatch`
- `pinned_resource_unavailable`
- `resource_unhealthy`

Reservationszustände: `Reserved → Committed | Released | Expired`. Übergänge sind
idempotent; dieselbe Reservation darf nie doppelt abgerechnet werden.

## 9. Was der Resource Manager entscheiden darf

- Ressourcen registrieren, deaktivierte Profile ausfiltern und Telemetrie aktualisieren
- Kandidaten anhand expliziter Anforderungen filtern und deterministisch bewerten
- Kapazität und geschätzte Kosten atomar reservieren
- eine Anforderung mit stabilem Reason-Code ablehnen
- Reservations freigeben, verfallen lassen und tatsächliche Kosten verbuchen
- eine auditierbare Empfehlung an Scheduler/Runtime zurückgeben

## 10. Was er ausdrücklich nicht entscheiden darf

- Task-Owner, Rolle, Scheduler-Priorität oder Execution-Reihenfolge
- Session-Permissions, Tool-Capabilities oder Workspace-Locks
- Patch-Approval, Signatur, Marketplace-Freigabe oder Trust-Level
- Tool-Ausführung oder direkte Änderungen an Projekten
- Budgeterhöhung, Währungsumrechnung oder Umgehung eines Hard Limits
- Auswahl aufgrund von Vendor-/Modellnamen
- Secrets laden, speichern oder an Entscheidungen anhängen
- MCP-, UI- oder Netzwerkaktionen auslösen

## 11. Integrationsnaht zu Phase 8.2

Die spätere interne Integration erfolgt zwischen Lease und `Running`:

1. Scheduler vergibt eine Session-Lease.
2. Runtime erstellt eine Resource-Anforderung.
3. Resource Manager filtert, wählt und reserviert atomar.
4. Nur bei `Selected` darf der Scheduler nach `Running` wechseln.
5. Bei retryfähiger Ablehnung gibt der Scheduler Claim und Lease kontrolliert frei und
   setzt `NotBeforeUtc` auf `RetryAfterUtc`. Diese Resource-Deferral verbraucht weder
   einen Tool-Ausführungsversuch noch `MaxAttempts`; dafür ist ein eigener, begrenzter
   Deferral-Zähler vorzusehen.
6. Nicht retryfähige Ablehnungen setzen die Execution mit dem Resource-Reason-Code auf
   `Failed`, ohne einen Tool-Handler aufzurufen.
7. Bei Start wird die Reservation gebunden; bei Cancel/Failure freigegeben bzw. verbucht.

Die bestehende Tool-Sicherheitskette bleibt danach unverändert vollständig aktiv.

## 12. Pflicht-Tests für die Implementierung

### Profile und Filter

1. fehlende Capability wird hart abgelehnt,
2. unbekannte Mindestkapazität wird nicht als unbegrenzt behandelt,
3. deaktivierte, ungesunde und gedrosselte Ressourcen werden ausgeschlossen,
4. harter Resource-Pin fällt nicht still auf eine andere Ressource zurück,
5. veraltete Telemetrie kann keine harte Kapazitätsanforderung erfüllen.

### Kapazität und Parallelität

6. Concurrent-Slot-Limit wird exakt eingehalten,
7. parallele Reservierungen können denselben letzten Slot nicht doppelt erhalten,
8. Release und Expiry geben Kapazität genau einmal frei,
9. Commit/Release/Expiry sind idempotent.

### Kosten und Budgets

10. Budget-Hard-Limit blockiert vor der Ausführung,
11. `Spent + Reserved` wird gemeinsam berücksichtigt,
12. inkompatible Cost Units werden abgelehnt,
13. parallele Budget-Reservierungen können das Limit nicht überziehen,
14. tatsächliche Kosten werden nur einmal committed.

### Ranking und Neutralität

15. gleiche Eingaben erzeugen dieselbe Auswahl und denselben Score,
16. Last, Kosten, Zuverlässigkeit, Latenz und Locality folgen den dokumentierten Gewichten,
17. Gleichstand wird stabil über `ResourceId` gelöst,
18. Vendor-/Display-Namen verändern die Auswahl nicht.

### Sicherheits- und Integrationsgrenzen

19. Resource Manager verändert weder Task-Owner noch Scheduler-Priorität,
20. Resource Manager führt keinen Tool-Handler aus,
21. Denial umgeht keine Permission-, Lock- oder Approval-Regel,
22. Cancel/Failure hinterlässt keine Reservation oder Budgetbuchung im Schwebezustand,
23. Contracts bleiben BCL-only,
24. retryfähige Ablehnung requeued ohne Verbrauch von `MaxAttempts`,
25. nicht retryfähige Ablehnung ruft keinen Tool-Handler auf,
26. vollständige bestehende Regression bleibt grün.

## 13. Implementierungsreihenfolge nach Freigabe

1. reine Contracts: Profile, Request, Decision, Reservation, Budget, Telemetrie,
2. Registry und Telemetrie-Store,
3. atomare Capacity-/Budget-Reservations,
4. deterministischer Filter und Scorer,
5. Unit- und Parallelitätstests,
6. interne Scheduler-Integration,
7. vollständige Regression und eigener PR.

MCP, UI und Host-Adapter beginnen erst in Phase 8.4 und sind kein Teil dieses Plans.
