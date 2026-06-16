#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
DOCS_ROOT = REPO_ROOT / "docs" / "rulesets"

POLICY = {
    "sr4": {
        "core_baseline": "legacy Chummer4 XML as implemented for core readiness",
        "authority_book_profile": "sr4a_core_2009",
        "supplements_in_scope": False,
        "errata_policy": "official errata or official web notices only",
        "core_domains": [
            "dice",
            "tests",
            "build_points",
            "metatype_ranges",
            "attributes",
            "skills",
            "qualities",
            "derived_stats",
            "combat",
            "magic",
            "matrix",
            "rigging",
        ],
    },
    "sr6": {
        "core_baseline": "Shadowrun_6_Downloadversion_2024.pdf",
        "authority_book_profile": "sr6_core_2024_selected_baseline",
        "supplements_in_scope": False,
        "errata_policy": "official errata or official web notices only",
        "core_domains": [
            "dice",
            "tests",
            "edge",
            "action_economy",
            "priority_creation",
            "metatype_ranges",
            "skills",
            "derived_stats",
            "combat",
            "magic",
            "matrix",
            "rigging",
            "status_effects",
        ],
    },
}


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def load_yaml(path: Path) -> dict[str, Any]:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def existing_or_empty(path: Path) -> dict[str, Any]:
    return load_json(path) if path.is_file() else {}


def fixture_receipt(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    policy = POLICY[ruleset]
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    existing = existing_or_empty(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    plan = load_yaml(DOCS_ROOT / f"{ruleset}-rule-authority" / f"{upper}_GOLDEN_FIXTURE_PLAN.yaml")
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    required = [fixture for fixture in plan.get("required_fixtures", []) if isinstance(fixture, dict)]
    passed = int(existing.get("passed") or 0)
    failed = int(existing.get("failed") or 0)
    total = int(existing.get("total") or passed + failed)
    return {
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "core_seed_fixture_pack_passed" if failed == 0 and passed > 0 else "fail",
        "scope": "core_readiness_only",
        "core_baseline": policy["core_baseline"],
        "supplements_in_scope": policy["supplements_in_scope"],
        "required_fixture_ids": [fixture.get("id") for fixture in required],
        "required_fixture_count": len(required),
        "required_fixture_purposes": {fixture.get("id"): fixture.get("purpose") for fixture in required if fixture.get("id")},
        "fixture_policy": plan.get("fixture_policy", {}),
        "core_domains": policy["core_domains"],
        "rulefact_count": int(registry.get("rulefact_count") or 0),
        "passed": passed,
        "failed": failed,
        "skipped": int(existing.get("skipped") or 0),
        "total": total,
        "test_filter": existing.get("test_filter", f"FullyQualifiedName~{upper.title()}"),
        "ready_for_gold": False,
        "remaining_gates": [
            "human-reviewed expected values against approved row-level authority",
            "human signoff before ready token",
        ],
    }


def explain_receipt(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    policy = POLICY[ruleset]
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    provider_coverage = load_json(root / f"{upper}_PROVIDER_COVERAGE.generated.json")
    existing = existing_or_empty(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    provider_name = "Sr4ExplainReceiptProvider" if ruleset == "sr4" else "Sr6ExplainReceiptProvider"
    providers_with_facts = sorted({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")})
    return {
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "core_seed_receipt_pack_available",
        "scope": "core_readiness_only",
        "core_baseline": policy["core_baseline"],
        "supplements_in_scope": policy["supplements_in_scope"],
        "public_safe": True,
        "provider": provider_name,
        "provider_coverage_status": provider_coverage.get("status"),
        "implemented_provider_count": provider_coverage.get("implemented_provider_count"),
        "providers_with_rulefacts": providers_with_facts,
        "coverage_domains": policy["core_domains"],
        "receipt_kind": "public_safe_seed_explain_receipts",
        "reason": existing.get(
            "reason",
            "Seed-level public-safe explain receipts exist for the core-only authority scope; reviewed row-level mappings remain required before a ready token.",
        ),
        "ready_for_gold": False,
        "remaining_gates": [
            "reviewed row-level authority mapping for cited facts",
            "human-confirmed explain corpus against approved baseline and errata posture",
        ],
    }


def main() -> int:
    for ruleset in ("sr4", "sr6"):
        upper = ruleset.upper()
        fixture = fixture_receipt(ruleset)
        explain = explain_receipt(ruleset)
        for base in (COMPLETION_ROOT / f"{ruleset}_rule_authority", PUBLISHED_ROOT):
            write_json(base / f"{upper}_GOLDEN_FIXTURES.generated.json", fixture)
            write_json(base / f"{upper}_EXPLAIN_RECEIPTS.generated.json", explain)
    print(json.dumps({"status": "ok", "rulesets": ["sr4", "sr6"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
