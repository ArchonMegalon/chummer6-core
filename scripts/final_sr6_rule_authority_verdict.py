#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

import yaml

from verify_rule_authority_human_review import validate_review


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr6_rule_authority")
PROFILE_PATH = REPO_ROOT / "docs/rulesets/sr6-rule-authority/SR6_RULESET_PROFILE.yaml"
REQUIRED = [
    "SR6_RULEFACT_REGISTRY.generated.json",
    "SR6_PROVIDER_COVERAGE.generated.json",
    "SR6_TABLE_IMPORTS.generated.json",
    "SR6_GOLDEN_FIXTURES.generated.json",
    "SR6_EXPLAIN_RECEIPTS.generated.json",
    "SR6_ERRATA_SOURCE_POSTURE.generated.json",
    "SR6_HUMAN_RULE_REVIEW.md",
    "FINAL_SR6_RULE_AUTHORITY_VERDICT.md",
]


def load_json(name: str) -> dict:
    return json.loads((COMPLETION_ROOT / name).read_text(encoding="utf-8"))


def main() -> int:
    missing = [name for name in REQUIRED if not (COMPLETION_ROOT / name).is_file()]
    if missing:
        print(json.dumps({"status": "fail", "missing": missing}, indent=2))
        return 1

    registry = load_json("SR6_RULEFACT_REGISTRY.generated.json")
    provider = load_json("SR6_PROVIDER_COVERAGE.generated.json")
    tables = load_json("SR6_TABLE_IMPORTS.generated.json")
    errata = load_json("SR6_ERRATA_SOURCE_POSTURE.generated.json")
    profile = yaml.safe_load(PROFILE_PATH.read_text(encoding="utf-8")) or {}
    copyright_safe = str(profile.get("public_copy_policy") or "").strip().lower() == "no rulebook prose"
    verdict_text = (COMPLETION_ROOT / "FINAL_SR6_RULE_AUTHORITY_VERDICT.md").read_text(encoding="utf-8")
    human_review = validate_review("sr6")
    verdict_first_line = next((line.strip() for line in verdict_text.splitlines() if line.strip()), "")

    ready_allowed = (
        registry.get("final_verdict") == "SR6_RULE_AUTHORITY_READY"
        and provider.get("missing_implemented_providers") == []
        and tables.get("status") == "reviewed"
        and errata.get("status") == "applied"
        and errata.get("ready_for_gold") is True
        and copyright_safe
        and human_review.get("review_ready") is True
    )
    bounded_not_ready = (
        registry.get("final_verdict") == "NOT_READY"
        and provider.get("missing_implemented_providers") == []
        and tables.get("status") == "reviewed"
        and tables.get("sourcebook_count", 0) >= 1
        and tables.get("candidate_table_line_count", 0) > 0
        and errata.get("status") == "applied"
        and copyright_safe
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
