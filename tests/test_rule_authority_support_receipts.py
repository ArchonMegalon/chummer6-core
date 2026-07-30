from __future__ import annotations

import importlib.util
import json
import unittest
import tempfile
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
    def test_trx_counters_are_parsed_for_executed_fixture_receipts(self) -> None:
        module = load_module()
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "fixtures.trx"
            path.write_text(
                """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary>
    <Counters total="9" executed="9" passed="9" failed="0" notExecuted="0" />
  </ResultSummary>
  <TestDefinitions>
    <UnitTest>
      <TestMethod className="Chummer.Tests.Sr4DiceProviderTests" name="Dice_provider_counts_hits" />
    </UnitTest>
  </TestDefinitions>
</TestRun>
""",
                encoding="utf-8",
            )

            self.assertEqual(
                {"total": 9, "passed": 9, "failed": 0, "skipped": 0},
                module.parse_trx_counts(path),
            )
            self.assertEqual(["Sr4DiceProviderTests"], module.parse_trx_test_classes(path))

    def test_fixture_receipt_uses_current_execution_instead_of_stale_counts(self) -> None:
        module = load_module()
        test_classes = module.POLICY["sr4"]["fixture_test_classes"]
        receipt = module.fixture_receipt(
            "sr4",
            {
                "total": 9,
                "passed": 9,
                "failed": 0,
                "skipped": 0,
                "test_filter": "FullyQualifiedName~Sr4",
                "test_returncode": 0,
                "executed_test_classes": test_classes,
            },
        )

        self.assertEqual("core_seed_fixture_pack_passed", receipt["status"])
        self.assertEqual(9, receipt["passed"])
        self.assertEqual(0, receipt["failed"])
        self.assertTrue(receipt["coverage_complete"])

    def test_fixture_receipt_fails_closed_when_test_process_fails(self) -> None:
        module = load_module()
        test_classes = module.POLICY["sr4"]["fixture_test_classes"]
        receipt = module.fixture_receipt(
            "sr4",
            {
                "total": 9,
                "passed": 9,
                "failed": 0,
                "skipped": 0,
                "test_filter": "FullyQualifiedName~Sr4",
                "test_returncode": 1,
                "executed_test_classes": test_classes,
            },
        )

        self.assertEqual("fail", receipt["status"])

    def test_fixture_receipt_fails_closed_when_a_mapped_test_class_does_not_execute(self) -> None:
        module = load_module()
        test_classes = module.POLICY["sr4"]["fixture_test_classes"]
        receipt = module.fixture_receipt(
            "sr4",
            {
                "total": 3,
                "passed": 3,
                "failed": 0,
                "skipped": 0,
                "test_returncode": 0,
                "executed_test_classes": test_classes[:-1],
            },
        )

        self.assertEqual("fail", receipt["status"])
        self.assertFalse(receipt["coverage_complete"])

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
