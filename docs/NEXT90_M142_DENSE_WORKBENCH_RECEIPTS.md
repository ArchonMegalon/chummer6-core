## M142 core: deterministic dense-workbench and workflow-state receipts

Package: `next90-m142-core-keep-initiative-action-notes-and-workflow-state-receipts-deterministic`  
Frontier: `7923205254`

This slice binds the parity-proof runtime surfaces for Wave `22P / M142` to deterministic receipts:

- `SessionActionBudgetDeterministicReceipt` proves initiative and action-budget state on the SR6 session lane.
- `WorkspaceWorkflowDeterministicReceipt` proves notes, contacts, lifestyles, and workflow-state coverage on import/save/download/export/print flows.

The shared parity family id is:

- `family:initiative_action_notes_and_workflow_state`

Targeted proof command:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release
```
