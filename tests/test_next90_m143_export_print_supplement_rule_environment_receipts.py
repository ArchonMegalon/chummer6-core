#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-next90-m143-export-print-supplement-rule-environment-receipts.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json"
PUBLISHED_RECEIPT_PATH = "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_next90_m143_export_print_supplement_rule_environment_receipts", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load verifier from {GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


class Next90M143ExportPrintSupplementRuleEnvironmentReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual(
            "next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi",
            payload["package_id"],
        )
        self.assertEqual(2778308338, payload["frontier_id"])
        self.assertEqual(143, payload["milestone_id"])
        self.assertEqual("143.2", payload["work_task_id"])
        self.assertEqual(["keep_export_print_supplement_and_rule_environment_receip:core"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(PUBLISHED_RECEIPT_PATH, payload["published_receipt_path"])
        self.assertEqual(
            [
                "family:sheet_export_print_viewer_and_exchange",
                "family:sr6_supplements_designers_and_house_rules",
            ],
            payload["parity_family_ids"],
        )
        self.assertEqual(
            [
                "menu:open_for_printing",
                "menu:open_for_export",
                "menu:file_print_multiple",
            ],
            payload["output_route_ids"],
        )
        self.assertEqual(
            [
                "workflow:sr6_supplements",
                "workflow:house_rules",
            ],
            payload["supplement_route_ids"],
        )
        self.assertEqual(
            [
                "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
                "python3 tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py",
                "python3 scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json",
            ],
            payload["verification_commands"],
        )
        self.assertEqual("parity-m143", payload["test_filter"])
        self.assertEqual(10, payload["proof_anchor_count"])
        self.assertEqual(3, payload["authority_anchor_count"])
        self.assertEqual(
            {
                "successor_registry": 1,
                "successor_queue": 1,
                "design_successor_queue": 1,
            },
            payload["authority_row_counts"],
        )
        self.assertEqual(
            {
                "successor_registry": 1,
                "successor_queue": 1,
                "design_successor_queue": 1,
            },
            payload["expected_authority_row_counts"],
        )
        self.assertEqual([], payload["unresolved"]["missing_files"])
        self.assertEqual({}, payload["unresolved"]["snippet_failures"])
        self.assertEqual({}, payload["unresolved"]["authority_row_issues"])

    def test_checked_in_receipt_matches_regenerated_payload(self) -> None:
        checked_in_payload = json.loads(CHECKED_IN_RECEIPT_PATH.read_text(encoding="utf-8"))
        regenerated_payload = self.generator.build_payload(REPO_ROOT, CHECKED_IN_RECEIPT_PATH)

        self.assertEqual(
            without_generated_at(regenerated_payload),
            without_generated_at(checked_in_payload),
        )

    def test_cli_writes_and_rechecks_receipt(self) -> None:
        result = subprocess.run(
            [
                sys.executable,
                str(GENERATOR_PATH),
                "--repo-root",
                str(REPO_ROOT),
                "--out",
                str(self.output_path),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )

        self.assertEqual("", result.stderr)
        self.assertEqual(0, result.returncode)
        self.assertIn(str(self.output_path), result.stdout)

        written_payload = json.loads(self.output_path.read_text(encoding="utf-8"))
        regenerated_payload = self.generator.build_payload(REPO_ROOT, self.output_path)
        self.assertEqual(
            without_generated_at(regenerated_payload),
            without_generated_at(written_payload),
        )

        check_result = subprocess.run(
            [
                sys.executable,
                str(GENERATOR_PATH),
                "--repo-root",
                str(REPO_ROOT),
                "--out",
                str(self.output_path),
                "--check",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )

        self.assertEqual("", check_result.stderr)
        self.assertEqual(0, check_result.returncode)
        self.assertIn(str(self.output_path), check_result.stdout)

    def test_cli_check_fails_when_checked_in_receipt_is_stale(self) -> None:
        stale_payload = self.generator.build_payload(REPO_ROOT, CHECKED_IN_RECEIPT_PATH)
        stale_payload["status"] = "failed"
        self.output_path.write_text(json.dumps(stale_payload, indent=2) + "\n", encoding="utf-8")

        check_result = subprocess.run(
            [
                sys.executable,
                str(GENERATOR_PATH),
                "--repo-root",
                str(REPO_ROOT),
                "--out",
                str(self.output_path),
                "--check",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            check=False,
        )

        self.assertEqual("", check_result.stderr)
        self.assertEqual(1, check_result.returncode)
        self.assertIn("checked-in receipt is stale", check_result.stdout)


if __name__ == "__main__":
    unittest.main()
