from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "verify_sr5_rule_authority_seed.py"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_sr5_rule_authority_seed", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class Sr5RuleAuthoritySeedTests(unittest.TestCase):
    def test_build_registry_generates_rich_fact_set(self) -> None:
        module = load_module()
        registry, provider_coverage, golden_fixtures, refreshed_depth = module.build_registry()

        self.assertEqual("pass", registry["status"])
        self.assertGreaterEqual(registry["rulefact_count"], 100)
        self.assertEqual("pass", provider_coverage["status"])
        self.assertGreaterEqual(len(provider_coverage["providers"]), 5)
        self.assertIn("SR5DiceProvider", registry["implemented_providers"])
        self.assertIn("SR5TestProvider", registry["implemented_providers"])
        self.assertIn("SR5ExplainReceiptProvider", registry["implemented_providers"])
        self.assertIn("SR5GearProvider", registry["implemented_providers"])
        self.assertIn("SR5CharacterCreationProvider", registry["implemented_providers"])
        self.assertIn("SR5CombatProvider", registry["implemented_providers"])
        self.assertIn("SR5MagicProvider", registry["implemented_providers"])
        self.assertIn("SR5MatrixProvider", registry["implemented_providers"])
        self.assertIn("SR5RiggingProvider", registry["implemented_providers"])
        self.assertNotIn("SR5DiceProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5TestProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5ExplainReceiptProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5GearProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5CharacterCreationProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5CombatProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5MagicProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5MatrixProvider", registry["missing_implemented_providers"])
        self.assertNotIn("SR5RiggingProvider", registry["missing_implemented_providers"])
        self.assertEqual([], registry["missing_implemented_providers"])
        self.assertEqual(registry["missing_implemented_providers"], provider_coverage["missing_providers"])
        self.assertEqual(registry["rulefact_count"], refreshed_depth["rule_authority"]["mapped_rulefacts"])
        self.assertEqual("pass", refreshed_depth["rule_authority"]["status"])
        self.assertGreaterEqual(len(golden_fixtures["fixtures"]), 3)

        fact_ids = {fact["id"] for fact in registry["rulefacts"]}
        self.assertIn("sr5.dice.hit_faces", fact_ids)
        self.assertIn("sr5.tests.opposed_test", fact_ids)
        self.assertIn("sr5.explain.public_safe_receipt", fact_ids)
        self.assertIn("sr5.gear.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.gear.table_import.file.gear_xml", fact_ids)
        self.assertIn("sr5.gear.table_import.file.weapons_xml.container.weapons", fact_ids)
        self.assertIn("sr5.character_creation.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.character_creation.table_import.file.metatypes_xml", fact_ids)
        self.assertIn("sr5.character_creation.table_import.file.priorities_xml.container.priorities", fact_ids)
        self.assertIn("sr5.combat.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.magic.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.matrix.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.rigging.index.public_safe_metadata_only", fact_ids)
        self.assertIn("sr5.combat.table_import.file.actions_xml.container.actions", fact_ids)
        self.assertIn("sr5.magic.table_import.file.spells_xml.container.spells", fact_ids)
        self.assertIn("sr5.matrix.table_import.file.programs_xml.container.programs", fact_ids)
        self.assertIn("sr5.rigging.table_import.file.vehicles_xml.container.vehicles", fact_ids)
        self.assertNotIn("sr5.shell.command.new_character", fact_ids)
        self.assertNotIn("sr5.navigation.tab.tab_create", fact_ids)
        self.assertNotIn("sr5.workspace_action.tab_info_summary", fact_ids)
        self.assertIn("sr5.workflow.surface.sr5_shell_toolbar", fact_ids)
        self.assertIn("sr5.workflow.surface.sr5_career_section", fact_ids)
        self.assertEqual(
            [
                {
                    "path": "Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs",
                    "reason": "Shell catalogs are UI/workbench metadata, not SR5 mechanical rule authority.",
                }
            ],
            registry["excluded_inputs"],
        )
        self.assertIn("sr5.table_import.file.gear_xml", fact_ids)
        self.assertIn("sr5.parity.capability.derive_stat", fact_ids)


if __name__ == "__main__":
    unittest.main()
