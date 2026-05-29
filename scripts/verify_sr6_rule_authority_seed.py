#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
SEED_ROOT = REPO_ROOT / "docs" / "rulesets" / "sr6-rule-authority"
OUT_ROOT = REPO_ROOT / ".codex-studio" / "published"

REQUIRED_FILES = [
    "COPYRIGHT_SAFE_BOUNDARY.md",
    "FINAL_ACCEPTANCE_GATES.yaml",
    "SOURCE_MAP.yaml",
    "SR6_CHARACTER_CREATION_SEED.yaml",
    "SR6_COMBAT_SEED.yaml",
    "SR6_CORE_MECHANICS_SEED.yaml",
    "SR6_EXTRACTION_PIPELINE.md",
    "SR6_GEAR_DATA_PLAN.md",
    "SR6_GOLDEN_FIXTURE_PLAN.yaml",
    "SR6_IMPLEMENTATION_WORKPACKAGES.yaml",
    "SR6_MAGIC_SEED.yaml",
    "SR6_MATRIX_SEED.yaml",
    "SR6_PROVIDER_INTERFACES.md",
    "SR6_RIGGING_SEED.yaml",
    "SR6_RULEFACT_SCHEMA.yaml",
    "SR6_RULESET_PROFILE.yaml",
    "SR6_RULE_AUTHORITY_DECISION.md",
    "SR6_STATUS_EFFECTS_SEED.yaml",
    "VERIFICATION_MATRIX.yaml",
]

REQUIRED_PROVIDERS = [
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
    "Sr6GearProvider",
    "Sr6TableImportProvider",
    "Sr6AdvancementProvider",
    "Sr6ExplainReceiptProvider",
]

COPYRIGHT_RISK_PATTERNS = [
    re.compile(r"sourcebook prose", re.IGNORECASE),
    re.compile(r"page image", re.IGNORECASE),
    re.compile(r"copy(?:ing|ied)? rulebook prose", re.IGNORECASE),
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
        print(f"missing SR6 authority seed files: {missing}")
        return 1

    profile = load_yaml(SEED_ROOT / "SR6_RULESET_PROFILE.yaml")
    workpackages = load_yaml(SEED_ROOT / "SR6_IMPLEMENTATION_WORKPACKAGES.yaml")
    gates = load_yaml(SEED_ROOT / "FINAL_ACCEPTANCE_GATES.yaml")

    seed_files = sorted(SEED_ROOT.glob("SR6_*_SEED.yaml"))
    seed_payloads = {path.name: load_yaml(path) for path in seed_files}
    rulefacts: list[dict[str, Any]] = []
    source_refs: set[str] = set()
    for file_name, payload in seed_payloads.items():
        for fact in walk_rulefacts(payload):
            normalized = dict(fact)
            normalized.setdefault("ruleset", "sr6")
            normalized.setdefault("book_profile", "sr6_core_2019")
            normalized.setdefault("status", "seed")
            normalized["seed_file"] = file_name
            rulefacts.append(normalized)
        source_refs.update(collect_source_refs(payload))

    provider_status = profile.get("provider_status", {})
    provider_source = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in (REPO_ROOT / "Chummer.Rulesets.Sr6").glob("*.cs")
    )
    implemented_providers = [
        provider for provider in REQUIRED_PROVIDERS
        if re.search(rf"\b(class|record)\s+{re.escape(provider)}\b", provider_source)
    ]
    missing_profile_status = [provider for provider in REQUIRED_PROVIDERS if provider not in provider_status]
    missing_implemented_providers = [provider for provider in REQUIRED_PROVIDERS if provider not in implemented_providers]
    production_claim_allowed = bool(profile.get("claim_allowed", {}).get("production_grade"))
    readiness_token_allowed = (
        not missing_implemented_providers
        and production_claim_allowed
        and gates.get("final_verdict_required") == "SR6_RULE_AUTHORITY_READY"
    )
    final_verdict = "SR6_RULE_AUTHORITY_READY" if readiness_token_allowed else "NOT_READY"

    all_text = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in sorted(SEED_ROOT.glob("*"))
        if path.is_file()
    )
    boundary_mentions = {
        pattern.pattern: bool(pattern.search(all_text))
        for pattern in COPYRIGHT_RISK_PATTERNS
    }
    copyright_safe = (
        "implementation_facts_only_no_prose" in all_text
        and "quote_allowed: false" in (SEED_ROOT / "SR6_RULEFACT_SCHEMA.yaml").read_text(encoding="utf-8")
        and not readiness_token_allowed
    )

    registry = {
        "schema": "sr6-rulefact-registry-v1",
        "ruleset": "sr6",
        "book_profile": "sr6_core_2019",
        "source_package": "sr6_rule_authority_extraction_package_20260529.zip",
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
        "status": "pass" if copyright_safe and rulefacts and final_verdict == "NOT_READY" else "fail",
        "ruleset": "sr6",
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
            "quote_allowed_false": "quote_allowed: false" in (SEED_ROOT / "SR6_RULEFACT_SCHEMA.yaml").read_text(encoding="utf-8"),
            "risk_pattern_mentions_are_boundary_context": boundary_mentions,
        },
        "claim_allowed": profile.get("claim_allowed", {}),
        "final_verdict": final_verdict,
        "readiness_token_allowed": readiness_token_allowed,
        "workpackage_count": len(workpackages.get("workpackages", [])),
    }

    provider_coverage = {
        "status": "provider_classes_covered_not_authority_ready"
        if not missing_implemented_providers and not missing_profile_status
        else "incomplete",
        "ruleset": "sr6",
        "implemented_provider_count": len(implemented_providers),
        "implemented_providers": implemented_providers,
        "missing_implemented_providers": missing_implemented_providers,
        "missing_profile_status": missing_profile_status,
        "provider_status_count": len(provider_status),
        "final_verdict": final_verdict,
        "readiness_token_allowed": readiness_token_allowed,
    }

    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    (OUT_ROOT / "SR6_RULEFACT_REGISTRY.generated.json").write_text(
        json.dumps(registry, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    (OUT_ROOT / "SR6_RULE_AUTHORITY_INTEGRATION.generated.json").write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    (OUT_ROOT / "SR6_PROVIDER_COVERAGE.generated.json").write_text(
        json.dumps(provider_coverage, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
