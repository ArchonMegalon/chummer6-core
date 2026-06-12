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
AUTHORITY_OUT_ROOT = OUT_ROOT / "rule-authority"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr6_rule_authority")

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
    ruleset: str = "sr6",
    book_profile: str = "sr6_core_2019",
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
    def add_character_creation(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p58-79")
        for priority, bundle in (payload.get("priority_table") or {}).items():
            for key, value in (bundle or {}).items():
                if isinstance(value, dict):
                    for nested_key, nested_value in value.items():
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.character_creation.priority.{slug(priority)}.{slug(key)}.{slug(nested_key)}", provider="Sr6CharacterCreationProvider", source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
                else:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.character_creation.priority.{slug(priority)}.{slug(key)}", provider="Sr6CharacterCreationProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for metatype, attrs in (payload.get("metatype_attribute_ranges") or {}).items():
            for key, value in (attrs or {}).items():
                if key == "racial_qualities":
                    for quality in value or []:
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.character_creation.metatype.{slug(metatype)}.quality.{slug(quality)}", provider="Sr6MetatypeProvider", source_ref=source_ref, fact={"metatype": metatype, "quality": quality}, seed_file=path.name)
                else:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.character_creation.metatype.{slug(metatype)}.{slug(key)}", provider="Sr6MetatypeProvider", source_ref=source_ref, fact={"metatype": metatype, "attribute": key, "range": value}, seed_file=path.name)

    def add_combat(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p104-125")
        for step in payload.get("process") or []:
            append_fact(rulefacts, seen_ids, fact_id=f"sr6.combat.process.{slug(step.get('id'))}", provider="Sr6CombatProvider", source_ref=source_ref, fact=step, seed_file=path.name)
        for section_name in ("initiative", "surprise", "ranges_meters"):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.combat.{slug(section_name)}.{slug(key)}", provider="Sr6CombatProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for mode, details in (payload.get("firing_modes") or {}).items():
            for key, value in (details or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.combat.firing_mode.{slug(mode)}.{slug(key)}", provider="Sr6CombatProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        append_fact(rulefacts, seen_ids, fact_id="sr6.combat.vehicle_weapon_exception", provider="Sr6CombatProvider", source_ref=source_ref, fact={"vehicle_weapon_exception": payload.get("vehicle_weapon_exception")}, seed_file=path.name)

    def add_magic(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p126-169")
        for section_name in ("drain", "combat_spells"):
            for key, value in (payload.get(section_name) or {}).items():
                if isinstance(value, dict):
                    for nested_key, nested_value in value.items():
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.{slug(section_name)}.{slug(key)}.{slug(nested_key)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
                else:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.{slug(section_name)}.{slug(key)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        spellcasting = payload.get("spellcasting") or {}
        for key, value in spellcasting.items():
            if key in {"steps", "spell_categories", "range_types", "duration_types", "type_values"}:
                for entry in value or []:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.spellcasting.{slug(key)}.{slug(entry)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={key.rstrip('s'): entry}, seed_file=path.name)
            elif key == "adjustments" and isinstance(value, dict):
                for adjustment, adjustment_payload in value.items():
                    for nested_key, nested_value in (adjustment_payload or {}).items():
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.spellcasting.adjustment.{slug(adjustment)}.{slug(nested_key)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.spellcasting.{slug(key)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        for section_name in ("ritual_spellcasting", "summoning", "banishing"):
            for key, value in (payload.get(section_name) or {}).items():
                if isinstance(value, list):
                    for entry in value:
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.{slug(section_name)}.{slug(key)}.{slug(entry)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={key.rstrip('s'): entry}, seed_file=path.name)
                else:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.magic.{slug(section_name)}.{slug(key)}", provider="Sr6MagicProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        append_fact(rulefacts, seen_ids, fact_id="sr6.magic.sustained_spell_penalty", provider="Sr6MagicProvider", source_ref=source_ref, fact={"sustained_spell_penalty": payload.get("sustained_spell_penalty")}, seed_file=path.name)

    def add_matrix(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p170-195")
        for section_name in ("matrix_test_process", "bonus_edge_expires_when", "matrix_edge_actions"):
            for entry in payload.get(section_name) or []:
                fact_key = section_name[:-1] if section_name.endswith("s") else section_name
                fact_id_suffix = slug(entry.get("id")) if isinstance(entry, dict) and entry.get("id") else slug(entry)
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.matrix.{slug(section_name)}.{fact_id_suffix}", provider="Sr6MatrixProvider", source_ref=source_ref, fact={fact_key: entry}, seed_file=path.name)
        for section_name in ("dice_pools", "matrix_edge_compare", "overwatch_score", "dumpshock"):
            for key, value in (payload.get(section_name) or {}).items():
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.matrix.{slug(section_name)}.{slug(key)}", provider="Sr6MatrixProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)
        noise = payload.get("noise") or {}
        for key, value in noise.items():
            if key == "distance":
                for row in value or []:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.matrix.noise.distance.{slug(row.get('range'))}", provider="Sr6MatrixProvider", source_ref=source_ref, fact=row, seed_file=path.name)
            else:
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.matrix.noise.{slug(key)}", provider="Sr6MatrixProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)

    def add_rigging(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p196-201")
        for section_name in ("core", "control_rig", "rcc", "vehicles", "drones"):
            for key, value in (payload.get(section_name) or {}).items():
                if isinstance(value, dict):
                    for nested_key, nested_value in value.items():
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.rigging.{slug(section_name)}.{slug(key)}.{slug(nested_key)}", provider="Sr6RiggingProvider", source_ref=source_ref, fact={nested_key: nested_value}, seed_file=path.name)
                elif isinstance(value, list):
                    for entry in value:
                        append_fact(rulefacts, seen_ids, fact_id=f"sr6.rigging.{slug(section_name)}.{slug(key)}.{slug(entry)}", provider="Sr6RiggingProvider", source_ref=source_ref, fact={key.rstrip('s'): entry}, seed_file=path.name)
                else:
                    append_fact(rulefacts, seen_ids, fact_id=f"sr6.rigging.{slug(section_name)}.{slug(key)}", provider="Sr6RiggingProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)

    def add_status_effects(path: Path) -> None:
        payload = load_yaml(path)
        source_ref = payload.get("source_ref", "sr6_core_2019:p51-54")
        for status in payload.get("statuses") or []:
            status_id = str(status.get("id"))
            for key, value in status.items():
                if key == "id":
                    continue
                append_fact(rulefacts, seen_ids, fact_id=f"sr6.status.{slug(status_id)}.{slug(key)}", provider="Sr6StatusProvider", source_ref=source_ref, fact={key: value}, seed_file=path.name)

    add_character_creation(SEED_ROOT / "SR6_CHARACTER_CREATION_SEED.yaml")
    add_combat(SEED_ROOT / "SR6_COMBAT_SEED.yaml")
    add_magic(SEED_ROOT / "SR6_MAGIC_SEED.yaml")
    add_matrix(SEED_ROOT / "SR6_MATRIX_SEED.yaml")
    add_rigging(SEED_ROOT / "SR6_RIGGING_SEED.yaml")
    add_status_effects(SEED_ROOT / "SR6_STATUS_EFFECTS_SEED.yaml")


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
    seen_ids: set[str] = set()
    source_refs: set[str] = set()
    for file_name, payload in seed_payloads.items():
        for fact in walk_rulefacts(payload):
            normalized = dict(fact)
            normalized.setdefault("ruleset", "sr6")
            normalized.setdefault("book_profile", "sr6_core_2019")
            normalized.setdefault("status", "seed")
            normalized["seed_file"] = file_name
            fact_id = str(normalized.get("id") or normalized.get("fact_id") or "")
            if fact_id and fact_id not in seen_ids:
                seen_ids.add(fact_id)
                rulefacts.append(normalized)
        source_refs.update(collect_source_refs(payload))
    add_structured_seed_facts(rulefacts, seen_ids)

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
        "schema": "sr6-rule-authority-public-registry-v2",
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
    AUTHORITY_OUT_ROOT.mkdir(parents=True, exist_ok=True)
    COMPLETION_ROOT.mkdir(parents=True, exist_ok=True)
    (AUTHORITY_OUT_ROOT / "SR6_RULEFACT_REGISTRY.generated.json").write_text(
        json.dumps(registry, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    for directory in (OUT_ROOT, COMPLETION_ROOT):
        (directory / "SR6_RULE_AUTHORITY_INTEGRATION.generated.json").write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        (directory / "SR6_PROVIDER_COVERAGE.generated.json").write_text(
            json.dumps(provider_coverage, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        (directory / "SR6_RULEFACT_REGISTRY.generated.json").write_text(
            json.dumps(registry, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )

    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
