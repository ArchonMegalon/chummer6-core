from __future__ import annotations

import importlib.util
import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "materialize_rule_authority_support_receipts.py"


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_rule_authority_support_receipts", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthoritySupportReceiptTests(unittest.TestCase):
    def test_materialized_support_receipts_track_core_only_scope(self) -> None:
        module = load_module()
        for ruleset in ("sr4", "sr6"):
            fixture = module.fixture_receipt(ruleset)
            explain = module.explain_receipt(ruleset)
            self.assertEqual("core_readiness_only", fixture["scope"])
            self.assertEqual("core_readiness_only", explain["scope"])
            self.assertFalse(fixture["supplements_in_scope"])
            self.assertFalse(explain["supplements_in_scope"])
            self.assertGreater(len(fixture["required_fixture_ids"]), 0)
            self.assertGreater(len(explain["coverage_domains"]), 0)
            self.assertTrue(explain["public_safe"])
            self.assertFalse(fixture["ready_for_gold"])
            self.assertFalse(explain["ready_for_gold"])

    def test_published_support_receipts_match_enriched_statuses(self) -> None:
        for ruleset in ("SR4", "SR6"):
            root = REPO_ROOT / ".codex-studio" / "published"
            fixture = json.loads((root / f"{ruleset}_GOLDEN_FIXTURES.generated.json").read_text(encoding="utf-8"))
            explain = json.loads((root / f"{ruleset}_EXPLAIN_RECEIPTS.generated.json").read_text(encoding="utf-8"))
            self.assertIn(fixture["status"], {"seed_fixtures_passed", "core_seed_fixture_pack_passed"})
            self.assertIn(explain["status"], {"seed_receipts_available", "seeded", "core_seed_receipt_pack_available"})


if __name__ == "__main__":
    unittest.main()
