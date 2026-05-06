## M121 core: SR6 action-budget, turn-ledger, and source-anchor receipts

Package: `next90-m121-core-implement-actionbudgetresult-actionaffordance-turnledger`  
Frontier: `1122632660`

This slice lands the first core-only combat-round proof for Wave `15 / M121` on the existing session action-budget contract plane.

It now publishes:

- `SessionActionAffordance` entries for the first SR6 combat-round proof.
- `SessionTurnLedgerDelta` previews so promoted consumers can show bounded before/after action-economy outcomes without mutating current truth.
- anchored `SessionActionBudgetReceipt` objects whose `SourceAnchor` payloads stay in-core and can be cited downstream without local rule forks.
- `SessionActionBudgetDeterministicReceipt` fields for `turnLedgerDeltaIds`, `sourceAnchorReceiptCount`, and `missingSourceAnchorReceiptCount` so proof fails closed when anchored receipts drift away.
- fail-closed posture when custom receipt sets keep some `SourceAnchor` objects but drop required SR6 turn-ledger anchor refs for `full-defense` or `convert-four-minor-to-anytime-major`.

The first combat-round proof covers:

- `take-major-action`
- `take-minor-action`
- `full-defense`
- `convert-four-minor-to-anytime-major`

The default SR6 receipts stay source-anchored through:

- `sr6_core_major_actions`
- `sr6_core_minor_actions`
- `sr6_core_full_defense`
- `sr6_core_anytime_major_conversion`

Targeted proof commands:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m121-action-economy dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m121_action_economy.py
python3 scripts/verify-next90-m121-action-economy.py --repo-root . --out .codex-studio/published/NEXT90_M121_ACTION_ECONOMY.generated.json
```

`scripts/ai/verify.sh` now runs the focused M121 contract lane so later shards can prove the turn-ledger and source-anchor slice without reopening sibling queue packages.
