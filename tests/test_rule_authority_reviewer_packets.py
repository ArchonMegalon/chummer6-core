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
        self.assertEqual(
            [
                "review row-level mapping packet and approve or reject normalized public-safe records",
                "complete human rule review signoff",
            ],
            sr4["recommended_next_actions"],
        )

        self.assertEqual("awaiting_human_decision", sr6["status"])
        self.assertEqual("Shadowrun_6_Downloadversion_2024.pdf", sr6["selected_core_baseline"])
        self.assertEqual("pending_manual_review", sr6["errata"]["recommended_decision"])
        self.assertFalse(sr6["fixtures"]["review_expected_values_required"])
        self.assertFalse(sr6["explain_receipts"]["review_required"])

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
