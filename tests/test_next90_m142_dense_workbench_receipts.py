#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from contextlib import contextmanager
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "scripts" / "verify-next90-m142-dense-workbench-receipts.py"
CHECKED_IN_RECEIPT_PATH = REPO_ROOT / ".codex-studio" / "published" / "NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json"


def load_generator() -> Any:
    spec = importlib.util.spec_from_file_location("verify_next90_m142_dense_workbench_receipts", GENERATOR_PATH)
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


@contextmanager
def override_authority_file(module: Any, key: str, content: str):
    with tempfile.TemporaryDirectory() as temp_dir:
        authority_path = Path(temp_dir) / f"{key}.yaml"
        authority_path.write_text(content, encoding="utf-8")
        original = module.CANONICAL_AUTHORITY_FILES[key]
        module.CANONICAL_AUTHORITY_FILES[key] = (authority_path, original[1])
        try:
            yield authority_path
        finally:
            module.CANONICAL_AUTHORITY_FILES[key] = original


class Next90M142DenseWorkbenchReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = load_generator()
        self.temp_dir = tempfile.TemporaryDirectory()
        self.output_path = Path(self.temp_dir.name) / "NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json"

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_build_payload_verifies_current_package_proof(self) -> None:
        payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("passed", payload["status"])
        self.assertEqual(
            "next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic",
            payload["package_id"],
        )
        self.assertEqual(7923205254, payload["frontier_id"])
        self.assertEqual(142, payload["milestone_id"])
        self.assertEqual("142.3", payload["work_task_id"])
        self.assertEqual(["keep_initiative_action_notes_and_workflow_state_receipts:core"], payload["owned_surfaces"])
        self.assertEqual(["src", "tests", "docs", "scripts"], payload["allowed_paths"])
        self.assertEqual(
            ".codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json",
            payload["published_receipt_path"],
        )
        self.assertEqual("family:initiative_action_notes_and_workflow_state", payload["parity_family_id"])
        self.assertEqual(
            ["workflow:initiative", "workflow:actions", "workflow:turn-ledger", "workflow:rules-reference"],
            payload["action_budget_route_ids"],
        )
        self.assertEqual(
            ["workflow:workflow-state", "workflow:contacts", "workflow:lifestyles", "workflow:notes"],
            payload["workflow_state_route_ids"],
        )
        self.assertEqual(
            [
                "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
                "python3 tests/test_next90_m142_dense_workbench_receipts.py",
                "python3 scripts/verify-next90-m142-dense-workbench-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json",
            ],
            payload["verification_commands"],
        )
        self.assertEqual("parity-m142", payload["test_filter"])
        self.assertEqual(11, payload["proof_anchor_count"])
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
        self.assertEqual(
            {
                "successor_queue": 7923205254,
                "design_successor_queue": 7923205254,
            },
            payload["canonical_queue_frontier_ids"],
        )
        self.assertEqual(
            {
                "successor_queue": {
                    "allowed_paths": ["src", "tests", "docs", "scripts"],
                    "frontier_id": "7923205254",
                    "milestone_id": "142",
                    "owned_surfaces": ["keep_initiative_action_notes_and_workflow_state_receipts:core"],
                    "package_id": "next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic",
                    "repo": "chummer6-core",
                    "status": "not_started",
                    "task": "Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.",
                    "wave": "W22P",
                    "work_task_id": "142.3",
                },
                "design_successor_queue": {
                    "allowed_paths": ["src", "tests", "docs", "scripts"],
                    "frontier_id": "7923205254",
                    "milestone_id": "142",
                    "owned_surfaces": ["keep_initiative_action_notes_and_workflow_state_receipts:core"],
                    "package_id": "next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic",
                    "repo": "chummer6-core",
                    "status": "not_started",
                    "task": "Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.",
                    "wave": "W22P",
                    "work_task_id": "142.3",
                },
            },
            payload["canonical_queue_contracts"],
        )
        self.assertEqual([], payload["unresolved"]["missing_files"])
        self.assertEqual({}, payload["unresolved"]["snippet_failures"])
        self.assertEqual({}, payload["unresolved"]["authority_row_issues"])

    def test_build_payload_fails_closed_when_workflow_receipt_scope_snippet_drifts(self) -> None:
        workspace_path = REPO_ROOT / "Chummer.Application" / "Workspaces" / "WorkspaceService.cs"
        original_workspace = workspace_path.read_text(encoding="utf-8")
        drifted_workspace = original_workspace.replace(
            "ReceiptScopeId: BuildWorkflowReceiptScopeId(rulesetId, payloadSha256),",
            "ReceiptScopeId: receiptId,",
            1,
        )

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_workspace_path = Path(temp_dir) / "WorkspaceService.cs"
            temp_workspace_path.write_text(drifted_workspace, encoding="utf-8")
            drifted_status = self.generator.inspect_file(
                temp_workspace_path,
                "workspace_service",
                self.generator.REQUIRED_SNIPPETS["workspace_service"],
            )

        self.assertIn(
            "ReceiptScopeId: BuildWorkflowReceiptScopeId(rulesetId, payloadSha256),",
            drifted_status.missing_snippets,
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

    def test_build_payload_fails_closed_when_registry_row_is_duplicated(self) -> None:
        original_registry = self.generator.CANONICAL_AUTHORITY_FILES["successor_registry"][0].read_text(encoding="utf-8")
        duplicated_registry = original_registry.replace(
            "    - id: '142.4'\n",
            "    - id: '142.3'\n"
            "      owner: chummer6-core\n"
            "      title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.\n"
            "    - id: '142.4'\n",
            1,
        )

        with override_authority_file(self.generator, "successor_registry", duplicated_registry):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual(7923205254, payload["frontier_id"])
        self.assertEqual(2, payload["authority_row_counts"]["successor_registry"])
        self.assertEqual(
            "expected 1 canonical row(s), found 2",
            payload["unresolved"]["authority_row_issues"]["successor_registry"],
        )

    def test_build_payload_fails_closed_when_queue_row_is_duplicated(self) -> None:
        original_queue = self.generator.CANONICAL_AUTHORITY_FILES["successor_queue"][0].read_text(encoding="utf-8")
        duplicated_queue = f"{original_queue.rstrip()}\n{self.generator.AUTHORITY_ROW_MARKERS['successor_queue']}"
        duplicated_queue += (
            "  task: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.\n"
            "  package_id: next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic\n"
            "  work_task_id: '142.3'\n"
            "  frontier_id: 7923205254\n"
            "  milestone_id: 142\n"
            "  status: not_started\n"
            "  wave: W22P\n"
            "  repo: chummer6-core\n"
            "  allowed_paths:\n"
            "  - src\n"
            "  - tests\n"
            "  - docs\n"
            "  - scripts\n"
            "  owned_surfaces:\n"
            "  - keep_initiative_action_notes_and_workflow_state_receipts:core\n"
        )

        with override_authority_file(self.generator, "successor_queue", duplicated_queue):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIsNone(payload["frontier_id"])
        self.assertEqual(2, payload["authority_row_counts"]["successor_queue"])
        self.assertEqual(
            "expected exactly one package row with one parseable frontier id",
            payload["unresolved"]["authority_row_issues"]["successor_queue"],
        )
        self.assertEqual(
            "unable to derive one canonical frontier id from queue mirrors",
            payload["unresolved"]["authority_row_issues"]["canonical_frontier_id"],
        )

    def test_build_payload_fails_closed_when_queue_allowed_paths_drift(self) -> None:
        original_queue = self.generator.CANONICAL_AUTHORITY_FILES["successor_queue"][0].read_text(encoding="utf-8")
        marker = self.generator.AUTHORITY_ROW_MARKERS["successor_queue"]
        start = original_queue.index(marker)
        end = original_queue.find("\n- title:", start + len(marker))
        block = original_queue[start:] if end < 0 else original_queue[start:end]
        drifted_block = block.replace("  - docs\n", "  - docs-drifted\n", 1)
        drifted_queue = original_queue.replace(block, drifted_block, 1)

        with override_authority_file(self.generator, "successor_queue", drifted_queue):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual(
            "expected allowed_paths to match the package contract",
            payload["unresolved"]["authority_row_issues"]["successor_queue_allowed_paths"],
        )

    def test_build_payload_fails_closed_when_queue_owned_surfaces_drift(self) -> None:
        original_queue = self.generator.CANONICAL_AUTHORITY_FILES["design_successor_queue"][0].read_text(encoding="utf-8")
        drifted_queue = original_queue.replace(
            "  - keep_initiative_action_notes_and_workflow_state_receipts:core\n",
            "  - keep_initiative_action_notes_and_workflow_state_receipts:ui\n",
            1,
        )

        with override_authority_file(self.generator, "design_successor_queue", drifted_queue):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual(
            "expected owned_surfaces to match the package contract",
            payload["unresolved"]["authority_row_issues"]["design_successor_queue_owned_surfaces"],
        )

    def test_build_payload_fails_closed_when_queue_repo_drift_breaks_contract(self) -> None:
        original_queue = self.generator.CANONICAL_AUTHORITY_FILES["successor_queue"][0].read_text(encoding="utf-8")
        marker = self.generator.AUTHORITY_ROW_MARKERS["successor_queue"]
        start = original_queue.index(marker)
        end = original_queue.find("\n- title:", start + len(marker))
        block = original_queue[start:] if end < 0 else original_queue[start:end]
        drifted_block = block.replace("  repo: chummer6-core\n", "  repo: chummer6-ui\n", 1)
        drifted_queue = original_queue.replace(block, drifted_block, 1)

        with override_authority_file(self.generator, "successor_queue", drifted_queue):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertEqual(
            "expected repo to match the package contract",
            payload["unresolved"]["authority_row_issues"]["successor_queue_repo"],
        )

    def test_build_payload_fails_closed_when_queue_mirrors_disagree_on_frontier_id(self) -> None:
        original_design_queue = self.generator.CANONICAL_AUTHORITY_FILES["design_successor_queue"][0].read_text(encoding="utf-8")
        drifted_design_queue = original_design_queue.replace("  frontier_id: 7923205254", "  frontier_id: 7923205255", 1)

        with override_authority_file(self.generator, "design_successor_queue", drifted_design_queue):
            payload = self.generator.build_payload(REPO_ROOT, self.output_path)

        self.assertEqual("failed", payload["status"])
        self.assertIsNone(payload["frontier_id"])
        self.assertEqual(
            {
                "successor_queue": 7923205254,
                "design_successor_queue": 7923205255,
            },
            payload["canonical_queue_frontier_ids"],
        )
        self.assertEqual(
            "canonical queue mirrors disagree on frontier id",
            payload["unresolved"]["authority_row_issues"]["queue_frontier_id_drift"],
        )
        self.assertEqual(
            "unable to derive one canonical frontier id from queue mirrors",
            payload["unresolved"]["authority_row_issues"]["canonical_frontier_id"],
        )

    def test_build_payload_ignores_unrelated_program_changes_outside_m142_method_scope(self) -> None:
        program_path = REPO_ROOT / "Chummer.CoreEngine.Tests" / "Program.cs"
        original_program = program_path.read_text(encoding="utf-8")
        original_status = self.generator.inspect_file(
            program_path,
            "test_program",
            self.generator.REQUIRED_SNIPPETS["test_program"],
        )
        drifted_program = original_program.replace(
            "    private static void DenseWorkbenchAndWorkflowReceiptsStayDeterministic()\n",
            "    // unrelated verifier drift outside the scoped M142 method block\n    private static void DenseWorkbenchAndWorkflowReceiptsStayDeterministic()\n",
            1,
        )

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_program_path = Path(temp_dir) / "Program.cs"
            temp_program_path.write_text(drifted_program, encoding="utf-8")
            drifted_status = self.generator.inspect_file(
                temp_program_path,
                "test_program",
                self.generator.REQUIRED_SNIPPETS["test_program"],
            )

        self.assertEqual("m142-test-method", original_status.digest_scope)
        self.assertEqual("m142-test-method", drifted_status.digest_scope)
        self.assertEqual(original_status.digest, drifted_status.digest)


if __name__ == "__main__":
    unittest.main()
