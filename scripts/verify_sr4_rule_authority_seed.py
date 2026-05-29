#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
SEED_ROOT = REPO_ROOT / "docs" / "rulesets" / "sr4-rule-authority"
OUT_ROOT = REPO_ROOT / ".codex-studio" / "published"

REQUIRED_FILES = [
    "COPYRIGHT_SAFE_BOUNDARY.md",
    "FINAL_ACCEPTANCE_GATES.yaml",
    "SOURCE_MAP.yaml",
    "SR4_CHARACTER_CREATION_SEED.yaml",
    "SR4_COMBAT_SEED.yaml",
    "SR4_CORE_MECHANICS_SEED.yaml",
    "SR4_EXTRACTION_PIPELINE.md",
    "SR4_GEAR_DATA_PLAN.md",
    "SR4_GOLDEN_FIXTURE_PLAN.yaml",
    "SR4_IMPLEMENTATION_WORKPACKAGES.yaml",
    "SR4_MAGIC_SEED.yaml",
    "SR4_MATRIX_SEED.yaml",
    "SR4_PROVIDER_INTERFACES.md",
    "SR4_RIGGING_SEED.yaml",
    "SR4_RULEFACT_SCHEMA.yaml",
    "SR4_RULESET_PROFILE.yaml",
    "SR4_RULE_AUTHORITY_DECISION.md",
    "SR4_SKILLS_SEED.yaml",
    "VERIFICATION_MATRIX.yaml",
]

REQUIRED_PROVIDERS = [
    "Sr4DiceProvider",
    "Sr4TestProvider",
    "Sr4EdgeProvider",
    "Sr4ActionEconomyProvider",
    "Sr4CharacterCreationProvider",
    "Sr4SkillProvider",
    "Sr4DerivedStatsProvider",
    "Sr4CombatProvider",
    "Sr4DamageProvider",
    "Sr4MatrixProvider",
    "Sr4MagicProvider",
    "Sr4RiggingProvider",
    "Sr4GearProvider",
    "Sr4ExplainReceiptProvider",
]


def load_yaml(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


def walk_rulefacts(node: Any) -> list[dict[str, Any]]:
    facts: list[dict[str, Any]] = []
    if isinstance(node, dict):
        rulefacts = node.get("rulefacts")
        if isinstance(rulefacts, list):
            facts.extend(candidate for candidate in rulefacts if isinstance(candidate, dict))
        for value in node.values():
            facts.extend(walk_rulefacts(value))
    elif isinstance(node, list):
        for value in node:
            facts.extend(walk_rulefacts(value))
    return facts


def collect_source_refs(node: Any) -> set[str]:
    refs: set[str] = set()
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "source_ref" and isinstance(value, str):
                refs.add(value)
            else:
                refs.update(collect_source_refs(value))
    elif isinstance(node, list):
        for value in node:
            refs.update(collect_source_refs(value))
    return refs


def main() -> int:
    missing = [name for name in REQUIRED_FILES if not (SEED_ROOT / name).is_file()]
    if missing:
        print(f"missing SR4 authority seed files: {missing}")
        return 1

    profile = load_yaml(SEED_ROOT / "SR4_RULESET_PROFILE.yaml")
    gates = load_yaml(SEED_ROOT / "FINAL_ACCEPTANCE_GATES.yaml")
    workpackages = load_yaml(SEED_ROOT / "SR4_IMPLEMENTATION_WORKPACKAGES.yaml")

    rulefacts: list[dict[str, Any]] = []
    source_refs: set[str] = set()
    for path in sorted(SEED_ROOT.glob("SR4_*_SEED.yaml")):
        payload = load_yaml(path)
        for fact in walk_rulefacts(payload):
            normalized = dict(fact)
            normalized.setdefault("ruleset", "sr4")
            normalized.setdefault("book_profile", "sr4a_core_2009")
            normalized.setdefault("status", "seed")
            normalized["seed_file"] = path.name
            rulefacts.append(normalized)
        source_refs.update(collect_source_refs(payload))

    provider_status = profile.get("provider_status", {})
    provider_source = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in (REPO_ROOT / "Chummer.Rulesets.Sr4").glob("*.cs")
    )
    implemented_providers = [
        provider for provider in REQUIRED_PROVIDERS
        if re.search(rf"\b(class|record)\s+{re.escape(provider)}\b", provider_source)
    ]
    missing_profile_status = [provider for provider in REQUIRED_PROVIDERS if provider not in provider_status]
    missing_implemented_providers = [provider for provider in REQUIRED_PROVIDERS if provider not in implemented_providers]
    production_claim_allowed = bool(profile.get("claim_allowed", {}).get("production_grade"))
    errata_ready = profile.get("errata_profile", {}).get("status") == "applied"
    readiness_token_allowed = (
        not missing_implemented_providers
        and production_claim_allowed
        and errata_ready
        and gates.get("final_verdict_required") == "SR4_RULE_AUTHORITY_READY"
    )
    final_verdict = "SR4_RULE_AUTHORITY_READY" if readiness_token_allowed else "NOT_READY"

    schema_text = (SEED_ROOT / "SR4_RULEFACT_SCHEMA.yaml").read_text(encoding="utf-8")
    all_text = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in sorted(SEED_ROOT.glob("*"))
        if path.is_file()
    )
    copyright_safe = (
        "implementation_facts_only_no_prose" in all_text
        and "quote_allowed: false" in schema_text
        and final_verdict == "NOT_READY"
    )

    registry = {
        "schema": "sr4-rulefact-registry-v1",
        "ruleset": "sr4",
        "book_profile": "sr4a_core_2009",
        "source_package": "sr4_rule_authority_extraction_package_20260529.zip",
        "final_verdict": final_verdict,
        "rulefact_count": len(rulefacts),
        "source_ref_count": len(source_refs),
        "required_providers": REQUIRED_PROVIDERS,
        "implemented_providers": implemented_providers,
        "missing_profile_status": missing_profile_status,
        "missing_implemented_providers": missing_implemented_providers,
        "rulefacts": rulefacts,
    }
    digest_input = json.dumps(registry, sort_keys=True, separators=(",", ":")).encode("utf-8")
    registry["registry_sha256"] = hashlib.sha256(digest_input).hexdigest()

    report = {
        "status": "pass" if copyright_safe and rulefacts else "fail",
        "ruleset": "sr4",
        "seed_root": str(SEED_ROOT),
        "required_file_count": len(REQUIRED_FILES),
        "rulefact_count": len(rulefacts),
        "source_ref_count": len(source_refs),
        "provider_status_count": len(provider_status),
        "implemented_provider_count": len(implemented_providers),
        "implemented_providers": implemented_providers,
        "missing_profile_status": missing_profile_status,
        "missing_implemented_providers": missing_implemented_providers,
        "copyright_boundary": {
            "implementation_facts_only": "implementation_facts_only_no_prose" in all_text,
            "quote_allowed_false": "quote_allowed: false" in schema_text,
        },
        "claim_allowed": profile.get("claim_allowed", {}),
        "errata_profile": profile.get("errata_profile", {}),
        "final_verdict": final_verdict,
        "readiness_token_allowed": readiness_token_allowed,
        "workpackage_count": len(workpackages.get("workpackages", [])),
    }

    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    (OUT_ROOT / "SR4_RULEFACT_REGISTRY.generated.json").write_text(
        json.dumps(registry, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    (OUT_ROOT / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json").write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
