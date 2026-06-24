# AAIA-Dokumentation

Diese Markdown-Dateien sind die kanonische Dokumentationsquelle des AAIA Module Managers
und der hier gepflegten AIR-Phasen. Inhalte werden nach Zielgruppe getrennt und später für
Website, PDF, In-App-Hilfe und KI-Handoffs wiederverwendet.

## Bereiche

| Bereich | Für wen? | Einstieg |
|---|---|---|
| Benutzerhandbuch | Anwender und neue Nutzer | [user-manual/index.md](user-manual/index.md) |
| Entwicklerhandbuch | ETWs und Entwickler | [developer-guide/index.md](developer-guide/index.md) |
| Administratorhandbuch | Betreiber und Administratoren | [admin-guide/index.md](admin-guide/index.md) |
| Architektur | Maintainer und technische Prüfer | [architecture/index.md](architecture/index.md) |
| Fehlerbehebung | Anwender, Entwickler und Betrieb | [troubleshooting/index.md](troubleshooting/index.md) |
| Glossar | alle Zielgruppen | [glossary/index.md](glossary/index.md) |
| Website-Hilfe | Web- und Content-Integration | [website-help/index.md](website-help/index.md) |
| Phasenabschlüsse | Maintainer und KI-Assistenten | [phases/index.md](phases/index.md) |

## Verbindlicher Ablauf für neue Phasen

1. Während der Implementierung relevante Zielgruppen und Dokumentationsauswirkungen notieren.
2. Vor dem Merge die betroffenen Handbuch-, Architektur- und Troubleshooting-Seiten aktualisieren.
3. Eine Abschlussdatei aus
   [`PHASE_FINAL_TEMPLATE.md`](phases/PHASE_FINAL_TEMPLATE.md) erstellen.
4. Tests, Sicherheitsgrenzen, bekannte Grenzen und nächsten Schritt mit überprüfbaren Quellen angeben.
5. Prüfen, dass keine Secrets, echten Zugangsdaten oder privaten Pfade enthalten sind.

Eine Phase ist dokumentarisch nicht abgeschlossen, solange ihre Abschlussdatei oder die
begründete Kennzeichnung „keine externe Dokumentationsauswirkung“ fehlt.

## Wiederverwendung

- **Website:** `/handbuch` nutzt Benutzertexte, `/docs` Entwickler- und Architekturtexte,
  `/help` Fehlerbehebung, FAQ und Glossar.
- **PDF:** Eine spätere Build-Pipeline erzeugt versionierte Ausgaben aus freigegebenen Seiten.
- **In-App-Hilfe:** Die Anwendung darf kuratierte Inhalte einbetten oder auf stabile öffentliche
  Routen verweisen; sicherheitskritische Hinweise müssen offline verfügbar bleiben.
- **KI-Handoffs:** Nur explizit freigegebene, versionsgebundene Abschnitte werden übernommen.

## Bestehende Quellen

Die bisherigen Texte unter [`help/`](help/), [`air/`](air/) und die vorhandenen
Phasenspezifikationen bleiben Quellen. Sie sind noch nicht vollständig in die neue Struktur
migriert. Das separate Repository `aaia-developer-docs` bleibt eine weitere Quelle und muss
vor einer öffentlichen Veröffentlichung gegen diese kanonische Struktur abgeglichen werden.

## Sicherheitsregel

Niemals Secrets, Tokens, Passwörter, private Schlüssel, reale Zugangsdaten oder private
Server-/Benutzerpfade dokumentieren. Beispiele müssen eindeutig fiktiv sein.
