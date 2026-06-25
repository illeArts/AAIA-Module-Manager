# Website Staging Review

> Geprüfter Stand: 2026-06-25  
> Status: Staging vorbereitet, nicht live  
> Scope: lokale Prüfung von `/handbuch`, `/docs` und `/help`

Der Website-Staging-Review prüft die vorbereiteten Website-Release-Candidate-Artefakte in
einer geschützten lokalen Staging-Ausgabe. Es findet kein Live-Deployment statt.

## RC, Staging und Live

| Begriff | Bedeutung |
|---|---|
| Release Candidate | lokale, hashgeprüfte Website-Ausgabe unter `docs/.release-candidate/website/` |
| Staging | lokale oder interne Prüfumgebung unter `docs/.staging/website/` |
| Live | öffentliche Website oder Domain; nicht Bestandteil dieser Phase |

## Zielrouten

Die Staging-Prüfung muss mindestens diese Einstiegspunkte enthalten:

- `/handbuch`
- `/docs`
- `/help`

## Prüfpflichten

- Navigation und Einstiegspunkte prüfen.
- Markdown-Links und generierte Routen prüfen.
- Legacy-Aliase prüfen.
- Quellenstatus und Zielgruppen prüfen.
- `DOCUMENTATION_TRUTH_RULE.md` anwenden.
- `glossary/term-status.md` beachten.
- Keine Secrets, Tokens, Schlüssel oder privaten Pfade.
- Keine Live-, deployed- oder Veröffentlichungsbehauptung.

## Staging-Script

Der lokale Staging-Lauf:

```powershell
python docs/scripts/stage_website_help.py .
```

Das Script darf eine lokale Kopie unter `docs/.staging/website/` erzeugen und ein
`staging-manifest.json` schreiben. Es darf keine Domain ändern, keinen Server beschreiben,
keinen WordPress-Upload ausführen und keinen Status auf deployed setzen.

## Review-Checklist

Die maschinenlesbare Checklist liegt unter
[`../export/website-staging-review-checklist.json`](../export/website-staging-review-checklist.json).
Sie bleibt initial `pending`, verlangt menschliche Prüfung und erlaubt keine KI-Freigabe.
