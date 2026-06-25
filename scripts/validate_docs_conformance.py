#!/usr/bin/env python3
"""
AAIA Documentation Conformance Guard — Phase 11.5.6

Validiert kanonische Markdown-/Help-Artefakte ohne externe Dependencies:

- JSON-Dateien parsebar,
- Markdown-Links auflösbar,
- JSON-sourcePath-/Kontextpfade auflösbar,
- bekannte abgelöste Doku-Pfade und Dublettenbegriffe nicht wieder eingeführt,
- offensichtliche Secrets und private Workspace-Pfade nicht dokumentiert.

Aufruf:
    python scripts/validate_docs_conformance.py [repo-root]
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path
from typing import Iterable


REQUIRED_JSON = [
    "docs/help/index.json",
    "docs/help/aaiam-import-map.json",
    "docs/help/in-app-context-map.json",
    "docs/website-help/legacy-aliases.json",
    "docs/export/export-manifest.json",
    "docs/schemas/help-index.schema.json",
    "docs/schemas/aaiam-import-map.schema.json",
    "docs/schemas/in-app-context-map.schema.json",
    "docs/schemas/legacy-aliases.schema.json",
    "docs/schemas/export-manifest.schema.json",
]

SCANNED_DOC_DIRS = [
    "admin-guide",
    "architecture",
    "developer-guide",
    "glossary",
    "help",
    "export",
    "phases",
    "troubleshooting",
    "user-manual",
    "website-help",
    "schemas",
]

MARKDOWN_LINK_RE = re.compile(r"\[[^\]]+\]\(([^)]+)\)")

FORBIDDEN_TEXT = [
    ("Entwickler-Trusted-Workspace", "ETW-Langform ist nicht belegt"),
    ("ETW steht", "ETW-Langform darf nicht behauptet werden"),
    ("Signatur und Marketplace-Release", "alte Signatur-Dublette"),
    ("Lokale Signaturkette", "alte Signatur-Dublette"),
    ("Signatur/Marketplace-Release", "alte Signatur-Dublette"),
    ("03-validierung-tests-build.md", "abgelöster Developer-Guide-Pfad"),
    ("02-manifest-und-permissions.md", "abgelöster Developer-Guide-Pfad"),
    ("09-sicherheit-und-laufzeitstatus.md", "abgelöster Developer-Guide-Pfad"),
]

SECRET_PATTERNS = [
    (re.compile(r"gho_[A-Za-z0-9_]{20,}"), "GitHub token"),
    (re.compile(r"ghp_[A-Za-z0-9_]{20,}"), "GitHub token"),
    (re.compile(r"sk-[A-Za-z0-9_-]{20,}"), "API key pattern"),
    (re.compile(r"(?i)\b(api[_-]?key|token|password|passwd|secret)\s*[:=]\s*['\"][^'\"]{8,}['\"]"), "secret assignment"),
    (re.compile(r"-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----"), "private key block"),
]

PRIVATE_PATH_PATTERNS = [
    (re.compile(r"C:\\Users\\[^\\\s]+", re.IGNORECASE), "private Windows user path"),
    (re.compile(r"H:\\AAIAGitHub", re.IGNORECASE), "local workspace path"),
]

ALLOWED_PRIVATE_PATH_EXAMPLES = [
    "C:\\Pfad\\",
]


def load_json(root: Path, rel: str) -> object:
    path = root / rel
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def iter_text_files(root: Path) -> Iterable[Path]:
    docs = root / "docs"
    for rel_dir in SCANNED_DOC_DIRS:
        base = docs / rel_dir
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if path.suffix.lower() in {".md", ".json"}:
                yield path


def is_external_link(target: str) -> bool:
    return (
        target.startswith("http://")
        or target.startswith("https://")
        or target.startswith("mailto:")
        or target.startswith("#")
    )


def resolve_markdown_target(base_file: Path, target: str) -> Path | None:
    clean = target.split("#", 1)[0].strip()
    if not clean or is_external_link(clean):
        return None
    return (base_file.parent / clean).resolve()


def assert_exists(errors: list[str], root: Path, path: Path, context: str) -> None:
    try:
        path.relative_to(root.resolve())
    except ValueError:
        errors.append(f"{context}: target escapes repository: {path}")
        return

    if not path.exists():
        errors.append(f"{context}: missing target: {path}")


def validate_markdown_links(root: Path, errors: list[str]) -> None:
    for md in (root / "docs").rglob("*.md"):
        if ".preview" in md.parts:
            continue
        text = md.read_text(encoding="utf-8", errors="replace")
        for match in MARKDOWN_LINK_RE.finditer(text):
            target = match.group(1)
            resolved = resolve_markdown_target(md, target)
            if resolved is not None:
                assert_exists(errors, root, resolved, f"markdown {md.relative_to(root)} -> {target}")


def validate_json_sources(root: Path, errors: list[str]) -> None:
    index_path = root / "docs/help/index.json"
    index = load_json(root, "docs/help/index.json")
    for article in index.get("articles", []):
        source = article.get("sourcePath")
        if source:
            assert_exists(
                errors,
                root,
                (index_path.parent / source).resolve(),
                f"help index {article.get('id')} sourcePath",
            )

    alias_path = root / "docs/website-help/legacy-aliases.json"
    aliases = load_json(root, "docs/website-help/legacy-aliases.json")
    for alias in aliases.get("aliases", []):
        for prop in ("sourcePath", "targetSourcePath"):
            source = alias.get(prop)
            if source:
                assert_exists(errors, root, (root / source).resolve(), f"legacy alias {alias.get('legacyPath')} {prop}")

    import_map = load_json(root, "docs/help/aaiam-import-map.json")
    for entry in import_map.get("entries", []):
        source = entry.get("sourcePath")
        if source:
            assert_exists(errors, root, (root / "docs" / source).resolve(), f"AAIAM import {entry.get('id')}")

    context_map = load_json(root, "docs/help/in-app-context-map.json")
    for context in context_map.get("contexts", []):
        for source in [context.get("primaryHelp"), *context.get("related", [])]:
            if source:
                assert_exists(errors, root, (root / "docs" / source).resolve(), f"in-app context {context.get('contextId')}")

    export_manifest = load_json(root, "docs/export/export-manifest.json")
    for export in export_manifest.get("exports", []):
        status = export.get("status", "")
        if status in {"deployed", "generated", "imported"}:
            errors.append(f"export manifest {export.get('id')}: forbidden active status '{status}'")
        for source in export.get("sources", []):
            assert_exists(errors, root, (root / "docs" / source).resolve(), f"export manifest {export.get('id')}")


def validate_forbidden_text(root: Path, errors: list[str]) -> None:
    for path in iter_text_files(root):
        rel = str(path.relative_to(root))
        text = path.read_text(encoding="utf-8", errors="replace")

        for needle, reason in FORBIDDEN_TEXT:
            if needle in text:
                errors.append(f"{rel}: forbidden text '{needle}' ({reason})")

        for pattern, reason in SECRET_PATTERNS:
            if pattern.search(text):
                errors.append(f"{rel}: possible secret ({reason})")

        for pattern, reason in PRIVATE_PATH_PATTERNS:
            for match in pattern.finditer(text):
                value = match.group(0)
                if any(value.startswith(example) for example in ALLOWED_PRIVATE_PATH_EXAMPLES):
                    continue
                errors.append(f"{rel}: private path '{value}' ({reason})")


def validate_json_shape(root: Path, errors: list[str]) -> None:
    for rel in REQUIRED_JSON:
        try:
            load_json(root, rel)
        except Exception as exc:  # noqa: BLE001 - CLI validator should report parse details
            errors.append(f"{rel}: invalid JSON: {exc}")

    import_map = load_json(root, "docs/help/aaiam-import-map.json")
    for entry in import_map.get("entries", []):
        for key in ("id", "sourcePath", "type", "audience", "status", "importAllowed", "requiresRedaction"):
            if key not in entry:
                errors.append(f"AAIAM import entry missing '{key}': {entry.get('id', '<unknown>')}")

    context_map = load_json(root, "docs/help/in-app-context-map.json")
    for context in context_map.get("contexts", []):
        for key in ("contextId", "title", "primaryHelp", "related", "reasonCodes", "audience"):
            if key not in context:
                errors.append(f"in-app context missing '{key}': {context.get('contextId', '<unknown>')}")

    export_manifest = load_json(root, "docs/export/export-manifest.json")
    for key in ("schemaVersion", "checkedAt", "status", "canonicalSource", "exports"):
        if key not in export_manifest:
            errors.append(f"export manifest missing '{key}'")
    if export_manifest.get("status") != "prepared-not-deployed":
        errors.append("export manifest status must remain 'prepared-not-deployed'")
    if export_manifest.get("canonicalSource") != "markdown":
        errors.append("export manifest canonicalSource must remain 'markdown'")
    for export in export_manifest.get("exports", []):
        for key in ("id", "type", "status", "routeBase", "sources"):
            if key not in export:
                errors.append(f"export manifest entry missing '{key}': {export.get('id', '<unknown>')}")


def validate_preview_artifacts(root: Path, errors: list[str]) -> None:
    preview_root = root / "docs/.preview"
    if not preview_root.exists():
        return

    def validate_preview_json_value(path: Path, value: object, location: str) -> None:
        if isinstance(value, dict):
            status = value.get("status")
            if status in {"deployed", "generated", "imported"}:
                errors.append(f"preview artifact has active status {status}: {path} at {location}.status")
            if value.get("dbWrite") is True:
                errors.append(f"preview artifact enables DB write: {path} at {location}.dbWrite")
            for key, child in value.items():
                validate_preview_json_value(path, child, f"{location}.{key}")
        elif isinstance(value, list):
            for index, child in enumerate(value):
                validate_preview_json_value(path, child, f"{location}[{index}]")

    required = [
        preview_root / "export-plan.json",
        preview_root / "in-app/help-contexts.json",
        preview_root / "aaiam/aaiam-import-preview.json",
    ]
    for path in required:
        if not path.exists():
            errors.append(f"preview missing required artifact: {path}")

    for path in preview_root.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() == ".json":
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except Exception as exc:  # noqa: BLE001
                errors.append(f"preview JSON invalid {path}: {exc}")
                continue
            validate_preview_json_value(path, data, "$")

    for path in preview_root.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".md", ".json", ".html"}:
            text = path.read_text(encoding="utf-8", errors="replace")
            for pattern, reason in SECRET_PATTERNS:
                if pattern.search(text):
                    errors.append(f"{path.relative_to(root)}: possible secret in preview ({reason})")
            for pattern, reason in PRIVATE_PATH_PATTERNS:
                if pattern.search(text):
                    errors.append(f"{path.relative_to(root)}: private path in preview ({reason})")
            for active_word in ("Status: deployed", "Status: generated", "Status: imported"):
                if active_word in text:
                    errors.append(f"{path.relative_to(root)}: forbidden active output claim '{active_word}'")


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    errors: list[str] = []

    validate_json_shape(root, errors)
    validate_markdown_links(root, errors)
    validate_json_sources(root, errors)
    validate_forbidden_text(root, errors)
    validate_preview_artifacts(root, errors)

    if errors:
        print("DOC CONFORMANCE: FAILED")
        for error in sorted(errors):
            print(f"  - {error}")
        return 1

    print("OK Documentation Conformance: Markdown, JSON, Redaction und AAIAM-Mappings geprueft")
    return 0


if __name__ == "__main__":
    sys.exit(main())
