#!/usr/bin/env python3
"""
AAIA Documentation Controlled First Publication Adapter — Phase 11.5.12

Prüft Gate, Checklist, Release-Manifest und Target-Plan. Ohne approved Gate blockiert das
Script fail-closed. Es setzt keine Freigabe und veröffentlicht nichts.

Aufruf:
    python docs/scripts/execute_docs_release_candidate.py [repo-root] [--target all] [--dry-run]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path


AI_APPROVER_IDS = {"ai", "codex", "chatgpt", "claude", "assistant", "llm"}
TARGET_ALIASES = {"in-app": "inAppHelp", "inapp": "inAppHelp", "inAppHelp": "inAppHelp"}
PRIVATE_PATH_MARKERS = ("C:\\Users\\", "H:\\AAIAGitHub")


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as fh:
        return json.load(fh)


def write_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def is_placeholder_path(value: str | None) -> bool:
    return not value or value.startswith("<") or value.endswith(">")


def normalize_target(value: str) -> str:
    return TARGET_ALIASES.get(value, value)


def scan_text_artifacts(root: Path) -> tuple[bool, bool]:
    no_secrets = True
    no_private_paths = True
    secret_markers = ("-----BEGIN PRIVATE KEY-----", "ghp_", "gho_", "sk-")
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in {".md", ".json", ".html"}:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if any(marker in text for marker in secret_markers):
            no_secrets = False
        if any(marker in text for marker in PRIVATE_PATH_MARKERS):
            no_private_paths = False
    return no_secrets, no_private_paths


class ExecutionAdapter:
    def __init__(self, root: Path, rc_root: Path, gate: dict, release_manifest: dict, target: dict) -> None:
        self.root = root
        self.rc_root = rc_root
        self.gate = gate
        self.release_manifest = release_manifest
        self.target = target

    @property
    def id(self) -> str:
        return str(self.target.get("id"))

    @property
    def output_path(self) -> Path:
        configured = str(self.target.get("outputPath") or "")
        if configured.startswith("docs/.release-candidate/"):
            return self.root / configured
        return self.rc_root / configured

    def artifact_hashes(self) -> list[dict]:
        prefix = str(Path(str(self.target.get("outputPath", ""))).as_posix()).removeprefix("docs/.release-candidate/")
        return [
            artifact
            for artifact in self.release_manifest.get("artifacts", [])
            if str(artifact.get("path", "")).startswith(prefix)
        ]

    def audit_plan(self) -> dict:
        return {
            "target": self.id,
            "adapter": self.target.get("adapter"),
            "targetMode": self.target.get("targetMode"),
            "enabled": self.target.get("enabled"),
            "requiredGateFlag": self.target.get("requiredGateFlag"),
            "executionAllowed": self.target.get("executionAllowed"),
            "auditRequired": self.target.get("auditRequired"),
            "outputPath": self.target.get("outputPath"),
            "targetPath": self.target.get("targetPath"),
        }

    def base_reasons(self, staging_only: bool) -> list[str]:
        reasons: list[str] = []
        gate_flag = self.target.get("requiredGateFlag")
        if self.gate.get("gateStatus") != "approved":
            reasons.append("gate_not_approved")
        if gate_flag and self.gate.get(gate_flag) is not True:
            reasons.append(f"{gate_flag}_false")
        if self.target.get("enabled") is not True:
            reasons.append("target_not_enabled")
        if self.target.get("executionAllowed") is not True:
            reasons.append("target_execution_not_allowed")
        if not self.output_path.exists():
            reasons.append("target_output_missing")
        if is_placeholder_path(str(self.target.get("targetPath"))):
            reasons.append("target_config_missing")
        if self.id == "website" and self.target.get("targetMode") != "staging":
            reasons.append("website_not_staging_mode")
        if self.id == "website" and not staging_only:
            reasons.append("staging_only_required")
        return reasons

    def execute(self, dry_run: bool, staging_only: bool) -> dict:
        reasons = list(dict.fromkeys(self.base_reasons(staging_only)))
        status = "blocked" if reasons else "dry_run" if dry_run else "executed"
        return {
            "target": self.id,
            "mode": self.target.get("targetMode"),
            "status": status,
            "dryRun": dry_run,
            "reasons": reasons,
            "reasonCode": reasons[0] if reasons else "ok",
            "artifactHashes": self.artifact_hashes(),
            "notLiveDeployment": True,
            "audit": self.audit_plan(),
        }


class WebsiteExecutionAdapter(ExecutionAdapter):
    def execute(self, dry_run: bool, staging_only: bool) -> dict:
        result = super().execute(dry_run, staging_only)
        if result["status"] == "executed":
            target = Path(str(self.target["targetPath"])).resolve()
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(self.output_path, target)
            write_json(target / "execution-manifest.json", {"status": "staging", "notLiveDeployment": True})
        return result


class PdfPublicationAdapter(ExecutionAdapter):
    def execute(self, dry_run: bool, staging_only: bool) -> dict:
        result = super().execute(dry_run, staging_only)
        if result["status"] == "executed":
            result["pdfResult"] = "local_finalization_prepared"
        elif "target_config_missing" not in result["reasons"]:
            result["pdfResult"] = "pdf_toolchain_unavailable"
        return result


class InAppHelpPackagingAdapter(ExecutionAdapter):
    def execute(self, dry_run: bool, staging_only: bool) -> dict:
        result = super().execute(dry_run, staging_only)
        result["activationAllowed"] = False
        return result


class AaiamImportAdapter(ExecutionAdapter):
    def base_reasons(self, staging_only: bool) -> list[str]:
        reasons = super().base_reasons(staging_only)
        if self.target.get("libraryAvailable") is not True:
            reasons.append("aaiam_library_unavailable")
        if self.target.get("targetConfigured") is not True:
            reasons.append("target_config_missing")
        if self.target.get("targetMode") != "dry_run":
            reasons.append("aaiam_must_be_dry_run")
        if self.target.get("productiveImportAllowed") is True:
            reasons.append("productive_aaiam_import_forbidden")
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
            errors.append(f"hash_mismatch:{rel}")
    return errors


def validate_checklist(checklist: dict) -> list[str]:
    errors: list[str] = []
    if checklist.get("aiMayApprove") is True:
        errors.append("ai_may_approve_true")
    if checklist.get("requiresHumanApproval") is not True:
        errors.append("human_approval_not_required")
    if checklist.get("reviewStatus") != "approved":
        errors.append("checklist_incomplete")
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
    parser.add_argument("--target", choices=["website", "pdf", "in-app", "inapp", "inAppHelp", "aaiam", "all"], default="all")
    parser.add_argument("--dry-run", action="store_true", default=True)
    parser.add_argument("--staging-only", action="store_true", default=True)
    parser.add_argument("--require-approved-gate", action="store_true", default=True)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    rc_root = root / "docs/.release-candidate"
    gate = load_json(root / "docs/export/release-gate-manifest.json")
    checklist = load_json(root / "docs/export/manual-review-checklist.json")
    execution_plan = load_json(root / "docs/export/release-execution-plan.json")
    release_manifest = load_json(root / "docs/.release-candidate/release-manifest.json")

    global_reasons: list[str] = []
    if args.require_approved_gate:
        global_reasons.extend(validate_gate(gate))
        global_reasons.extend(validate_checklist(checklist))
    global_reasons.extend(validate_release_manifest(rc_root, release_manifest))

    selected = normalize_target(args.target)
    target_results = []
    for target in execution_plan.get("targets", []):
        if selected != "all" and target.get("id") != selected:
            continue
        adapter_type = ADAPTERS.get(target.get("adapter"), ExecutionAdapter)
        adapter = adapter_type(root, rc_root, gate, release_manifest, target)
        target_results.append(adapter.execute(args.dry_run, args.staging_only))

    no_secrets, no_private_paths = scan_text_artifacts(rc_root)
    blocked = bool(global_reasons) or any(result["status"] == "blocked" for result in target_results)
    status = "blocked" if blocked else "dry_run" if args.dry_run else "executed"
    audit_plan = {
        "startedAtUtc": datetime.now(timezone.utc).isoformat(),
        "operator": gate.get("approvedBy"),
        "approvedBy": gate.get("approvedBy"),
        "sourceCommit": release_manifest.get("sourceCommit"),
        "executionStatus": status,
        "dryRun": args.dry_run,
        "stagingOnly": args.staging_only,
        "target": selected,
        "mode": "controlled_first_publication",
        "sourceGateManifest": execution_plan.get("sourceGateManifest"),
        "sourceReleaseManifest": execution_plan.get("sourceReleaseManifest"),
        "globalReasons": global_reasons,
        "reasonCode": global_reasons[0] if global_reasons else target_results[0]["reasonCode"] if target_results else "no_target",
        "artifactHashes": release_manifest.get("artifacts", []),
        "noSecretsDetected": no_secrets,
        "noPrivatePathsDetected": no_private_paths,
        "notLiveDeployment": True,
        "targets": target_results,
    }

    write_json(rc_root / "execution-audit.json", audit_plan)
    print(json.dumps(audit_plan, ensure_ascii=False, indent=2))
    if blocked:
        print("EXECUTION: BLOCKED")
        return 0

    print("EXECUTION: READY - no live publication performed by this adapter run")
    return 0


if __name__ == "__main__":
    sys.exit(main())
