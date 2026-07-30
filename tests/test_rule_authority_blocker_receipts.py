from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "materialize_rule_authority_blocker_receipts.py"
VERIFY_PATH = REPO_ROOT / "scripts" / "ai" / "verify.sh"


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_rule_authority_blocker_receipts", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityBlockerReceiptTests(unittest.TestCase):
    def test_local_verify_materializes_table_imports_before_consuming_them(self) -> None:
        verify = VERIFY_PATH.read_text(encoding="utf-8")
        sr6_generator = verify.index(
            "bash scripts/ai/python-with-rule-authority-deps.sh scripts/generate_sr6_pdf_private_import.py"
        )
        coverage_generator = verify.index("python3 scripts/generate_sr456_table_import_coverage.py")
        blocker_materializer = verify.index("python3 scripts/materialize_rule_authority_blocker_receipts.py")

        self.assertLess(sr6_generator, coverage_generator)
        self.assertLess(coverage_generator, blocker_materializer)
        self.assertTrue((REPO_ROOT / "scripts" / "ai" / "python-with-rule-authority-deps.sh").is_file())
        self.assertTrue((REPO_ROOT / "scripts" / "ai" / "rule-authority-requirements.txt").is_file())

    def test_errata_profiles_load_from_authoritative_ruleset_profiles(self) -> None:
        module = load_module()

        for ruleset in ("sr4", "sr6"):
            with self.subTest(ruleset=ruleset):
                profile = module.load_errata_profile(ruleset)
                self.assertEqual("pending", profile["status"])
                self.assertTrue(profile["required_before_gold"])
                self.assertFalse(profile["production_claim_allowed"])

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
            self.assertTrue(row_level["review_packet"]["spot_check_plan"])
            self.assertEqual("approved if bounded spot checks do not reveal contradictions", row_level["review_packet"]["recommended_decision"])
            self.assertIn("human_required", row_level)
            self.assertTrue(row_level["human_required"]["must_sign_off_before_ready_token"])
            self.assertIn("selected_core_baseline", row_level["review_packet"])
            if ruleset == "sr6":
                self.assertEqual("operator_policy_selected_core_baseline", row_level["source_baseline_decision_status"])
                self.assertFalse(row_level["human_required"]["must_select_source_baseline"])
                self.assertEqual(["Shadowrun_6_Downloadversion_2024.pdf"], row_level["review_packet"]["indexed_source_files"])
                self.assertEqual("Shadowrun_6_Downloadversion_2024.pdf", row_level["review_packet"]["selected_core_baseline"])
            if ruleset == "sr4":
                self.assertEqual("operator_policy_selected_core_baseline", row_level["source_baseline_decision_status"])
                self.assertFalse(row_level["human_required"]["must_select_source_baseline"])
                self.assertTrue(row_level["review_packet"]["indexed_source_files"])
                self.assertNotEqual(["none"], row_level["review_packet"]["indexed_source_files"])
                self.assertIn("(SR4) Shadowrun 4e Core Rules.pdf", row_level["review_packet"]["source_identity"][0]["file"])
                self.assertTrue(row_level["review_packet"]["source_identity"][0]["exists"])
            self.assertFalse(errata["ready_for_gold"])
            self.assertIn(errata["status"], {"pending_reviewed_application", "not_applicable_by_policy"})
            self.assertTrue(errata["required_before_gold"])
            self.assertIn("review_packet", errata)
            if ruleset == "sr4":
                self.assertEqual("not_applicable", errata["review_packet"]["decision"])
            else:
                self.assertEqual("pending", errata["review_packet"]["decision"])
                self.assertEqual("applied", errata["review_packet"]["recommended_decision"])
            self.assertEqual("official errata or official web notices only", errata["review_packet"]["errata_policy"])
            self.assertIn("human_required", errata)
            self.assertTrue(errata["human_required"]["must_sign_off_before_ready_token"])
            handoff = module.build_review_handoff(ruleset, row_level, errata)
            self.assertIn("Recommended Signoff Path", handoff)
            self.assertIn("Suggested Default Decisions", handoff)
            self.assertIn("Bounded Spot-Check Plan", handoff)
            review = module.build_human_rule_review(ruleset, row_level, errata)
            self.assertIn("Status: pending", review)
            self.assertIn("Row-level decision: pending", review)
            self.assertIn("Ready token approved: false", review)
            self.assertIn("Fastest Defensible Pass Path", review)
            self.assertIn("Suggested Default Decisions", review)
            self.assertIn("Bounded Spot-Check Plan", review)
            self.assertIn(f"{ruleset.upper()}_ROW_LEVEL_AUTHORITY_MAPPING.generated.json", review)
            self.assertIn(f"{ruleset.upper()}_ERRATA_SOURCE_POSTURE.generated.json", review)
            if ruleset == "sr6":
                self.assertIn("Errata decision: pending", review)
                self.assertIn("Source baseline decision status: `operator_policy_selected_core_baseline`", review)
                self.assertNotIn("Source baseline decision: <selected baseline>", review)
                self.assertIn("Errata decision: `applied` unless a specific official errata source remains unreconciled", review)
                self.assertIn("page=`", review)
                self.assertIn("line_sha256=`", review)
            if ruleset == "sr4":
                self.assertIn("Errata decision: not_applicable", review)
                self.assertNotIn("Confirm fixture expectations are valid against reviewed rule authority.", review)
                self.assertIn("Source Identity Evidence", review)
                self.assertIn("(SR4) Shadowrun 4e Core Rules.pdf", review)
                self.assertNotIn("- `none`\n\n## Source Identity Evidence", review)
                self.assertIn("keep Errata decision at not_applicable", review)
                self.assertIn("gear.xml", review)


if __name__ == "__main__":
    unittest.main()
