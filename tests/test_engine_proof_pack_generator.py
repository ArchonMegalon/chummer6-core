#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from unittest import mock
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "generate-engine-proof-pack.py"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("generate_engine_proof_pack", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load generator from {GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


class EngineProofPackReceiptReproducibilityTests(unittest.TestCase):
    def test_checked_in_engine_proof_pack_matches_generator_except_timestamp(self) -> None:
        generator = load_generator()
        receipt_path = REPO_ROOT / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"
        checked_in_payload = json.loads(receipt_path.read_text(encoding="utf-8"))

        regenerated_payload = generator.build_payload(REPO_ROOT, receipt_path)

        self.assertEqual(
            without_generated_at(regenerated_payload),
            without_generated_at(checked_in_payload),
            "Checked-in ENGINE_PROOF_PACK.generated.json should be reproducible from the generator except generated_at.",
        )


class EngineProofPackGeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        self.output_path = self.root / ".codex-studio" / "published" / "ENGINE_PROOF_PACK.generated.json"
        self._seed_passing_repo()

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_passes_with_all_required_oracles_budgets_and_successor_metadata(self) -> None:
        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual("next90-m104-core-proof-pack", payload["package_id"])
        self.assertEqual(104, payload["milestone_id"])
        self.assertEqual("next_90_day_product_advance", payload["successor_wave_package"]["program_wave"])
        self.assertEqual(["engine_proof_pack", "import_oracle_discipline"], payload["successor_wave_package"]["owned_surfaces"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertEqual("skipped", payload["local_commit_proofs"]["status"])
        self.assertEqual([], payload["unresolved"]["local_commit_proofs"])
        self.assertIn(
            "56048971",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "769e7259",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d4b3b0ba",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a2173476",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "dafc1205",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "65df3894",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "4a56911d",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "4b124997",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "2187db33",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b488d109",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "b6fddf74",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "3b9a29c2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "f6608678",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "a3cbb548",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "df0527b2",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "8574f63f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "6b3a662c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "3b63478f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "31c75c02",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "ef46554c",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "0771b7ea",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "fdb6a273",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "d2ee91a9",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "cd30503f",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e10f2739",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "e7d4270e",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "bbc877d7",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertIn(
            "56ff7283",
            [row["commit"] for row in payload["local_commit_proofs"]["required_commits"]],
        )
        self.assertEqual("complete", payload["successor_wave_authority"]["closure_requirements"]["status"])
        self.assertEqual(3227666051, payload["successor_wave_authority"]["closure_requirements"]["frontier_id"])
        self.assertEqual("00800059", payload["successor_wave_authority"]["closure_requirements"]["landed_commit"])
        self.assertEqual("passed", payload["successor_wave_authority"]["status"])
        self.assertEqual([], payload["unresolved"]["oracle_suites"])
        self.assertEqual([], payload["unresolved"]["performance_budgets"])
        self.assertEqual([], payload["unresolved"]["release_commands"])
        self.assertEqual([], payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([], payload["unresolved"]["release_channel_binding"])
        self.assertEqual([], payload["unresolved"]["import_oracle_discipline"])
        self.assertEqual("passed", payload["release_channel_binding"]["status"])
        self.assertEqual("docker", payload["release_channel_binding"]["channel_id"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["successor_wave_authority"]["queue_allowed_paths"])
        self.assertEqual([], payload["successor_wave_authority"]["unexpected_queue_allowed_paths"])
        self.assertEqual(
            ["engine_proof_pack", "import_oracle_discipline"],
            payload["successor_wave_authority"]["queue_owned_surfaces"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["unexpected_queue_owned_surfaces"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_missing_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_missing_proof_anchors"])
        self.assertEqual([], payload["successor_wave_authority"]["off_package_queue_proof_anchors"])
        self.assertEqual([], payload["successor_wave_authority"]["design_queue_off_package_proof_anchors"])
        self.assertEqual({}, payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"])
        self.assertEqual([], payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["successor_wave_authority"]["design_queue_allowed_paths"])
        self.assertEqual(
            ["engine_proof_pack", "import_oracle_discipline"],
            payload["successor_wave_authority"]["design_queue_owned_surfaces"],
        )
        self.assertEqual(
            [
                "avalonia:linux:linux-x64",
                "avalonia:windows:win-x64",
                "avalonia:macos:osx-arm64",
            ],
            payload["release_channel_binding"]["required_promoted_desktop_tuples"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_a_suite_evidence_symbol_is_missing(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Program.cs").write_text("wrong symbol\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("creation", payload["unresolved"]["oracle_suites"])
        self.assertIn("advancement", payload["unresolved"]["oracle_suites"])

    def test_build_payload_fails_closed_when_budget_workload_is_not_executable(self) -> None:
        source_path = self.root / "Chummer.Benchmarks" / "MigrationWorkspaceBenchmarks.cs"
        source_path.write_text(source_path.read_text(encoding="utf-8").replace("runtime.explain.trace", "runtime.trace"), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("explain", payload["unresolved"]["performance_budgets"])
        explain_budget = next(row for row in payload["performance_budgets"] if row["id"] == "explain")
        self.assertTrue(explain_budget["missing_executable_workload"])

    def test_build_payload_fails_closed_when_adjacent_import_oracle_is_missing(self) -> None:
        cert_path = self.root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
        cert = json.loads(cert_path.read_text(encoding="utf-8"))
        cert["adjacent_oracles"] = ["Genesis"]
        cert_path.write_text(json.dumps(cert), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn("missing_adjacent_oracle:CommLink6", payload["unresolved"]["import_oracle_discipline"])

    def test_build_payload_fails_closed_when_release_channel_loses_promoted_tuple(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["desktopTupleCoverage"]["desktopRouteTruth"] = [
            row
            for row in release_payload["desktopTupleCoverage"]["desktopRouteTruth"]
            if row["tupleId"] != "avalonia:windows:win-x64"
        ]
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:windows:win-x64",
            payload["unresolved"]["release_channel_binding"],
        )

    def test_build_payload_fails_closed_when_release_channel_primary_tuple_is_not_primary(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        for row in release_payload["desktopTupleCoverage"]["desktopRouteTruth"]:
            if row["tupleId"] == "avalonia:linux:linux-x64":
                row["routeRole"] = "fallback"
                row["parityPosture"] = "explicit_fallback"
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:linux:linux-x64",
            payload["unresolved"]["release_channel_binding"],
        )
        linux_tuple = next(
            row
            for row in payload["release_channel_binding"]["promoted_primary_tuples"]
            if row["tuple_id"] == "avalonia:linux:linux-x64"
        )
        self.assertIn("routeRole:fallback", linux_tuple["unresolved"])
        self.assertIn("parityPosture:explicit_fallback", linux_tuple["unresolved"])

    def test_build_payload_fails_closed_when_release_channel_artifact_is_not_on_shelf(self) -> None:
        release_path = self.generator.RELEASE_CHANNEL_PATH
        release_payload = json.loads(release_path.read_text(encoding="utf-8"))
        release_payload["artifacts"] = [
            row
            for row in release_payload["artifacts"]
            if row["artifactId"] != "avalonia-osx-arm64-installer"
        ]
        release_path.write_text(json.dumps(release_payload), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIn(
            "required_promoted_tuple:avalonia:macos:osx-arm64",
            payload["unresolved"]["release_channel_binding"],
        )
        macos_tuple = next(
            row
            for row in payload["release_channel_binding"]["promoted_primary_tuples"]
            if row["tuple_id"] == "avalonia:macos:osx-arm64"
        )
        self.assertIn("artifact_not_on_shelf:avalonia-osx-arm64-installer", macos_tuple["unresolved"])

    def test_build_payload_fails_closed_when_successor_queue_loses_package_authority(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_path.write_text("package_id: different-package\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "package_id: next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["missing_queue_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_token_only_exists_on_another_item(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace("    status: complete\n", "    status: in_progress\n")
        queue_text += "\n  - package_id: different-package\n    status: complete\n"
        queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("status: complete", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_completion_status(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    status: complete\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("status: complete", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_frontier_id(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    frontier_id: 3227666051\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("frontier_id: 3227666051", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_landed_commit(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("    landed_commit: 00800059\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("landed_commit: 00800059", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_loses_allowed_path_authority(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(queue_text.replace("      - scripts\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("- scripts", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_adds_unassigned_allowed_path(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace("      - scripts\n", "      - scripts\n      - Chummer.Core\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["Chummer.Core"], payload["successor_wave_authority"]["unexpected_queue_allowed_paths"])
        self.assertIn("unexpected_allowed_path:Chummer.Core", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_adds_unassigned_owned_surface(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace("      - import_oracle_discipline\n", "      - import_oracle_discipline\n      - desktop_ui_receipts\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["desktop_ui_receipts"], payload["successor_wave_authority"]["unexpected_queue_owned_surfaces"])
        self.assertIn("unexpected_owned_surface:desktop_ui_receipts", payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_registry_token_only_exists_on_another_milestone(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_text = registry_text.replace("        owner: chummer6-core\n", "        owner: chummer6-ui\n")
        registry_text += "\n  - id: 105\n    work_tasks:\n      - id: 105.1\n        owner: chummer6-core\n"
        registry_path.write_text(registry_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("104.1:owner: chummer6-core", payload["successor_wave_authority"]["missing_registry_tokens"])
        self.assertIn("104.2:owner: chummer6-core", payload["successor_wave_authority"]["missing_registry_tokens"])

    def test_build_payload_fails_closed_when_registry_task_completion_token_only_exists_on_later_task(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_text = registry_text.replace("        status: complete\n", "", 1)
        registry_text += (
            "      - id: 104.3\n"
            "        owner: chummer6-core\n"
            "        status: complete\n"
            "        evidence:\n"
            "          - required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package\n"
            "          - python3 tests/test_engine_proof_pack_generator.py exits 0\n"
        )
        registry_path.write_text(registry_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertIn("104.1:status: complete", payload["successor_wave_authority"]["missing_registry_tokens"])
        self.assertEqual(
            ["status: complete"],
            payload["successor_wave_authority"]["missing_registry_task_tokens"]["104.1"],
        )

    def test_build_payload_fails_closed_when_successor_registry_task_cites_active_run_proof(self) -> None:
        registry_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
        registry_text = registry_path.read_text(encoding="utf-8")
        registry_path.write_text(
            registry_text.replace(
                "          - successor_wave_authority=passed\n",
                "          - successor_wave_authority=passed\n"
                "          - operator telemetry transcript\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_registry", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            {"104.2": ["operator telemetry"]},
            payload["successor_wave_authority"]["disallowed_registry_active_run_tokens"],
        )
        self.assertIn(
            "104.2:disallowed_active_run_proof:operator telemetry",
            payload["successor_wave_authority"]["missing_registry_tokens"],
        )

    def test_build_payload_fails_closed_when_successor_queue_loses_proof_anchor(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        proof_anchor = "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md"
        queue_path.write_text(queue_text.replace(f"      - {proof_anchor}\n", ""), encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(proof_anchor, payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_proof_anchor_does_not_resolve(self) -> None:
        missing_anchor = str(self.root / "docs" / "missing-engine-proof-pack.md")
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        original_tokens = self.generator.SUCCESSOR_QUEUE_TOKENS
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = (missing_anchor,)
        self.generator.SUCCESSOR_QUEUE_TOKENS = original_tokens + (missing_anchor,)
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {missing_anchor}\n",
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors
            self.generator.SUCCESSOR_QUEUE_TOKENS = original_tokens

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([missing_anchor], payload["successor_wave_authority"]["missing_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_proof_anchor_only_exists_on_later_item(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        proof_anchor = "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md"
        queue_text = queue_text.replace(f"      - {proof_anchor}\n", "")
        queue_text += (
            "\n"
            "  - title: Later unrelated package\n"
            "    package_id: different-package\n"
            "    proof:\n"
            f"      - {proof_anchor}\n"
        )
        queue_path.write_text(queue_text, encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(proof_anchor, payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_successor_queue_anchor_points_to_sibling_package_repo(self) -> None:
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors + (off_package_anchor,)
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {off_package_anchor}\n",
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual([off_package_anchor], payload["successor_wave_authority"]["off_package_queue_proof_anchors"])

    def test_build_payload_fails_closed_when_successor_queue_cites_active_run_proof(self) -> None:
        queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
        queue_text = queue_path.read_text(encoding="utf-8")
        queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - /var/lib/codex-fleet/chummer_design_supervisor/shard-4/ACTIVE_RUN_HANDOFF.generated.md\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["ACTIVE_RUN_HANDOFF"], payload["successor_wave_authority"]["disallowed_queue_active_run_tokens"])

    def test_build_payload_fails_closed_when_design_queue_loses_package_authority(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        design_queue_path.write_text("package_id: different-package\n", encoding="utf-8")

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertIn(
            "package_id: next90-m104-core-proof-pack",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_anchor_points_to_sibling_package_repo(self) -> None:
        original_anchors = self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS
        off_package_anchor = "/docker/chummercomplete/chummer6-ui-finish/scripts/ai/milestones/next90-m104-ui-explain-receipts-check.sh"
        self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors + (off_package_anchor,)
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        queue_text = queue_text.replace(
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
            "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
            f"      - {off_package_anchor}\n",
        )
        design_queue_path.write_text(queue_text, encoding="utf-8")
        try:
            payload = self.generator.build_payload(self.root, self.output_path)
        finally:
            self.generator.SUCCESSOR_QUEUE_PROOF_ANCHORS = original_anchors

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            [off_package_anchor],
            payload["successor_wave_authority"]["design_queue_off_package_proof_anchors"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_cites_active_run_proof(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace(
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n",
                "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md\n"
                "      - active-run telemetry helper output\n",
            ),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(
            ["active-run telemetry"],
            payload["successor_wave_authority"]["disallowed_design_queue_active_run_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_unassigned_allowed_path(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace("      - scripts\n", "      - scripts\n      - Chummer.Core\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["Chummer.Core"], payload["successor_wave_authority"]["unexpected_design_queue_allowed_paths"])
        self.assertIn(
            "unexpected_allowed_path:Chummer.Core",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_design_queue_adds_unassigned_owned_surface(self) -> None:
        design_queue_path = Path(self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])
        queue_text = design_queue_path.read_text(encoding="utf-8")
        design_queue_path.write_text(
            queue_text.replace("      - import_oracle_discipline\n", "      - import_oracle_discipline\n      - desktop_ui_receipts\n"),
            encoding="utf-8",
        )

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["successor_wave_authority"]["status"])
        self.assertIn("source_design_queue", payload["unresolved"]["successor_wave_authority"])
        self.assertEqual(["desktop_ui_receipts"], payload["successor_wave_authority"]["unexpected_design_queue_owned_surfaces"])
        self.assertIn(
            "unexpected_owned_surface:desktop_ui_receipts",
            payload["successor_wave_authority"]["design_queue_missing_tokens"],
        )
        self.assertEqual([], payload["successor_wave_authority"]["missing_queue_tokens"])

    def test_build_payload_fails_closed_when_required_local_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("56048971") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("56048971", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["56048971"])

    def test_build_payload_fails_closed_when_latest_package_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("769e7259") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("769e7259", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["769e7259"])

    def test_build_payload_fails_closed_when_current_package_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d4b3b0ba") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d4b3b0ba", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d4b3b0ba"])

    def test_build_payload_fails_closed_when_latest_current_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a2173476") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a2173476", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a2173476"])

    def test_build_payload_fails_closed_when_active_run_proof_hygiene_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("dafc1205") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("dafc1205", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["dafc1205"])

    def test_build_payload_fails_closed_when_latest_active_run_proof_hygiene_guard_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("65df3894") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("65df3894", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["65df3894"])

    def test_build_payload_fails_closed_when_active_run_hygiene_guard_binding_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("4a56911d") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("4a56911d", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["4a56911d"])

    def test_build_payload_fails_closed_when_current_proof_pack_guard_binding_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("4b124997") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("4b124997", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["4b124997"])

    def test_build_payload_fails_closed_when_latest_proof_pack_guard_authority_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("2187db33") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("2187db33", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["2187db33"])

    def test_build_payload_fails_closed_when_current_m104_proof_pack_authority_commit_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b488d109") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b488d109", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b488d109"])

    def test_build_payload_fails_closed_when_latest_m104_proof_pack_authority_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("b6fddf74") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("b6fddf74", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["b6fddf74"])

    def test_build_payload_fails_closed_when_latest_m104_local_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("3b9a29c2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("3b9a29c2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["3b9a29c2"])

    def test_build_payload_fails_closed_when_current_m104_local_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("f6608678") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("f6608678", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["f6608678"])

    def test_build_payload_fails_closed_when_latest_m104_receipt_refresh_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("a3cbb548") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("a3cbb548", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["a3cbb548"])

    def test_build_payload_fails_closed_when_latest_m104_receipt_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("df0527b2") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("df0527b2", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["df0527b2"])

    def test_build_payload_fails_closed_when_current_m104_receipt_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("8574f63f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("8574f63f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["8574f63f"])

    def test_build_payload_fails_closed_when_current_m104_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("6b3a662c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("6b3a662c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["6b3a662c"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("3b63478f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("3b63478f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["3b63478f"])

    def test_build_payload_fails_closed_when_latest_m104_closed_package_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("31c75c02") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("31c75c02", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["31c75c02"])

    def test_build_payload_fails_closed_when_current_m104_closed_package_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("ef46554c") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("ef46554c", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["ef46554c"])

    def test_build_payload_fails_closed_when_latest_m104_guard_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("0771b7ea") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("0771b7ea", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["0771b7ea"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("fdb6a273") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("fdb6a273", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["fdb6a273"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("d2ee91a9") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("d2ee91a9", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["d2ee91a9"])

    def test_build_payload_fails_closed_when_current_m104_engine_proof_floor_queue_citation_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("cd30503f") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("cd30503f", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["cd30503f"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e10f2739") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e10f2739", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e10f2739"])

    def test_build_payload_fails_closed_when_current_m104_queue_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("e7d4270e") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("e7d4270e", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["e7d4270e"])

    def test_build_payload_fails_closed_when_current_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("bbc877d7") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("bbc877d7", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["bbc877d7"])

    def test_build_payload_fails_closed_when_latest_m104_proof_floor_guard_does_not_resolve(self) -> None:
        (self.root / ".git").mkdir()

        def fake_cat_file(command: list[str], **_: Any) -> Any:
            commit_ref = command[-1]
            return mock.Mock(returncode=1 if commit_ref.startswith("56ff7283") else 0)

        with mock.patch.object(self.generator.subprocess, "run", side_effect=fake_cat_file):
            payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual("failed", payload["local_commit_proofs"]["status"])
        self.assertIn("56ff7283", payload["unresolved"]["local_commit_proofs"])
        missing = {
            row["commit"]: row["status"]
            for row in payload["local_commit_proofs"]["required_commits"]
        }
        self.assertEqual("failed", missing["56ff7283"])

    def test_list_item_block_for_nested_queue_key_stops_before_later_package(self) -> None:
        text = "\n".join(
            [
                "items:",
                "  - title: Target",
                "    package_id: next90-m104-core-proof-pack",
                "    status: in_progress",
                "  - title: Later",
                "    package_id: different-package",
                "    status: complete",
            ]
        )

        block = self.generator._extract_list_item_block(text, "package_id: next90-m104-core-proof-pack")

        self.assertIn("title: Target", block)
        self.assertIn("status: in_progress", block)
        self.assertNotIn("title: Later", block)
        self.assertNotIn("status: complete", block)

    def test_planned_generated_output_does_not_create_first_run_self_failure(self) -> None:
        self.assertFalse(self.output_path.exists())

        payload = self.generator.build_payload(self.root, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual([], payload["unresolved"]["release_commands"])

    def test_main_returns_nonzero_when_generated_pack_is_failed(self) -> None:
        (self.root / "Chummer.CoreEngine.Tests" / "Program.cs").write_text("wrong symbol\n", encoding="utf-8")
        with mock.patch(
            "sys.argv",
            [
                "generate-engine-proof-pack.py",
                "--repo-root",
                str(self.root),
                "--out",
                str(self.output_path),
            ],
        ):
            exit_code = self.generator.main()

        self.assertEqual(1, exit_code)
        generated = json.loads(self.output_path.read_text(encoding="utf-8"))
        self.assertEqual("failed", generated["status"])
        self.assertIn("creation", generated["unresolved"]["oracle_suites"])

    def test_main_returns_zero_when_generated_pack_passes(self) -> None:
        with mock.patch(
            "sys.argv",
            [
                "generate-engine-proof-pack.py",
                "--repo-root",
                str(self.root),
                "--out",
                str(self.output_path),
            ],
        ):
            exit_code = self.generator.main()

        self.assertEqual(0, exit_code)
        generated = json.loads(self.output_path.read_text(encoding="utf-8"))
        self.assertEqual("passed", generated["status"])

    def _seed_passing_repo(self) -> None:
        self._write("Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj", "<Project />\n")
        self._write("Chummer.Benchmarks/Chummer.Benchmarks.csproj", "<Project />\n")
        self._write(
            "Chummer.CoreEngine.Tests/Program.cs",
            "LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService\n",
        )
        self._write("Chummer.Tests/TestFiles/Fuzzy-chargen.chum5", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Munin_Career.chum5", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/HeroLabRulesParityAudit.cs", "audit\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr5/Two Banshees.por", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-technomancer-hacker.chum4", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr6/sr6-starter.hlo.json", "{}\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-hermetic-mage.chum4", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Spirit_Warden.chum5", "fixture\n")
        self._write("Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-rigger-wheelman.chum4", "fixture\n")
        self._write("Chummer.Tests/TestFiles/Apex Predator.chum5", "fixture\n")
        self._write("Chummer.Infrastructure/Xml/XmlToolCatalogService.cs", "BuildSourceToggleLaneReceipt\n")
        self._write("Chummer.Tests/ApiIntegrationTests.cs", "sourceToggleLaneReceipt\n")
        self._write("Chummer.Application/Content/DefaultRuleProfileApplicationService.cs", "service\n")
        self._write("Chummer.Application/Content/DefaultRuntimeLockDiffService.cs", "service\n")
        self._write(
            "Chummer.Benchmarks/workspace-benchmark-budgets.json",
            json.dumps(
                {
                    "workloads": [
                        {"name": "workspace.import.bastion", "maxMeanMilliseconds": 250, "maxAllocatedBytes": 32000000},
                        {"name": "workspace.section.skills.bastion", "maxMeanMilliseconds": 180, "maxAllocatedBytes": 32000000},
                        {"name": "workspace.save.bastion", "maxMeanMilliseconds": 80, "maxAllocatedBytes": 16000000},
                        {"name": "runtime.explain.trace", "maxMeanMilliseconds": 220, "maxAllocatedBytes": 24000000},
                        {"name": "workspace.export.bastion", "maxMeanMilliseconds": 160, "maxAllocatedBytes": 96000000},
                    ]
                }
            ),
        )
        self._write(
            "Chummer.Benchmarks/MigrationWorkspaceBenchmarks.cs",
            "\n".join(
                [
                    "workspace.import.bastion",
                    "workspace.section.skills.bastion",
                    "workspace.save.bastion",
                    "runtime.explain.trace",
                    "workspace.export.bastion",
                ]
            ),
        )
        self._write(
            ".codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json",
            json.dumps(
                {
                    "status": "passed",
                    "import_oracles": [
                        {"name": "Chummer4", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Chummer5a", "sources_covered": 1, "sources_expected": 1},
                        {"name": "Hero Lab Classic", "sources_covered": 1, "sources_expected": 1},
                    ],
                    "adjacent_oracles": ["Genesis", "CommLink6"],
                }
            ),
        )
        self._seed_successor_wave_authority()

    def _write(self, relative_path: str, content: str) -> None:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def _seed_successor_wave_authority(self) -> None:
        registry_path = self.root / "successor-registry.yaml"
        queue_path = self.root / "successor-queue.yaml"
        design_queue_path = self.root / "successor-design-queue.yaml"
        registry_path.write_text(
            "\n".join(
                [
                    "milestones:",
                    "  - id: 104",
                    "    title: Engine proof pack, explain budgets, and import-oracle discipline",
                    "    work_tasks:",
                    "      - id: 104.1",
                    "        owner: chummer6-core",
                    "        status: complete",
                    "        evidence:",
                    "          - required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package",
                    "          - python3 tests/test_engine_proof_pack_generator.py exits 0",
                    "      - id: 104.2",
                    "        owner: chummer6-core",
                    "        status: complete",
                    "        evidence:",
                    "          - successor_wave_authority=passed",
                    "          - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
                    "          - /docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so the completed M104 proof pack cannot pass if the latest guard disappears.",
                    "          - /docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
                    "          - /docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
                    "          - /docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
                    "          - /docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - /docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "          - dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json exits 0",
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        queue_text = (
            "\n".join(
                [
                    "items:",
                    "  - package_id: next90-m104-core-proof-pack",
                    "    frontier_id: 3227666051",
                    "    milestone_id: 104",
                    "    repo: chummer6-core",
                    "    status: complete",
                    "    landed_commit: 00800059",
                    "    proof:",
                    "      - /docker/chummercomplete/chummer-core-engine/.codex-studio/published/ENGINE_PROOF_PACK.generated.json",
                    "      - /docker/chummercomplete/chummer-core-engine/scripts/generate-engine-proof-pack.py",
                    "      - /docker/chummercomplete/chummer-core-engine/tests/test_engine_proof_pack_generator.py",
                    "      - /docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md",
                    "      - /docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
                    "      - /docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so future shards verify the closed package instead of repeating it.",
                    "      - /docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
                    "      - /docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
                    "      - /docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
                    "      - /docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "      - /docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
                    "    allowed_paths:",
                    "      - src",
                    "      - tests",
                    "      - docs",
                    "      - scripts",
                    "    owned_surfaces:",
                    "      - engine_proof_pack",
                    "      - import_oracle_discipline",
                ]
            )
            + "\n"
        )
        queue_path.write_text(queue_text, encoding="utf-8")
        design_queue_path.write_text(queue_text, encoding="utf-8")
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_registry_path"] = str(registry_path)
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_queue_path"] = str(queue_path)
        self.generator.SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"] = str(design_queue_path)
        release_channel_path = self.root / "release-channel.generated.json"
        release_channel_path.write_text(
            json.dumps(
                {
                    "status": "published",
                    "rolloutState": "promoted_preview",
                    "channelId": "docker",
                    "version": "run-test",
                    "releaseProof": {"status": "passed"},
                    "artifacts": [
                        {"artifactId": "avalonia-linux-x64-installer"},
                        {"artifactId": "avalonia-win-x64-installer"},
                        {"artifactId": "avalonia-osx-arm64-installer"},
                    ],
                    "desktopTupleCoverage": {
                        "complete": True,
                        "desktopRouteTruth": [
                            {
                                "tupleId": "avalonia:linux:linux-x64",
                                "head": "avalonia",
                                "platform": "linux",
                                "rid": "linux-x64",
                                "artifactId": "avalonia-linux-x64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                            {
                                "tupleId": "avalonia:windows:win-x64",
                                "head": "avalonia",
                                "platform": "windows",
                                "rid": "win-x64",
                                "artifactId": "avalonia-win-x64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                            {
                                "tupleId": "avalonia:macos:osx-arm64",
                                "head": "avalonia",
                                "platform": "macos",
                                "rid": "osx-arm64",
                                "artifactId": "avalonia-osx-arm64-installer",
                                "routeRole": "primary",
                                "promotionState": "promoted",
                                "parityPosture": "flagship_primary",
                                "updateEligibility": "eligible",
                                "revokeState": "not_revoked",
                                "installPosture": "installer_first",
                            },
                        ],
                    },
                }
            )
            + "\n",
            encoding="utf-8",
        )
        self.generator.RELEASE_CHANNEL_PATH = release_channel_path


if __name__ == "__main__":
    unittest.main()
