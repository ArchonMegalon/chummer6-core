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
AUTHORITY_OUT_ROOT = OUT_ROOT / "rule-authority"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr4_rule_authority")

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
    "Sr4MetatypeProvider",
    "Sr4AttributeProvider",
    "Sr4SkillProvider",
    "Sr4QualityProvider",
    "Sr4DerivedStatsProvider",
    "Sr4CombatProvider",
    "Sr4DamageProvider",
    "Sr4VehicleProvider",
    "Sr4MatrixProvider",
    "Sr4MagicProvider",
    "Sr4RiggingProvider",
    "Sr4GearProvider",
    "Sr4AdvancementProvider",
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


def slug(value: Any) -> str:
    text = str(value).strip().lower()
    text = re.sub(r"[^a-z0-9]+", "_", text)
    return text.strip("_")


def append_fact(
    facts: list[dict[str, Any]],
    seen_ids: set[str],
    *,
    fact_id: str,
    provider: str,
    source_ref: str,
    fact: dict[str, Any],
    seed_file: str,
    ruleset: str = "sr4",
    book_profile: str = "sr4a_core_2009",
) -> None:
    if fact_id in seen_ids:
        return
    seen_ids.add(fact_id)
    facts.append(
        {
            "id": fact_id,
            "source_ref": source_ref,
            "provider": provider,
            "fact": fact,
            "seed_file": seed_file,
            "ruleset": ruleset,
            "book_profile": book_profile,
            "status": "seed",
        }
    )


def add_structured_seed_facts(rulefacts: list[dict[str, Any]], seen_ids: set[str]) -> None:
    def add_core_mechanics(path: Path) -> None:
        payload = load_yaml(path)
        for section_name, provider in (
            ("tests", "Sr4TestProvider"),
            ("attributes", "Sr4AttributeProvider"),
            ("derived_stats", "Sr4DerivedStatsProvider"),
            ("edge", "Sr4EdgeProvider"),
        ):
            section = payload.get(section_name) or {}
            source_ref = section.get("source_ref", payload.get("source_ref", "sr4a_core_2009:p60-75")) if isinstance(section, dict) else payload.get("source_ref", "sr4a_core_2009:p60-75")
            if isinstance(section, dict):
                for key, value in section.items():
                    if key == "source_ref":
                        continue
                    if isinstance(value, dict):
                        for nested_key, nested_value in value.items():
                            append_fact(
                                rulefacts,
                                seen_ids,
                                fact_id=f"sr4.{slug(section_name)}.{slug(key)}.{slug(nested_key)}",
                                provider=provider,
                                source_ref=source_ref,
                                fact={nested_key: nested_value},
                                seed_file=path.name,
                            )
                    elif isinstance(value, list):
                        for entry in value:
                            append_fact(
                                rulefacts,
                                seen_ids,
                                fact_id=f"sr4.{slug(section_name)}.{slug(key)}.{slug(entry)}",
                                provider=provider,
                                source_ref=source_ref,
                                fact={key.rstrip('s'): entry},
                                seed_file=path.name,
                            )
                    else:
                        append_fact(
                            rulefacts,
                            seen_ids,
                            fact_id=f"sr4.{slug(section_name)}.{slug(key)}",
                            provider=provider,
                            source_ref=source_ref,
                            fact={key: value},
                            seed_file=path.name,
                        )

    def add_character_creation(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p80-97")
        model = payload.get("creation_model", {})
        for key, value in model.items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.character_creation.model.{slug(key)}", provider="Sr4CharacterCreationProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        metatypes = payload.get("metatype_attribute_table", {})
        for metatype, data in metatypes.items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.character_creation.metatype.{slug(metatype)}.bp_cost", provider="Sr4CharacterCreationProvider", source_ref=source_ref, fact={"metatype": metatype, "bp_cost": data.get("bp_cost")}, seed_file=path.name)
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.metatype.{slug(metatype)}.bp_cost", provider="Sr4MetatypeProvider", source_ref=source_ref, fact={"metatype": metatype, "bp_cost": data.get("bp_cost")}, seed_file=path.name)
            for attribute, values in (data.get("attributes") or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.character_creation.metatype.{slug(metatype)}.attribute.{slug(attribute)}", provider="Sr4CharacterCreationProvider", source_ref=source_ref, fact={"metatype": metatype, "attribute": attribute, "minimum": values[0], "natural_max": values[1], "augmented_cap": values[2]}, seed_file=path.name)
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.metatype.{slug(metatype)}.attribute.{slug(attribute)}", provider="Sr4MetatypeProvider", source_ref=source_ref, fact={"metatype": metatype, "attribute": attribute, "minimum": values[0], "natural_max": values[1], "augmented_cap": values[2]}, seed_file=path.name)
            for ability in data.get("abilities") or []:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.character_creation.metatype.{slug(metatype)}.ability.{slug(ability)}", provider="Sr4CharacterCreationProvider", source_ref=source_ref, fact={"metatype": metatype, "ability": ability}, seed_file=path.name)
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.metatype.{slug(metatype)}.ability.{slug(ability)}", provider="Sr4MetatypeProvider", source_ref=source_ref, fact={"metatype": metatype, "ability": ability}, seed_file=path.name)
        for key, value in (payload.get("attribute_costs") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.character_creation.attribute_cost.{slug(key)}", provider="Sr4CharacterCreationProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.attributes.cost.{slug(key)}", provider="Sr4AttributeProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for key, value in (payload.get("quality_constraints") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.quality.constraint.{slug(key)}", provider="Sr4QualityProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for key, value in (payload.get("skill_costs") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.advancement.skill_cost.{slug(key)}", provider="Sr4AdvancementProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        resources = payload.get("resources") or {}
        for key, value in resources.items():
            target_provider = "Sr4GearProvider"
            if key in {"magical_resources"}:
                target_provider = "Sr4MagicProvider"
            elif key in {"technomancer_resources"}:
                target_provider = "Sr4MatrixProvider"
            if isinstance(value, dict):
                for nested_key, nested_value in value.items():
                    if isinstance(nested_value, dict):
                        for leaf_key, leaf_value in nested_value.items():
                            append_fact(rulefacts, seen_ids, fact_id=f"sr4.resources.{slug(key)}.{slug(nested_key)}.{slug(leaf_key)}", provider=target_provider, source_ref=source_ref, fact={leaf_key: leaf_value}, seed_file=path.name)
                    else:
                        append_fact(rulefacts, seen_ids, fact_id=f"sr4.resources.{slug(key)}.{slug(nested_key)}", provider=target_provider, source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.resources.{slug(key)}", provider=target_provider, source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for key, value in (payload.get("condition_monitors") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.derived_stats.creation.{slug(key)}", provider="Sr4DerivedStatsProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)

    def add_combat(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p144-171")
        for key, value in (payload.get("turn_structure") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.combat.turn_structure.{slug(key)}", provider="Sr4CombatProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        action_economy = payload.get("action_economy") or {}
        phase = action_economy.get("action_phase") or {}
        for key, value in phase.items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.combat.action_phase.{slug(key)}", provider="Sr4ActionEconomyProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for bucket in ("free_actions_examples", "simple_actions_examples", "complex_actions_examples", "interrupt_actions"):
            for entry in action_economy.get(bucket) or []:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.combat.{slug(bucket)}.{slug(entry)}", provider="Sr4ActionEconomyProvider", source_ref=source_ref, fact={"bucket": bucket, "entry": entry}, seed_file=path.name)
        for step in payload.get("combat_sequence") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.combat.sequence.{slug(step.get('id'))}", provider="Sr4CombatProvider", source_ref=source_ref, fact=step, seed_file=path.name)
        for section_name, provider in (("damage", "Sr4DamageProvider"), ("armor", "Sr4CombatProvider"), ("vehicles", "Sr4RiggingProvider")):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.combat.{slug(section_name)}.{slug(key)}", provider=provider, source_ref=source_ref, fact={key: value}, seed_file=path.name)

    def add_magic(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p176-211")
        for section_name in ("core", "drain", "traditions", "sorcery", "adepts"):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.magic.{slug(section_name)}.{slug(key)}", provider="Sr4MagicProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for conjuring_mode, mode_payload in (payload.get("conjuring") or {}).items():
            if isinstance(mode_payload, dict):
                for key, value in mode_payload.items():
                    append_fact(rulefacts, seen_ids, fact_id=f"sr4.magic.conjuring.{slug(conjuring_mode)}.{slug(key)}", provider="Sr4MagicProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.magic.conjuring.{slug(conjuring_mode)}", provider="Sr4MagicProvider", source_ref=source_ref, fact={conjuring_mode: mode_payload}, seed_file=path.name)
        for entry in payload.get("table_imports_required") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.magic.table_import.{slug(entry)}", provider="Sr4MagicProvider", source_ref=source_ref, fact={"table_import": entry}, seed_file=path.name)

    def add_matrix(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p216-247")
        core = payload.get("core") or {}
        for key, value in core.items():
            if isinstance(value, list):
                for entry in value:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr4.matrix.core.{slug(key)}.{slug(entry)}", provider="Sr4MatrixProvider", source_ref=source_ref, fact={key: entry}, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.matrix.core.{slug(key)}", provider="Sr4MatrixProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for section_name in ("initiative", "program_model", "hacking", "technomancy"):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.matrix.{slug(section_name)}.{slug(key)}", provider="Sr4MatrixProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        actions = payload.get("actions") or {}
        for action in actions.get("required_imports") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.matrix.action_import.{slug(action)}", provider="Sr4MatrixProvider", source_ref=source_ref, fact={"action": action}, seed_file=path.name)
        for key, value in actions.items():
            if key == "required_imports":
                continue
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.matrix.actions.{slug(key)}", provider="Sr4MatrixProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)

    def add_rigging(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p244-247,p167-171,p348")
        for key, value in (payload.get("core") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.rigging.core.{slug(key)}", provider="Sr4RiggingProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for mode, details in (payload.get("control_modes") or {}).items():
            for key, value in (details or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.rigging.control_mode.{slug(mode)}.{slug(key)}", provider="Sr4RiggingProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for section_name in ("electronic_warfare", "vehicle_rules"):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.rigging.{slug(section_name)}.{slug(key)}", provider="Sr4RiggingProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for entry in payload.get("required_imports") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.rigging.import.{slug(entry)}", provider="Sr4RiggingProvider", source_ref=source_ref, fact={"import": entry}, seed_file=path.name)

    def add_skills(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr4a_core_2009:p118-138")
        for key, value in (payload.get("skill_model") or {}).items():
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.skills.model.{slug(key)}", provider="Sr4SkillProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for entry in payload.get("skill_families") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.skills.family.{slug(entry)}", provider="Sr4SkillProvider", source_ref=source_ref, fact={"family": entry}, seed_file=path.name)
        for entry in payload.get("required_imports") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr4.skills.import.{slug(entry)}", provider="Sr4SkillProvider", source_ref=source_ref, fact={"import": entry}, seed_file=path.name)
        provider_rules = payload.get("provider_rules") or {}
        for section_name, value in provider_rules.items():
            if isinstance(value, dict):
                for nested_key, nested_value in value.items():
                    append_fact(rulefacts, seen_ids, fact_id=f"sr4.skills.provider_rule.{slug(section_name)}.{slug(nested_key)}", provider="Sr4SkillProvider", source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr4.skills.provider_rule.{slug(section_name)}", provider="Sr4SkillProvider", source_ref=source_ref, fact={section_name: value}, seed_file=path.name)

    add_core_mechanics(SEED_ROOT / "SR4_CORE_MECHANICS_SEED.yaml")
    add_character_creation(SEED_ROOT / "SR4_CHARACTER_CREATION_SEED.yaml")
    add_combat(SEED_ROOT / "SR4_COMBAT_SEED.yaml")
    add_magic(SEED_ROOT / "SR4_MAGIC_SEED.yaml")
    add_matrix(SEED_ROOT / "SR4_MATRIX_SEED.yaml")
    add_rigging(SEED_ROOT / "SR4_RIGGING_SEED.yaml")
    add_skills(SEED_ROOT / "SR4_SKILLS_SEED.yaml")


def main() -> int:
    missing = [name for name in REQUIRED_FILES if not (SEED_ROOT / name).is_file()]
    if missing:
        print(f"missing SR4 authority seed files: {missing}")
        return 1

    profile = load_yaml(SEED_ROOT / "SR4_RULESET_PROFILE.yaml")
    gates = load_yaml(SEED_ROOT / "FINAL_ACCEPTANCE_GATES.yaml")
    workpackages = load_yaml(SEED_ROOT / "SR4_IMPLEMENTATION_WORKPACKAGES.yaml")

    rulefacts: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    source_refs: set[str] = set()
    for path in sorted(SEED_ROOT.glob("SR4_*_SEED.yaml")):
        payload = load_yaml(path)
        for fact in walk_rulefacts(payload):
            normalized = dict(fact)
            normalized.setdefault("ruleset", "sr4")
            normalized.setdefault("book_profile", "sr4a_core_2009")
            normalized.setdefault("status", "seed")
            normalized["seed_file"] = path.name
            fact_id = str(normalized.get("id") or normalized.get("fact_id") or "")
            if fact_id and fact_id not in seen_ids:
                seen_ids.add(fact_id)
                rulefacts.append(normalized)
        source_refs.update(collect_source_refs(payload))
    add_structured_seed_facts(rulefacts, seen_ids)

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
        "schema": "sr4-rule-authority-public-registry-v2",
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

    provider_coverage = {
        "status": "pass" if not missing_implemented_providers and not missing_profile_status else "incomplete",
        "ruleset": "sr4",
        "implemented_provider_count": len(implemented_providers),
        "implemented_providers": implemented_providers,
        "missing_implemented_providers": missing_implemented_providers,
        "missing_profile_status": missing_profile_status,
        "provider_status_count": len(provider_status),
        "final_verdict": final_verdict,
        "readiness_token_allowed": readiness_token_allowed,
    }

    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    AUTHORITY_OUT_ROOT.mkdir(parents=True, exist_ok=True)
    COMPLETION_ROOT.mkdir(parents=True, exist_ok=True)
    (AUTHORITY_OUT_ROOT / "SR4_RULEFACT_REGISTRY.generated.json").write_text(
        json.dumps(registry, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    for directory in (OUT_ROOT, COMPLETION_ROOT):
        (directory / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json").write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        (directory / "SR4_PROVIDER_COVERAGE.generated.json").write_text(
            json.dumps(provider_coverage, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        (directory / "SR4_RULEFACT_REGISTRY.generated.json").write_text(
            json.dumps(registry, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )

    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
