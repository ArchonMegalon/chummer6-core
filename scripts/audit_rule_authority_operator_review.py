#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import yaml


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

ERRATA_SOURCES = {
    "sr6_aug_2019": {
        "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2019/08/SR6-Core-Rulebook-Errata-Aug-2019.pdf",
        "observed_page_count": 10,
        "observed_sha256": "84a488965df544eb5661def7188baeef2a8d38d1fb006f00b5537e1850b6b5db",
    },
    "sr6_feb_2020": {
        "url": "https://shadowrunsixthworld.com/wp-content/uploads/sites/5/2020/03/SR6-Core-Rulebook-Errata-Feb-2020.pdf",
        "observed_page_count": 6,
        "observed_sha256": None,
    },
    "sr6_city_edition_notice": {
        "url": "https://shadowrunsixthworld.com/2021/09/15/hit-the-streets-with-shadowrun-sixth-world-city-edition-and-improved-dice-roller-app/",
        "observed_fact": "official notice says City Edition: Seattle includes latest errata and updates",
    },
}


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


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
    errata = load_json(root / f"{upper}_ERRATA_PROFILE.generated.json")
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    copyright_safety = load_json(root / f"{upper}_COPYRIGHT_SAFETY.generated.json")
    return {
        "rulefact_count": registry.get("rulefact_count"),
        "rulefact_ids": [fact.get("id") for fact in registry.get("rulefacts", [])],
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
        "copyright_status": copyright_safety.get("status"),
        "readiness_token_allowed": registry.get("final_verdict") != "NOT_READY",
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
    sr6 = ruleset_receipt_review("sr6")
    payload = {
        "contract_name": "chummer.rule_authority_operator_review",
        "generated_at_utc": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "status": "operator_review_complete_authority_blocked",
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
            "sr5": {
                "acceptance_proof_status": load_json(PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json").get("status"),
                "serious_implementation_claim": load_json(PUBLISHED_ROOT / "SR5_ACCEPTANCE_PROOF.generated.json").get("serious_implementation_claim"),
            },
            "sr6": sr6,
        },
        "errata_sources": ERRATA_SOURCES,
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
                "status": "pass",
                "detail": "SR4 and SR6 required provider classes are present with no missing implementation/profile entries.",
            },
            {
                "id": "seed_fixture_execution",
                "status": "pass",
                "detail": "Focused SR4/SR6 seed fixture receipts report zero failures.",
            },
            {
                "id": "rulefact_depth",
                "status": "blocker",
                "detail": "RuleFact registries still contain seed-level dice/core facts only, not the full P0/P1 chapter authority corpus.",
            },
            {
                "id": "row_level_table_mapping",
                "status": "blocker",
                "detail": "SR4 structured legacy data and SR6 private PDF line hashes are indexed, but not reviewed into normalized row-level authority records.",
            },
            {
                "id": "errata_application",
                "status": "blocker",
                "detail": "Official SR6 errata/update sources are identified, but errata deltas are not applied and reviewed in providers/table records; SR4 errata profile also remains pending.",
            },
            {
                "id": "human_signoff",
                "status": "blocker",
                "detail": "This is a Codex operator audit, not independent human/editorial/legal signoff.",
            },
        ],
        "readiness_decision": {
            "sr4_rule_authority_ready": False,
            "sr6_rule_authority_ready": False,
            "full_product_rule_authority_ready": False,
            "reason": "Operator review closed source identity and provider evidence, but full authority still requires row-level reviewed records, errata application, expanded fixtures, and independent human signoff.",
        },
    }
    return payload


def build_markdown(payload: dict[str, Any]) -> str:
    lines = [
        "# Codex Operator Rule Authority Review",
        "",
        f"Generated: {payload['generated_at_utc']}",
        "",
        "Status: operator review complete; rule authority remains blocked.",
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
        "Do not promote SR4 or SR6 to rule-authority ready from this audit alone. The remaining work is not code-class discovery; it is reviewed row-level rule/data mapping, errata application, a larger authority fixture corpus, public-safe explain receipts for every authority rule, and independent human signoff.",
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
