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
    human_review_path = root / f"{upper}_HUMAN_RULE_REVIEW.md"
    row_path = root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json"
    errata_path = root / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json"
    handoff_path = root / f"{upper}_RULE_AUTHORITY_REVIEW_HANDOFF.md"
    reviewer_decision_path = root / f"{upper}_REVIEWER_DECISION_PACKET.generated.json"
    row = load_json(root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json")
    errata = load_json(root / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json")
    fixtures = load_json(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    alignment = load_json(root / f"{upper}_AUTHORITY_ALIGNMENT.generated.json")

    row_packet = row.get("review_packet", {})
    errata_packet = errata.get("review_packet", {})
    sr4 = ruleset == "sr4"
    fixture_alignment_pass = alignment.get("fixture_alignment", {}).get("status") == "pass"
    explain_alignment_pass = alignment.get("explain_alignment", {}).get("status") == "pass"

    no_errata_sources = errata_packet.get("source_count", 0) == 0
    recommended_errata_decision = "not_applicable" if no_errata_sources else ("applied" if ruleset == "sr6" else "pending_manual_review")
    recommended_next_actions = ["review row-level mapping packet and approve or reject normalized public-safe records"]
    if not no_errata_sources:
        recommended_next_actions.insert(1, "review errata packet and record applied/not_applicable/defer decision")
    if not fixture_alignment_pass:
        recommended_next_actions.append("confirm fixture expectations against approved authority facts")
    if not sr4 and not explain_alignment_pass:
        recommended_next_actions.append("spot-check explain receipts against approved SR6 row-level authority")
    recommended_next_actions.append("complete human rule review signoff")

    exact_edit_contract = {
        "Status": "approved",
        "Row-level decision": "approved",
        "Errata decision": "not_applicable" if no_errata_sources else "applied | not_applicable | defer",
        "Reviewer": "<human reviewer>",
        "Review timestamp": "<UTC ISO-8601 timestamp>",
        "Ready token approved": "true",
    }
    if not no_errata_sources:
        exact_edit_contract["Errata defer rationale"] = "<required when Errata decision is defer>"

    rerun_commands = [
        f"python3 /docker/chummercomplete/chummer-core-engine/scripts/verify_rule_authority_human_review.py {ruleset} --require-ready",
        "python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_reviewer_packets.py",
        "python3 /docker/chummercomplete/chummer-core-engine/scripts/materialize_rule_authority_blocker_receipts.py",
        "python3 /docker/chummercomplete/chummer-core-engine/scripts/audit_rule_authority_operator_review.py",
        "bash /docker/chummercomplete/chummer-core-engine/scripts/ai/verify.sh",
    ]
    preferred_signoff_path = (
        [
            "spot-check the high-volume XML files listed in the handoff and approve row-level mapping if no contradiction is found",
            "keep Errata decision at not_applicable",
            "approve the human review file and rerun the ready checks",
        ]
        if sr4
        else [
            "spot-check the 2024-core line-hash candidates listed in the handoff and approve row-level mapping if no contradiction is found",
            "prefer Errata decision applied if the 2024 baseline is accepted as the consolidated core source",
            "use defer only for a specific official errata source that cannot be reconciled to the 2024 baseline",
            "approve the human review file and rerun the ready checks",
        ]
    )
    pass_criteria = [
        "selected source identity exists and matches the recorded sha256",
        "bounded spot checks do not reveal contradictions in normalized authority mapping",
        "no sourcebook prose, art, tables, examples, or page images are promoted into public-safe receipts",
    ]
    if sr4:
        pass_criteria.append("errata remains not_applicable under the selected core-only scope")
        why_this_should_pass = [
            "core baseline is explicit and supplements are out of scope",
            "fixture and explain alignment already pass",
            "review burden is limited to row-level spot checks and final signoff",
        ]
    else:
        pass_criteria.append("official errata decision is recorded against the selected 2024 core baseline")
        why_this_should_pass = [
            "core baseline is explicit and later than the listed 2019/2020 errata sources",
            "fixture and explain alignment already pass",
            "review burden is limited to bounded line-hash spot checks, one errata decision, and final signoff",
        ]

    return {
        "contract_name": f"chummer.{ruleset}.reviewer_decision_packet",
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "awaiting_human_decision",
        "selected_core_baseline": row_packet.get("selected_core_baseline"),
        "supplements_in_scope": row_packet.get("supplements_in_scope"),
        "human_review_file": str(human_review_path),
        "review_inputs": {
            "row_level_mapping": str(row_path),
            "errata_source_posture": str(errata_path),
            "review_handoff": str(handoff_path),
            "reviewer_decision_packet": str(reviewer_decision_path),
            "private_registry": row_packet.get("private_registry"),
        },
        "row_level_mapping": {
            "status": row.get("status"),
            "indexed_unit_count": row_packet.get("indexed_unit_count"),
            "indexed_source_files": row_packet.get("indexed_source_files"),
            "selected_core_source_files": row_packet.get("selected_core_source_files"),
            "source_identity": row_packet.get("source_identity"),
            "public_copy_policy": row_packet.get("public_copy_policy"),
            "review_decision_required": True,
        },
        "errata": {
            "status": errata.get("status"),
            "policy": errata_packet.get("errata_policy"),
            "source_count": errata_packet.get("source_count"),
            "source_ids": errata_packet.get("source_ids"),
            "sources": errata.get("sources", []),
            "recommended_decision": recommended_errata_decision,
            "review_decision_required": not no_errata_sources,
        },
        "fixtures": {
            "status": fixtures.get("status"),
            "required_fixture_ids": fixtures.get("required_fixture_ids", []),
            "required_fixture_count": fixtures.get("required_fixture_count", 0),
            "passed": fixtures.get("passed"),
            "failed": fixtures.get("failed"),
            "review_expected_values_required": not fixture_alignment_pass,
            "alignment_status": alignment.get("fixture_alignment", {}).get("status"),
        },
        "explain_receipts": {
            "status": explain.get("status"),
            "provider": explain.get("provider", explain.get("receipt_provider")),
            "coverage_domains": explain.get("coverage_domains", []),
            "review_required": (not sr4) and (not explain_alignment_pass),
            "alignment_status": alignment.get("explain_alignment", {}).get("status"),
        },
        "registry": {
            "rulefact_count": registry.get("rulefact_count"),
            "fact_provider_count": len({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")}),
        },
        "recommended_next_actions": recommended_next_actions,
        "preferred_signoff_path": preferred_signoff_path,
        "pass_criteria": pass_criteria,
        "why_this_should_pass": why_this_should_pass,
        "suggested_default_decisions": {
            "row_level_decision": "approved if bounded spot checks do not reveal contradictions",
            "errata_decision": "not_applicable" if sr4 else "applied unless a specific official errata source remains unreconciled to the selected 2024 core baseline",
            "errata_rationale": (
                "no official errata sources are in scope for the selected SR4 core-only baseline"
                if sr4
                else "selected 2024 core baseline is the authority target; prefer applied if it is accepted as the consolidated official source"
            ),
        },
        "exact_edit_contract": exact_edit_contract,
        "rerun_commands": rerun_commands,
        "can_change_recommendation_to_sign_off_allowed_when": [
            "row-level decision is approved",
            "human review file is approved with ready token approved true",
        ]
        + ([] if fixture_alignment_pass else ["fixture expectations are human-confirmed"])
        + ([] if no_errata_sources else ["errata decision is applied/not_applicable/defer with rationale"])
        + ([] if sr4 or explain_alignment_pass else ["SR6 explain corpus is human-confirmed against approved row-level authority"]),
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
        f"- Human review file: `{packet['human_review_file']}`",
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
        "## Preferred Signoff Path",
        "",
        *[f"- {item}" for item in packet["preferred_signoff_path"]],
        "",
        "## Pass Criteria",
        "",
        *[f"- {item}" for item in packet["pass_criteria"]],
        "",
        "## Why This Should Pass",
        "",
        *[f"- {item}" for item in packet["why_this_should_pass"]],
        "",
        "## Suggested Default Decisions",
        "",
        f"- Row-level decision: `{packet['suggested_default_decisions']['row_level_decision']}`",
        f"- Errata decision: `{packet['suggested_default_decisions']['errata_decision']}`",
        f"- Errata rationale: `{packet['suggested_default_decisions']['errata_rationale']}`",
        "",
        "",
        "## Review Inputs",
        "",
        f"- Row-level mapping: `{packet['review_inputs']['row_level_mapping']}`",
        f"- Errata posture: `{packet['review_inputs']['errata_source_posture']}`",
        f"- Review handoff: `{packet['review_inputs']['review_handoff']}`",
        f"- Private registry: `{packet['review_inputs']['private_registry'] or 'none'}`",
        "",
        "## Exact File Edits",
        "",
        *[f"- `{key}: {value}`" for key, value in packet["exact_edit_contract"].items()],
        "",
        "## Rerun Commands",
        "",
        *[f"- `{command}`" for command in packet["rerun_commands"]],
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
