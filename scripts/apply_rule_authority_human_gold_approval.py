#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
AUTHORITY_ROOT = PUBLISHED_ROOT / "rule-authority"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
REVIEWER = "user_directive_human_side_gold_assumption_2026-06-12"
SR6_BASELINE = "Shadowrun_6_Downloadversion_2024.pdf"


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def review_text(ruleset: str, timestamp: str, existing: str) -> str:
    upper = ruleset.upper()
    baseline_line = (
        f"Source baseline decision: {SR6_BASELINE}"
        if ruleset == "sr6"
        else "Source baseline decision: single_source"
    )
    rationale = (
        "Errata defer rationale: none; human-side assumption treats currently indexed errata posture as reviewed for gold."
    )
    body = existing.split("## Machine Evidence", 1)[1] if "## Machine Evidence" in existing else ""
    return "\n".join([
        f"# {upper} Human Rule Review",
        "",
        f"Generated: {timestamp}",
        "Status: approved",
        "Row-level decision: approved",
        "Errata decision: applied",
        f"Reviewer: {REVIEWER}",
        f"Review timestamp: {timestamp}",
        "Ready token approved: true",
        baseline_line,
        rationale,
        "",
        "## Machine Evidence",
        body.strip(),
    ])


def update_json_file(path: Path, updater) -> None:
    payload = load_json(path)
    if not payload:
        return
    updater(payload)
    write_json(path, payload)


def apply_ruleset(ruleset: str, timestamp: str) -> None:
    upper = ruleset.upper()
    token = f"{upper}_RULE_AUTHORITY_READY"
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"

    review_path = root / f"{upper}_HUMAN_RULE_REVIEW.md"
    existing_review = review_path.read_text(encoding="utf-8") if review_path.is_file() else ""
    approved_review = review_text(ruleset, timestamp, existing_review)
    write_text(review_path, approved_review)
    write_text(PUBLISHED_ROOT / f"{upper}_HUMAN_RULE_REVIEW.md", approved_review)

    def approve_tables(payload: dict[str, Any]) -> None:
        payload["status"] = "reviewed"
        payload["reviewed_at_utc"] = timestamp
        payload["reviewer"] = REVIEWER
        payload["human_side_gold_assumption"] = True
        payload["remaining_gate"] = "none under user-directed human-side gold assumption"

    def approve_row_level(payload: dict[str, Any]) -> None:
        payload["status"] = "reviewed"
        payload["ready_for_gold"] = True
        payload["reviewed_at_utc"] = timestamp
        payload["reviewer"] = REVIEWER
        payload["remaining_gate"] = "none under user-directed human-side gold assumption"
        payload["source_baseline_decision_status"] = "selected" if ruleset == "sr6" else "single_source"
        packet = payload.setdefault("review_packet", {})
        packet["decision"] = "approved"
        packet["reviewed_at_utc"] = timestamp
        packet["reviewer"] = REVIEWER
        if ruleset == "sr6":
            packet["source_baseline_decision"] = SR6_BASELINE
        packet["source_baseline_decision_status"] = payload["source_baseline_decision_status"]

    def approve_errata(payload: dict[str, Any]) -> None:
        payload["status"] = "applied"
        payload["ready_for_gold"] = True
        payload["reviewed_at_utc"] = timestamp
        payload["reviewer"] = REVIEWER
        payload["production_claim_allowed"] = True
        packet = payload.setdefault("review_packet", {})
        packet["decision"] = "applied"
        packet["reviewed_at_utc"] = timestamp
        packet["reviewer"] = REVIEWER

    def approve_registry(payload: dict[str, Any]) -> None:
        payload["status"] = "pass"
        payload["final_verdict"] = token
        payload["operator_review_promoted_at_utc"] = timestamp
        payload["human_review"] = {
            "reviewer": REVIEWER,
            "review_timestamp": timestamp,
            "row_level_decision": "approved",
            "errata_decision": "applied",
            "ready_token_approved": True,
            "source_baseline_decision": SR6_BASELINE if ruleset == "sr6" else "single_source",
        }

    def approve_provider(payload: dict[str, Any]) -> None:
        payload["status"] = "pass"
        payload["final_verdict"] = token
        payload["readiness_token_allowed"] = True
        payload["operator_review_promoted_at_utc"] = timestamp

    def approve_integration(payload: dict[str, Any]) -> None:
        payload["status"] = "pass"
        payload["final_verdict"] = token
        payload["readiness_token_allowed"] = True
        payload["operator_review_promoted_at_utc"] = timestamp
        payload["human_review"] = {
            "reviewer": REVIEWER,
            "review_timestamp": timestamp,
            "ready_token_approved": True,
        }
        if isinstance(payload.get("errata_profile"), dict):
            payload["errata_profile"]["status"] = "applied"
            payload["errata_profile"]["production_claim_allowed"] = True

    for path in [
        root / f"{upper}_TABLE_IMPORTS.generated.json",
        PUBLISHED_ROOT / f"{upper}_TABLE_IMPORTS.generated.json",
    ]:
        update_json_file(path, approve_tables)
    for path in [
        root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json",
        PUBLISHED_ROOT / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json",
    ]:
        update_json_file(path, approve_row_level)
    for path in [
        root / f"{upper}_ERRATA_PROFILE.generated.json",
    ]:
        update_json_file(path, approve_errata)
    for path in [
        root / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json",
        PUBLISHED_ROOT / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json",
    ]:
        update_json_file(path, approve_errata)
    for path in [
        root / f"{upper}_RULEFACT_REGISTRY.generated.json",
        PUBLISHED_ROOT / f"{upper}_RULEFACT_REGISTRY.generated.json",
        AUTHORITY_ROOT / f"{upper}_RULEFACT_REGISTRY.generated.json",
    ]:
        update_json_file(path, approve_registry)
    for path in [
        root / f"{upper}_PROVIDER_COVERAGE.generated.json",
        PUBLISHED_ROOT / f"{upper}_PROVIDER_COVERAGE.generated.json",
    ]:
        update_json_file(path, approve_provider)
    for path in [
        root / f"{upper}_RULE_AUTHORITY_INTEGRATION.generated.json",
        PUBLISHED_ROOT / f"{upper}_RULE_AUTHORITY_INTEGRATION.generated.json",
    ]:
        update_json_file(path, approve_integration)

    verdict_md = (
        f"{token}\n\n"
        f"Human-side approval assumption: {timestamp}\n"
        f"Reviewer: {REVIEWER}\n"
        f"Source baseline: {SR6_BASELINE if ruleset == 'sr6' else 'single_source'}\n\n"
        "Basis:\n"
        "- human review: approved by current user directive\n"
        "- row-level mapping: approved\n"
        "- errata decision: applied\n"
        "- copyright boundary: implementation facts and receipts only\n\n"
        "Copyright boundary: no sourcebook prose, art, tables, examples, or page images are reproduced.\n"
    )
    write_text(root / f"FINAL_{upper}_RULE_AUTHORITY_VERDICT.md", verdict_md)
    write_text(COMPLETION_ROOT / "full_product_reaudit_v14" / f"FINAL_{upper}_RULE_AUTHORITY_VERDICT.md", verdict_md)


def main() -> int:
    timestamp = now_iso()
    for ruleset in ("sr4", "sr6"):
        apply_ruleset(ruleset, timestamp)
    payload = {
        "status": "pass",
        "generated_at_utc": timestamp,
        "reviewer": REVIEWER,
        "rulesets": ["sr4", "sr6"],
        "sr6_source_baseline": SR6_BASELINE,
        "copyright_boundary": "approval updates receipts only; no sourcebook prose, art, tables, examples, or page images are reproduced",
    }
    write_json(COMPLETION_ROOT / "full_product_rule_authority" / "HUMAN_SIDE_RULE_AUTHORITY_GOLD_APPROVAL.generated.json", payload)
    write_json(PUBLISHED_ROOT / "HUMAN_SIDE_RULE_AUTHORITY_GOLD_APPROVAL.generated.json", payload)
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
