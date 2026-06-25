#!/usr/bin/env python3
"""
AAIA Documentation Approved Release Execution Adapter — Phase 11.5.11

Prüft Gate, Checklist, Release-Manifest und Target-Plan. Ohne approved Gate blockiert das
Script fail-closed. Es setzt keine Freigabe und veröffentlicht nichts.

Aufruf:
    python docs/scripts/execute_docs_release_candidate.py [repo-root] [--dry-run]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path


AI_APPROVER_IDS = {"ai", "codex", "chatgpt", "claude", "assistant", "llm"}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ExecutionAdapter:
    def __init__(self, root: Path, rc_root: Path, gate: dict, target: dict) -> None:
        self.root = root
        self.rc_root = rc_root
        self.gate = gate
        self.target = target

    @property
    def id(self) -> str:
        return str(self.target.get("id"))

    def audit_plan(self) -> dict:
        return {
            "target": self.id,
            "adapter": self.target.get("adapter"),
            "enabled": self.target.get("enabled"),
            "requiredGateFlag": self.target.get("requiredGateFlag"),
            "executionAllowed": self.target.get("executionAllowed"),
            "auditRequired": self.target.get("auditRequired"),
            "outputPath": self.target.get("outputPath"),
            "targetPath": self.target.get("targetPath"),
        }

    def check(self) -> list[str]:
        reasons: list[str] = []
        gate_flag = self.target.get("requiredGateFlag")
        if self.gate.get("gateStatus") != "approved":
            reasons.append("gate_not_approved")
        if gate_flag and self.gate.get(gate_flag) is not True:
            reasons.append(f"{gate_flag}_false")
        if self.target.get("enabled") is not True:
            reasons.append("target_disabled")
        if self.target.get("executionAllowed") is not True:
            reasons.append("target_execution_not_allowed")
        return reasons

    def execute(self, dry_run: bool) -> dict:
        reasons = self.check()
        return {
            "target": self.id,
            "status": "blocked" if reasons else "ready",
            "dryRun": dry_run,
            "reasons": reasons,
            "audit": self.audit_plan(),
        }


class WebsiteExecutionAdapter(ExecutionAdapter):
    pass


class PdfPublicationAdapter(ExecutionAdapter):
    pass


class InAppHelpPackagingAdapter(ExecutionAdapter):
    pass


class AaiamImportAdapter(ExecutionAdapter):
    def check(self) -> list[str]:
        reasons = super().check()
        if self.target.get("libraryAvailable") is not True:
            reasons.append("aaiam_library_unavailable")
        if self.target.get("targetConfigured") is not True:
            reasons.append("aaiam_target_not_configured")
        return reasons


ADAPTERS = {
    "WebsiteExecutionAdapter": WebsiteExecutionAdapter,
    "PdfPublicationAdapter": PdfPublicationAdapter,
    "InAppHelpPackagingAdapter": InAppHelpPackagingAdapter,
    "AaiamImportAdapter": AaiamImportAdapter,
}


def validate_release_manifest(rc_root: Path, manifest: dict) -> list[str]:
    errors: list[str] = []
    if manifest.get("status") != "release_candidate":
        errors.append("release_manifest_not_release_candidate")
    if manifest.get("notDeployed") is not True:
        errors.append("release_manifest_notDeployed_not_true")
    if manifest.get("notImported") is not True:
        errors.append("release_manifest_notImported_not_true")
    for artifact in manifest.get("artifacts", []):
        rel = artifact.get("path")
        expected = artifact.get("sha256")
        if not rel or not expected:
            errors.append("artifact_missing_path_or_hash")
            continue
        path = rc_root / rel
        if not path.exists():
            errors.append(f"artifact_missing:{rel}")
            continue
        if sha256_file(path) != expected:
            errors.append(f"artifact_hash_mismatch:{rel}")
    return errors


def validate_checklist(checklist: dict) -> list[str]:
    errors: list[str] = []
    if checklist.get("aiMayApprove") is True:
        errors.append("ai_may_approve_true")
    if checklist.get("requiresHumanApproval") is not True:
        errors.append("human_approval_not_required")
    if checklist.get("reviewStatus") != "approved":
        errors.append("review_not_approved")
    for check in checklist.get("checks", []):
        if check.get("required") is True and check.get("status") != "approved":
            errors.append(f"required_check_not_approved:{check.get('id')}")
    return errors


def validate_gate(gate: dict) -> list[str]:
    errors: list[str] = []
    approved_by = str(gate.get("approvedBy") or "").lower()
    if approved_by in AI_APPROVER_IDS:
        errors.append("ai_approver_forbidden")
    if gate.get("requiresHumanApproval") is not True:
        errors.append("gate_human_approval_not_required")
    if gate.get("gateStatus") != "approved":
        errors.append("gate_not_approved")
    if gate.get("gateStatus") == "approved" and not gate.get("approvedBy"):
        errors.append("approved_by_missing")
    if gate.get("gateStatus") == "approved" and not gate.get("approvedAtUtc"):
        errors.append("approved_at_missing")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    rc_root = root / "docs/.release-candidate"
    gate = load_json(root / "docs/export/release-gate-manifest.json")
    checklist = load_json(root / "docs/export/manual-review-checklist.json")
    execution_plan = load_json(root / "docs/export/release-execution-plan.json")
    release_manifest = load_json(root / "docs/.release-candidate/release-manifest.json")

    global_reasons = []
    global_reasons.extend(validate_gate(gate))
    global_reasons.extend(validate_checklist(checklist))
    global_reasons.extend(validate_release_manifest(rc_root, release_manifest))

    target_results = []
    for target in execution_plan.get("targets", []):
        adapter_type = ADAPTERS.get(target.get("adapter"), ExecutionAdapter)
        adapter = adapter_type(root, rc_root, gate, target)
        target_results.append(adapter.execute(args.dry_run))

    blocked = bool(global_reasons) or any(result["status"] == "blocked" for result in target_results)
    status = "blocked" if blocked else "ready"
    audit_plan = {
        "executionStatus": status,
        "dryRun": args.dry_run,
        "sourceGateManifest": execution_plan.get("sourceGateManifest"),
        "sourceReleaseManifest": execution_plan.get("sourceReleaseManifest"),
        "globalReasons": global_reasons,
        "targets": target_results,
    }

    print(json.dumps(audit_plan, ensure_ascii=False, indent=2))
    if blocked:
        print("EXECUTION: BLOCKED")
        return 0

    print("EXECUTION: READY - no publication performed by this adapter run")
    return 0


if __name__ == "__main__":
    sys.exit(main())
