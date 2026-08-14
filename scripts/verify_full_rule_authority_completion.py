#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_rule_authority_human_review import validate_review


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
COMPLETION_ROOT = Path(
    os.environ.get("CHUMMER_COMPLETION_ROOT", "/docker/chummercomplete/_completion")
)
OUT_ROOT = COMPLETION_ROOT / "full_product_rule_authority"

SR5_PROVIDER_SPOT_CHECKS = {
    "SR5CharacterCreationProvider": "SR5 character creation",
    "SR5CombatProvider": "SR5 combat",
    "SR5MagicProvider": "SR5 magic",
    "SR5MatrixProvider": "SR5 matrix",
    "SR5RiggingProvider": "SR5 rigging",
    "SR5GearProvider": "SR5 gear",
    "SR5DiceProvider": "SR5 dice",
    "SR5TestProvider": "SR5 tests",
    "SR5ExplainReceiptProvider": "SR5 explain receipts",
}
MIN_RULEFACT_COUNT = 100


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def validate_promoted_operator_receipt(
    receipt: dict[str, Any],
    expected_rulefact_counts: dict[str, int],
) -> dict[str, Any]:
    failures: list[str] = []
    rulesets_by_id: dict[str, dict[str, Any]] = {}
    for row in receipt.get("rulesets") or []:
        ruleset = str(row.get("ruleset") or "").lower()
        if ruleset:
            rulesets_by_id[ruleset] = row

    if receipt.get("final_verdict") != "FULL_RULE_AUTHORITY_READY":
        failures.append("promoted operator receipt is not FULL_RULE_AUTHORITY_READY")
    if receipt.get("status") != "pass":
        failures.append("promoted operator receipt status is not pass")

    normalized_rulesets: dict[str, dict[str, Any]] = {}
    for ruleset, expected_count in expected_rulefact_counts.items():
        row = rulesets_by_id.get(ruleset)
        if row is None:
            failures.append(f"promoted operator receipt is missing {ruleset}")
            normalized_rulesets[ruleset] = {
                "present": False,
                "rulefact_count": None,
                "expected_rulefact_count": expected_count,
            }
            continue

        try:
            rulefact_count = int(row.get("rulefact_count"))
        except (TypeError, ValueError):
            failures.append(f"promoted operator receipt {ruleset} rulefact_count is missing or not numeric")
            normalized_rulesets[ruleset] = {
                "present": True,
                "rulefact_count": row.get("rulefact_count"),
                "expected_rulefact_count": expected_count,
            }
            continue

        if rulefact_count < MIN_RULEFACT_COUNT:
            failures.append(
                f"promoted operator receipt {ruleset} rulefact_count {rulefact_count} is below {MIN_RULEFACT_COUNT}"
            )
        if rulefact_count != expected_count:
            failures.append(
                f"promoted operator receipt {ruleset} rulefact_count {rulefact_count} does not match registry {expected_count}"
            )
        normalized_rulesets[ruleset] = {
            "present": True,
            "rulefact_count": rulefact_count,
            "expected_rulefact_count": expected_count,
            "verdict": row.get("verdict"),
            "status": row.get("status"),
        }

    return {
        "status": "pass" if not failures else "fail",
        "rulesets": normalized_rulesets,
        "failures": failures,
    }


def main() -> int:
    sr4_root = COMPLETION_ROOT / "sr4_rule_authority"
    sr6_root = COMPLETION_ROOT / "sr6_rule_authority"
    sr4_integration = load_json(sr4_root / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json")
    sr4_provider = load_json(sr4_root / "SR4_PROVIDER_COVERAGE.generated.json")
    sr4_tables = load_json(sr4_root / "SR4_TABLE_IMPORTS.generated.json")
    sr4_golden = load_json(sr4_root / "SR4_GOLDEN_FIXTURES.generated.json")
    sr4_errata = load_json(sr4_root / "SR4_ERRATA_PROFILE.generated.json")
    sr4_row_level = load_json(sr4_root / "SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json")
    sr4_errata_posture = load_json(sr4_root / "SR4_ERRATA_SOURCE_POSTURE.generated.json")
    sr4_matrix = load_json(sr4_root / "SR4_VERIFICATION_MATRIX_RUN.generated.json")
    sr4_alignment = load_json(sr4_root / "SR4_AUTHORITY_ALIGNMENT.generated.json")
    sr6_integration = load_json(sr6_root / "SR6_RULE_AUTHORITY_INTEGRATION.generated.json")
    sr6_provider = load_json(sr6_root / "SR6_PROVIDER_COVERAGE.generated.json")
    sr6_tables = load_json(sr6_root / "SR6_TABLE_IMPORTS.generated.json")
    sr6_golden = load_json(sr6_root / "SR6_GOLDEN_FIXTURES.generated.json")
    sr6_errata = load_json(sr6_root / "SR6_ERRATA_PROFILE.generated.json")
    sr6_row_level = load_json(sr6_root / "SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json")
    sr6_errata_posture = load_json(sr6_root / "SR6_ERRATA_SOURCE_POSTURE.generated.json")
    sr6_matrix = load_json(sr6_root / "SR6_VERIFICATION_MATRIX_RUN.generated.json")
    sr6_alignment = load_json(sr6_root / "SR6_AUTHORITY_ALIGNMENT.generated.json")
    sr4_human_review = validate_review("sr4")
    sr6_human_review = validate_review("sr6")
    sr5_acceptance = load_json(PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json")
    sr5_depth = load_json(PUBLISHED_ROOT / "SR5_RULESET_DEPTH.generated.json")
    sr5_registry = load_json(PUBLISHED_ROOT / "SR5_RULE_AUTHORITY_REGISTRY.generated.json")
    promoted_receipt = load_json(PUBLISHED_ROOT / "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json")
    expected_rulefact_counts = {
        "sr4": int(sr4_integration.get("rulefact_count") or 0),
        "sr5": int(sr5_registry.get("rulefact_count") or 0),
        "sr6": int(sr6_integration.get("rulefact_count") or 0),
    }
    promoted_receipt_validation = validate_promoted_operator_receipt(promoted_receipt, expected_rulefact_counts)

    sr4_ready = bool(sr4_integration.get("readiness_token_allowed"))
    sr6_ready = bool(sr6_integration.get("readiness_token_allowed"))
    sr5_missing_providers = list(sr5_registry.get("missing_implemented_providers") or [])
    sr5_ready = (
        sr5_acceptance.get("status") == "pass"
        and sr5_acceptance.get("serious_implementation_claim") == "allowed"
        and sr5_depth.get("serious_implementation_claim") == "allowed"
        and sr5_registry.get("final_verdict") == "SR5_RULE_AUTHORITY_READY"
        and int(sr5_registry.get("rulefact_count") or 0) >= 100
        and not sr5_missing_providers
    )
    blockers = []
    if not sr4_ready:
        blockers.append({
            "ruleset": "sr4",
            "blocked_token": "SR4_RULE_AUTHORITY_READY",
            "machine_closed": {
                "provider_status": sr4_provider.get("status"),
                "missing_implemented_providers": sr4_provider.get("missing_implemented_providers"),
                "missing_profile_status": sr4_provider.get("missing_profile_status"),
                "golden_fixture_status": sr4_golden.get("status"),
                "table_import_status": sr4_tables.get("status"),
                "verification_matrix_status": sr4_matrix.get("status"),
                "verification_matrix_unexpected_failed_gates": sr4_matrix.get("unexpected_failed_gates", []),
            },
            "remaining_gates": [
                "human-reviewed row-level mapping from indexed table evidence into normalized records",
                *([] if sr4_alignment.get("fixture_alignment", {}).get("status") == "pass" else ["fixture expectations reviewed against approved row-level authority"]),
                "human rule review signoff",
            ],
            "errata_status": sr4_errata.get("status"),
            "blocker_receipts": {
                "row_level_mapping": str(sr4_root / "SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json"),
                "errata_posture": str(sr4_root / "SR4_ERRATA_SOURCE_POSTURE.generated.json"),
                "review_handoff": str(sr4_root / "SR4_RULE_AUTHORITY_REVIEW_HANDOFF.md"),
                "reviewer_decision_packet": str(sr4_root / "SR4_REVIEWER_DECISION_PACKET.generated.json"),
                "human_review": str(sr4_root / "SR4_HUMAN_RULE_REVIEW.md"),
                "verification_matrix_run": str(sr4_root / "SR4_VERIFICATION_MATRIX_RUN.generated.json"),
            },
            "row_level_mapping_status": sr4_row_level.get("status"),
            "preferred_signoff_path": [
                "spot-check the listed high-volume XML files first",
                "approve row-level mapping if no contradiction is found",
                "keep errata not_applicable",
                "approve the human review file and rerun the ready checks",
            ],
            "spot_check_plan": sr4_row_level.get("review_packet", {}).get("spot_check_plan", []),
            "errata_posture_status": sr4_errata_posture.get("status"),
            "suggested_errata_decision": sr4_errata_posture.get("review_packet", {}).get("recommended_decision"),
            "human_review_status": sr4_human_review,
            "verification_matrix_status": sr4_matrix.get("status"),
            "verification_matrix_failed_gates": sr4_matrix.get("failed_gates", []),
            "verification_matrix_unexpected_failed_gates": sr4_matrix.get("unexpected_failed_gates", []),
            "verification_matrix_expected_ready_blockers": sr4_matrix.get("expected_ready_blockers", []),
            "readiness_token_allowed": sr4_ready,
        })
    if not sr6_ready:
        blockers.append({
            "ruleset": "sr6",
            "blocked_token": "SR6_RULE_AUTHORITY_READY",
            "machine_closed": {
                "provider_status": sr6_provider.get("status"),
                "missing_implemented_providers": sr6_provider.get("missing_implemented_providers"),
                "missing_profile_status": sr6_provider.get("missing_profile_status"),
                "golden_fixture_status": sr6_golden.get("status"),
                "table_import_status": sr6_tables.get("status"),
                "verification_matrix_status": sr6_matrix.get("status"),
                "verification_matrix_unexpected_failed_gates": sr6_matrix.get("unexpected_failed_gates", []),
            },
            "remaining_gates": [
                "human-reviewed mapping of 2024-core PDF line-hash candidates into normalized public-safe records",
                "official errata posture reviewed against the selected 2024 core baseline",
                *([] if sr6_alignment.get("fixture_alignment", {}).get("status") == "pass" else ["fixture expectations reviewed against approved row-level authority"]),
                *([] if sr6_alignment.get("explain_alignment", {}).get("status") == "pass" else ["provider-backed explain corpus reviewed against approved row-level authority"]),
                "human rule review signoff",
            ],
            "errata_status": sr6_errata.get("status"),
            "blocker_receipts": {
                "row_level_mapping": str(sr6_root / "SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json"),
                "errata_posture": str(sr6_root / "SR6_ERRATA_SOURCE_POSTURE.generated.json"),
                "review_handoff": str(sr6_root / "SR6_RULE_AUTHORITY_REVIEW_HANDOFF.md"),
                "reviewer_decision_packet": str(sr6_root / "SR6_REVIEWER_DECISION_PACKET.generated.json"),
                "human_review": str(sr6_root / "SR6_HUMAN_RULE_REVIEW.md"),
                "verification_matrix_run": str(sr6_root / "SR6_VERIFICATION_MATRIX_RUN.generated.json"),
            },
            "row_level_mapping_status": sr6_row_level.get("status"),
            "preferred_signoff_path": [
                "spot-check the listed 2024-core line-hash candidates first",
                "approve row-level mapping if no contradiction is found",
                "prefer errata applied if the 2024 baseline is accepted as the consolidated core source",
                "approve the human review file and rerun the ready checks",
            ],
            "spot_check_plan": sr6_row_level.get("review_packet", {}).get("spot_check_plan", []),
            "errata_posture_status": sr6_errata_posture.get("status"),
            "suggested_errata_decision": sr6_errata_posture.get("review_packet", {}).get("recommended_decision"),
            "human_review_status": sr6_human_review,
            "verification_matrix_status": sr6_matrix.get("status"),
            "verification_matrix_failed_gates": sr6_matrix.get("failed_gates", []),
            "verification_matrix_unexpected_failed_gates": sr6_matrix.get("unexpected_failed_gates", []),
            "verification_matrix_expected_ready_blockers": sr6_matrix.get("expected_ready_blockers", []),
            "readiness_token_allowed": sr6_ready,
        })
    if not sr5_ready:
        sr5_missing_spot_checks = [
            SR5_PROVIDER_SPOT_CHECKS.get(provider, provider)
            for provider in sr5_missing_providers
        ]
        blockers.append({
            "ruleset": "sr5",
            "blocked_token": "SR5_RULE_AUTHORITY_READY",
            "machine_closed": {
                "acceptance_status": sr5_acceptance.get("status"),
                "acceptance_claim": sr5_acceptance.get("serious_implementation_claim"),
                "depth_status": sr5_depth.get("status"),
                "depth_claim": sr5_depth.get("serious_implementation_claim"),
                "final_verdict": sr5_registry.get("final_verdict"),
                "rulefact_count": sr5_registry.get("rulefact_count"),
                "missing_implemented_providers": sr5_missing_providers,
            },
            "remaining_gates": [
                "implementation-backed SR5 mechanical provider RuleFacts",
                "SR5 missing provider coverage: " + ", ".join(sr5_missing_spot_checks),
            ],
            "preferred_signoff_path": [
                "implement or bind SR5 mechanical providers to public-safe RuleFacts",
                "rerun scripts/verify_sr5_rule_authority_seed.py",
                "rerun scripts/promote_rule_authority_operator_gold.py",
                "rerun scripts/verify_full_rule_authority_completion.py",
            ],
            "spot_check_plan": sr5_missing_spot_checks,
            "blocker_receipts": {
                "registry": str(PUBLISHED_ROOT / "SR5_RULE_AUTHORITY_REGISTRY.generated.json"),
                "authority_registry": str(PUBLISHED_ROOT / "rule-authority" / "SR5_RULEFACT_REGISTRY.generated.json"),
                "provider_coverage": str(PUBLISHED_ROOT / "rule-authority" / "SR5_PROVIDER_COVERAGE.generated.json"),
            },
            "readiness_token_allowed": sr5_ready,
        })
    if promoted_receipt_validation["status"] != "pass":
        blockers.append({
            "ruleset": "full_product",
            "blocked_token": "FULL_RULE_AUTHORITY_READY",
            "machine_closed": {
                "promoted_operator_receipt_status": promoted_receipt_validation["status"],
                "promoted_operator_receipt_failures": promoted_receipt_validation["failures"],
            },
            "remaining_gates": [
                "regenerate the promoted operator rule-authority receipt from current structured registries",
                "ensure every promoted ruleset row carries rulefact_count and matches the registry count",
            ],
            "blocker_receipts": {
                "promoted_operator_receipt": str(PUBLISHED_ROOT / "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json"),
            },
            "readiness_token_allowed": False,
        })

    payload = {
        "contract_name": "chummer.full_product_rule_authority_completion",
        "generated_at_utc": now_iso(),
        "status": "blocked" if blockers else "pass",
        "final_verdict": "NOT_READY" if blockers else "FULL_RULE_AUTHORITY_READY",
        "readiness_token_allowed": not blockers,
        "rulesets": {
            "sr4": {
                "rule_authority_ready": sr4_ready,
                "provider_coverage_status": sr4_provider.get("status"),
                "implemented_provider_count": sr4_provider.get("implemented_provider_count"),
                "final_verdict": sr4_integration.get("final_verdict"),
                "human_review": sr4_human_review,
                "verification_matrix_status": sr4_matrix.get("status"),
                "verification_matrix_unexpected_failed_gates": sr4_matrix.get("unexpected_failed_gates", []),
            },
            "sr5": {
                "rule_authority_ready": sr5_ready,
                "acceptance_status": sr5_acceptance.get("status"),
                "acceptance_claim": sr5_acceptance.get("serious_implementation_claim"),
                "depth_claim": sr5_depth.get("serious_implementation_claim"),
                "final_verdict": sr5_registry.get("final_verdict"),
                "rulefact_count": sr5_registry.get("rulefact_count"),
                "missing_implemented_providers": sr5_missing_providers,
            },
            "sr6": {
                "rule_authority_ready": sr6_ready,
                "provider_coverage_status": sr6_provider.get("status"),
                "implemented_provider_count": sr6_provider.get("implemented_provider_count"),
                "final_verdict": sr6_integration.get("final_verdict"),
                "human_review": sr6_human_review,
                "verification_matrix_status": sr6_matrix.get("status"),
                "verification_matrix_unexpected_failed_gates": sr6_matrix.get("unexpected_failed_gates", []),
            },
        },
        "promoted_operator_receipt": promoted_receipt_validation,
        "blockers": blockers,
        "copyright_boundary": {
            "public_safe": True,
            "sourcebook_prose_copied": False,
            "ready_tokens_withheld_until_human_review": bool(blockers),
        },
    }

    write_json(OUT_ROOT / "FULL_PRODUCT_RULE_AUTHORITY_COMPLETION.generated.json", payload)
    write_json(PUBLISHED_ROOT / "FULL_PRODUCT_RULE_AUTHORITY_COMPLETION.generated.json", payload)
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0 if payload["status"] == "pass" else 2


if __name__ == "__main__":
    raise SystemExit(main())
