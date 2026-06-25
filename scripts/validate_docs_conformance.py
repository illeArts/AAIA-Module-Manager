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
import hashlib
from pathlib import Path
from typing import Iterable


REQUIRED_JSON = [
    "docs/help/index.json",
    "docs/help/aaiam-import-map.json",
    "docs/help/in-app-context-map.json",
    "docs/website-help/legacy-aliases.json",
    "docs/export/export-manifest.json",
    "docs/export/manual-review-checklist.json",
    "docs/export/release-gate-manifest.json",
    "docs/export/release-execution-plan.json",
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
        if ".preview" in md.parts or ".release-candidate" in md.parts:
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

    checklist = load_json(root, "docs/export/manual-review-checklist.json")
    if checklist.get("reviewStatus") not in {"pending", "approved", "rejected"}:
        errors.append("manual review checklist reviewStatus must be pending, approved or rejected")
    if checklist.get("requiresHumanApproval") is not True:
        errors.append("manual review checklist must require human approval")
    if checklist.get("aiMayApprove") is True:
        errors.append("manual review checklist must not allow AI approval")
    for check in checklist.get("checks", []):
        for key in ("id", "label", "required", "status"):
            if key not in check:
                errors.append(f"manual review checklist entry missing '{key}': {check.get('id', '<unknown>')}")
        if check.get("status") not in {"pending", "approved", "rejected"}:
            errors.append(f"manual review checklist entry has invalid status: {check.get('id', '<unknown>')}")

    gate = load_json(root, "docs/export/release-gate-manifest.json")
    if gate.get("gateStatus") not in {"pending", "approved", "rejected"}:
        errors.append("release gate status must be pending, approved or rejected")
    if gate.get("requiresHumanApproval") is not True:
        errors.append("release gate must require human approval")
    if gate.get("gateStatus") == "approved" and not gate.get("approvedBy"):
        errors.append("release gate approved status requires approvedBy")
    if gate.get("gateStatus") == "approved" and not gate.get("approvedAtUtc"):
        errors.append("release gate approved status requires approvedAtUtc")
    if gate.get("gateStatus") != "approved":
        for flag in ("deploymentAllowed", "importAllowed", "pdfPublicationAllowed", "inAppPackagingAllowed"):
            if gate.get(flag) is True:
                errors.append(f"release gate must not set {flag}=true without approved status")
    if gate.get("deploymentAllowed") is True and gate.get("gateStatus") != "approved":
        errors.append("deploymentAllowed requires approved release gate")
    if gate.get("importAllowed") is True and gate.get("gateStatus") != "approved":
        errors.append("importAllowed requires approved release gate")
    approved_by = str(gate.get("approvedBy") or "").lower()
    if approved_by in {"ai", "codex", "chatgpt", "claude", "assistant", "llm"}:
        errors.append("release gate approvedBy must not be an AI identity")
    source = gate.get("source")
    if source:
        source_path = (root / source).resolve()
        if source_path.exists():
            try:
                source_path.relative_to(root.resolve())
            except ValueError:
                errors.append(f"release gate source escapes repository: {source_path}")

    execution_plan = load_json(root, "docs/export/release-execution-plan.json")
    if execution_plan.get("requiresApprovedGate") is not True:
        errors.append("release execution plan must require approved gate")
    if execution_plan.get("executionStatus") not in {"blocked", "ready", "executed", "failed"}:
        errors.append("release execution plan has invalid executionStatus")
    if execution_plan.get("executionAllowed") is True and gate.get("gateStatus") != "approved":
        errors.append("release execution plan executionAllowed=true requires approved gate")
    for target in execution_plan.get("targets", []):
        for key in ("id", "enabled", "requiredGateFlag", "dryRunSupported", "executionAllowed", "auditRequired", "targetMode"):
            if key not in target:
                errors.append(f"release execution target missing '{key}': {target.get('id', '<unknown>')}")
        flag = target.get("requiredGateFlag")
        if target.get("enabled") is True and flag and gate.get(flag) is not True:
            errors.append(f"release execution target {target.get('id')} enabled without gate flag {flag}=true")
        if target.get("executionAllowed") is True and gate.get("gateStatus") != "approved":
            errors.append(f"release execution target {target.get('id')} executionAllowed=true without approved gate")
        if target.get("executionAllowed") is True and flag and gate.get(flag) is not True:
            errors.append(f"release execution target {target.get('id')} executionAllowed=true without {flag}=true")
        if target.get("id") == "aaiam" and target.get("enabled") is True:
            if target.get("libraryAvailable") is not True or target.get("targetConfigured") is not True:
                errors.append("AAIAM execution target must fail closed without libraryAvailable and targetConfigured")
        if target.get("id") == "aaiam":
            if target.get("targetMode") != "dry_run":
                errors.append("AAIAM execution target must remain dry_run")
            if target.get("productiveImportAllowed") is True:
                errors.append("AAIAM productive import is forbidden in this phase")
        if target.get("id") == "website" and target.get("enabled") is True and gate.get("deploymentAllowed") is not True:
            errors.append("Website execution target enabled while deploymentAllowed=false")
        if target.get("id") == "website":
            if target.get("targetMode") != "staging":
                errors.append("Website execution target must remain staging")
            if target.get("stagingOnly") is not True:
                errors.append("Website execution target must be stagingOnly")
        if target.get("id") == "pdf" and target.get("enabled") is True and gate.get("pdfPublicationAllowed") is not True:
            errors.append("PDF execution target enabled while pdfPublicationAllowed=false")
        if target.get("id") == "pdf" and target.get("localFinalizationOnly") is not True:
            errors.append("PDF execution target must be localFinalizationOnly")
        if target.get("id") == "inAppHelp" and target.get("enabled") is True and gate.get("inAppPackagingAllowed") is not True:
            errors.append("In-App execution target enabled while inAppPackagingAllowed=false")
        if target.get("id") == "inAppHelp" and target.get("activationAllowed") is True:
            errors.append("In-App help activation is forbidden in this phase")


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


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_release_candidate_artifacts(root: Path, errors: list[str]) -> None:
    rc_root = root / "docs/.release-candidate"
    if not rc_root.exists():
        return

    gitignore = (root / ".gitignore").read_text(encoding="utf-8", errors="replace") if (root / ".gitignore").exists() else ""
    if "docs/.release-candidate/" not in gitignore:
        errors.append("docs/.release-candidate/ must be ignored and not versioned")

    required = [
        rc_root / "release-manifest.json",
        rc_root / "website/routing-map.json",
        rc_root / "website/legacy-aliases.json",
        rc_root / "pdf/pdf-status.json",
        rc_root / "in-app/help-contexts.json",
        rc_root / "aaiam/aaiam-import-package.json",
    ]
    for path in required:
        if not path.exists():
            errors.append(f"release candidate missing required artifact: {path}")

    def validate_rc_json_value(path: Path, value: object, location: str) -> None:
        if isinstance(value, dict):
            status = value.get("status")
            if status in {"deployed", "generated", "imported"}:
                errors.append(f"release candidate has active status {status}: {path} at {location}.status")
            if value.get("dbWrite") is True:
                errors.append(f"release candidate enables DB write: {path} at {location}.dbWrite")
            for key, child in value.items():
                validate_rc_json_value(path, child, f"{location}.{key}")
        elif isinstance(value, list):
            for index, child in enumerate(value):
                validate_rc_json_value(path, child, f"{location}[{index}]")

    for path in rc_root.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() == ".json":
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except Exception as exc:  # noqa: BLE001
                errors.append(f"release candidate JSON invalid {path}: {exc}")
                continue
            validate_rc_json_value(path, data, "$")

    manifest_path = rc_root / "release-manifest.json"
    if manifest_path.exists():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001
            errors.append(f"release-manifest invalid JSON: {exc}")
            manifest = {}
        if manifest:
            if manifest.get("status") != "release_candidate":
                errors.append("release-manifest status must be release_candidate")
            if manifest.get("notDeployed") is not True:
                errors.append("release-manifest must set notDeployed=true")
            if manifest.get("notImported") is not True:
                errors.append("release-manifest must set notImported=true")
            if not manifest.get("exportManifestHash"):
                errors.append("release-manifest missing exportManifestHash")
            artifacts = manifest.get("artifacts", [])
            if not artifacts:
                errors.append("release-manifest missing artifact list")
            for artifact in artifacts:
                rel = artifact.get("path")
                digest = artifact.get("sha256")
                if not rel or not digest:
                    errors.append(f"release-manifest artifact missing path/hash: {artifact}")
                    continue
                artifact_path = rc_root / rel
                if not artifact_path.exists():
                    errors.append(f"release-manifest artifact missing on disk: {rel}")
                    continue
                actual = sha256_file(artifact_path)
                if actual != digest:
                    errors.append(f"release-manifest hash mismatch for {rel}")

    aaiam_path = rc_root / "aaiam/aaiam-import-package.json"
    if aaiam_path.exists():
        package = json.loads(aaiam_path.read_text(encoding="utf-8"))
        if package.get("status") not in {"prepared", "release_candidate"}:
            errors.append("AAIAM RC package status must be prepared or release_candidate")
        if package.get("notImported") is not True:
            errors.append("AAIAM RC package must set notImported=true")
        if package.get("dbWrite") is True:
            errors.append("AAIAM RC package must not enable DB write")
        for entry in package.get("entries", []):
            source = entry.get("sourcePath")
            if source:
                assert_exists(errors, root, (root / "docs" / source).resolve(), f"AAIAM RC package {entry.get('id')}")
            if entry.get("status") not in {"prepared", "release_candidate"}:
                errors.append(f"AAIAM RC entry has invalid status: {entry.get('id')}")

    website_map = rc_root / "website/routing-map.json"
    if website_map.exists():
        routing = json.loads(website_map.read_text(encoding="utf-8"))
        if routing.get("notDeployed") is not True:
            errors.append("Website RC package must set notDeployed=true")
        for route in routing.get("routes", []):
            source = route.get("sourcePath", "")
            if source.startswith("docs/"):
                assert_exists(errors, root, (root / source).resolve(), f"Website RC route {route.get('route')}")

    for path in rc_root.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".md", ".json", ".html"}:
            text = path.read_text(encoding="utf-8", errors="replace")
            for pattern, reason in SECRET_PATTERNS:
                if pattern.search(text):
                    errors.append(f"{path.relative_to(root)}: possible secret in release candidate ({reason})")
            for pattern, reason in PRIVATE_PATH_PATTERNS:
                if pattern.search(text):
                    errors.append(f"{path.relative_to(root)}: private path in release candidate ({reason})")
            for active_word in ("Status: deployed", "Status: generated", "Status: imported"):
                if active_word in text:
                    errors.append(f"{path.relative_to(root)}: forbidden active output claim '{active_word}'")
            for active_word in ("status: deployed", "status: imported", "status: generated-final", "final publication", "productive import"):
                if active_word in text.lower():
                    errors.append(f"{path.relative_to(root)}: forbidden productive release claim '{active_word}'")

    audit_path = rc_root / "execution-audit.json"
    if audit_path.exists():
        try:
            audit = json.loads(audit_path.read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001
            errors.append(f"execution audit invalid JSON: {exc}")
            audit = {}
        if audit:
            for key in ("startedAtUtc", "sourceCommit", "target", "mode", "executionStatus", "reasonCode", "artifactHashes"):
                if key not in audit:
                    errors.append(f"execution audit missing '{key}'")
            if audit.get("notLiveDeployment") is not True:
                errors.append("execution audit must set notLiveDeployment=true")
            if audit.get("noSecretsDetected") is not True:
                errors.append("execution audit must report noSecretsDetected=true")
            if audit.get("noPrivatePathsDetected") is not True:
                errors.append("execution audit must report noPrivatePathsDetected=true")
            if audit.get("executionStatus") not in {"blocked", "dry_run", "executed", "failed"}:
                errors.append("execution audit has invalid executionStatus")
            for target in audit.get("targets", []):
                if target.get("target") == "aaiam" and target.get("mode") != "dry_run":
                    errors.append("execution audit AAIAM target must remain dry_run")
                if target.get("target") == "inAppHelp" and target.get("activationAllowed") is True:
                    errors.append("execution audit must not mark in-app help activated")
                if target.get("target") == "website" and target.get("notLiveDeployment") is not True:
                    errors.append("execution audit website target must not be live deployment")


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    errors: list[str] = []

    validate_json_shape(root, errors)
    validate_markdown_links(root, errors)
    validate_json_sources(root, errors)
    validate_forbidden_text(root, errors)
    validate_preview_artifacts(root, errors)
    validate_release_candidate_artifacts(root, errors)

    if errors:
        print("DOC CONFORMANCE: FAILED")
        for error in sorted(errors):
            print(f"  - {error}")
        return 1

    print("OK Documentation Conformance: Markdown, JSON, Redaction und AAIAM-Mappings geprueft")
    return 0


if __name__ == "__main__":
    sys.exit(main())
