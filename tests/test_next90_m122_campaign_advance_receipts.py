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
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-next90-m122-campaign-advance-receipts.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_next90_m122_campaign_advance_receipts", GENERATOR_PATH)
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

    prefix = queue_text[:items_start + len(items_marker)]
    items_body = queue_text[items_start + len(items_marker):]
    duplicated_items_body = items_body.replace(package_row, package_row + package_row, 1)
    if duplicated_items_body == items_body:
        raise AssertionError("queue fixture missing package row inside items section")

    return prefix + duplicated_items_body


class Next90M122CampaignAdvanceReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual("next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq", payload["package_id"])
        self.assertEqual(1771239378, payload["frontier_id"])
        self.assertEqual(122, payload["milestone_id"])
        self.assertEqual("122.2", payload["work_task_id"])
        self.assertEqual(["add_deterministic_reward_downtime_goal:core"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(
            "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json",
            payload["published_receipt_path"],
        )
        self.assertEqual(
            [
                "CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
                "python3 tests/test_next90_m122_campaign_advance_receipts.py",
                "python3 scripts/verify-next90-m122-campaign-advance-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json",
            ],
            payload["verification_commands"],
        )
        self.assertEqual("family:campaign_adoption_runner_goal_and_black_ledger", payload["receipt_family"])
        self.assertEqual("world-tick:campaign-7:rr-2048", payload["world_tick_id"])
        self.assertEqual("news-item:shadowfeed-2048:rr-2048", payload["news_item_id"])
        self.assertEqual("next90-m122-campaign-advance-receipts", payload["test_filter"])
        self.assertEqual(9, payload["proof_anchor_count"])
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
        self.assertEqual(
            ["full-file"] * 9,
            [entry["digest_scope"] for entry in payload["proof_files"]],
        )
        self.assertEqual(
            ["milestone-row", "package-rows", "package-rows"],
            [entry["digest_scope"] for entry in payload["authority_files"]],
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

    def test_authority_row_digest_ignores_unrelated_file_churn(self) -> None:
        source_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml")
        original_content = source_path.read_text(encoding="utf-8")
        variant_content = "header drift\n" + original_content + "\nfooter drift\n"

        original_scope, original_label = self.generator.extract_authority_scope("successor_registry", original_content)
        variant_scope, variant_label = self.generator.extract_authority_scope("successor_registry", variant_content)

        self.assertEqual("milestone-row", original_label)
        self.assertEqual("milestone-row", variant_label)
        self.assertEqual(original_scope, variant_scope)
        self.assertEqual(
            self.generator.sha256_digest(original_scope),
            self.generator.sha256_digest(variant_scope),
        )

    def test_build_payload_fails_closed_when_fleet_queue_has_duplicate_package_rows(self) -> None:
        queue_path = Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml")
        queue_text = queue_path.read_text(encoding="utf-8")
        package_row = (
            "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n"
            "    LEDGER flows.\n"
            "  task: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n"
            "    LEDGER flows.\n"
            "  package_id: next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq\n"
            "  work_task_id: '122.2'\n"
            "  frontier_id: 1771239378\n"
            "  milestone_id: 122\n"
            "  status: not_started\n"
            "  wave: W15\n"
            "  repo: chummer6-core\n"
            "  allowed_paths:\n"
            "  - src\n"
            "  - tests\n"
            "  - docs\n"
            "  - scripts\n"
            "  owned_surfaces:\n"
            "  - add_deterministic_reward_downtime_goal:core\n"
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
            "- title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n"
            "    LEDGER flows.\n"
            "  task: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n"
            "    LEDGER flows.\n"
            "  package_id: next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq\n"
            "  work_task_id: '122.2'\n"
            "  frontier_id: 1771239378\n"
            "  milestone_id: 122\n"
            "  status: not_started\n"
            "  wave: W15\n"
            "  repo: chummer6-core\n"
            "  allowed_paths:\n"
            "  - src\n"
            "  - tests\n"
            "  - docs\n"
            "  - scripts\n"
            "  owned_surfaces:\n"
            "  - add_deterministic_reward_downtime_goal:core\n"
        )
        try:
            queue_path.write_text(duplicate_queue_row_in_items(queue_text, package_row), encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(2, payload["authority_row_counts"]["design_successor_queue"])
            self.assertEqual({"design_successor_queue": 2}, payload["unresolved"]["authority_row_issues"])
        finally:
            queue_path.write_text(queue_text, encoding="utf-8")

    def test_build_payload_fails_closed_when_registry_has_duplicate_work_task_rows(self) -> None:
        registry_path = Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml")
        registry_text = registry_path.read_text(encoding="utf-8")
        duplicate_row = (
            "    - id: '122.2'\n"
            "      owner: chummer6-core\n"
            "      title: Add deterministic reward, downtime, goal-update, and consequence receipt contracts consumed by adoption and BLACK\n"
            "        LEDGER flows.\n"
        )
        try:
            registry_path.write_text(registry_text.replace("    - id: '122.3'\n", duplicate_row + "    - id: '122.3'\n"), encoding="utf-8")

            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

            self.assertEqual("failed", payload["status"])
            self.assertEqual(2, payload["authority_row_counts"]["successor_registry"])
            self.assertEqual({"successor_registry": 2}, payload["unresolved"]["authority_row_issues"])
        finally:
            registry_path.write_text(registry_text, encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
