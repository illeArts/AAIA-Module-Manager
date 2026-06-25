#!/usr/bin/env python3
"""
AAIA Documentation Output Pipeline Dry Run — Phase 11.5.8

Liest docs/export/export-manifest.json und erzeugt lokale Vorschau-Artefakte unter
docs/.preview/. Es wird nichts deployed, nichts importiert und keine produktive Ausgabe
erzeugt.

Aufruf:
    python docs/scripts/export_docs_dry_run.py [repo-root]
"""
from __future__ import annotations

import html
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path


PREVIEW_STATUS = "preview-only"
FORBIDDEN_ACTIVE_STATUSES = {"deployed", "generated", "imported"}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def write_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def repo_path(root: Path, docs_rel: str) -> Path:
    return root / "docs" / docs_rel


def assert_manifest(manifest: dict) -> None:
    if manifest.get("canonicalSource") != "markdown":
        raise ValueError("Exportmanifest muss canonicalSource=markdown verwenden.")
    if manifest.get("status") != "prepared-not-deployed":
        raise ValueError("Exportmanifest darf nicht als deployed markiert sein.")
    for export in manifest.get("exports", []):
        status = export.get("status")
        if status in FORBIDDEN_ACTIVE_STATUSES:
            raise ValueError(f"Export {export.get('id')} hat aktiven Status {status}.")
        for source in export.get("sources", []):
            if source.startswith("/") or ".." in Path(source).parts:
                raise ValueError(f"Unsicherer Exportpfad: {source}")


def validate_sources(root: Path, manifest: dict) -> list[dict]:
    plan: list[dict] = []
    for export in manifest.get("exports", []):
        missing: list[str] = []
        existing: list[str] = []
        for source in export.get("sources", []):
            path = repo_path(root, source)
            if path.exists():
                existing.append(source)
            else:
                missing.append(source)
        if missing:
            raise FileNotFoundError(f"Export {export.get('id')} hat fehlende Quellen: {missing}")
        plan.append(
            {
                "id": export.get("id"),
                "type": export.get("type"),
                "status": PREVIEW_STATUS,
                "routeBase": export.get("routeBase"),
                "sources": existing,
            }
        )
    return plan


def render_markdown_as_preview_html(title: str, source_rel: str, markdown: str) -> str:
    escaped = html.escape(markdown)
    return f"""<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <title>{html.escape(title)}</title>
  <meta name="robots" content="noindex">
</head>
<body>
  <header>
    <p><strong>AAIA Documentation Preview</strong> — lokale Vorschau, keine Veröffentlichung.</p>
    <p>Quelle: <code>{html.escape(source_rel)}</code></p>
  </header>
  <main>
    <pre>{escaped}</pre>
  </main>
</body>
</html>
"""


def first_heading(markdown: str, fallback: str) -> str:
    for line in markdown.splitlines():
        if line.startswith("# "):
            return line[2:].strip()
    return fallback


def create_website_preview(root: Path, preview_root: Path, plan: list[dict]) -> None:
    website_root = preview_root / "website"
    for export in plan:
        for source in export["sources"]:
            path = repo_path(root, source)
            if path.suffix.lower() != ".md":
                continue
            markdown = path.read_text(encoding="utf-8", errors="replace")
            title = first_heading(markdown, source)
            safe_name = source.replace("/", "__").replace("\\", "__").replace(".md", ".html")
            write_text(website_root / export["id"] / safe_name, render_markdown_as_preview_html(title, source, markdown))


def create_pdf_sources(root: Path, preview_root: Path, plan: list[dict]) -> None:
    pdf_root = preview_root / "pdf-source"
    for export in plan:
        markdown_sources = [s for s in export["sources"] if s.endswith(".md")]
        if not markdown_sources:
            continue
        parts = [
            f"# AAIA {export['id']} Preview Source",
            "",
            "> Status: lokale Vorschau, keine PDF-Veröffentlichung",
            f"> Erstellt: {datetime.now(timezone.utc).isoformat()}",
            "> Hinweis: Markdown bleibt kanonische Quelle; diese Datei ist kein Produktversprechen.",
            "",
        ]
        for source in markdown_sources:
            content = repo_path(root, source).read_text(encoding="utf-8", errors="replace")
            parts.extend([f"\n\n---\n\n## Quelle: `{source}`\n", content])
        write_text(pdf_root / f"{export['id']}.md", "\n".join(parts).strip() + "\n")


def create_in_app_preview(root: Path, preview_root: Path) -> None:
    context_map = load_json(root / "docs/help/in-app-context-map.json")
    payload = {
        "schemaVersion": context_map.get("schemaVersion"),
        "status": PREVIEW_STATUS,
        "canonicalSource": "markdown",
        "contexts": context_map.get("contexts", []),
    }
    write_json(preview_root / "in-app/help-contexts.json", payload)


def create_aaiam_preview(root: Path, preview_root: Path) -> None:
    import_map = load_json(root / "docs/help/aaiam-import-map.json")
    entries = []
    for entry in import_map.get("entries", []):
        preview_entry = dict(entry)
        preview_entry["previewStatus"] = PREVIEW_STATUS
        preview_entry["dbWrite"] = False
        entries.append(preview_entry)
    payload = {
        "schemaVersion": import_map.get("schemaVersion"),
        "status": PREVIEW_STATUS,
        "canonicalSource": "markdown",
        "dbWrite": False,
        "entries": entries,
    }
    write_json(preview_root / "aaiam/aaiam-import-preview.json", payload)


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    manifest_path = root / "docs/export/export-manifest.json"
    preview_root = root / "docs/.preview"

    manifest = load_json(manifest_path)
    assert_manifest(manifest)
    plan = validate_sources(root, manifest)

    if preview_root.exists():
        shutil.rmtree(preview_root)
    preview_root.mkdir(parents=True)

    write_json(
        preview_root / "export-plan.json",
        {
            "schemaVersion": 1,
            "status": PREVIEW_STATUS,
            "canonicalSource": "markdown",
            "exports": plan,
        },
    )
    create_website_preview(root, preview_root, plan)
    create_pdf_sources(root, preview_root, plan)
    create_in_app_preview(root, preview_root)
    create_aaiam_preview(root, preview_root)

    print(f"OK Documentation export dry run: {preview_root}")
    print("  - website/")
    print("  - pdf-source/")
    print("  - in-app/help-contexts.json")
    print("  - aaiam/aaiam-import-preview.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
