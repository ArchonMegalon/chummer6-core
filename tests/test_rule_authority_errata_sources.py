from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "rule_authority_errata_sources.py"


def load_module():
    spec = importlib.util.spec_from_file_location("rule_authority_errata_sources", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityErrataSourcesTests(unittest.TestCase):
    def test_sr6_sources_are_id_addressable_and_sr4_is_empty(self) -> None:
        module = load_module()

        sr4_sources = module.errata_sources_for_ruleset("sr4")
        sr6_sources = module.errata_sources_for_ruleset("sr6")
        by_id = module.errata_sources_by_id()

        self.assertEqual([], sr4_sources)
        self.assertEqual(3, len(sr6_sources))
        self.assertEqual({source["id"] for source in sr6_sources}, set(by_id))
        self.assertIn("sr6_aug_2019", by_id)
        self.assertIn("sr6_feb_2020", by_id)
        self.assertIn("sr6_city_edition_notice", by_id)


if __name__ == "__main__":
    unittest.main()
