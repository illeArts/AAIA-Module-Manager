#!/usr/bin/env python3
"""
AAIA Repository Integrity Guard — Phase 5.10.1

Erkennt abgeschnittene C#-Dateien (NTFS-Edit-Artefakte) vor dem Build.
Kompatibel mit Python 3.7+.

Aufruf: python scripts/check_integrity.py [verzeichnis ...]
        Standard: src/
"""
from __future__ import annotations
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

def has_namespace(content):
    return bool(re.search(r'^\s*namespace\b', content, re.MULTILINE))

def is_file_scoped_namespace(content):
    return bool(re.search(r'^\s*namespace\s+[\w.]+\s*;', content, re.MULTILINE))

def is_top_level_program(content):
    return not has_namespace(content) and \
           not re.search(r'^\s*(public|internal|private)\s+\w+\s+class\b', content, re.MULTILINE)

def collect_files(search_dirs):
    result = []
    for d in search_dirs:
        if os.path.isfile(d) and d.endswith('.cs'):
            result.append(d)
        elif os.path.isdir(d):
            for root, dirs, files in os.walk(d):
                dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
                for f in files:
                    if f.endswith('.cs'):
                        result.append(os.path.join(root, f))
    return sorted(result)

def check_file(path):
    errors = []
    try:
        with open(path, 'r', encoding='utf-8', errors='replace') as f:
            content = f.read()
    except OSError as e:
        return ["Lesefehler: {}".format(e)]

    if not content.strip():
        return ["Datei ist leer"]

    lines = content.splitlines()
    last_nws = next((l.rstrip('\r').rstrip() for l in reversed(lines) if l.strip()), '')
    stripped  = last_nws.strip()

    if stripped.endswith('}'):
        pass
    elif stripped.endswith(';'):
        if not (is_file_scoped_namespace(content) or is_top_level_program(content)):
            errors.append("Endet mit ';' aber kein file-scoped NS / top-level: {!r}".format(last_nws))
    else:
        errors.append("Letzte Zeile endet nicht mit '}}' oder ';': {!r}".format(last_nws))

    for pattern, msg in LAST_LINE_TRUNCATION:
        if re.search(pattern, last_nws, re.IGNORECASE):
            errors.append("Abschneide-Muster: {} — {!r}".format(msg, last_nws))
            break

    return errors


def main():
    search_dirs = sys.argv[1:] if len(sys.argv) > 1 else ['src']
    cs_files = collect_files(search_dirs)

    if not cs_files:
        print("Keine .cs-Dateien in {} gefunden.".format(search_dirs))
        return 0

    failures = {}
    for f in cs_files:
        errs = check_file(f)
        if errs:
            failures[f] = errs

    if failures:
        print("INTEGRITY GUARD: {}/{} Datei(en) verdaechtig:\n".format(len(failures), len(cs_files)))
        for path, errs in failures.items():
            rel = os.path.relpath(path)
            print("  " + rel)
            for e in errs:
                print("    -> " + e)
        print()
        return 1

    print("OK Integrity Guard: {} Datei(en) geprueft -- alles OK".format(len(cs_files)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
