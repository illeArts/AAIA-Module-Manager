# AAIAM Knowledge Sync

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, keine produktive DB-Anbindung  
> Scope: Dokumentations- und Help-Wissen für spätere AAIAM-Bibliothek

AAIAM wird als späterer strukturierter Wissensspeicher für Hilfe, Handbuch, Troubleshooting,
Fehlerwissen, Glossar und freigegebene KI-Handoff-Kontexte eingeplant. Markdown bleibt die
kanonische Quelle. AAIAM ist ein importierter, versionierter und durchsuchbarer Nutzspeicher.

## Abgrenzung

| Ebene | Rolle |
|---|---|
| Markdown unter `docs/` | kanonische Quelle und Review-Ort |
| Website/PDF/In-App-Hilfe | abgeleitete Ausgaben |
| AAIAM-DB | späterer importierter Wissensspeicher |

AAIAM darf keine eigene Wahrheitsschicht werden. Wenn eine Aussage im AAIAM-Eintrag von der
Markdown-Quelle abweicht, gilt die Markdown-Quelle.

## Importfluss

```text
Markdown-Dokumentation
  -> Metadaten prüfen
  -> DOCUMENTATION_TRUTH_RULE anwenden
  -> Secrets/private Pfade redigieren
  -> Status klassifizieren
  -> AAIAM Knowledge Entry erzeugen
  -> Suche, Fehlercode-Mapping und In-App-Hilfe nutzbar machen
```

## Importierbare Inhalte

- Benutzerhandbuch-Seiten,
- Entwicklerhandbuch-Seiten,
- Administratorhandbuch-Seiten,
- Troubleshooting-Lösungen,
- Fehlercodes und Reason-Codes,
- bekannte Ursachen und geprüfte sichere Handlungen,
- Recovery-Anleitungen,
- Sicherheitswarnungen,
- Glossar-Begriffe und Begriffstatus,
- Phase-Abschlusszusammenfassungen,
- freigegebene und redigierte KI-Handoff-Kontexte.

## Nicht automatisch importieren

- historische Rohtexte ohne Statusprüfung,
- interne Runbooks,
- private Benutzer- oder Serverpfade,
- Secrets, Tokens, Passwörter oder private Schlüssel,
- echte Zugangsdaten,
- nicht belegte Produktbehauptungen,
- offene oder erfundene Langformen, z. B. ETW, DUKI oder BBK, solange nicht bestätigt,
- ungeprüfte Chat- oder Handoff-Notizen.

## Knowledge-Entry-Mindestfelder

Ein späterer AAIAM-Eintrag sollte mindestens enthalten:

- `id`,
- `title`,
- `type`,
- `audience`,
- `source_path`,
- `source_version`,
- `last_verified`,
- `status`,
- `tags`,
- `error_codes`,
- `reason_codes`,
- `related_terms`,
- `content_markdown`,
- `content_summary`,
- `safety_level`,
- `redaction_status`.

## Sicherheitsregel

AAIAM darf nur geprüfte, redigierte und versionierte Inhalte übernehmen. Historische Texte,
Handoffs und alte Dokumente werden nicht automatisch als heutige Produktwahrheit importiert.
