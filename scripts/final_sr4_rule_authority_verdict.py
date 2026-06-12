#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

from verify_rule_authority_human_review import validate_review


COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr4_rule_authority")
REQUIRED = [
    "SR4_RULEFACT_REGISTRY.generated.json",
    "SR4_PROVIDER_COVERAGE.generated.json",
    "SR4_TABLE_IMPORTS.generated.json",
    "SR4_GOLDEN_FIXTURES.generated.json",
    "SR4_EXPLAIN_RECEIPTS.generated.json",
    "SR4_COPYRIGHT_SAFETY.generated.json",
    "SR4_ERRATA_PROFILE.generated.json",
    "SR4_HUMAN_RULE_REVIEW.md",
    "FINAL_SR4_RULE_AUTHORITY_VERDICT.md",
]


def load_json(name: str) -> dict:
    return json.loads((COMPLETION_ROOT / name).read_text(encoding="utf-8"))


def main() -> int:
    missing = [name for name in REQUIRED if not (COMPLETION_ROOT / name).is_file()]
    if missing:
        print(json.dumps({"status": "fail", "missing": missing}, indent=2))
        return 1

    registry = load_json("SR4_RULEFACT_REGISTRY.generated.json")
    provider = load_json("SR4_PROVIDER_COVERAGE.generated.json")
    tables = load_json("SR4_TABLE_IMPORTS.generated.json")
    errata = load_json("SR4_ERRATA_PROFILE.generated.json")
    copyright_safety = load_json("SR4_COPYRIGHT_SAFETY.generated.json")
    verdict_text = (COMPLETION_ROOT / "FINAL_SR4_RULE_AUTHORITY_VERDICT.md").read_text(encoding="utf-8")
    human_review = validate_review("sr4")
    verdict_first_line = next((line.strip() for line in verdict_text.splitlines() if line.strip()), "")
    ready_allowed = (
        registry.get("final_verdict") == "SR4_RULE_AUTHORITY_READY"
        and provider.get("missing_implemented_providers") == []
        and tables.get("status") == "reviewed"
        and errata.get("status") == "applied"
        and copyright_safety.get("status") == "pass"
        and human_review.get("review_ready") is True
    )
    bounded_not_ready = (
        registry.get("final_verdict") == "NOT_READY"
        and provider.get("missing_implemented_providers") == []
        and tables.get("status") == "structured_legacy_data_indexed_pending_human_review"
        and tables.get("file_count", 0) >= 20
        and tables.get("row_count", 0) > 0
        and errata.get("status") == "pending"
        and (verdict_first_line == "NOT_READY" or "Verdict: NOT_READY" in verdict_text)
        and human_review.get("pending_review") is True
    )
    ok = ready_allowed or bounded_not_ready
    print(json.dumps({
        "status": "pass" if ok else "fail",
        "ready_allowed": ready_allowed,
        "bounded_not_ready": bounded_not_ready,
        "verdict": registry.get("final_verdict"),
        "human_review": human_review,
    }, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
