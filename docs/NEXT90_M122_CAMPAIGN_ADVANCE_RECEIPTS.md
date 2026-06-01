## M122 core: campaign adoption, runner-goal, and BLACK LEDGER deterministic receipts

Package: `next90-m122-core-add-deterministic-reward-downtime-goal-update-and-conseq`  
Frontier: `1771239378`

This slice lands the core-owned deterministic receipt plane for Wave `15 / M122`.

It now publishes:

- `CampaignAdoptionConfidenceReceipt` contracts with governed, review-required, and blocked confidence posture for adoption flows.
- `RunnerRewardReceipt`, `DowntimeAllocationReceipt`, and `RunnerGoalUpdateReceipt` contracts so reward and downtime events can produce one deterministic goal-update trail.
- `BlackLedgerConsequenceReceipt` plus `CampaignAdvanceDeterministicReceipt` so one approved ResolutionReport can issue one `WorldTick` id and one player-safe news item id without Hub or UI inventing world-truth semantics locally.
- `RunSummarySeed` and `ShadowfeedSeed` payload reuse through the same bundle so downstream adoption and BLACK LEDGER surfaces share canonical consequence/news tags.
- fail-closed posture when campaign adoption has missing runner mappings or the consequence lane carries spoiler tags that would break player-safe publication.

The first governed proof covers:

- adoption confidence for mapped, missing, and conflict-bearing runner imports
- review-required adoption and BLACK LEDGER publication posture when conflict tags remain but spoiler tags do not
- reward-to-goal and downtime-to-goal progress aggregation
- achieved-goal closure when reward and downtime together finish the remaining progress
- deterministic downtime burden recovery via `ComputeDowntimeProgression`
- deterministic BLACK LEDGER faction-response scoring via `ComputeFactionResponseSeed`
- stable `world-tick` and `news-item` ids derived from `ResolutionReport` inputs
- fail-closed authority proof when the staged queue grows beyond its canonical two `122.2` package rows (activation row plus `items:` row) or when the canonical milestone row duplicates the core work task

Targeted proof commands:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m122_campaign_advance_receipts.py
python3 scripts/verify-next90-m122-campaign-advance-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json
```

`scripts/ai/verify.sh` now runs the focused M122 lane so later shards can prove the deterministic adoption and BLACK LEDGER contract surface without reopening sibling packages.
