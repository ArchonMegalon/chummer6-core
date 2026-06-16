from __future__ import annotations

import importlib.util
import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "materialize_rule_authority_reviewer_packets.py"


def load_module():
    spec = importlib.util.spec_from_file_location("materialize_rule_authority_reviewer_packets", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityReviewerPacketTests(unittest.TestCase):
    def test_packets_reduce_review_work_to_bounded_checklists(self) -> None:
        module = load_module()
        sr4 = module.build_packet("sr4")
        sr6 = module.build_packet("sr6")

        self.assertEqual("awaiting_human_decision", sr4["status"])
        self.assertEqual("legacy Chummer4 XML as implemented for core readiness", sr4["selected_core_baseline"])
        self.assertEqual("not_applicable", sr4["errata"]["recommended_decision"])
        self.assertFalse(sr4["errata"]["review_decision_required"])
        self.assertFalse(sr4["fixtures"]["review_expected_values_required"])
        self.assertGreater(sr4["registry"]["rulefact_count"], 0)
        self.assertTrue(sr4["human_review_file"].endswith("SR4_HUMAN_RULE_REVIEW.md"))
        self.assertIn("row_level_mapping", sr4["review_inputs"])
        self.assertEqual("approved", sr4["exact_edit_contract"]["Status"])
        self.assertEqual("not_applicable", sr4["exact_edit_contract"]["Errata decision"])
        self.assertGreaterEqual(len(sr4["rerun_commands"]), 4)
        self.assertIn("preferred_signoff_path", sr4)
        self.assertEqual("not_applicable", sr4["suggested_default_decisions"]["errata_decision"])
        self.assertIn("pass_criteria", sr4)
        self.assertIn("why_this_should_pass", sr4)
        self.assertIn("core-only baseline", sr4["suggested_default_decisions"]["errata_rationale"])
        self.assertEqual(
            [
                "review row-level mapping packet and approve or reject normalized public-safe records",
                "complete human rule review signoff",
            ],
            sr4["recommended_next_actions"],
        )

        self.assertEqual("awaiting_human_decision", sr6["status"])
        self.assertEqual("Shadowrun_6_Downloadversion_2024.pdf", sr6["selected_core_baseline"])
        self.assertEqual("applied", sr6["errata"]["recommended_decision"])
        self.assertFalse(sr6["fixtures"]["review_expected_values_required"])
        self.assertFalse(sr6["explain_receipts"]["review_required"])
        self.assertEqual("applied | not_applicable | defer", sr6["exact_edit_contract"]["Errata decision"])
        self.assertIn("Errata defer rationale", sr6["exact_edit_contract"])
        self.assertEqual(3, len(sr6["errata"]["sources"]))
        self.assertIn("2024 core baseline", sr6["suggested_default_decisions"]["errata_decision"])
        self.assertIn("consolidated official source", sr6["suggested_default_decisions"]["errata_rationale"])

    def test_published_packets_exist(self) -> None:
        for ruleset in ("SR4", "SR6"):
            packet = REPO_ROOT / ".codex-studio" / "published" / f"{ruleset}_REVIEWER_DECISION_PACKET.generated.json"
            md = REPO_ROOT / ".codex-studio" / "published" / f"{ruleset}_REVIEWER_DECISION_PACKET.generated.md"
            self.assertTrue(packet.is_file())
            self.assertTrue(md.is_file())
            payload = json.loads(packet.read_text(encoding="utf-8"))
            self.assertEqual("awaiting_human_decision", payload["status"])


if __name__ == "__main__":
    unittest.main()
