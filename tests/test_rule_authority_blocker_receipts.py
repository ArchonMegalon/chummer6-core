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
            self.assertIn("review_packet", row_level)
            self.assertEqual("pending", row_level["review_packet"]["decision"])
            self.assertGreater(row_level["review_packet"]["indexed_unit_count"], 0)
            self.assertIn("human_required", row_level)
            self.assertTrue(row_level["human_required"]["must_sign_off_before_ready_token"])
            if ruleset == "sr6":
                self.assertEqual("pending_human_review", row_level["source_baseline_decision_status"])
                self.assertTrue(row_level["human_required"]["must_select_source_baseline"])
                self.assertIn("Shadowrun_6_Downloadversion_2024.pdf", row_level["review_packet"]["indexed_source_files"])
            if ruleset == "sr4":
                self.assertEqual("single_source", row_level["source_baseline_decision_status"])
                self.assertFalse(row_level["human_required"]["must_select_source_baseline"])
                self.assertTrue(row_level["review_packet"]["indexed_source_files"])
                self.assertNotEqual(["none"], row_level["review_packet"]["indexed_source_files"])
                self.assertIn("(SR4) Shadowrun 4e Core Rules.pdf", row_level["review_packet"]["source_identity"][0]["file"])
                self.assertTrue(row_level["review_packet"]["source_identity"][0]["exists"])
            self.assertFalse(errata["ready_for_gold"])
            self.assertIn("pending", errata["status"])
            self.assertTrue(errata["required_before_gold"])
            self.assertIn("review_packet", errata)
            self.assertEqual("pending", errata["review_packet"]["decision"])
            self.assertIn("human_required", errata)
            self.assertTrue(errata["human_required"]["must_sign_off_before_ready_token"])
            review = module.build_human_rule_review(ruleset, row_level, errata)
            self.assertIn("Status: pending", review)
            self.assertIn("Row-level decision: pending", review)
            self.assertIn("Errata decision: pending", review)
            self.assertIn("Ready token approved: false", review)
            self.assertIn(f"{ruleset.upper()}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json", review)
            self.assertIn(f"{ruleset.upper()}_ERRATA_SOURCE_POSTURE.generated.json", review)
            if ruleset == "sr6":
                self.assertIn("Source baseline decision status: `pending_human_review`", review)
                self.assertIn("Source baseline decision: <selected baseline>", review)
            if ruleset == "sr4":
                self.assertIn("Source Identity Evidence", review)
                self.assertIn("(SR4) Shadowrun 4e Core Rules.pdf", review)
                self.assertNotIn("- `none`\n\n## Source Identity Evidence", review)


if __name__ == "__main__":
    unittest.main()
