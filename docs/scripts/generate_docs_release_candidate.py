#!/usr/bin/env python3
"""
AAIA Documentation Output Generation & Release Candidate Packaging — Phase 11.5.9

Liest docs/export/export-manifest.json und erzeugt lokale Release-Candidate-Artefakte
unter docs/.release-candidate/. Es wird nichts deployed, nichts importiert und keine
produktive Ausgabe veröffentlicht.

Aufruf:
    python docs/scripts/generate_docs_release_candidate.py [repo-root]
"""
from __future__ import annotations

import hashlib
import html
import json
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


RC_STATUS = "release_candidate"
FORBIDDEN_ACTIVE_STATUSES = {"deployed", "generated", "imported"}
PDF_EXPORTS = {
    "user-manual": "AAIA-Benutzerhandbuch.md",
    "developer-guide": "AAIA-Entwicklerhandbuch.md",
    "admin-guide": "AAIA-Administratorhandbuch.md",
}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def write_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def repo_docs_path(root: Path, docs_rel: str) -> Path:
    return root / "docs" / docs_rel


def safe_slug(value: str) -> str:
    return value.strip("/").replace("/", "__").replace("\\", "__") or "index"


def source_commit(root: Path) -> str | None:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=root,
            text=True,
            capture_output=True,
            check=True,
        )
        return result.stdout.strip()
    except Exception:  # noqa: BLE001 - commit is optional metadata
        return None


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
            path = repo_docs_path(root, source)
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
                "status": RC_STATUS,
                "routeBase": export.get("routeBase"),
                "sources": existing,
            }
        )
    return plan


def first_heading(markdown: str, fallback: str) -> str:
    for line in markdown.splitlines():
        if line.startswith("# "):
            return line[2:].strip()
    return fallback


def render_html(title: str, route: str, source_rel: str, markdown: str) -> str:
    escaped = html.escape(markdown)
    return f"""<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <title>{html.escape(title)}</title>
  <meta name="robots" content="noindex">
  <meta name="aaia-status" content="{RC_STATUS}">
</head>
<body>
  <header>
    <p><strong>AAIA Documentation Release Candidate</strong> — lokale RC-Ausgabe, keine Veröffentlichung.</p>
    <p>Route: <code>{html.escape(route)}</code></p>
    <p>Quelle: <code>{html.escape(source_rel)}</code></p>
  </header>
  <main>
    <pre>{escaped}</pre>
  </main>
</body>
</html>
"""


def parse_routing_map(root: Path) -> list[dict]:
    routing_path = root / "docs/website-help/routing-map.md"
    routes: list[dict] = []
    for line in routing_path.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line.startswith("| `/"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) < 2:
            continue
        route = cells[0].strip("`")
        source_cell = cells[1]
        source = source_cell.split(" und ", 1)[0].strip("`")
        if not source.startswith("docs/") or not source.endswith(".md"):
            continue
        routes.append({"route": route, "sourcePath": source})
    return routes


def create_website_rc(root: Path, rc_root: Path) -> None:
    website_root = rc_root / "website"
    routes = parse_routing_map(root)
    written_routes: list[dict] = []
    for route in routes:
        source_rel = route["sourcePath"].removeprefix("docs/")
        source_path = repo_docs_path(root, source_rel)
        if not source_path.exists():
            raise FileNotFoundError(f"Routing-Quelle fehlt: {route['sourcePath']}")
        markdown = source_path.read_text(encoding="utf-8", errors="replace")
        title = first_heading(markdown, source_rel)
        target = website_root / safe_slug(route["route"]) / "index.html"
        write_text(target, render_html(title, route["route"], route["sourcePath"], markdown))
        written_routes.append({"route": route["route"], "sourcePath": route["sourcePath"], "artifact": str(target.relative_to(rc_root)).replace("\\", "/")})

    aliases = load_json(root / "docs/website-help/legacy-aliases.json")
    write_json(website_root / "legacy-aliases.json", aliases)
    write_json(
        website_root / "routing-map.json",
        {
            "schemaVersion": 1,
            "status": RC_STATUS,
            "notDeployed": True,
            "routes": written_routes,
        },
    )


def combined_markdown(root: Path, export: dict, title: str) -> str:
    parts = [
        f"# {title}",
        "",
        "> Status: release_candidate, lokale RC-Ausgabe",
        f"> Erstellt: {datetime.now(timezone.utc).isoformat()}",
        "> Hinweis: Markdown bleibt kanonische Quelle; diese Datei ist kein Produktversprechen.",
        "",
    ]
    for source in [s for s in export["sources"] if s.endswith(".md")]:
        content = repo_docs_path(root, source).read_text(encoding="utf-8", errors="replace")
        parts.extend([f"\n\n---\n\n## Quelle: `docs/{source}`\n", content])
    return "\n".join(parts).strip() + "\n"


def create_pdf_rc(root: Path, rc_root: Path, plan: list[dict]) -> None:
    pdf_root = rc_root / "pdf"
    pdf_status = {
        "schemaVersion": 1,
        "status": RC_STATUS,
        "pdfGeneration": "skipped",
        "reason": "Keine lokale PDF-Toolchain erkannt.",
        "markdownSources": [],
    }
    titles = {
        "user-manual": "AAIA Benutzerhandbuch",
        "developer-guide": "AAIA Entwicklerhandbuch",
        "admin-guide": "AAIA Administratorhandbuch",
    }
    for export in plan:
        if export["id"] not in PDF_EXPORTS:
            continue
        file_name = PDF_EXPORTS[export["id"]]
        write_text(pdf_root / file_name, combined_markdown(root, export, titles[export["id"]]))
        pdf_status["markdownSources"].append(file_name)

    pandoc = shutil.which("pandoc")
    if pandoc:
        generated: list[str] = []
        skipped: list[dict] = []
        for md_name in pdf_status["markdownSources"]:
            md_path = pdf_root / md_name
            pdf_path = md_path.with_suffix(".pdf")
            try:
                subprocess.run([pandoc, str(md_path), "-o", str(pdf_path)], cwd=root, check=True, capture_output=True, text=True)
                generated.append(pdf_path.name)
            except Exception as exc:  # noqa: BLE001
                skipped.append({"source": md_name, "reason": str(exc)})
        pdf_status["pdfGeneration"] = "attempted"
        pdf_status["generatedPdfFiles"] = generated
        pdf_status["skippedPdfFiles"] = skipped

    write_json(pdf_root / "pdf-status.json", pdf_status)


def create_in_app_rc(root: Path, rc_root: Path) -> None:
    context_map = load_json(root / "docs/help/in-app-context-map.json")
    contexts = []
    source_files: dict[str, dict] = {}
    for context in context_map.get("contexts", []):
        primary = context["primaryHelp"]
        related = context.get("related", [])
        contexts.append(
            {
                "contextId": context["contextId"],
                "title": context["title"],
                "status": RC_STATUS,
                "audience": context.get("audience", []),
                "primaryHelp": primary,
                "related": related,
                "reasonCodes": context.get("reasonCodes", []),
            }
        )
        for source in [primary, *related]:
            if not source.endswith(".md"):
                continue
            source_path = repo_docs_path(root, source)
            source_files[source] = {
                "sourcePath": source,
                "content": source_path.read_text(encoding="utf-8", errors="replace"),
            }

    write_json(
        rc_root / "in-app/help-contexts.json",
        {
            "schemaVersion": context_map.get("schemaVersion"),
            "status": RC_STATUS,
            "notDeployed": True,
            "notImported": True,
            "contexts": contexts,
            "sources": source_files,
        },
    )


def create_aaiam_rc(root: Path, rc_root: Path) -> None:
    import_map = load_json(root / "docs/help/aaiam-import-map.json")
    entries = []
    for entry in import_map.get("entries", []):
        source = entry["sourcePath"]
        source_path = repo_docs_path(root, source)
        entry_payload = {
            "id": entry["id"],
            "sourcePath": source,
            "sourceVersion": sha256_file(source_path) if source_path.exists() else None,
            "status": RC_STATUS,
            "audience": entry.get("audience", []),
            "redactionStatus": "requires-redaction" if entry.get("requiresRedaction") else "redaction-not-required",
            "importAllowed": bool(entry.get("importAllowed")),
            "dbWrite": False,
        }
        entries.append(entry_payload)

    write_json(
        rc_root / "aaiam/aaiam-import-package.json",
        {
            "schemaVersion": import_map.get("schemaVersion"),
            "status": RC_STATUS,
            "notImported": True,
            "dbWrite": False,
            "canonicalSource": "markdown",
            "entries": entries,
        },
    )


def collect_artifacts(rc_root: Path) -> list[dict]:
    artifacts = []
    for path in sorted(rc_root.rglob("*")):
        if not path.is_file() or path.name == "release-manifest.json":
            continue
        artifacts.append(
            {
                "path": str(path.relative_to(rc_root)).replace("\\", "/"),
                "sha256": sha256_file(path),
                "bytes": path.stat().st_size,
            }
        )
    return artifacts


def create_release_manifest(root: Path, rc_root: Path, manifest_hash: str) -> None:
    release_manifest = {
        "schemaVersion": 1,
        "status": RC_STATUS,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "sourceCommit": source_commit(root),
        "exportManifestHash": manifest_hash,
        "notDeployed": True,
        "notImported": True,
        "artifacts": collect_artifacts(rc_root),
    }
    write_json(rc_root / "release-manifest.json", release_manifest)


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    manifest_path = root / "docs/export/export-manifest.json"
    rc_root = root / "docs/.release-candidate"

    manifest = load_json(manifest_path)
    assert_manifest(manifest)
    plan = validate_sources(root, manifest)

    if rc_root.exists():
        shutil.rmtree(rc_root)
    rc_root.mkdir(parents=True)

    create_website_rc(root, rc_root)
    create_pdf_rc(root, rc_root, plan)
    create_in_app_rc(root, rc_root)
    create_aaiam_rc(root, rc_root)
    create_release_manifest(root, rc_root, sha256_file(manifest_path))

    print(f"OK Documentation release candidate: {rc_root}")
    print("  - website/")
    print("  - pdf/")
    print("  - in-app/help-contexts.json")
    print("  - aaiam/aaiam-import-package.json")
    print("  - release-manifest.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
