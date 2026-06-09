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
    def test_build_payload_stays_blocked_while_sr4_and_sr6_are_not_ready(self) -> None:
        module = load_module()
        payload = module.build_payload()

        self.assertEqual("operator_review_complete_authority_blocked", payload["status"])
        self.assertFalse(payload["readiness_decision"]["sr4_rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr5"]["serious_implementation_claim"] == "allowed")
        self.assertFalse(payload["readiness_decision"]["sr6_rule_authority_ready"])
        self.assertFalse(payload["readiness_decision"]["full_product_rule_authority_ready"])

        findings = {finding["id"]: finding["status"] for finding in payload["audit_findings"]}
        self.assertEqual("blocker", findings["rulefact_depth"])
        self.assertEqual("blocker", findings["errata_application"])
        self.assertEqual("blocker", findings["human_signoff"])


if __name__ == "__main__":
    unittest.main()
