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
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-explain-value-packets.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "EXPLAIN_VALUE_PACKETS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_explain_value_packets", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load generator from {GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


class ExplainValuePacketReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "EXPLAIN_VALUE_PACKETS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual("next90-m145-core-explain-every-value-packets", payload["package_id"])
        self.assertEqual(1451045101, payload["frontier_id"])
        self.assertEqual(145, payload["milestone_id"])
        self.assertEqual(["explain_every_value_packets", "counterfactual_explain:core"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(
            "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json",
            payload["published_receipt_path"],
        )
        self.assertEqual(6, payload["proof_anchor_count"])
        self.assertEqual(2, payload["authority_anchor_count"])
        self.assertEqual([], payload["unresolved"]["missing_files"])
        self.assertEqual({}, payload["unresolved"]["snippet_failures"])
        self.assertEqual(
            [
                "CHUMMER_CORE_ENGINE_TEST_FILTER=explain-value-packets dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false",
                "python3 tests/test_explain_value_packet_receipt.py",
                "python3 scripts/verify-explain-value-packets.py --repo-root . --out .codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json",
            ],
            payload["verification_commands"],
        )
        self.assertEqual(
            [
                "mechanical-result",
                "legality-state",
                "warning",
                "before-after-delta",
                "counterfactual",
                "source-anchor",
            ],
            payload["coverage_registry_kinds"],
        )
        self.assertEqual(["why", "why-not", "what-if"], payload["counterfactual_outcome_kinds"])
        self.assertEqual(3, payload["bounded_counterfactual_limit"])
        self.assertTrue(
            all(file_row["status"] == "passed" and file_row["digest"].startswith("sha256:") for file_row in payload["proof_files"]),
        )
        self.assertTrue(
            all(file_row["status"] == "passed" and file_row["digest"].startswith("sha256:") for file_row in payload["authority_files"]),
        )

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

        checked_in_payload = json.loads(self.output_path.read_text(encoding="utf-8"))
        regenerated_payload = self.generator.build_payload(REPO_ROOT, self.output_path)
        self.assertEqual(
            without_generated_at(regenerated_payload),
            without_generated_at(checked_in_payload),
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
