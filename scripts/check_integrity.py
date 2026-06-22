#!/usr/bin/env python3
"""
AAIA Repository Integrity Guard — Phase 5.10.1

Erkennt abgeschnittene C#-Dateien (NTFS-Edit-Artefakte) vor dem Build.
Läuft lokal und im CI als erster Schritt vor dotnet build.

Aufruf: python3 scripts/check_integrity.py [verzeichnis ...]
        Standard: src/
"""
import sys, os, glob, re

# Verzeichnisse komplett überspringen (generierte Dateien)
SKIP_DIRS = {'obj', 'bin', '.vs', 'TestResults'}

# Abschneide-Muster: gegen letzte nicht-leere Zeile
LAST_LINE_TRUNCATION = [
    (r'=\s*"/[a-z]',                 "Route-String abgeschnitten"),
    (r'\.ToListAsync\s*\(\s*$',      "ToListAsync()-Aufruf unvollständig"),
    (r'\.CountAsync\s*\(\s*$',       "CountAsync()-Aufruf unvollständig"),
    (r'logger\.\w+\s*\(\s*$',        "Log-Aufruf unvollständig"),
    (r'///\s*<summary>[^<]{0,100}$', "XML-Kommentar ohne </summary>"),
    (r'\bConvert\.ToBase64String\s*$',"Ausdruck abgeschnitten"),
    (r'return\s*\(token,\s*hash\s*$',"return-Ausdruck unvollständig"),
    (r'\.Replace\s*\(\s*$',          "String.Replace unvollständig"),
]

def has_namespace(content: str) -> bool:
    return bool(re.search(r'^\s*namespace\b', content, re.MULTILINE))

def is_file_scoped_namespace(content: str) -> bool:
    """namespace Foo; — ohne öffnende { Klammer."""
    return bool(re.search(r'^\s*namespace\s+[\w.]+\s*;', content, re.MULTILINE))

def is_top_level_program(content: str) -> bool:
    """Program.cs-Stil: kein namespace, keine Klasse, endet mit app.Run() o.ä."""
    return not has_namespace(content) and not re.search(r'^\s*(public|internal|private)\s+\w+\s+class\b', content, re.MULTILINE)

def collect_files(search_dirs: list[str]) -> list[str]:
    result: list[str] = []
    for d in search_dirs:
        if os.path.isfile(d) and d.endswith('.cs'):
            result.append(d)
        elif os.path.isdir(d):
            for root, dirs, files in os.walk(d):
                # Generierte Verzeichnisse überspringen
                dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
                for f in files:
                    if f.endswith('.cs'):
                        result.append(os.path.join(root, f))
    return sorted(result)

def check_file(path: str) -> list[str]:
    errors = []
    try:
        with open(path, 'r', encoding='utf-8', errors='replace') as f:
            content = f.read()
    except OSError as e:
        return [f"Lesefehler: {e}"]

    if not content.strip():
        return ["Datei ist leer"]

    lines = content.splitlines()
    last_nws = next((l.rstrip() for l in reversed(lines) if l.strip()), '')
    stripped  = last_nws.strip()

    # ── Regel 1: Erlaubte Datei-Enden ────────────────────────────────────────
    ends_with_brace     = stripped.endswith('}')
    ends_with_semicolon = stripped.endswith(';')

    if ends_with_brace:
        pass  # immer OK
    elif ends_with_semicolon:
        # OK wenn: file-scoped namespace ODER top-level program
        if not (is_file_scoped_namespace(content) or is_top_level_program(content)):
            errors.append(f"Endet mit ';' aber weder file-scoped namespace noch top-level program: {last_nws!r}")
    else:
        errors.append(f"Letzte Zeile endet nicht mit '}}' oder ';': {last_nws!r}")

    # ── Regel 2: Abschneide-Muster in letzter nicht-leerer Zeile ────────────
    for pattern, msg in LAST_LINE_TRUNCATION:
        if re.search(pattern, last_nws, re.IGNORECASE):
            errors.append(f"Abschneide-Muster: {msg}")
            break

    return errors


def main() -> int:
    search_dirs = sys.argv[1:] if len(sys.argv) > 1 else ['src']
    cs_files = collect_files(search_dirs)

    if not cs_files:
        print(f"Keine .cs-Dateien in {search_dirs} gefunden.")
        return 0

    failures: dict[str, list[str]] = {}
    for f in cs_files:
        errs = check_file(f)
        if errs:
            failures[f] = errs

    if failures:
        print(f"⚠  INTEGRITY GUARD: {len(failures)}/{len(cs_files)} Datei(en) verdächtig:\n")
        for path, errs in failures.items():
            rel = os.path.relpath(path)
            print(f"  {rel}")
            for e in errs:
                print(f"    → {e}")
        print(f"\nWiederherstellung (git-Workaround):")
        for path in failures:
            rel = os.path.relpath(path)
            print(f"  git show HEAD:{rel} > {rel}")
        print()
        return 1

    print(f"✓ Integrity Guard: {len(cs_files)} Datei(en) geprüft — alles OK")
    return 0


if __name__ == '__main__':
    sys.exit(main())
