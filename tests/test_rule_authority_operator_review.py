from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "audit_rule_authority_operator_review.py"


def load_module():
    spec = importlib.util.spec_from_file_location("audit_rule_authority_operator_review", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityOperatorReviewTests(unittest.TestCase):
    def test_build_payload_tracks_current_blocked_rule_authority_state(self) -> None:
        module = load_module()
        payload = module.build_payload()

        self.assertEqual("operator_review_complete_authority_blocked", payload["status"])
        self.assertFalse(payload["readiness_decision"]["sr4_rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr5"]["serious_implementation_claim"] == "allowed")
        self.assertFalse(payload["readiness_decision"]["sr6_rule_authority_ready"])
        self.assertFalse(payload["readiness_decision"]["full_product_rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr4"]["human_review_status"]["pending_review"])
        self.assertFalse(payload["rulesets"]["sr4"]["human_review_status"]["review_ready"])
        self.assertTrue(payload["rulesets"]["sr6"]["human_review_status"]["pending_review"])
        self.assertFalse(payload["rulesets"]["sr6"]["human_review_status"]["review_ready"])
        self.assertIn("human_review", payload["rulesets"]["sr4"]["blocker_receipts"])
        self.assertIn("human_review", payload["rulesets"]["sr6"]["blocker_receipts"])

        findings = {finding["id"]: finding["status"] for finding in payload["audit_findings"]}
        self.assertEqual("blocker", findings["rulefact_depth"])
        self.assertEqual("blocker", findings["errata_application"])
        self.assertEqual("blocker", findings["human_signoff"])

    def test_full_completion_reports_blockers(self) -> None:
        import importlib.util

        script_path = REPO_ROOT / "scripts" / "verify_full_rule_authority_completion.py"
        spec = importlib.util.spec_from_file_location("verify_full_rule_authority_completion", script_path)
        module = importlib.util.module_from_spec(spec)
        assert spec and spec.loader
        spec.loader.exec_module(module)

        self.assertEqual(2, module.main())
        import json

        payload = json.loads((REPO_ROOT / ".codex-studio" / "published" / "FULL_PRODUCT_RULE_AUTHORITY_COMPLETION.generated.json").read_text(encoding="utf-8"))
        self.assertEqual("blocked", payload["status"])
        self.assertEqual("NOT_READY", payload["final_verdict"])
        self.assertNotEqual([], payload["blockers"])
        self.assertFalse(payload["rulesets"]["sr4"]["rule_authority_ready"])
        self.assertFalse(payload["rulesets"]["sr6"]["rule_authority_ready"])
        self.assertFalse(payload["rulesets"]["sr4"]["human_review"]["review_ready"])
        self.assertFalse(payload["rulesets"]["sr6"]["human_review"]["review_ready"])


if __name__ == "__main__":
    unittest.main()
