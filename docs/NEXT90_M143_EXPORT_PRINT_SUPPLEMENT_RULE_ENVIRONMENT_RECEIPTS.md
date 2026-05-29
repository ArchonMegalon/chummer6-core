## M143 core: deterministic export/print supplement and rule-environment receipts

Package: `next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi`  
Frontier: `2778308338`

This slice binds the parity-proof runtime surfaces for Wave `22P / M143` to deterministic receipts:

- `Sr6SuccessorLaneDeterministicReceipt` proves supplement, designer, house-rule, and online-storage posture on `family:sr6_supplements_designers_and_house_rules`.
- `WorkspaceExchangeDeterministicReceipt` proves export/print rule-environment continuity on `family:sheet_export_print_viewer_and_exchange`.
- The route-local proof plane stays pinned to `menu:open_for_printing`, `menu:open_for_export`, `menu:file_print_multiple`, `workflow:sr6_supplements`, and `workflow:house_rules`.

The package is only honest when there is exactly one canonical package row in each staged queue root, and the core-owned receipts still line up with the live successor registry row for `143.2`.

Targeted proof command:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

Focused proof refresh:

```bash
python3 tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py
python3 scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json
```
