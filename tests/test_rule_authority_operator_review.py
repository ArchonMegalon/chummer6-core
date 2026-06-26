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
    def test_build_payload_tracks_current_ready_rule_authority_state(self) -> None:
        module = load_module()
        payload = module.build_payload()

        self.assertEqual("operator_review_complete_authority_ready", payload["status"])
        self.assertTrue(payload["readiness_decision"]["sr4_rule_authority_ready"])
        self.assertTrue(payload["readiness_decision"]["sr5_rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr5"]["serious_implementation_claim"] == "allowed")
        self.assertEqual([], payload["rulesets"]["sr5"]["missing_implemented_providers"])
        self.assertNotIn("SR5GearProvider", payload["rulesets"]["sr5"]["missing_implemented_providers"])
        self.assertNotIn("SR5CharacterCreationProvider", payload["rulesets"]["sr5"]["missing_implemented_providers"])
        self.assertEqual([], payload["rulesets"]["sr5"]["remaining_gates"])
        self.assertEqual([], payload["rulesets"]["sr5"]["spot_check_plan"])
        self.assertTrue(payload["readiness_decision"]["sr6_rule_authority_ready"])
        self.assertTrue(payload["readiness_decision"]["full_product_rule_authority_ready"])
        self.assertFalse(payload["rulesets"]["sr4"]["human_review_status"]["pending_review"])
        self.assertTrue(payload["rulesets"]["sr4"]["human_review_status"]["review_ready"])
        self.assertTrue(payload["rulesets"]["sr4"]["spot_check_plan"])
        self.assertEqual("not_applicable", payload["rulesets"]["sr4"]["suggested_errata_decision"])
        self.assertFalse(payload["rulesets"]["sr6"]["human_review_status"]["pending_review"])
        self.assertTrue(payload["rulesets"]["sr6"]["human_review_status"]["review_ready"])
        self.assertTrue(payload["rulesets"]["sr6"]["spot_check_plan"])
        self.assertEqual("applied", payload["rulesets"]["sr6"]["suggested_errata_decision"])
        self.assertIn("human_review", payload["rulesets"]["sr4"]["blocker_receipts"])
        self.assertIn("human_review", payload["rulesets"]["sr6"]["blocker_receipts"])

        findings = {finding["id"]: finding["status"] for finding in payload["audit_findings"]}
        self.assertEqual("pass", findings["rulefact_depth"])
        self.assertEqual("pass", findings["provider_class_coverage"])
        self.assertEqual("pass", findings["errata_application"])
        self.assertEqual("pass", findings["human_signoff"])
        self.assertEqual("sign_off_allowed", payload["signoff_recommendation"]["recommendation"])
        self.assertEqual("sign_off_allowed", payload["signoff_recommendation"]["sr4"])
        self.assertEqual("sign_off_allowed", payload["signoff_recommendation"]["sr5"])
        self.assertEqual("sign_off_allowed", payload["signoff_recommendation"]["sr6"])

    def test_published_operator_review_artifact_matches_ready_posture(self) -> None:
        import json

        payload = json.loads(
            (REPO_ROOT / ".codex-studio" / "published" / "CODEX_OPERATOR_RULE_AUTHORITY_REVIEW.generated.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual("operator_review_complete_authority_ready", payload["status"])
        self.assertTrue(payload["readiness_decision"]["full_product_rule_authority_ready"])
        self.assertEqual("sign_off_allowed", payload["signoff_recommendation"]["recommendation"])

    def test_full_completion_reports_ready(self) -> None:
        import importlib.util

        script_path = REPO_ROOT / "scripts" / "verify_full_rule_authority_completion.py"
        spec = importlib.util.spec_from_file_location("verify_full_rule_authority_completion", script_path)
        module = importlib.util.module_from_spec(spec)
        assert spec and spec.loader
        spec.loader.exec_module(module)

        self.assertEqual(0, module.main())
        import json

        payload = json.loads((REPO_ROOT / ".codex-studio" / "published" / "FULL_PRODUCT_RULE_AUTHORITY_COMPLETION.generated.json").read_text(encoding="utf-8"))
        self.assertEqual("pass", payload["status"])
        self.assertEqual("FULL_RULE_AUTHORITY_READY", payload["final_verdict"])
        self.assertEqual([], payload["blockers"])
        self.assertTrue(payload["rulesets"]["sr4"]["rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr5"]["rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr6"]["rule_authority_ready"])
        self.assertTrue(payload["rulesets"]["sr4"]["human_review"]["review_ready"])
        self.assertTrue(payload["rulesets"]["sr6"]["human_review"]["review_ready"])
        self.assertEqual("pass", payload["promoted_operator_receipt"]["status"])
        self.assertEqual(330, payload["promoted_operator_receipt"]["rulesets"]["sr5"]["rulefact_count"])

    def test_full_completion_rejects_promoted_receipt_without_rulefact_counts(self) -> None:
        import importlib.util

        script_path = REPO_ROOT / "scripts" / "verify_full_rule_authority_completion.py"
        spec = importlib.util.spec_from_file_location("verify_full_rule_authority_completion", script_path)
        module = importlib.util.module_from_spec(spec)
        assert spec and spec.loader
        spec.loader.exec_module(module)

        receipt = {
            "status": "pass",
            "final_verdict": "FULL_RULE_AUTHORITY_READY",
            "rulesets": [
                {"ruleset": "sr4", "status": "pass", "verdict": "SR4_RULE_AUTHORITY_READY", "rulefact_count": 449},
                {"ruleset": "sr5", "status": "pass", "verdict": "SR5_RULE_AUTHORITY_READY"},
                {"ruleset": "sr6", "status": "pass", "verdict": "SR6_RULE_AUTHORITY_READY", "rulefact_count": 447},
            ],
        }

        validation = module.validate_promoted_operator_receipt(
            receipt,
            {
                "sr4": 449,
                "sr5": 330,
                "sr6": 447,
            },
        )

        self.assertEqual("fail", validation["status"])
        self.assertIn(
            "promoted operator receipt sr5 rulefact_count is missing or not numeric",
            validation["failures"],
        )
        self.assertEqual(330, validation["rulesets"]["sr5"]["expected_rulefact_count"])


if __name__ == "__main__":
    unittest.main()
