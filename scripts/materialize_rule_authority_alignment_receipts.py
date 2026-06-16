#!/usr/bin/env python3
from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def build_alignment(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    fixtures = load_json(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    row = load_json(root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json")

    rulefact_providers = sorted({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")})
    coverage_domains = explain.get("coverage_domains", [])
    required_fixture_ids = fixtures.get("required_fixture_ids", [])
    fixture_alignment_pass = fixtures.get("failed") == 0 and len(required_fixture_ids) > 0 and fixtures.get("status") == "core_seed_fixture_pack_passed"
    explain_alignment_pass = explain.get("status") == "core_seed_receipt_pack_available" and len(coverage_domains) > 0

    return {
        "contract_name": f"chummer.{ruleset}.authority_alignment",
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "pass" if fixture_alignment_pass and explain_alignment_pass else "fail",
        "selected_core_baseline": row.get("review_packet", {}).get("selected_core_baseline"),
        "fixture_alignment": {
            "status": "pass" if fixture_alignment_pass else "fail",
            "required_fixture_count": len(required_fixture_ids),
            "required_fixture_ids": required_fixture_ids,
            "passed": fixtures.get("passed"),
            "failed": fixtures.get("failed"),
            "supports_row_level_review": True,
        },
        "explain_alignment": {
            "status": "pass" if explain_alignment_pass else "fail",
            "provider": explain.get("provider", explain.get("receipt_provider")),
            "coverage_domains": coverage_domains,
            "rulefact_provider_count": len(rulefact_providers),
            "rulefact_providers": rulefact_providers,
            "supports_row_level_review": True,
        },
        "remaining_human_dependence": [
            "alignment receipts support human review but do not replace row-level authority approval",
        ],
    }


def main() -> int:
    for ruleset in ("sr4", "sr6"):
        upper = ruleset.upper()
        payload = build_alignment(ruleset)
        for base in (COMPLETION_ROOT / f"{ruleset}_rule_authority", PUBLISHED_ROOT):
            write_json(base / f"{upper}_AUTHORITY_ALIGNMENT.generated.json", payload)
    print(json.dumps({"status": "ok", "rulesets": ["sr4", "sr6"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
