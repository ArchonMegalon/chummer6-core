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


PACKAGE_ID = "next90-m115-core-exchange-contracts"
REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-next90-m115-core-exchange-contracts.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_next90_m115_core_exchange_contracts", GENERATOR_PATH)
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


class Next90M115CoreExchangeContractsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual(PACKAGE_ID, payload["package_id"])
        self.assertEqual(2537363316, payload["frontier_id"])
        self.assertEqual(115, payload["milestone_id"])
        self.assertEqual("115.1", payload["work_task_id"])
        self.assertEqual(["portable_dossier_contracts", "replay_recap_exchange_contracts"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(
            [
                "portable-dossier",
                "campaign-bundle",
                "replay-timeline",
                "session-recap",
                "external-exchange",
            ],
            payload["covered_output_kinds"],
        )
        self.assertEqual("complete", payload["authority_expectations"]["registry_status"])
        self.assertEqual("complete", payload["authority_expectations"]["queue_status"])
        self.assertEqual("complete", payload["authority_expectations"]["design_queue_status"])
        self.assertEqual([], payload["unresolved"]["missing_files"])
        self.assertEqual({}, payload["unresolved"]["snippet_failures"])
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
        stale_payload = self.generator.build_payload(REPO_ROOT, self.output_path)
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
