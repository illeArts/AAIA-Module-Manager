# AAIAS und AAIAC verbinden

> Zielgruppe: Anwender, ETWs und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: sicherer Vorbereitungsrahmen; keine vollständige Installationsmatrix

AAIAS ist das serverseitige Ziel für Module. AAIAC bezeichnet Client-Ziele und
Benutzeroberflächen. Der Module Manager kann Pakete vorbereiten und übertragen, ersetzt aber
keine Zielprüfung durch AAIAS oder AAIAC.

## Vor einer Verbindung prüfen

1. Produktversionen und Zielhost festlegen.
2. Serveradresse aus vertrauenswürdiger Quelle übernehmen.
3. Authentisierung konfigurieren, ohne Tokens in Dokumente oder Prompts zu kopieren.
4. Netzwerkzugriff testen.
5. Zielhost prüfen lassen, ob Paketformat, Signatur, Manifest und Permissions akzeptiert werden.

## Sicherheitsgrenzen

- AAIAS validiert Module selbst.
- AAIAC validiert Client-Plugins selbst.
- Marketplace-Status wird nicht durch eine lokale Verbindung gesetzt.
- Test- und Developer-Funktionen gehören nicht ungeprüft in produktive Umgebungen.

## Noch offen

Eine vollständige Installations- und Verbindungsmatrix folgt erst, wenn unterstützte
AAIAS-/AAIAC-Versionen, Paketquellen und Updatekanäle verbindlich freigegeben sind.
