#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
from datetime import datetime
from pathlib import Path


COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
REQUIRED_FIELDS = {
    "Status",
    "Row-level decision",
    "Errata decision",
    "Reviewer",
    "Review timestamp",
    "Ready token approved",
}
APPROVED_ERRATA_DECISIONS = {"applied", "not_applicable", "defer"}


def review_path(ruleset: str) -> Path:
    upper = ruleset.upper()
    return COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_HUMAN_RULE_REVIEW.md"


def row_mapping_path(ruleset: str) -> Path:
    upper = ruleset.upper()
    return COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json"


def errata_posture_path(ruleset: str) -> Path:
    upper = ruleset.upper()
    return COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json"


def source_baseline_required(ruleset: str) -> bool:
    path = row_mapping_path(ruleset)
    if not path.is_file():
        return False
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False
    return payload.get("source_baseline_decision_status") == "pending_human_review"


def no_errata_sources_in_scope(ruleset: str) -> bool:
    path = errata_posture_path(ruleset)
    if not path.is_file():
        return False
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False
    review_packet = payload.get("review_packet", {})
    return int(review_packet.get("source_count") or 0) == 0


def parse_fields(text: str) -> dict[str, str]:
    fields: dict[str, str] = {}
    for line in text.splitlines():
        match = re.match(r"^([A-Za-z][A-Za-z -]+):\s*(.+?)\s*$", line)
        if match:
            fields[match.group(1)] = match.group(2)
    return fields


def timestamp_is_utc_iso(value: str) -> bool:
    if not value.endswith("Z"):
        return False
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return True


def validate_fields(
    ruleset: str,
    fields: dict[str, str],
    path: Path | None = None,
    source_baseline_required: bool = False,
    no_errata_sources_in_scope: bool = False,
) -> dict[str, object]:
    missing = sorted(REQUIRED_FIELDS - set(fields))
    if missing:
        return {"status": "fail", "ruleset": ruleset, "reason": "missing required fields", "missing": missing, "path": str(path) if path else ""}

    status = fields["Status"].lower()
    row_decision = fields["Row-level decision"].lower()
    errata_decision = fields["Errata decision"].lower()
    reviewer = fields["Reviewer"].strip()
    timestamp = fields["Review timestamp"].strip()
    ready_approved = fields["Ready token approved"].lower()
    baseline_decision = fields.get("Source baseline decision", "").strip()

    pending_ok = (
        status == "pending"
        and row_decision == "pending"
        and errata_decision in ({"pending", "not_applicable"} if no_errata_sources_in_scope else {"pending"})
        and reviewer.lower() == "pending"
        and timestamp.lower() == "pending"
        and ready_approved == "false"
    )
    approved_ok = (
        status == "approved"
        and row_decision == "approved"
        and errata_decision in APPROVED_ERRATA_DECISIONS
        and reviewer
        and reviewer.lower() != "pending"
        and timestamp
        and timestamp.lower() != "pending"
        and timestamp_is_utc_iso(timestamp)
        and ready_approved == "true"
        and (
            errata_decision != "defer"
            or bool(fields.get("Errata defer rationale", "").strip())
            and fields.get("Errata defer rationale", "").strip().lower() != "pending"
        )
        and (
            not source_baseline_required
            or bool(baseline_decision)
            and baseline_decision.lower() != "pending"
        )
    )

    ok = pending_ok or approved_ok
    return {
        "status": "pass" if ok else "fail",
        "ruleset": ruleset,
        "review_ready": approved_ok,
        "pending_review": pending_ok,
        "source_baseline_required": source_baseline_required,
        "fields": fields,
        "path": str(path) if path else "",
    }


def validate_review(ruleset: str) -> dict[str, object]:
    ruleset = ruleset.lower()
    path = review_path(ruleset)
    if ruleset not in {"sr4", "sr6"}:
        return {"status": "fail", "ruleset": ruleset, "reason": "unsupported ruleset"}
    if not path.is_file():
        return {"status": "fail", "ruleset": ruleset, "reason": "missing human review", "path": str(path)}

    text = path.read_text(encoding="utf-8")
    return validate_fields(
        ruleset,
        parse_fields(text),
        path,
        source_baseline_required(ruleset),
        no_errata_sources_in_scope(ruleset),
    )


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    require_ready = "--require-ready" in argv
    rulesets = [arg.lower() for arg in argv if arg != "--require-ready"] or ["sr4", "sr6"]
    payload = {"status": "pass", "reviews": []}
    for ruleset in rulesets:
        result = validate_review(ruleset)
        if require_ready and result.get("review_ready") is not True:
            result = dict(result)
            result["status"] = "fail"
            result["reason"] = "human review is not approved for ready token"
        payload["reviews"].append(result)
        if result.get("status") != "pass":
            payload["status"] = "fail"
    payload["require_ready"] = require_ready
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
