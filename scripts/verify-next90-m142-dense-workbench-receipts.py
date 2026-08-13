#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PACKAGE_ID = "next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic"
MILESTONE_ID = 142
WORK_TASK_ID = "142.3"
TITLE = "Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof."
TASK = TITLE
OWNED_SURFACES = ["keep_initiative_action_notes_and_workflow_state_receipts:core"]
ALLOWED_PATHS = ["src", "tests", "docs", "scripts"]
PACKAGE_REPO = "chummer6-core"
PACKAGE_REPO_ROOT = "."
PUBLISHED_RECEIPT_PATH = ".codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json"
DEFAULT_OUTPUT_RELATIVE_PATH = Path(".codex-studio") / "published" / "NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json"

REQUIRED_FILES = {
    "receipt_verifier": Path("scripts/verify-next90-m142-dense-workbench-receipts.py"),
    "receipt_test": Path("tests/test_next90_m142_dense_workbench_receipts.py"),
    "action_budget_contracts": Path("Chummer.Contracts/Session/SessionActionBudgetContracts.cs"),
    "workspace_contracts": Path("Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs"),
    "action_budget_service": Path("Chummer.Application/Session/DefaultSessionActionBudgetService.cs"),
    "workspace_service": Path("Chummer.Application/Workspaces/WorkspaceService.cs"),
    "test_program": Path("Chummer.CoreEngine.Tests/Program.cs"),
    "session_action_budget_mstest": Path("Chummer.Tests/SessionActionBudgetServiceTests.cs"),
    "workspace_service_mstest": Path("Chummer.Tests/WorkspaceServiceTests.cs"),
    "repo_verify": Path("scripts/ai/verify.sh"),
    "docs": Path("docs/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.md"),
}

REQUIRED_SNIPPETS = {
    "receipt_verifier": [
        'PACKAGE_ID = "next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic"',
        'WORK_TASK_ID = "142.3"',
        'def extract_queue_package_metadata(content: str) -> dict[str, Any] | None:',
        '"expected allowed_paths to match the package contract"',
        '"expected owned_surfaces to match the package contract"',
    ],
    "receipt_test": [
        "def test_build_payload_fails_closed_when_queue_allowed_paths_drift(self) -> None:",
        "def test_build_payload_fails_closed_when_queue_owned_surfaces_drift(self) -> None:",
        "def test_build_payload_fails_closed_when_queue_repo_drift_breaks_contract(self) -> None:",
    ],
    "action_budget_contracts": [
        "public sealed record SessionTurnLedgerDelta(",
        "public sealed record SessionActionBudgetDeterministicReceipt(",
        "IReadOnlyList<string> CoveredWorkflowRouteIds,",
        "IReadOnlyList<string> TurnLedgerDeltaIds,",
        "IReadOnlyList<string> ReceiptSourceAnchors,",
        "int MissingSourceAnchorReceiptCount,",
    ],
    "workspace_contracts": [
        "public sealed record WorkspaceWorkflowDeterministicReceipt(",
        "string ReceiptScopeId,",
        "IReadOnlyList<string> CoveredWorkflowRouteIds,",
        "IReadOnlyList<string> MissingWorkflowRouteIds,",
        "bool HasNotesField,",
        "bool HasGameNotesField,",
        "bool HasNotesContent,",
        "bool HasGameNotesContent);",
    ],
    "action_budget_service": [
        'private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";',
        '"workflow:initiative"',
        '"workflow:actions"',
        '"workflow:turn-ledger"',
        '"workflow:rules-reference"',
        "private static SessionActionBudgetDeterministicReceipt BuildDeterministicReceipt(",
        "MissingSourceAnchorReceiptCount: missingSourceAnchorReceiptCount,",
        "ResolveReceiptSourceAnchors(",
    ],
    "workspace_service": [
        'private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";',
        '"workflow:workflow-state"',
        '"workflow:contacts"',
        '"workflow:lifestyles"',
        '"workflow:notes"',
        "private static WorkspaceWorkflowDeterministicReceipt BuildWorkflowDeterministicReceipt(",
        "string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(payload));",
        "ReceiptScopeId: BuildWorkflowReceiptScopeId(rulesetId, payloadSha256),",
        "private static string BuildWorkflowReceiptScopeId(string rulesetId, string payloadSha256)",
        "string normalizedEntityId = NormalizeReceiptEntityId(entityId);",
        "private static string NormalizeReceiptEntityId(string entityId)",
        "private static bool LooksLikeTransientWorkspaceId(string value)",
        "HasNotesContent: noteSummary.HasNotesContent,",
        "HasGameNotesContent: noteSummary.HasGameNotesContent);",
    ],
    "test_program": [
        'string.Equals(filter, "parity-m142", StringComparison.OrdinalIgnoreCase)',
        "DenseWorkbenchAndWorkflowReceiptsStayDeterministic();",
        '"workflow:initiative"',
        '"workflow:notes"',
        "repeatedImport.ImportReceiptId",
        "Workflow-state deterministic receipts should publish a content-addressed proof scope for the governed SR5 parity fixture.",
        "Workflow-state deterministic receipts should confirm that gameplay notes remain present on the SR5 parity fixture.",
    ],
    "session_action_budget_mstest": [
        "Compute_uses_sr6_minor_formula_and_turn_start_cap",
        'Assert.AreEqual("governed", result.DeterministicReceipt!.ActionBudgetPosture);',
        "CoveredWorkflowRouteIds",
        "TurnLedgerDeltaIds",
        "SourceAnchorReceiptCount",
        "MissingSourceAnchorReceiptCount",
    ],
    "workspace_service_mstest": [
        'Assert.AreEqual("governed", imported.WorkflowDeterministicReceipt?.WorkflowStatePosture);',
        '"workflow:workflow-state"',
        '"workflow:contacts"',
        '"workflow:lifestyles"',
        '"workflow:notes"',
        "Import_reuses_content_addressed_receipt_ids_across_distinct_workspace_instances",
        "Assert.AreEqual(first.ImportReceiptId, second.ImportReceiptId);",
        "Assert.AreEqual(first.WorkflowDeterministicReceipt?.ReceiptScopeId, second.WorkflowDeterministicReceipt?.ReceiptScopeId);",
        "Assert.AreEqual(firstExport.Value?.PackageId, secondExport.Value?.PackageId);",
        "Assert.IsNotNull(save.Value?.WorkflowDeterministicReceipt);",
        "Assert.IsNotNull(print.Value?.WorkflowDeterministicReceipt);",
    ],
    "repo_verify": [
        "test -f docs/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.md",
        "test -f scripts/verify-next90-m142-dense-workbench-receipts.py",
        "test -f tests/test_next90_m142_dense_workbench_receipts.py",
        'CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false',
        "python3 tests/test_next90_m142_dense_workbench_receipts.py",
        "python3 scripts/verify-next90-m142-dense-workbench-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json --check",
    ],
    "docs": [
        PACKAGE_ID,
        "SessionActionBudgetDeterministicReceipt",
        "WorkspaceWorkflowDeterministicReceipt",
        "ReceiptScopeId",
        "family:initiative_action_notes_and_workflow_state",
        "workflow:initiative",
        "workflow:notes",
        "content-addressed",
        "exactly one canonical package row in each staged queue root",
        "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
    ],
}

CANONICAL_AUTHORITY_FILES = {
    "successor_registry": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml"),
        [
            "  - id: 142",
            "    title: Direct parity proof for dense workbench, dice utilities, and identity or lifestyle workflows",
            "    - id: '142.3'",
            "      owner: chummer6-core",
            "      title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.",
        ],
    ),
    "successor_queue": (
        Path("/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.",
            "  package_id: next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic",
            "  work_task_id: '142.3'",
            "  frontier_id: 7923205254",
            "  milestone_id: 142",
            "  repo: chummer6-core",
            "  - keep_initiative_action_notes_and_workflow_state_receipts:core",
        ],
    ),
    "design_successor_queue": (
        Path("/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml"),
        [
            "- title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.",
            "  package_id: next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic",
            "  work_task_id: '142.3'",
            "  frontier_id: 7923205254",
            "  milestone_id: 142",
            "  repo: chummer6-core",
            "  - keep_initiative_action_notes_and_workflow_state_receipts:core",
        ],
    ),
}

AUTHORITY_ROW_MARKERS = {
    "successor_registry": "    - id: '142.3'\n",
    "successor_queue": "- title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.\n",
    "design_successor_queue": "- title: Keep initiative, action, notes, and workflow-state receipts deterministic enough for parity and task-speed proof.\n",
}

EXPECTED_AUTHORITY_ROW_COUNTS = {
    "successor_registry": 1,
    "successor_queue": 1,
    "design_successor_queue": 1,
}


@dataclass(frozen=True)
class ProofFileStatus:
    key: str
    path: Path
    exists: bool
    digest: str | None
    digest_scope: str
    missing_snippets: list[str]

    def to_json(self, repo_root: Path | None = None) -> dict[str, Any]:
        return {
            "key": self.key,
            "path": receipt_path_for(self.path, repo_root),
            "exists": self.exists,
            "digest": self.digest,
            "digest_scope": self.digest_scope,
            "missing_snippets": self.missing_snippets,
            "status": "passed" if self.exists and not self.missing_snippets else "failed",
        }


def sha256_digest(text: str) -> str:
    return f"sha256:{hashlib.sha256(text.encode('utf-8')).hexdigest()}"


def receipt_path_for(path: Path, repo_root: Path | None) -> str:
    resolved = path.resolve()
    if repo_root is None:
        return str(resolved)
    try:
        return resolved.relative_to(repo_root.resolve()).as_posix()
    except ValueError:
        return str(resolved)




def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def extract_authority_scope(key: str, content: str) -> tuple[str, str]:
    if key == "successor_registry":
        start_marker = "  - id: 142\n"
        next_marker = "  - id: 143\n"
        scope_label = "milestone-row"
    elif key in {"successor_queue", "design_successor_queue"}:
        scoped_rows = extract_queue_package_rows(content)
        if scoped_rows:
            return "\n".join(scoped_rows), "package-rows"
        return content, "full-file"
    else:
        return content, "full-file"

    start = content.find(start_marker)
    if start < 0:
        return content, "full-file"

    end = content.find(next_marker, start + len(start_marker))
    scoped = content[start:] if end < 0 else content[start:end]
    return scoped.rstrip(), scope_label


def extract_queue_package_rows(content: str) -> list[str]:
    rows: list[str] = []
    start_marker = AUTHORITY_ROW_MARKERS["successor_queue"]

    mode_index = content.find("\nmode:")
    if mode_index >= 0:
        rows.extend(extract_package_row_blocks(content[:mode_index], start_marker))

    items_index = content.find("\nitems:\n")
    if items_index >= 0:
        rows.extend(extract_package_row_blocks(content[items_index + 1 :], start_marker))

    return rows


def extract_package_row_blocks(content: str, start_marker: str) -> list[str]:
    blocks: list[str] = []
    search_start = 0
    while True:
        start = content.find(start_marker, search_start)
        if start < 0:
            break

        next_marker = content.find("\n- title:", start + len(start_marker))
        block = content[start:] if next_marker < 0 else content[start:next_marker]
        blocks.append(block.rstrip())
        search_start = start + len(start_marker)

    return blocks


def extract_between_markers(
    content: str,
    start_marker: str,
    end_marker: str | None,
    scope_label: str,
) -> tuple[str, str]:
    start = content.find(start_marker)
    if start < 0:
        return content, "full-file"

    if end_marker is None:
        scoped = content[start:]
    else:
        end = content.find(end_marker, start + len(start_marker))
        if end <= start:
            return content, "full-file"
        scoped = content[start:end]

    return scoped.rstrip(), scope_label


def extract_scoped_blocks(
    content: str,
    blocks: list[tuple[str, str | None]],
    scope_label: str,
) -> tuple[str, str]:
    scoped_parts: list[str] = []
    for start_marker, end_marker in blocks:
        scoped, label = extract_between_markers(content, start_marker, end_marker, scope_label)
        if label == "full-file":
            return content, "full-file"
        scoped_parts.append(scoped)

    return "\n\n".join(scoped_parts), scope_label


def count_authority_rows(key: str, content: str) -> int:
    if key == "successor_registry":
        return content.count(AUTHORITY_ROW_MARKERS[key])

    if key in {"successor_queue", "design_successor_queue"}:
        return len(extract_queue_package_rows(content))

    return content.count(AUTHORITY_ROW_MARKERS[key])


def extract_frontier_id_from_package_row(content: str) -> int | None:
    for line in content.splitlines():
        stripped = line.strip()
        if not stripped.startswith("frontier_id:"):
            continue

        value = stripped.split(":", 1)[1].strip()
        if not value:
            return None

        try:
            return int(value)
        except ValueError:
            return None

    return None


def extract_queue_package_metadata(content: str) -> dict[str, Any] | None:
    metadata: dict[str, Any] = {
        "allowed_paths": [],
        "owned_surfaces": [],
    }
    active_list_key: str | None = None

    for line in content.splitlines():
        stripped = line.strip()
        if not stripped:
            active_list_key = None
            continue

        if stripped.startswith("- "):
            if active_list_key is not None:
                metadata[active_list_key].append(stripped[2:].strip())
            continue

        active_list_key = None
        if ":" not in stripped:
            continue

        key, raw_value = stripped.split(":", 1)
        key = key.strip()
        value = raw_value.strip()
        if key in {"allowed_paths", "owned_surfaces"}:
            active_list_key = key
            continue

        metadata[key] = value.strip("'\"")

    required_scalar_keys = {"package_id", "work_task_id", "milestone_id", "repo", "frontier_id"}
    if any(not metadata.get(key) for key in required_scalar_keys):
        return None

    return metadata


def inspect_file(path: Path, key: str, required_snippets: list[str]) -> ProofFileStatus:
    if not path.exists():
        return ProofFileStatus(key=key, path=path, exists=False, digest=None, digest_scope="full-file", missing_snippets=required_snippets)

    content = read_text(path)
    if key in CANONICAL_AUTHORITY_FILES:
        scope_text, scope_label = extract_authority_scope(key, content)
    elif key == "repo_verify":
        start_marker = "test -f docs/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.md"
        end_marker = "test -f scripts/generate-engine-proof-pack.py"
        start = content.find(start_marker)
        end = content.find(end_marker, start + len(start_marker)) if start >= 0 else -1
        if start >= 0 and end > start:
            scope_text = content[start:end].rstrip()
            scope_label = "m142-verify-block"
        else:
            scope_text = content
            scope_label = "full-file"
    elif key == "test_program":
        start_marker = "    private static void DenseWorkbenchAndWorkflowReceiptsStayDeterministic()\n"
        end_marker = "    private static void Sr6CombatRoundActionEconomyPublishesAnchoredTurnLedgerProof()\n"
        scope_text, scope_label = extract_between_markers(content, start_marker, end_marker, "m142-test-method")
    elif key == "action_budget_contracts":
        scope_text, scope_label = extract_between_markers(
            content,
            "public sealed record SessionTurnLedgerDelta(\n",
            "public sealed record SessionActionBudgetInput(\n",
            "m142-action-budget-contract-block",
        )
    elif key == "workspace_contracts":
        scope_text, scope_label = extract_between_markers(
            content,
            "public sealed record WorkspaceWorkflowDeterministicReceipt(\n",
            "public sealed record WorkspaceExchangeDeterministicReceipt(\n",
            "m142-workflow-contract-block",
        )
    elif key == "action_budget_service":
        scope_text, scope_label = extract_scoped_blocks(
            content,
            [
                (
                    '    private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";\n',
                    "    public SessionActionBudgetResult Compute(SessionActionBudgetInput input)\n",
                ),
                (
                    "    private static SessionActionBudgetDeterministicReceipt BuildDeterministicReceipt(\n",
                    "    private static string ResolveActionBudgetPosture(\n",
                ),
                (
                    "    private static string[] ResolveReceiptSourceAnchors(string actionKey, IReadOnlyList<SessionActionBudgetReceipt> receipts)\n",
                    None,
                ),
            ],
            "m142-action-budget-blocks",
        )
    elif key == "workspace_service":
        scope_text, scope_label = extract_scoped_blocks(
            content,
            [
                (
                    '    private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";\n',
                    "    private readonly IWorkspaceStore _workspaceStore;\n",
                ),
                (
                    "    private static WorkspaceWorkflowDeterministicReceipt BuildWorkflowDeterministicReceipt(\n",
                    "    private static WorkspaceExchangeDeterministicReceipt BuildExchangeDeterministicReceipt(\n",
                ),
                (
                    "    private static string BuildReceiptId(string prefix, string entityId, string payloadSha256)\n",
                    "    private static string ComputeSha256(byte[] bytes)\n",
                ),
            ],
            "m142-workflow-blocks",
        )
    elif key == "session_action_budget_mstest":
        scope_text, scope_label = extract_between_markers(
            content,
            "    public void Compute_uses_sr6_minor_formula_and_turn_start_cap()\n",
            "    [TestMethod]\n    public void Compute_marks_full_defense_available_when_four_minor_actions_remain()\n",
            "m142-session-test-method",
        )
    elif key == "workspace_service_mstest":
        scope_text, scope_label = extract_scoped_blocks(
            content,
            [
                (
                    "    public void Import_get_profile_update_and_save_roundtrip()\n",
                    "    [TestMethod]\n    public void Import_get_build_lab_projection_from_sr5_workspace()\n",
                ),
                (
                    "    public void Import_reuses_content_addressed_receipt_ids_across_distinct_workspace_instances()\n",
                    "    [TestMethod]\n    public void Import_save_download_export_and_section_parse_support_every_checked_in_chummer5_fixture()\n",
                ),
            ],
            "m142-workspace-test-methods",
        )
    else:
        scope_text, scope_label = content, "full-file"

    missing_snippets = [snippet for snippet in required_snippets if snippet not in content]
    return ProofFileStatus(
        key=key,
        path=path,
        exists=True,
        digest=sha256_digest(scope_text),
        digest_scope=scope_label,
        missing_snippets=missing_snippets,
    )


def build_payload(repo_root: Path, out_path: Path) -> dict[str, Any]:
    proof_files = [
        inspect_file(repo_root / relative_path, key, REQUIRED_SNIPPETS.get(key, []))
        for key, relative_path in REQUIRED_FILES.items()
    ]
    authority_files = [
        inspect_file(path, key, snippets)
        for key, (path, snippets) in CANONICAL_AUTHORITY_FILES.items()
    ]

    authority_row_counts: dict[str, int] = {}
    authority_row_issues: dict[str, str] = {}
    authority_frontier_ids: dict[str, int | None] = {}
    queue_contracts: dict[str, dict[str, Any]] = {}
    for key, (path, _) in CANONICAL_AUTHORITY_FILES.items():
        if not path.exists():
            continue

        content = read_text(path)
        count = count_authority_rows(key, content)
        authority_row_counts[key] = count
        expected = EXPECTED_AUTHORITY_ROW_COUNTS[key]
        if count != expected:
            authority_row_issues[key] = f"expected {expected} canonical row(s), found {count}"

        if key in {"successor_queue", "design_successor_queue"}:
            blocks = extract_queue_package_rows(content)
            frontier_ids = [extract_frontier_id_from_package_row(block) for block in blocks]
            authority_frontier_ids[key] = frontier_ids[0] if len(frontier_ids) == 1 else None
            if len(frontier_ids) != 1 or frontier_ids[0] is None:
                authority_row_issues[key] = "expected exactly one package row with one parseable frontier id"
                continue

            package_metadata = extract_queue_package_metadata(blocks[0])
            if package_metadata is None:
                authority_row_issues[key] = "expected exactly one package row with parseable package metadata"
                continue

            queue_contracts[key] = package_metadata
            if package_metadata["package_id"] != PACKAGE_ID:
                authority_row_issues[f"{key}_package_id"] = "expected package_id to match the package contract"
            if package_metadata["work_task_id"] != WORK_TASK_ID:
                authority_row_issues[f"{key}_work_task_id"] = "expected work_task_id to match the package contract"
            if package_metadata["milestone_id"] != str(MILESTONE_ID):
                authority_row_issues[f"{key}_milestone_id"] = "expected milestone_id to match the package contract"
            if package_metadata["repo"] != PACKAGE_REPO:
                authority_row_issues[f"{key}_repo"] = "expected repo to match the package contract"
            if package_metadata["allowed_paths"] != ALLOWED_PATHS:
                authority_row_issues[f"{key}_allowed_paths"] = "expected allowed_paths to match the package contract"
            if package_metadata["owned_surfaces"] != OWNED_SURFACES:
                authority_row_issues[f"{key}_owned_surfaces"] = "expected owned_surfaces to match the package contract"

    queue_mirror_keys = ("successor_queue", "design_successor_queue")
    canonical_frontier_ids = {
        key: value
        for key, value in authority_frontier_ids.items()
        if value is not None
    }
    queue_mirrors_are_resolved = all(
        key in canonical_frontier_ids and key not in authority_row_issues
        for key in queue_mirror_keys
    )
    unique_frontier_ids = sorted(set(canonical_frontier_ids.values()))
    if queue_mirrors_are_resolved and len(unique_frontier_ids) > 1:
        authority_row_issues["queue_frontier_id_drift"] = "canonical queue mirrors disagree on frontier id"

    canonical_frontier_id = unique_frontier_ids[0] if queue_mirrors_are_resolved and len(unique_frontier_ids) == 1 else None
    if canonical_frontier_id is None:
        authority_row_issues["canonical_frontier_id"] = "unable to derive one canonical frontier id from queue mirrors"

    all_files = proof_files + authority_files
    missing_files = [status.key for status in all_files if not status.exists]
    snippet_failures = {
        status.key: status.missing_snippets
        for status in all_files
        if status.exists and status.missing_snippets
    }
    passed = not missing_files and not snippet_failures and not authority_row_issues

    return {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "status": "passed" if passed else "failed",
        "package_id": PACKAGE_ID,
        "frontier_id": canonical_frontier_id,
        "milestone_id": MILESTONE_ID,
        "work_task_id": WORK_TASK_ID,
        "title": TITLE,
        "task": TASK,
        "repo": PACKAGE_REPO,
        "repo_root": ".",
        "owned_surfaces": OWNED_SURFACES,
        "allowed_paths": ALLOWED_PATHS,
        "published_receipt_path": PUBLISHED_RECEIPT_PATH,
        "receipt_path": receipt_path_for(out_path, repo_root),
        "proof_anchor_count": len(proof_files),
        "authority_anchor_count": len(authority_files),
        "proof_files": [status.to_json(repo_root) for status in proof_files],
        "authority_files": [status.to_json(repo_root) for status in authority_files],
        "authority_row_counts": authority_row_counts,
        "expected_authority_row_counts": EXPECTED_AUTHORITY_ROW_COUNTS,
        "canonical_queue_frontier_ids": canonical_frontier_ids,
        "canonical_queue_contracts": queue_contracts,
        "parity_family_id": "family:initiative_action_notes_and_workflow_state",
        "action_budget_route_ids": [
            "workflow:initiative",
            "workflow:actions",
            "workflow:turn-ledger",
            "workflow:rules-reference",
        ],
        "workflow_state_route_ids": [
            "workflow:workflow-state",
            "workflow:contacts",
            "workflow:lifestyles",
            "workflow:notes",
        ],
        "verification_commands": [
            "CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false",
            "python3 tests/test_next90_m142_dense_workbench_receipts.py",
            "python3 scripts/verify-next90-m142-dense-workbench-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json",
        ],
        "test_filter": "parity-m142",
        "unresolved": {
            "missing_files": missing_files,
            "snippet_failures": snippet_failures,
            "authority_row_issues": authority_row_issues,
        },
    }


def without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    parser.add_argument("--out", type=Path, default=DEFAULT_OUTPUT_RELATIVE_PATH)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    out_path = args.out.resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)

    payload = build_payload(repo_root, out_path)

    if args.check and out_path.exists():
        checked_in = json.loads(out_path.read_text(encoding="utf-8"))
        if without_generated_at(payload) != without_generated_at(checked_in):
            print(f"checked-in receipt is stale: {out_path}")
            return 1

    out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0 if payload["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
