#!/usr/bin/env python3
"""
AAIA Website Staging Review Helper — Phase 11.5.14

Erzeugt eine lokale Website-Staging-Kopie aus docs/.release-candidate/website/.
Kein Live-Deployment, kein Upload, keine Domainänderung.

Aufruf:
    python docs/scripts/stage_website_help.py [repo-root]
"""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


REQUIRED_ROUTES = {"/handbuch", "/docs", "/help"}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def write_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_commit(root: Path) -> str | None:
    try:
        result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, text=True, capture_output=True, check=True)
        return result.stdout.strip()
    except Exception:  # noqa: BLE001
        return None


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    rc_root = root / "docs/.release-candidate"
    website_rc = rc_root / "website"
    staging_root = root / "docs/.staging/website"
    release_manifest_path = rc_root / "release-manifest.json"
    routing_map_path = website_rc / "routing-map.json"
    aliases_path = website_rc / "legacy-aliases.json"

    if not website_rc.exists():
        print(f"WEBSITE STAGING: BLOCKED missing {website_rc}")
        return 1
    if not release_manifest_path.exists():
        print(f"WEBSITE STAGING: BLOCKED missing {release_manifest_path}")
        return 1
    if not routing_map_path.exists():
        print(f"WEBSITE STAGING: BLOCKED missing {routing_map_path}")
        return 1
    if not aliases_path.exists():
        print(f"WEBSITE STAGING: BLOCKED missing {aliases_path}")
        return 1

    routing = load_json(routing_map_path)
    routes = routing.get("routes", [])
    route_paths = {route.get("route") for route in routes}
    missing_routes = sorted(REQUIRED_ROUTES - route_paths)
    if missing_routes:
        print(f"WEBSITE STAGING: BLOCKED missing routes {missing_routes}")
        return 1

    if staging_root.exists():
        shutil.rmtree(staging_root)
    shutil.copytree(website_rc, staging_root)

    release_manifest = load_json(release_manifest_path)
    aliases = load_json(aliases_path)
    website_hashes = [
        artifact for artifact in release_manifest.get("artifacts", [])
        if str(artifact.get("path", "")).startswith("website/")
    ]

    manifest = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "sourceCommit": source_commit(root),
        "sourceReleaseManifestHash": sha256_file(release_manifest_path),
        "status": "staging",
        "notLive": True,
        "notDeployed": True,
        "routes": routes,
        "aliases": aliases.get("aliases", []),
        "artifactHashes": website_hashes,
    }
    write_json(staging_root / "staging-manifest.json", manifest)
    write_json(
        staging_root / "staging-audit.json",
        {
            "schemaVersion": 1,
            "generatedAtUtc": manifest["generatedAtUtc"],
            "status": "staging",
            "notLive": True,
            "notDeployed": True,
            "reviewStatus": "pending",
            "routesChecked": sorted(route_paths),
            "legacyAliases": len(aliases.get("aliases", [])),
        },
    )

    print(f"OK Website staging prepared: {staging_root}")
    print("  - staging-manifest.json")
    print("  - staging-audit.json")
    print("  - no live deployment")
    return 0


if __name__ == "__main__":
    sys.exit(main())
