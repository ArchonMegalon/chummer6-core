from __future__ import annotations

import importlib.util
import io
import json
import unittest
from pathlib import Path
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts" / "verify_rule_authority_human_review.py"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_rule_authority_human_review", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


class RuleAuthorityHumanReviewTests(unittest.TestCase):
    def test_current_reviews_are_structured_approved_and_ready(self) -> None:
        module = load_module()
        sr4 = module.validate_review("sr4")
        sr6 = module.validate_review("sr6")

        self.assertEqual("pass", sr4["status"])
        self.assertFalse(sr4["pending_review"])
        self.assertTrue(sr4["review_ready"])
        self.assertEqual("user_directive_human_side_gold_assumption_2026-06-12", sr4["fields"]["Reviewer"])
        self.assertEqual("applied", sr4["fields"]["Errata decision"])
        self.assertEqual("true", sr4["fields"]["Ready token approved"])

        self.assertEqual("pass", sr6["status"])
        self.assertFalse(sr6["pending_review"])
        self.assertTrue(sr6["review_ready"])
        self.assertEqual("user_directive_human_side_gold_assumption_2026-06-12", sr6["fields"]["Reviewer"])
        self.assertEqual("applied", sr6["fields"]["Errata decision"])
        self.assertEqual("true", sr6["fields"]["Ready token approved"])

    def test_approved_review_requires_utc_timestamp(self) -> None:
        module = load_module()
        fields = {
            "Status": "approved",
            "Row-level decision": "approved",
            "Errata decision": "applied",
            "Reviewer": "Rules Reviewer",
            "Review timestamp": "2026-06-11 09:30:00",
            "Ready token approved": "true",
        }

        result = module.validate_fields("sr6", fields)

        self.assertEqual("fail", result["status"])
        self.assertFalse(result["review_ready"])

    def test_deferred_errata_approval_requires_rationale(self) -> None:
        module = load_module()
        fields = {
            "Status": "approved",
            "Row-level decision": "approved",
            "Errata decision": "defer",
            "Reviewer": "Rules Reviewer",
            "Review timestamp": "2026-06-11T09:30:00Z",
            "Ready token approved": "true",
        }

        missing_rationale = module.validate_fields("sr6", fields)
        fields["Errata defer rationale"] = "Reviewed source does not apply to selected core edition baseline."
        with_rationale = module.validate_fields("sr6", fields)

        self.assertEqual("fail", missing_rationale["status"])
        self.assertFalse(missing_rationale["review_ready"])
        self.assertEqual("pass", with_rationale["status"])
        self.assertTrue(with_rationale["review_ready"])

    def test_source_baseline_is_required_only_when_flagged(self) -> None:
        module = load_module()
        fields = {
            "Status": "approved",
            "Row-level decision": "approved",
            "Errata decision": "applied",
            "Reviewer": "Rules Reviewer",
            "Review timestamp": "2026-06-11T09:30:00Z",
            "Ready token approved": "true",
        }

        without_required_baseline = module.validate_fields("sr4", fields, source_baseline_required=False)
        missing_required_baseline = module.validate_fields("sr6", fields, source_baseline_required=True)
        fields["Source baseline decision"] = "sr6_core_2024_plus_reviewed_supplement_defer"
        with_required_baseline = module.validate_fields("sr6", fields, source_baseline_required=True)

        self.assertEqual("pass", without_required_baseline["status"])
        self.assertTrue(without_required_baseline["review_ready"])
        self.assertEqual("fail", missing_required_baseline["status"])
        self.assertFalse(missing_required_baseline["review_ready"])
        self.assertEqual("pass", with_required_baseline["status"])
        self.assertTrue(with_required_baseline["review_ready"])

    def test_cli_require_ready_accepts_current_approved_reviews(self) -> None:
        module = load_module()
        stdout = io.StringIO()

        with mock.patch("sys.stdout", stdout):
            status = module.main(["--require-ready", "sr4", "sr6"])

        payload = json.loads(stdout.getvalue())
        self.assertEqual(0, status)
        self.assertTrue(payload["require_ready"])
        self.assertEqual("pass", payload["status"])
        self.assertTrue(all(review["status"] == "pass" for review in payload["reviews"]))
        self.assertTrue(all(review["review_ready"] is True for review in payload["reviews"]))

    def test_cli_default_accepts_structured_pending_reviews(self) -> None:
        module = load_module()
        stdout = io.StringIO()

        with mock.patch("sys.stdout", stdout):
            status = module.main(["sr4", "sr6"])

        payload = json.loads(stdout.getvalue())
        self.assertEqual(0, status)
        self.assertFalse(payload["require_ready"])
        self.assertEqual("pass", payload["status"])


if __name__ == "__main__":
    unittest.main()
