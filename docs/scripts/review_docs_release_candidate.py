#!/usr/bin/env python3
"""
AAIA Documentation Manual Review Gate Helper — Phase 11.5.10

Liest Release-Candidate-Manifest und Review-Checklist, prüft Artefakt-Hashes erneut und
zeigt den Gate-Status an. Das Script setzt keine Freigabe und veröffentlicht nichts.

Aufruf:
    python docs/scripts/review_docs_release_candidate.py [repo-root]
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    rc_root = root / "docs/.release-candidate"
    release_manifest_path = rc_root / "release-manifest.json"
    checklist_path = root / "docs/export/manual-review-checklist.json"
    gate_path = root / "docs/export/release-gate-manifest.json"

    errors: list[str] = []

    if not release_manifest_path.exists():
        errors.append(f"Release-Candidate-Manifest fehlt: {release_manifest_path}")
        release_manifest = {}
    else:
        release_manifest = load_json(release_manifest_path)

    checklist = load_json(checklist_path)
    gate = load_json(gate_path)

    if checklist.get("aiMayApprove") is True:
        errors.append("Checklist erlaubt KI-Freigabe; das ist verboten.")
    if gate.get("requiresHumanApproval") is not True:
        errors.append("Release-Gate verlangt keine menschliche Freigabe.")
    if gate.get("gateStatus") == "approved" and not gate.get("approvedBy"):
        errors.append("Gate ist approved, aber approvedBy fehlt.")
    if gate.get("gateStatus") != "approved":
        for flag in ("deploymentAllowed", "importAllowed", "pdfPublicationAllowed", "inAppPackagingAllowed"):
            if gate.get(flag) is True:
                errors.append(f"{flag}=true ist ohne approved Gate verboten.")

    pending_required = [
        check["id"]
        for check in checklist.get("checks", [])
        if check.get("required") is True and check.get("status") == "pending"
    ]

    for artifact in release_manifest.get("artifacts", []):
        rel = artifact.get("path")
        expected = artifact.get("sha256")
        if not rel or not expected:
            errors.append(f"Artefakt ohne path/sha256: {artifact}")
            continue
        path = rc_root / rel
        if not path.exists():
            errors.append(f"Artefakt fehlt: {rel}")
            continue
        actual = sha256_file(path)
        if actual != expected:
            errors.append(f"Hash-Abweichung: {rel}")

    print(f"Release manifest: {release_manifest_path}")
    print(f"Release status: {release_manifest.get('status', '<missing>')}")
    print(f"Gate status: {gate.get('gateStatus', '<missing>')}")
    print(f"Review status: {checklist.get('reviewStatus', '<missing>')}")
    print(f"Required pending checks: {len(pending_required)}")
    for check_id in pending_required:
        print(f"  - {check_id}")

    if errors:
        print("REVIEW GATE: FAILED")
        for error in errors:
            print(f"  - {error}")
        return 1

    print("REVIEW GATE: OK for manual review state; no approval was changed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
