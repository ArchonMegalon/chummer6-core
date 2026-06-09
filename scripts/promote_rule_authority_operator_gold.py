#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED = REPO_ROOT / ".codex-studio" / "published"
AUTHORITY_ROOT = PUBLISHED / "rule-authority"
COMPLETION = Path("/docker/chummercomplete/_completion")
V14 = COMPLETION / "full_product_reaudit_v14"
OUT = COMPLETION / "full_product_rule_authority"

PDFS = {
    "sr4": Path("/mnt/pcloud/personal/Roleplay/sr/(SR4) Shadowrun 4e Core Rules.pdf"),
    "sr5": Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun Fifth Edition Core Rulebook.pdf"),
    "sr6": Path("/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf"),
}

EXPECTED_SHA256 = {
    "sr4": "28da9d6dfd8eba79a2ae46dc41e2ec825d16067d288e6f20e23c65767616d41d",
    "sr5": "b6769553a7348286e6396b49e364960c71bba5436412b88c5672e4f522ad52d5",
    "sr6": "104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023",
}
MIN_RULEFACT_COUNT = 100


def now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def normalize_public_rulefact_registry(
    ruleset: str,
    authority_registry: dict[str, Any],
    final_verdict: str,
    status: str,
) -> dict[str, Any]:
    entries = list(authority_registry.get("rulefact_entries") or [])
    return {
        "schema": f"{ruleset}-rule-authority-public-registry-v2",
        "ruleset": ruleset,
        "edition": authority_registry.get("edition", ruleset.upper()),
        "status": status,
        "final_verdict": final_verdict,
        "generated_at_utc": GENERATED_AT,
        "runtime_receipt_path": f".codex-studio/published/{ruleset.upper()}_RULEFACT_REGISTRY.generated.json",
        "copyright_boundary": authority_registry.get("copyright_boundary", {}),
        "rulefact_families": authority_registry.get("rulefact_families", []),
        "rulefact_count": len(entries),
        "rulefacts": [
            {
                "id": entry.get("fact_id"),
                "family": entry.get("family"),
                "provider": entry.get("provider"),
                "fixture_ids": entry.get("fixture_ids", []),
                "copyright_safe": entry.get("copyright_safe", False),
            }
            for entry in entries
        ],
        "human_review": authority_registry.get("human_review", {}),
    }


def pdf_gate() -> dict[str, Any]:
    sources: dict[str, Any] = {}
    failures: list[str] = []
    for ruleset, path in PDFS.items():
        exists = path.is_file()
        digest = sha256(path) if exists else None
        ok = exists and digest == EXPECTED_SHA256[ruleset]
        sources[ruleset] = {
            "path": str(path),
            "exists": exists,
            "sha256": digest,
            "sha256_matches_expected": ok,
        }
        if not ok:
            failures.append(f"{ruleset} source PDF identity failed")
    return {"status": "pass" if not failures else "fail", "sources": sources, "failures": failures}


def sr4_or_sr6_gate(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    root = COMPLETION / f"{ruleset}_rule_authority"
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    authority_registry = load_json(AUTHORITY_ROOT / f"{upper}_RULEFACT_REGISTRY.generated.json")
    provider = load_json(root / f"{upper}_PROVIDER_COVERAGE.generated.json")
    tables = load_json(root / f"{upper}_TABLE_IMPORTS.generated.json")
    fixtures = load_json(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    explain = load_json(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    copyright_safety = load_json(root / f"{upper}_COPYRIGHT_SAFETY.generated.json")
    failures: list[str] = []

    if provider.get("missing_implemented_providers"):
        failures.append("provider classes missing")
    if provider.get("missing_profile_status"):
        failures.append("provider profile statuses missing")
    if fixtures.get("failed", 0) != 0 or fixtures.get("passed", 0) <= 0:
        failures.append("golden fixtures are not clean")
    if copyright_safety.get("status") != "pass":
        failures.append("copyright safety receipt failed")
    if not explain.get("public_safe", False):
        failures.append("explain receipts are not public-safe")
    table_status = str(tables.get("status", ""))
    table_indexed = "indexed" in table_status or int(tables.get("row_count") or 0) > 0
    if not table_indexed:
        failures.append("table evidence is not indexed")
    authority_source = authority_registry if authority_registry else registry
    authority_rulefact_count = len(list(authority_source.get("rulefact_entries") or authority_source.get("rulefacts") or []))
    if authority_rulefact_count < MIN_RULEFACT_COUNT:
        failures.append(f"rulefact registry has {authority_rulefact_count} facts; minimum is {MIN_RULEFACT_COUNT}")

    ready = not failures
    token = f"{upper}_RULE_AUTHORITY_READY"
    normalized_registry = normalize_public_rulefact_registry(
        ruleset,
        authority_source,
        token if ready else "NOT_READY",
        "pass" if ready else "fail",
    )
    registry["final_verdict"] = token if ready else "NOT_READY"
    registry["operator_review_promoted_at_utc"] = GENERATED_AT
    registry["operator_review_scope"] = "implementation facts, provider behavior, table indexes, fixtures, explain receipts, and public-safe copyright receipts"
    provider["final_verdict"] = token if ready else "NOT_READY"
    provider["readiness_token_allowed"] = ready
    provider["status"] = "pass" if ready else provider.get("status")
    integration = load_json(root / f"{upper}_RULE_AUTHORITY_INTEGRATION.generated.json")
    integration["final_verdict"] = token if ready else "NOT_READY"
    integration["readiness_token_allowed"] = ready
    integration["operator_review_promoted_at_utc"] = GENERATED_AT

    for directory in (root, PUBLISHED):
        write_json(directory / f"{upper}_RULEFACT_REGISTRY.generated.json", normalized_registry)
        write_json(directory / f"{upper}_PROVIDER_COVERAGE.generated.json", provider)
        write_json(directory / f"{upper}_RULE_AUTHORITY_INTEGRATION.generated.json", integration)

    verdict = token if ready else "NOT_READY"
    verdict_md = (
        f"{verdict}\n\n"
        f"Operator promotion: {GENERATED_AT}\n\n"
        "Basis:\n"
        f"- provider coverage: {provider.get('status')}\n"
        f"- rulefacts: {normalized_registry.get('rulefact_count')}\n"
        f"- fixtures passed/failed: {fixtures.get('passed')}/{fixtures.get('failed')}\n"
        f"- table evidence status: {tables.get('status')}\n"
        f"- explain public-safe: {explain.get('public_safe')}\n"
        f"- copyright safety: {copyright_safety.get('status')}\n\n"
        "Copyright boundary: implementation facts and receipts only; no sourcebook prose, art, tables, or page images are reproduced.\n"
    )
    for directory in (root, V14):
        write_text(directory / f"FINAL_{upper}_RULE_AUTHORITY_VERDICT.md", verdict_md)

    return {
        "ruleset": ruleset,
        "status": "pass" if ready else "fail",
        "verdict": verdict,
        "failures": failures,
        "rulefact_count": normalized_registry.get("rulefact_count"),
        "fixture_passed": fixtures.get("passed"),
        "fixture_failed": fixtures.get("failed"),
        "table_status": tables.get("status"),
    }


def sr5_gate() -> dict[str, Any]:
    acceptance = load_json(PUBLISHED / "SR5_ACCEPTANCE_PROOF.generated.json")
    depth = load_json(PUBLISHED / "SR5_RULESET_DEPTH.generated.json")
    tables = load_json(PUBLISHED / "SR5_TABLE_IMPORTS.generated.json")
    authority_registry = load_json(AUTHORITY_ROOT / "SR5_RULEFACT_REGISTRY.generated.json")
    failures: list[str] = []
    authority_rulefact_count = len(list(authority_registry.get("rulefact_entries") or authority_registry.get("rulefacts") or []))
    if acceptance.get("status") != "pass":
        failures.append("SR5 acceptance proof failed")
    if depth.get("status") != "pass":
        failures.append("SR5 depth proof failed")
    if "indexed" not in str(tables.get("status", "")):
        failures.append("SR5 table imports are not indexed")
    if authority_rulefact_count < MIN_RULEFACT_COUNT:
        failures.append(f"SR5 rulefact registry has {authority_rulefact_count} facts; minimum is {MIN_RULEFACT_COUNT}")

    ready = not failures
    registry = normalize_public_rulefact_registry(
        "sr5",
        authority_registry,
        "SR5_RULE_AUTHORITY_READY" if ready else "NOT_READY",
        "pass" if ready else "fail",
    )
    registry["acceptance_proof_status"] = acceptance.get("status")
    registry["depth_status"] = depth.get("status")
    registry["table_import_status"] = tables.get("status")
    registry["operator_review_promoted_at_utc"] = GENERATED_AT
    write_json(PUBLISHED / "SR5_RULE_AUTHORITY_REGISTRY.generated.json", registry)
    write_json(COMPLETION / "sr5_rule_authority" / "SR5_RULE_AUTHORITY_REGISTRY.generated.json", registry)
    verdict_md = (
        f"{registry['final_verdict']}\n\n"
        f"Operator promotion: {GENERATED_AT}\n\n"
        "Basis:\n"
        f"- acceptance proof: {acceptance.get('status')}\n"
        f"- depth proof: {depth.get('status')}\n"
        f"- table imports: {tables.get('status')}\n\n"
        f"- rulefacts: {registry.get('rulefact_count')}\n\n"
        "Copyright boundary: implementation facts and structured Chummer data only; no sourcebook prose, art, tables, or page images are reproduced.\n"
    )
    write_text(V14 / "FINAL_SR5_RULE_AUTHORITY_VERDICT.md", verdict_md)
    write_text(COMPLETION / "sr5_rule_authority" / "FINAL_SR5_RULE_AUTHORITY_VERDICT.md", verdict_md)
    return {"ruleset": "sr5", "status": "pass" if ready else "fail", "verdict": registry["final_verdict"], "failures": failures}


GENERATED_AT = now()


def main() -> int:
    source_gate = pdf_gate()
    ruleset_gates = [sr4_or_sr6_gate("sr4"), sr5_gate(), sr4_or_sr6_gate("sr6")]
    failures = source_gate["failures"][:]
    for gate in ruleset_gates:
        failures.extend(f"{gate['ruleset']}: {failure}" for failure in gate["failures"])

    payload = {
        "contract_name": "chummer.operator_promoted_rule_authority_gold",
        "generated_at_utc": GENERATED_AT,
        "status": "pass" if not failures else "fail",
        "final_verdict": "FULL_RULE_AUTHORITY_READY" if not failures else "NOT_READY",
        "source_identity": source_gate,
        "rulesets": ruleset_gates,
        "copyright_boundary": {
            "sourcebook_text_committed": False,
            "sourcebook_art_committed": False,
            "verdict_uses_public_safe_facts_and_receipts": True,
        },
        "failures": failures,
    }
    write_json(OUT / "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json", payload)
    write_json(PUBLISHED / "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json", payload)
    write_json(PUBLISHED / "FULL_PRODUCT_RULE_AUTHORITY_COMPLETION.generated.json", {
        "contract_name": "chummer.full_product_rule_authority_completion",
        "status": payload["status"],
        "final_verdict": payload["final_verdict"],
        "readiness_token_allowed": not failures,
        "rulesets": {gate["ruleset"]: gate for gate in ruleset_gates},
        "copyright_boundary": payload["copyright_boundary"],
        "source_identity_status": source_gate["status"],
    })
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
