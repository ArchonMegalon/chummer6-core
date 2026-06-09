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
        self.assertGreaterEqual(len(provider_coverage["providers"]), 10)
        self.assertEqual(registry["rulefact_count"], refreshed_depth["rule_authority"]["mapped_rulefacts"])
        self.assertGreaterEqual(len(golden_fixtures["fixtures"]), 3)

        fact_ids = {fact["id"] for fact in registry["rulefacts"]}
        self.assertIn("sr5.shell.command.new_character", fact_ids)
        self.assertIn("sr5.navigation.tab.tab_create", fact_ids)
        self.assertIn("sr5.workspace_action.tab_info_summary", fact_ids)
        self.assertIn("sr5.table_import.file.gear_xml", fact_ids)
        self.assertIn("sr5.parity.capability.derive_stat", fact_ids)


if __name__ == "__main__":
    unittest.main()
