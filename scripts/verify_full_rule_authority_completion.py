#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
OUT_ROOT = COMPLETION_ROOT / "full_product_rule_authority"


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    sr4_root = COMPLETION_ROOT / "sr4_rule_authority"
    sr6_root = COMPLETION_ROOT / "sr6_rule_authority"
    sr4_integration = load_json(sr4_root / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json")
    sr4_provider = load_json(sr4_root / "SR4_PROVIDER_COVERAGE.generated.json")
    sr4_tables = load_json(sr4_root / "SR4_TABLE_IMPORTS.generated.json")
    sr4_golden = load_json(sr4_root / "SR4_GOLDEN_FIXTURES.generated.json")
    sr4_errata = load_json(sr4_root / "SR4_ERRATA_PROFILE.generated.json")
    sr6_integration = load_json(sr6_root / "SR6_RULE_AUTHORITY_INTEGRATION.generated.json")
    sr6_provider = load_json(sr6_root / "SR6_PROVIDER_COVERAGE.generated.json")
    sr6_tables = load_json(sr6_root / "SR6_TABLE_IMPORTS.generated.json")
    sr6_golden = load_json(sr6_root / "SR6_GOLDEN_FIXTURES.generated.json")
    sr6_errata = load_json(sr6_root / "SR6_ERRATA_PROFILE.generated.json")
    sr5_acceptance = load_json(PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json")
    sr5_depth = load_json(PUBLISHED_ROOT / "SR5_RULESET_DEPTH.generated.json")
    sr5_registry = load_json(PUBLISHED_ROOT / "SR5_RULE_AUTHORITY_REGISTRY.generated.json")

    sr4_ready = bool(sr4_integration.get("readiness_token_allowed"))
    sr6_ready = bool(sr6_integration.get("readiness_token_allowed"))
    sr5_ready = (
        sr5_acceptance.get("status") == "pass"
        and sr5_acceptance.get("serious_implementation_claim") == "allowed"
        and sr5_depth.get("serious_implementation_claim") == "allowed"
        and sr5_registry.get("final_verdict") == "SR5_RULE_AUTHORITY_READY"
        and int(sr5_registry.get("rulefact_count") or 0) >= 100
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
            },
            "remaining_gates": [
                "human-reviewed row-level mapping from indexed table evidence into normalized records",
                "errata profile applied and reviewed",
                "complete authority golden fixture corpus, beyond seed fixtures",
                "human rule review signoff",
            ],
            "errata_status": sr4_errata.get("status"),
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
            },
            "remaining_gates": [
                "human-reviewed mapping of private PDF line-hash candidates into normalized public-safe records",
                "errata profile applied and reviewed",
                "complete authority golden fixture corpus, beyond seed fixtures",
                "full provider-backed explain receipt corpus",
                "human rule review signoff",
            ],
            "errata_status": sr6_errata.get("status"),
            "readiness_token_allowed": sr6_ready,
        })

    payload = {
        "contract_name": "chummer.full_product_rule_authority_completion",
        "status": "blocked" if blockers else "pass",
        "final_verdict": "NOT_READY" if blockers else "FULL_RULE_AUTHORITY_READY",
        "readiness_token_allowed": not blockers,
        "rulesets": {
            "sr4": {
                "rule_authority_ready": sr4_ready,
                "provider_coverage_status": sr4_provider.get("status"),
                "implemented_provider_count": sr4_provider.get("implemented_provider_count"),
                "final_verdict": sr4_integration.get("final_verdict"),
            },
            "sr5": {
                "rule_authority_ready": sr5_ready,
                "acceptance_status": sr5_acceptance.get("status"),
                "acceptance_claim": sr5_acceptance.get("serious_implementation_claim"),
                "depth_claim": sr5_depth.get("serious_implementation_claim"),
                "final_verdict": sr5_registry.get("final_verdict"),
                "rulefact_count": sr5_registry.get("rulefact_count"),
            },
            "sr6": {
                "rule_authority_ready": sr6_ready,
                "provider_coverage_status": sr6_provider.get("status"),
                "implemented_provider_count": sr6_provider.get("implemented_provider_count"),
                "final_verdict": sr6_integration.get("final_verdict"),
            },
        },
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
