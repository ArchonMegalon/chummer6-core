## M143 core: deterministic export/print supplement and rule-environment receipts

Package: `next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi`  
Frontier: `2778308338`

This slice binds the parity-proof runtime surfaces for Wave `22P / M143` to deterministic receipts:

- `Sr6SuccessorLaneDeterministicReceipt` proves supplement, designer, house-rule, and online-storage posture on `family:sr6_supplements_designers_and_house_rules`.
- `WorkspaceExchangeDeterministicReceipt` proves export/print rule-environment continuity on `family:sheet_export_print_viewer_and_exchange`.

Targeted proof command:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release
```
