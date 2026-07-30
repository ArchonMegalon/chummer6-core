#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
DOCS_ROOT = REPO_ROOT / "docs" / "rulesets"

POLICY = {
    "sr4": {
        "core_baseline": "legacy Chummer4 XML as implemented for core readiness",
        "authority_book_profile": "sr4a_core_2009",
        "supplements_in_scope": False,
        "errata_policy": "official errata or official web notices only",
        "core_domains": [
            "dice",
            "tests",
            "build_points",
            "metatype_ranges",
            "attributes",
            "skills",
            "qualities",
            "derived_stats",
            "combat",
            "magic",
            "matrix",
            "rigging",
        ],
        "fixture_test_classes": [
            "Sr4DiceProviderTests",
            "Sr4TestAndEdgeProviderTests",
            "Sr4CharacterAndDerivedProviderTests",
            "Sr4CombatMagicMatrixRiggingProviderTests",
        ],
        "fixture_coverage": {
            "sr4_basic_dice_edge": ["Sr4DiceProviderTests", "Sr4TestAndEdgeProviderTests"],
            "sr4_bp_human_street_sam": ["Sr4CharacterAndDerivedProviderTests"],
            "sr4_bp_elf_mage": ["Sr4CharacterAndDerivedProviderTests", "Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_bp_dwarf_hacker": ["Sr4CharacterAndDerivedProviderTests", "Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_firearms_attack": ["Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_melee_attack": ["Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_summoning_spirit": ["Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_matrix_hacking_on_fly": ["Sr4CombatMagicMatrixRiggingProviderTests"],
            "sr4_rigging_drone_remote_control": ["Sr4CombatMagicMatrixRiggingProviderTests"],
        },
    },
    "sr6": {
        "core_baseline": "Shadowrun_6_Downloadversion_2024.pdf",
        "authority_book_profile": "sr6_core_2024_selected_baseline",
        "supplements_in_scope": False,
        "errata_policy": "official errata or official web notices only",
        "core_domains": [
            "dice",
            "tests",
            "edge",
            "action_economy",
            "priority_creation",
            "metatype_ranges",
            "skills",
            "derived_stats",
            "combat",
            "magic",
            "matrix",
            "rigging",
            "status_effects",
        ],
        "fixture_test_classes": [
            "Sr6DiceProviderTests",
            "Sr6TestProviderTests",
            "Sr6EdgeProviderTests",
            "Sr6ActionEconomyProviderTests",
            "Sr6CharacterCreationProviderTests",
            "Sr6MetatypeProviderTests",
            "Sr6SkillAndQualityProviderTests",
            "Sr6DerivedStatsProviderTests",
            "Sr6StatusProviderTests",
            "Sr6CombatProviderTests",
            "Sr6MatrixProviderTests",
            "Sr6MagicProviderTests",
            "Sr6RiggingProviderTests",
            "Sr6GearAdvancementAndExplainProviderTests",
        ],
        "fixture_coverage": {
            "sr6_basic_dice_and_edge": ["Sr6DiceProviderTests", "Sr6EdgeProviderTests"],
            "sr6_priority_human_street_sam": [
                "Sr6CharacterCreationProviderTests",
                "Sr6DerivedStatsProviderTests",
                "Sr6SkillAndQualityProviderTests",
            ],
            "sr6_priority_elf_mage": ["Sr6CharacterCreationProviderTests", "Sr6MagicProviderTests"],
            "sr6_firearms_attack": ["Sr6CombatProviderTests", "Sr6EdgeProviderTests"],
            "sr6_direct_spell": ["Sr6MagicProviderTests"],
            "sr6_indirect_spell_fireball": ["Sr6MagicProviderTests", "Sr6CombatProviderTests", "Sr6StatusProviderTests"],
            "sr6_matrix_illegal_action_os": ["Sr6MatrixProviderTests", "Sr6EdgeProviderTests"],
            "sr6_rigging_drone_attack": ["Sr6RiggingProviderTests", "Sr6ActionEconomyProviderTests"],
        },
    },
}


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def load_yaml(path: Path) -> dict[str, Any]:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def existing_or_empty(path: Path) -> dict[str, Any]:
    return load_json(path) if path.is_file() else {}


def parse_trx_counts(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    counters = next(
        (node for node in root.iter() if node.tag.rsplit("}", 1)[-1] == "Counters"),
        None,
    )
    if counters is None:
        raise ValueError(f"TRX result is missing counters: {path}")

    def count(name: str) -> int:
        try:
            return int(counters.attrib.get(name, "0"))
        except ValueError as exc:
            raise ValueError(f"TRX counter {name!r} is not numeric: {path}") from exc

    total = count("total")
    executed = count("executed")
    return {
        "total": total,
        "passed": count("passed"),
        "failed": count("failed"),
        "skipped": max(count("notExecuted"), total - executed),
    }


def parse_trx_test_classes(path: Path) -> list[str]:
    root = ET.parse(path).getroot()
    classes = {
        node.attrib["className"].rsplit(".", 1)[-1]
        for node in root.iter()
        if node.tag.rsplit("}", 1)[-1] == "TestMethod"
        and node.attrib.get("className")
    }
    return sorted(classes)


def run_fixture_tests(ruleset: str) -> dict[str, Any]:
    test_classes = POLICY[ruleset]["fixture_test_classes"]
    test_filter = "|".join(test_classes)
    with tempfile.TemporaryDirectory(prefix=f"chummer-{ruleset}-fixtures-") as temp_dir:
        result_path = Path(temp_dir) / f"{ruleset}-fixtures.trx"
        command = [
            "dotnet",
            "test",
            "Chummer.Tests/Chummer.Tests.csproj",
            "--framework",
            "net10.0",
            "--no-restore",
            "--filter",
            test_filter,
            "--logger",
            f"trx;LogFileName={result_path.name}",
            "--results-directory",
            temp_dir,
            "-m:1",
            "-p:UseSharedCompilation=false",
        ]
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        counts = (
            parse_trx_counts(result_path)
            if result_path.is_file()
            else {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
        )
        executed_test_classes = parse_trx_test_classes(result_path) if result_path.is_file() else []
    return {
        **counts,
        "executed_test_classes": executed_test_classes,
        "test_filter": test_filter,
        "test_command": command,
        "test_returncode": completed.returncode,
        "stdout_tail": completed.stdout[-4000:],
        "stderr_tail": completed.stderr[-4000:],
    }


def fixture_receipt(
    ruleset: str,
    execution: dict[str, Any] | None = None,
) -> dict[str, Any]:
    upper = ruleset.upper()
    policy = POLICY[ruleset]
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    existing = (
        execution
        if execution is not None
        else existing_or_empty(root / f"{upper}_GOLDEN_FIXTURES.generated.json")
    )
    plan = load_yaml(DOCS_ROOT / f"{ruleset}-rule-authority" / f"{upper}_GOLDEN_FIXTURE_PLAN.yaml")
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    required = [fixture for fixture in plan.get("required_fixtures", []) if isinstance(fixture, dict)]
    required_fixture_ids = [str(fixture.get("id")) for fixture in required if fixture.get("id")]
    fixture_coverage = policy["fixture_coverage"]
    passed = int(existing.get("passed") or 0)
    failed = int(existing.get("failed") or 0)
    total = int(existing.get("total") or passed + failed)
    test_returncode = existing.get("test_returncode")
    execution_succeeded = (
        test_returncode in (None, 0)
        and total > 0
        and failed == 0
        and passed > 0
        and passed + failed + int(existing.get("skipped") or 0) == total
    )
    executed_test_classes = {
        str(test_class)
        for test_class in existing.get("executed_test_classes", [])
        if test_class
    }
    expected_test_classes = set(policy["fixture_test_classes"])
    coverage_complete = (
        set(required_fixture_ids) == set(fixture_coverage)
        and expected_test_classes.issubset(executed_test_classes)
    )
    return {
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "core_seed_fixture_pack_passed" if execution_succeeded and coverage_complete else "fail",
        "scope": "core_readiness_only",
        "core_baseline": policy["core_baseline"],
        "supplements_in_scope": policy["supplements_in_scope"],
        "required_fixture_ids": required_fixture_ids,
        "required_fixture_count": len(required),
        "required_fixture_purposes": {fixture.get("id"): fixture.get("purpose") for fixture in required if fixture.get("id")},
        "fixture_policy": plan.get("fixture_policy", {}),
        "fixture_coverage": fixture_coverage,
        "expected_test_classes": sorted(expected_test_classes),
        "executed_test_classes": sorted(executed_test_classes),
        "coverage_complete": coverage_complete,
        "core_domains": policy["core_domains"],
        "rulefact_count": int(registry.get("rulefact_count") or 0),
        "passed": passed,
        "failed": failed,
        "skipped": int(existing.get("skipped") or 0),
        "total": total,
        "test_filter": existing.get("test_filter", f"FullyQualifiedName~{upper.title()}"),
        "test_command": existing.get("test_command", []),
        "test_returncode": test_returncode,
        "test_stdout_tail": existing.get("stdout_tail", ""),
        "test_stderr_tail": existing.get("stderr_tail", ""),
        "ready_for_gold": False,
        "remaining_gates": [
            "human-reviewed expected values against approved row-level authority",
            "human signoff before ready token",
        ],
    }


def explain_receipt(ruleset: str) -> dict[str, Any]:
    upper = ruleset.upper()
    policy = POLICY[ruleset]
    root = COMPLETION_ROOT / f"{ruleset}_rule_authority"
    registry = load_json(root / f"{upper}_RULEFACT_REGISTRY.generated.json")
    provider_coverage = load_json(root / f"{upper}_PROVIDER_COVERAGE.generated.json")
    existing = existing_or_empty(root / f"{upper}_EXPLAIN_RECEIPTS.generated.json")
    provider_name = "Sr4ExplainReceiptProvider" if ruleset == "sr4" else "Sr6ExplainReceiptProvider"
    providers_with_facts = sorted({fact.get("provider") for fact in registry.get("rulefacts", []) if fact.get("provider")})
    return {
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "status": "core_seed_receipt_pack_available",
        "scope": "core_readiness_only",
        "core_baseline": policy["core_baseline"],
        "supplements_in_scope": policy["supplements_in_scope"],
        "public_safe": True,
        "provider": provider_name,
        "provider_coverage_status": provider_coverage.get("status"),
        "implemented_provider_count": provider_coverage.get("implemented_provider_count"),
        "providers_with_rulefacts": providers_with_facts,
        "coverage_domains": policy["core_domains"],
        "receipt_kind": "public_safe_seed_explain_receipts",
        "reason": existing.get(
            "reason",
            "Seed-level public-safe explain receipts exist for the core-only authority scope; reviewed row-level mappings remain required before a ready token.",
        ),
        "ready_for_gold": False,
        "remaining_gates": [
            "reviewed row-level authority mapping for cited facts",
            "human-confirmed explain corpus against approved baseline and errata posture",
        ],
    }


def main() -> int:
    for ruleset in ("sr4", "sr6"):
        upper = ruleset.upper()
        fixture = fixture_receipt(ruleset, run_fixture_tests(ruleset))
        explain = explain_receipt(ruleset)
        for base in (COMPLETION_ROOT / f"{ruleset}_rule_authority", PUBLISHED_ROOT):
            write_json(base / f"{upper}_GOLDEN_FIXTURES.generated.json", fixture)
            write_json(base / f"{upper}_EXPLAIN_RECEIPTS.generated.json", explain)
    print(json.dumps({"status": "ok", "rulesets": ["sr4", "sr6"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
