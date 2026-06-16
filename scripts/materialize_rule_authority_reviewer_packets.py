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


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def build_packet(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    row = load_json(root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json")
    errata = load_json(root / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json")
    fixtures = load_json(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")

    row_packet = row.get("review_packet", {})
    errata_packet = errata.get("review_packet", {})
    sr4 = ruleset == "sr4"

    recommended_errata_decision = "not_applicable" if sr4 and errata_packet.get("source_count", 0) == 0 else "pending_manual_review"
    recommended_next_actions = [
        "review row-level mapping packet and approve or reject normalized public-safe records",
        "review errata packet and record applied/not_applicable/defer decision",
        "confirm fixture expectations against approved authority facts",
    ]
    if not sr4:
        recommended_next_actions.append("spot-check explain receipts against approved SR6 row-level authority")
    recommended_next_actions.append("complete human rule review signoff")

    return {
        "contract_name": f"chummer.{ruleset}.reviewer_decision_packet",
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "awaiting_human_decision",
        "selected_core_baseline": row_packet.get("selected_core_baseline"),
        "supplements_in_scope": row_packet.get("supplements_in_scope"),
        "row_level_mapping": {
            "status": row.get("status"),
            "indexed_unit_count": row_packet.get("indexed_unit_count"),
            "indexed_source_files": row_packet.get("indexed_source_files"),
            "public_copy_policy": row_packet.get("public_copy_policy"),
            "review_decision_required": True,
        },
        "errata": {
            "status": errata.get("status"),
            "policy": errata_packet.get("errata_policy"),
            "source_count": errata_packet.get("source_count"),
            "source_ids": errata_packet.get("source_ids"),
            "recommended_decision": recommended_errata_decision,
            "review_decision_required": True,
        },
        "fixtures": {
            "status": fixtures.get("status"),
            "required_fixture_ids": fixtures.get("required_fixture_ids", []),
            "required_fixture_count": fixtures.get("required_fixture_count", 0),
            "passed": fixtures.get("passed"),
            "failed": fixtures.get("failed"),
            "review_expected_values_required": True,
        },
        "explain_receipts": {
            "status": explain.get("status"),
            "provider": explain.get("provider", explain.get("receipt_provider")),
            "coverage_domains": explain.get("coverage_domains", []),
            "review_required": not sr4,
        },
        "registry": {
            "rulefact_count": registry.get("rulefact_count"),
            "fact_provider_count": len({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")}),
        },
        "recommended_next_actions": recommended_next_actions,
        "can_change_recommendation_to_sign_off_allowed_when": [
            "row-level decision is approved",
            "errata decision is applied/not_applicable/defer with rationale",
            "fixture expectations are human-confirmed",
            "human review file is approved with ready token approved true",
        ] + ([] if sr4 else ["SR6 explain corpus is human-confirmed against approved row-level authority"]),
    }


def build_markdown(packet: dict[str, Any]) -> str:
    lines = [
        f"# {packet['ruleset'].upper()} Reviewer Decision Packet",
        "",
        f"Generated: {packet['generated_at_utc']}",
        "",
        f"Status: {packet['status']}",
        "",
        "## Baseline",
        "",
        f"- Selected core baseline: `{packet['selected_core_baseline']}`",
        f"- Supplements in scope: `{packet['supplements_in_scope']}`",
        "",
        "## Review Checklist",
        "",
        f"- Row-level mapping status: `{packet['row_level_mapping']['status']}`",
        f"- Indexed units: `{packet['row_level_mapping']['indexed_unit_count']}`",
        f"- Errata status: `{packet['errata']['status']}`",
        f"- Errata recommended decision: `{packet['errata']['recommended_decision']}`",
        f"- Fixture status: `{packet['fixtures']['status']}`",
        f"- Explain receipt status: `{packet['explain_receipts']['status']}`",
        f"- Rulefact count: `{packet['registry']['rulefact_count']}`",
        "",
        "## Required Human Actions",
        "",
        *[f"- {item}" for item in packet["recommended_next_actions"]],
        "",
        "## Signoff Preconditions",
        "",
        *[f"- {item}" for item in packet["can_change_recommendation_to_sign_off_allowed_when"]],
    ]
    return "\n".join(lines)


def main() -> int:
    for ruleset in ("sr4", "sr6"):
        upper = ruleset.upper()
        packet = build_packet(ruleset)
        markdown = build_markdown(packet)
        for base in (COMPLETION_ROOT / f"{ruleset}_rule_authority", PUBLISHED_ROOT):
            write_json(base / f"{upper}_REVIEWER_DECISION_PACKET.generated.json", packet)
            write_text(base / f"{upper}_REVIEWER_DECISION_PACKET.generated.md", markdown)
    print(json.dumps({"status": "ok", "rulesets": ["sr4", "sr6"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
