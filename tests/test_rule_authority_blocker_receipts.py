from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "materialize_rule_authority_blocker_receipts.py"


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_rule_authority_blocker_receipts", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityBlockerReceiptTests(unittest.TestCase):
    def test_sr4_and_sr6_blocker_receipts_stay_pending_not_ready(self) -> None:
        module = load_module()
        for ruleset in ("sr4", "sr6"):
            row_level, errata = module.materialize_ruleset(ruleset)
            self.assertEqual("pending_human_review", row_level["status"])
            self.assertFalse(row_level["ready_for_gold"])
            self.assertIn("review", row_level["remaining_gate"].lower())
            self.assertFalse(errata["ready_for_gold"])
            self.assertIn("pending", errata["status"])
            self.assertTrue(errata["required_before_gold"])


if __name__ == "__main__":
    unittest.main()
