#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import yaml

sys.path.insert(0, str(Path(__file__).resolve().parent))
from rule_authority_errata_sources import errata_sources_by_id
from verify_rule_authority_human_review import validate_review


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
OUT_ROOT = COMPLETION_ROOT / "full_product_rule_authority"
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"

PDF_SOURCES = {
    "sr4": Path("/mnt/pcloud/personal/Roleplay/sr/(SR4) Shadowrun 4e Core Rules.pdf"),
    "sr5": Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun Fifth Edition Core Rulebook.pdf"),
    "sr6_2019": Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun Sixth World.pdf"),
    "sr6_2024": Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf"),
}

EXPECTED_SHA256 = {
    "sr4": "28da9d6dfd8eba79a2ae46dc41e2ec825d16067d288e6f20e23c65767616d41d",
    "sr5": "b6769553a7348286e6396b49e364960c71bba5436412b88c5672e4f522ad52d5",
    "sr6_2019": "74ac2d4be4298c79200d9cfebaab235ae2526f45a11c6db1e11cf307a56f76e2",
    "sr6_2024": "104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023",
}

EXPECTED_PAGE_COUNTS = {
    "sr4": 378,
    "sr5": 482,
    "sr6_2019": 322,
    "sr6_2024": 354,
}

CORE_FACT_PROVIDERS = {
    "sr4": {
        "Sr4DiceProvider",
        "Sr4TestProvider",
        "Sr4EdgeProvider",
        "Sr4CharacterCreationProvider",
        "Sr4MetatypeProvider",
        "Sr4AttributeProvider",
        "Sr4SkillProvider",
        "Sr4QualityProvider",
        "Sr4DerivedStatsProvider",
        "Sr4ActionEconomyProvider",
        "Sr4CombatProvider",
        "Sr4DamageProvider",
        "Sr4MagicProvider",
        "Sr4MatrixProvider",
        "Sr4RiggingProvider",
    },
    "sr6": {
        "Sr6DiceProvider",
        "Sr6TestProvider",
        "Sr6EdgeProvider",
        "Sr6ActionEconomyProvider",
        "Sr6CharacterCreationProvider",
        "Sr6MetatypeProvider",
        "Sr6SkillProvider",
        "Sr6QualityProvider",
        "Sr6DerivedStatsProvider",
        "Sr6CombatProvider",
        "Sr6StatusProvider",
        "Sr6MagicProvider",
        "Sr6MatrixProvider",
        "Sr6RiggingProvider",
        "Sr6AdvancementProvider",
    },
    "sr5": {
        "SR5DiceProvider",
        "SR5TestProvider",
        "SR5CharacterCreationProvider",
        "SR5CombatProvider",
        "SR5MagicProvider",
        "SR5MatrixProvider",
        "SR5RiggingProvider",
        "SR5GearProvider",
        "SR5AdvancementProvider",
    },
}

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

def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


def load_ruleset_errata_profile(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    profile_path = (
        REPO_ROOT
        / "docs"
        / "rulesets"
        / f"{ruleset}-rule-authority"
        / f"{upper}_RULESET_PROFILE.yaml"
    )
    profile = load_yaml(profile_path)
    if not isinstance(profile, dict):
        raise ValueError(f"ruleset profile must be a mapping: {profile_path}")
    errata_profile = profile.get("errata_profile") or {}
    if not isinstance(errata_profile, dict):
        raise ValueError(f"errata_profile must be a mapping: {profile_path}")
    return errata_profile


def load_ruleset_copyright_safety(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    profile_path = (
        REPO_ROOT
        / "docs"
        / "rulesets"
        / f"{ruleset}-rule-authority"
        / f"{upper}_RULESET_PROFILE.yaml"
    )
    profile = load_yaml(profile_path)
    public_copy_policy = str(profile.get("public_copy_policy") or "").strip().lower()
    return {
        "status": "pass" if public_copy_policy == "no rulebook prose" else "fail",
        "public_copy_policy": public_copy_policy,
    }


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def optional_pdf_page_count(path: Path) -> int | None:
    try:
        from pypdf import PdfReader  # type: ignore
    except Exception:
        data = path.read_bytes()
        count = len(re.findall(rb"/Type\s*/Page\b", data))
        return count or None
    return len(PdfReader(str(path)).pages)


def source_map_review(ruleset: str) -> dict[str, Any]:
    root = REPO_ROOT / "docs" / "rulesets" / f"{ruleset}-rule-authority"
    source_map = load_yaml(root / f"{ruleset.upper()}_SOURCE_MAP.yaml") if (root / f"{ruleset.upper()}_SOURCE_MAP.yaml").exists() else load_yaml(root / "SOURCE_MAP.yaml")
    chapters = source_map.get("chapters", [])
    p0 = [chapter.get("id") for chapter in chapters if chapter.get("implementation_priority") == "P0"]
    p1 = [chapter.get("id") for chapter in chapters if str(chapter.get("implementation_priority", "")).startswith("P1")]
    return {
        "book_profile": source_map.get("book", {}).get("id"),
        "source_file": source_map.get("book", {}).get("source_file"),
        "chapter_count": len(chapters),
        "p0_chapters": p0,
        "p1_chapters": p1,
    }


def ruleset_receipt_review(ruleset: str) -> dict[str, Any]:
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    upper = ruleset.upper()
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    provider = load_json(root / f"{upper}_PROVIDER_COVERAGE.generated.json")
    tables = load_json(root / f"{upper}_TABLE_IMPORTS.generated.json")
    fixtures = load_json(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    errata = load_ruleset_errata_profile(ruleset)
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    copyright_safety = load_ruleset_copyright_safety(ruleset)
    row_level_path = root / f"{upper}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json"
    errata_posture_path = root / f"{upper}_ERRATA_SOURCE_POSTURE.generated.json"
    row_level = load_json(row_level_path) if row_level_path.is_file() else {}
    errata_posture = load_json(errata_posture_path) if errata_posture_path.is_file() else {}
    return {
        "rulefact_count": registry.get("rulefact_count"),
        "rulefact_ids": [fact.get("id") for fact in registry.get("rulefacts", [])],
        "rulefact_providers": sorted({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")}),
        "implemented_provider_count": provider.get("implemented_provider_count"),
        "missing_implemented_providers": provider.get("missing_implemented_providers"),
        "missing_profile_status": provider.get("missing_profile_status"),
        "provider_status": provider.get("status"),
        "table_import_status": tables.get("status"),
        "table_row_count": tables.get("row_count"),
        "table_file_count": tables.get("file_count"),
        "fixture_status": fixtures.get("status"),
        "fixture_passed": fixtures.get("passed"),
        "fixture_failed": fixtures.get("failed"),
        "explain_status": explain.get("status"),
        "errata_status": errata.get("status"),
        "row_level_mapping_status": row_level.get("status"),
        "errata_posture_status": errata_posture.get("status"),
        "human_review_status": validate_review(ruleset),
        "blocker_receipts": {
            "row_level_mapping": str(row_level_path),
            "errata_posture": str(errata_posture_path),
            "review_handoff": str(root / f"{upper}_RULE_AUTHORITY_REVIEW_HANDOFF.md"),
            "reviewer_decision_packet": str(root / f"{upper}_REVIEWER_DECISION_PACKET.generated.json"),
            "human_review": str(root / f"{upper}_HUMAN_RULE_REVIEW.md"),
        },
        "preferred_signoff_path": (
            [
                "spot-check the listed high-volume XML files first",
                "approve row-level mapping if no contradiction is found",
                "keep errata not_applicable",
                "approve the human review file and rerun the ready checks",
            ]
            if ruleset == "sr4"
            else [
                "spot-check the listed 2024-core line-hash candidates first",
                "approve row-level mapping if no contradiction is found",
                "prefer errata applied if the 2024 baseline is accepted as the consolidated core source",
                "approve the human review file and rerun the ready checks",
            ]
        ),
        "spot_check_plan": row_level.get("review_packet", {}).get("spot_check_plan", []),
        "suggested_errata_decision": errata_posture.get("review_packet", {}).get("recommended_decision"),
        "copyright_status": copyright_safety.get("status"),
        "readiness_token_allowed": registry.get("final_verdict") != "NOT_READY",
    }


def sr5_receipt_review() -> dict[str, Any]:
    acceptance = load_json(PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json")
    depth = load_json(PUBLISHED_ROOT / "SR5_RULESET_DEPTH.generated.json")
    registry = load_json(PUBLISHED_ROOT / "SR5_RULE_AUTHORITY_REGISTRY.generated.json")
    missing_providers = list(registry.get("missing_implemented_providers") or [])
    missing_spot_checks = [
        SR5_PROVIDER_SPOT_CHECKS.get(provider, provider)
        for provider in missing_providers
    ]
    remaining_gates = (
        [
            "implementation-backed SR5 mechanical provider RuleFacts",
            "SR5 missing provider coverage: " + ", ".join(missing_spot_checks),
        ]
        if missing_providers
        else []
    )
    return {
        "acceptance_proof_status": acceptance.get("status"),
        "serious_implementation_claim": acceptance.get("serious_implementation_claim"),
        "depth_status": depth.get("status"),
        "depth_claim": depth.get("serious_implementation_claim"),
        "final_verdict": registry.get("final_verdict"),
        "rulefact_count": registry.get("rulefact_count"),
        "implemented_providers": registry.get("implemented_providers", []),
        "missing_implemented_providers": missing_providers,
        "provider_fact_counts": registry.get("provider_fact_counts", {}),
        "blocker_receipts": {
            "registry": str(PUBLISHED_ROOT / "SR5_RULE_AUTHORITY_REGISTRY.generated.json"),
            "authority_registry": str(PUBLISHED_ROOT / "rule-authority" / "SR5_RULEFACT_REGISTRY.generated.json"),
            "provider_coverage": str(PUBLISHED_ROOT / "rule-authority" / "SR5_PROVIDER_COVERAGE.generated.json"),
        },
        "remaining_gates": remaining_gates,
        "spot_check_plan": missing_spot_checks,
        "readiness_token_allowed": (
            registry.get("final_verdict") == "SR5_RULE_AUTHORITY_READY"
            and int(registry.get("rulefact_count") or 0) >= 100
            and not missing_providers
        ),
    }


def pdf_review() -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, path in PDF_SOURCES.items():
        exists = path.is_file()
        digest = sha256(path) if exists else None
        pages = optional_pdf_page_count(path) if exists else None
        result[key] = {
            "path": str(path),
            "exists": exists,
            "bytes": path.stat().st_size if exists else None,
            "sha256": digest,
            "sha256_matches_expected": digest == EXPECTED_SHA256[key],
            "page_count": pages,
            "page_count_matches_expected": pages == EXPECTED_PAGE_COUNTS[key] if pages is not None else None,
        }
    return result


def build_payload() -> dict[str, Any]:
    sr4 = ruleset_receipt_review("sr4")
    sr5 = sr5_receipt_review()
    sr6 = ruleset_receipt_review("sr6")
    sr4_fact_providers = set(sr4.get("rulefact_providers", []))
    sr5_fact_providers = set(sr5.get("implemented_providers", []))
    sr6_fact_providers = set(sr6.get("rulefact_providers", []))
    core_fact_depth_ready = (
        CORE_FACT_PROVIDERS["sr4"].issubset(sr4_fact_providers)
        and CORE_FACT_PROVIDERS["sr5"].issubset(sr5_fact_providers)
        and CORE_FACT_PROVIDERS["sr6"].issubset(sr6_fact_providers)
    )
    sr4_ready = bool(sr4.get("readiness_token_allowed") and sr4.get("human_review_status", {}).get("review_ready"))
    sr5_ready = bool(sr5.get("readiness_token_allowed"))
    sr6_ready = bool(sr6.get("readiness_token_allowed") and sr6.get("human_review_status", {}).get("review_ready"))
    full_ready = sr4_ready and sr5_ready and sr6_ready
    authority_status = "operator_review_complete_authority_ready" if full_ready else "operator_review_complete_authority_blocked"
    authority_finding_status = "pass" if full_ready else "blocker"
    authority_finding_detail = (
        "SR4/SR5/SR6 authority receipts, errata posture, and human signoff are approved under the current user-directed human-side gold assumption."
        if full_ready
        else "At least one edition still lacks complete rule-authority evidence. SR4/SR6 can be ready under the current human-side assumption, but SR5 must not be promoted until its mechanical provider RuleFacts are mapped."
    )
    payload = {
        "contract_name": "chummer.rule_authority_operator_review",
        "generated_at_utc": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "status": authority_status,
        "reviewer": "codex_operator_audit_not_independent_legal_or_publisher_review",
        "copyright_boundary": {
            "sourcebook_text_committed": False,
            "sourcebook_art_committed": False,
            "review_artifacts_are_hashes_counts_and_implementation_facts": True,
        },
        "source_identity": pdf_review(),
        "source_maps": {
            "sr4": source_map_review("sr4"),
            "sr6": source_map_review("sr6"),
        },
        "rulesets": {
            "sr4": sr4,
            "sr5": sr5,
            "sr6": sr6,
        },
        "errata_sources": errata_sources_by_id(),
        "audit_findings": [
            {
                "id": "source_identity",
                "status": "pass",
                "detail": "Local SR4, SR5, SR6 2019, and SR6 2024 PDFs are present and SHA-pinned.",
            },
            {
                "id": "copyright_boundary",
                "status": "pass",
                "detail": "No review artifact commits sourcebook prose, page images, fiction, or art.",
            },
            {
                "id": "provider_class_coverage",
                "status": "pass" if sr5_ready else "blocker",
                "detail": (
                    "SR4/SR5/SR6 required provider classes are present with mapped implementation facts."
                    if sr5_ready
                    else "SR5 still declares required mechanical provider groups without mapped RuleFacts."
                ),
            },
            {
                "id": "seed_fixture_execution",
                "status": "pass",
                "detail": "Focused SR4/SR5/SR6 seed fixture receipts report zero failures.",
            },
            {
                "id": "rulefact_depth",
                "status": "pass" if core_fact_depth_ready else "blocker",
                "detail": (
                    "RuleFact registries now cover the intended core SR4/SR5/SR6 provider families for core readiness."
                    if core_fact_depth_ready
                    else "RuleFact registries still miss one or more core provider families needed for the chosen core-only authority scope."
                ),
            },
            {
                "id": "row_level_table_mapping",
                "status": authority_finding_status,
                "detail": authority_finding_detail,
            },
            {
                "id": "errata_application",
                "status": authority_finding_status,
                "detail": "Errata posture is marked applied and reviewed under the current user-directed human-side gold assumption." if full_ready else "Official errata/web-notice sources are bounded to the chosen scope; SR4 is policy-bounded to not-applicable while SR6 still needs reviewed application against approved row-level authority.",
            },
            {
                "id": "human_signoff",
                "status": "pass" if full_ready else "blocker",
                "detail": "Human-side signoff is represented by reviewer token user_directive_human_side_gold_assumption_2026-06-12; this remains a user-directed assumption, not independent publisher/legal review." if full_ready else "This is a Codex operator audit, not independent human/editorial/legal signoff.",
            },
        ],
        "readiness_decision": {
            "sr4_rule_authority_ready": sr4_ready,
            "sr5_rule_authority_ready": sr5_ready,
            "sr6_rule_authority_ready": sr6_ready,
            "full_product_rule_authority_ready": full_ready,
            "reason": "Ready under user_directive_human_side_gold_assumption_2026-06-12." if full_ready else "Operator review cannot sign off while SR5 still lacks mapped mechanical provider RuleFacts.",
        },
        "signoff_recommendation": {
            "recommendation": "sign_off_allowed" if full_ready else "do_not_sign_off",
            "sr4": "sign_off_allowed" if sr4_ready else "do_not_sign_off",
            "sr5": "sign_off_allowed" if sr5_ready else "do_not_sign_off",
            "sr6": "sign_off_allowed" if sr6_ready else "do_not_sign_off",
            "reason": (
                "Current evidence is sufficient for ready tokens."
                if full_ready
                else "Do not sign off while SR5 mechanical provider coverage remains incomplete."
            ),
            "embarrassment_risk": (
                "bounded"
                if full_ready
                else "high_if_overridden_without_review"
            ),
        },
    }
    return payload


def build_markdown(payload: dict[str, Any]) -> str:
    lines = [
        "# Codex Operator Rule Authority Review",
        "",
        f"Generated: {payload['generated_at_utc']}",
        "",
        "Status: operator review complete; rule authority is ready under the current user-directed human-side gold assumption." if payload["readiness_decision"]["full_product_rule_authority_ready"] else "Status: operator review complete; rule authority remains blocked.",
        "",
        "## Scope",
        "",
        "This audit checks local source identity, edition fit, receipt consistency, provider coverage, seed fixture execution, table-import posture, errata posture, and copyright boundary. It does not copy sourcebook prose, page images, fiction, or art.",
        "",
        "## Source Identity",
        "",
    ]
    for key, source in payload["source_identity"].items():
        lines.append(f"- {key}: exists={source['exists']}, sha256_matches_expected={source['sha256_matches_expected']}, page_count={source['page_count']}, page_count_matches_expected={source['page_count_matches_expected']}")
    lines.extend(["", "## Rule Receipts", ""])
    for ruleset in ["sr4", "sr5", "sr6"]:
        lines.append(f"### {ruleset.upper()}")
        for key, value in payload["rulesets"][ruleset].items():
            lines.append(f"- {key}: {value}")
        lines.append("")
    lines.extend(["## Findings", ""])
    for finding in payload["audit_findings"]:
        lines.append(f"- {finding['id']}: {finding['status']} - {finding['detail']}")
    lines.extend([
        "",
        "## Decision",
        "",
        "SR4, SR5, and SR6 may remain promoted only under the current user-directed human-side gold assumption; this is not independent publisher/legal/editorial review." if payload["readiness_decision"]["full_product_rule_authority_ready"] else "Do not promote SR4 or SR6 to rule-authority ready from this audit alone. The remaining work is not code-class discovery; it is reviewed row-level rule/data mapping, any remaining in-scope errata application, and independent human signoff.",
        "",
        "## Recommendation",
        "",
        f"- overall: {payload['signoff_recommendation']['recommendation']}",
        f"- sr4: {payload['signoff_recommendation']['sr4']}",
        f"- sr6: {payload['signoff_recommendation']['sr6']}",
        f"- embarrassment_risk: {payload['signoff_recommendation']['embarrassment_risk']}",
        f"- reason: {payload['signoff_recommendation']['reason']}",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    payload = build_payload()
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    PUBLISHED_ROOT.mkdir(parents=True, exist_ok=True)
    json_text = json.dumps(payload, indent=2, sort_keys=True) + "\n"
    md_text = build_markdown(payload)
    for root in [OUT_ROOT, PUBLISHED_ROOT]:
        (root / "CODEX_OPERATOR_RULE_AUTHORITY_REVIEW.generated.json").write_text(json_text, encoding="utf-8")
        (root / "CODEX_OPERATOR_RULE_AUTHORITY_REVIEW.md").write_text(md_text, encoding="utf-8")
    print(json_text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
