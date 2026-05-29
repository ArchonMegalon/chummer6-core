## M142 core: deterministic dense-workbench and workflow-state receipts

Package: `next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic`  
Frontier: `7923205254`

This slice closes the core-owned deterministic receipt plane for Wave `22P / M142`.

It binds the parity-proof runtime surfaces for dense workbench, initiative utilities, and identity or lifestyle workflows to direct engine-owned receipts:

- `SessionActionBudgetDeterministicReceipt` proves initiative and action-budget state on the SR6 session lane.
- `WorkspaceWorkflowDeterministicReceipt` proves notes, contacts, lifestyles, and workflow-state coverage on import/save/download/export/print flows.
- `WorkspaceWorkflowDeterministicReceipt.ReceiptScopeId` is content-addressed from the normalized ruleset and workspace payload hash, so identical payloads keep one proof scope even when the workspace store issues fresh runtime ids.
- Workspace import/save/download/export/print receipt ids are content-addressed, so repeated imports of the same payload keep the same deterministic proof ids even when the workspace store assigns fresh runtime ids.

Direct route ids carried by those receipts:

- `workflow:initiative`
- `workflow:actions`
- `workflow:turn-ledger`
- `workflow:rules-reference`
- `workflow:workflow-state`
- `workflow:contacts`
- `workflow:lifestyles`
- `workflow:notes`

The shared parity family id is:

- `family:initiative_action_notes_and_workflow_state`

The focused proof must fail closed when the canonical milestone row duplicates `142.3`, when either staged queue root stops exposing exactly one canonical package row in each staged queue root for this package id, or when the two queue mirrors stop agreeing on the same canonical frontier id. The verifier derives the frontier from those queue rows instead of trusting task-local run-brief metadata.

The verifier and its focused Python test are also first-class proof anchors. Future shards now have to keep both staged queue mirrors structurally aligned on `package_id`, `work_task_id`, `milestone_id`, `repo`, `allowed_paths`, and `owned_surfaces`; a renamed scope row can no longer reuse the same frontier id and still pass as M142 proof.

Targeted proof commands:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m142_dense_workbench_receipts.py
python3 scripts/verify-next90-m142-dense-workbench-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json
```

`scripts/ai/verify.sh` now runs the focused M142 lane so future shards can verify direct receipt coverage for initiative, action, notes, and workflow-state parity without reopening sibling packages.
