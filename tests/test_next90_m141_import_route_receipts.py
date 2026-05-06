#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from typing import Any
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-next90-m141-import-route-receipts.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_next90_m141_import_route_receipts", GENERATOR_PATH)
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


def duplicate_queue_row_in_items(queue_text: str, package_row: str) -> str:
    items_marker = "\nitems:\n"
    items_start = queue_text.find(items_marker)
    if items_start < 0:
        raise AssertionError("queue fixture missing items section")

    prefix = queue_text[: items_start + len(items_marker)]
    items_body = queue_text[items_start + len(items_marker) :]
    duplicated_items_body = items_body.replace(package_row, package_row + package_row, 1)
    if duplicated_items_body == items_body:
        raise AssertionError("queue fixture missing package row inside items section")

    return prefix + duplicated_items_body


class Next90M141ImportRouteReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual(
            "next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic",
            payload["package_id"],
        )
        self.assertEqual(4304178368, payload["frontier_id"])
        self.assertEqual(2350979521, payload["flagship_frontier_id"])
        self.assertEqual(141, payload["milestone_id"])
        self.assertEqual("141.2", payload["work_task_id"])
        self.assertEqual(["bind_import_oracle_custom_data_and_amend_package_flows_t:core"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(
            "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json",
            payload["published_receipt_path"],
        )
        self.assertEqual("/api/tools/master-index", payload["master_index_route"])
        self.assertEqual("source:translator_route", payload["parity_route_id"])
        self.assertEqual(
            [
                "family:custom_data_xml_and_translator_bridge",
                "family:legacy_and_adjacent_import_oracles",
            ],
            payload["receipt_family_ids"],
        )
        self.assertEqual(
            [
                "customDataXmlBridgeDeterministicReceipt",
                "translatorDeterministicReceipt",
                "importOracleDeterministicReceipt",
                "amendPackageDeterministicReceipt",
            ],
            payload["deterministic_receipt_fields"],
        )
        self.assertEqual(["source_toggle", "amend_package"], payload["engine_proof_pack_required_suite_ids"])
        self.assertEqual(
            [
                "python3 tests/test_next90_m141_import_route_receipts.py",
                "python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check",
            ],
            payload["verification_commands"],
        )
        self.assertEqual(12, payload["proof_anchor_count"])
        self.assertEqual(3, payload["authority_anchor_count"])
        self.assertEqual(
            {
                "successor_registry": 1,
                "successor_queue": 1,
                "design_successor_queue": 1,
            },
            payload["authority_row_counts"],
        )
        self.assertEqual([], payload["unresolved"]["missing_files"])
        self.assertEqual({}, payload["unresolved"]["snippet_failures"])
        self.assertEqual({}, payload["unresolved"]["authority_row_issues"])
        self.assertEqual({}, payload["unresolved"]["supporting_receipt_semantic_issues"])
        self.assertEqual(
            ["stable-json-subset", "stable-json-subset"],
            [
                entry["digest_scope"]
                for entry in payload["proof_files"]
                if entry["key"] in {"engine_proof_pack_published", "import_parity_certification"}
            ],
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

    def test_cli_check_fails_when_checked_in_receipt_matches_failed_payload(self) -> None:
        failed_payload = self.generator.build_payload(REPO_ROOT, self.output_path)
        failed_payload["status"] = "failed"
        self.output_path.write_text(json.dumps(failed_payload, indent=2) + "\n", encoding="utf-8")

        stdout = StringIO()
        with mock.patch.object(self.generator, "build_payload", return_value=failed_payload):
            with mock.patch.object(
                sys,
                "argv",
                [
                    str(GENERATOR_PATH),
                    "--repo-root",
                    str(REPO_ROOT),
                    "--out",
                    str(self.output_path),
                    "--check",
                ],
            ):
                with redirect_stdout(stdout):
                    result = self.generator.main()

        self.assertEqual(1, result)
        self.assertIn("checked-in receipt does not pass current proof", stdout.getvalue())

    def test_authority_row_digest_ignores_unrelated_file_churn(self) -> None:
        source_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml")
        original_content = source_path.read_text(encoding="utf-8")
        variant_content = "header drift\n" + original_content + "\nfooter drift\n"

        original_scope, original_label = self.generator.extract_authority_scope("successor_registry", original_content)
        variant_scope, variant_label = self.generator.extract_authority_scope("successor_registry", variant_content)

        self.assertEqual("work-task-row", original_label)
        self.assertEqual("work-task-row", variant_label)
        self.assertEqual(original_scope, variant_scope)
        self.assertEqual(
            self.generator.sha256_digest(original_scope),
            self.generator.sha256_digest(variant_scope),
        )

    def test_authority_row_digest_ignores_sibling_registry_row_churn(self) -> None:
        source_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml")
        original_content = source_path.read_text(encoding="utf-8")
        sibling_marker = "    - id: '141.3'\n"
        sibling_replacement = (
            "    - id: '141.3'\n"
            "      owner: chummer6-hub\n"
            "      title: Keep route, support, and publication surfaces from claiming parity for these routes unless the direct proof receipts are current.\n"
            "      status: in_progress\n"
            "      evidence:\n"
            "      - sibling churn should not stale the core receipt digest.\n"
        )
        variant_content = original_content.replace(
            (
                "    - id: '141.3'\n"
                "      owner: chummer6-hub\n"
                "      title: Keep route, support, and publication surfaces from claiming parity for these routes unless the direct proof receipts are current.\n"
            ),
            sibling_replacement,
            1,
        )

        self.assertNotEqual(original_content, variant_content)

        original_scope, original_label = self.generator.extract_authority_scope("successor_registry", original_content)
        variant_scope, variant_label = self.generator.extract_authority_scope("successor_registry", variant_content)

        self.assertEqual("work-task-row", original_label)
        self.assertEqual("work-task-row", variant_label)
        self.assertEqual(original_scope, variant_scope)
        self.assertEqual(
            self.generator.sha256_digest(original_scope),
            self.generator.sha256_digest(variant_scope),
        )

    def test_build_payload_fails_closed_when_registry_has_duplicate_package_rows(self) -> None:
        registry_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml")
        registry_text = registry_path.read_text(encoding="utf-8")
        package_row = (
            "    - id: '141.2'\n"
            "      owner: chummer6-core\n"
            "      title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and workflow gates.\n"
        )
        try:
            registry_path.write_text(registry_text.replace(package_row, package_row + package_row, 1), encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(2, payload["authority_row_counts"]["successor_registry"])
            self.assertEqual({"successor_registry": 2}, payload["unresolved"]["authority_row_issues"])
        finally:
            registry_path.write_text(registry_text, encoding="utf-8")

    def test_build_payload_fails_closed_when_fleet_queue_has_duplicate_package_rows(self) -> None:
        queue_path = Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml")
        queue_text = queue_path.read_text(encoding="utf-8")
        package_row = (
            "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n"
            "    workflow gates.\n"
            "  task: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n"
            "    workflow gates.\n"
            "  package_id: next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic\n"
            "  work_task_id: '141.2'\n"
            "  frontier_id: 4304178368\n"
            "  milestone_id: 141\n"
            "  status: not_started\n"
            "  wave: W22P\n"
            "  repo: chummer6-core\n"
            "  allowed_paths:\n"
            "  - src\n"
            "  - tests\n"
            "  - docs\n"
            "  - scripts\n"
            "  owned_surfaces:\n"
            "  - bind_import_oracle_custom_data_and_amend_package_flows_t:core\n"
        )
        try:
            queue_path.write_text(duplicate_queue_row_in_items(queue_text, package_row), encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(2, payload["authority_row_counts"]["successor_queue"])
            self.assertEqual({"successor_queue": 2}, payload["unresolved"]["authority_row_issues"])
        finally:
            queue_path.write_text(queue_text, encoding="utf-8")

    def test_build_payload_fails_closed_when_design_queue_has_duplicate_package_rows(self) -> None:
        queue_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml")
        queue_text = queue_path.read_text(encoding="utf-8")
        package_row = (
            "- title: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n"
            "    workflow gates.\n"
            "  task: Bind import-oracle, custom-data, and amend-package flows to deterministic receipts that can be cited by parity and\n"
            "    workflow gates.\n"
            "  package_id: next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic\n"
            "  work_task_id: '141.2'\n"
            "  frontier_id: 4304178368\n"
            "  milestone_id: 141\n"
            "  status: not_started\n"
            "  wave: W22P\n"
            "  repo: chummer6-core\n"
            "  allowed_paths:\n"
            "  - src\n"
            "  - tests\n"
            "  - docs\n"
            "  - scripts\n"
            "  owned_surfaces:\n"
            "  - bind_import_oracle_custom_data_and_amend_package_flows_t:core\n"
        )
        try:
            queue_path.write_text(duplicate_queue_row_in_items(queue_text, package_row), encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(2, payload["authority_row_counts"]["design_successor_queue"])
            self.assertEqual({"design_successor_queue": 2}, payload["unresolved"]["authority_row_issues"])
        finally:
            queue_path.write_text(queue_text, encoding="utf-8")

    def test_build_payload_fails_closed_when_engine_proof_pack_semantics_drift(self) -> None:
        proof_pack_path = REPO_ROOT / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"
        original_payload = json.loads(proof_pack_path.read_text(encoding="utf-8"))
        mutated_payload = json.loads(json.dumps(original_payload))
        for suite in mutated_payload["oracle_suites"]:
            if suite.get("id") == "amend_package":
                suite["coverage_focus"] = "unexpected_drift"
                break
        else:
            raise AssertionError("missing amend_package suite fixture")

        try:
            proof_pack_path.write_text(json.dumps(mutated_payload, indent=2) + "\n", encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(
                {"engine_proof_pack_published": ["unexpected_amend_coverage_focus:unexpected_drift"]},
                payload["unresolved"]["supporting_receipt_semantic_issues"],
            )
        finally:
            proof_pack_path.write_text(json.dumps(original_payload, indent=2) + "\n", encoding="utf-8")

    def test_build_payload_fails_closed_when_import_parity_receipt_semantics_drift(self) -> None:
        parity_path = REPO_ROOT / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        original_payload = json.loads(parity_path.read_text(encoding="utf-8"))
        mutated_payload = json.loads(json.dumps(original_payload))
        mutated_payload["adjacent_oracles"] = [{"name": "Genesis", "sources_covered": 1, "sources_expected": 1}]
        mutated_payload["coverage"]["coverage_percent"] = 80

        try:
            parity_path.write_text(json.dumps(mutated_payload, indent=2) + "\n", encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(
                {
                    "import_parity_certification": [
                        "unexpected_adjacent_oracles:Genesis",
                        "unexpected_coverage_percent:80",
                    ]
                },
                payload["unresolved"]["supporting_receipt_semantic_issues"],
            )
        finally:
            parity_path.write_text(json.dumps(original_payload, indent=2) + "\n", encoding="utf-8")

    def test_build_payload_fails_closed_when_import_parity_receipt_loses_per_oracle_coverage(self) -> None:
        parity_path = REPO_ROOT / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        original_payload = json.loads(parity_path.read_text(encoding="utf-8"))
        mutated_payload = json.loads(json.dumps(original_payload))
        mutated_payload["import_oracles"][0]["sources_covered"] = 0
        mutated_payload["adjacent_oracles"][1]["sources_expected"] = 2

        try:
            parity_path.write_text(json.dumps(mutated_payload, indent=2) + "\n", encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(
                {
                    "import_parity_certification": [
                        "unexpected_import_oracle_counts:Chummer4:0:1",
                        "unexpected_adjacent_oracle_counts:CommLink6:1:2",
                    ]
                },
                payload["unresolved"]["supporting_receipt_semantic_issues"],
            )
        finally:
            parity_path.write_text(json.dumps(original_payload, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
