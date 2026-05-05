# Explain Value Packets

Milestone `145.1` promotes a first-party packet shape for visible mechanical results in `chummer6-core`.
Package id: `next90-m145-core-explain-every-value-packets`
Frontier id: `1451045101`

The core packet now carries:

- one normalized explain trace for the visible result
- deduplicated source anchors and evidence pointers
- optional legality and warning posture through `ValidationSummary`
- optional before/after deltas through `RuntimeLockDiffProjection`
- bounded deterministic counterfactual packets, capped at `3`
- coverage-registry rows for the promoted surfaces the product must prove before closeout, including explicit `source-anchor` coverage
- repeated coverage rows for each promoted counterfactual result so downstream proof can verify result, legality, warnings, deltas, and source anchors without re-deriving nested packet state

The coverage registry is the fail-closed hook for downstream release proof.
When a value packet omits the visible result, legality state, warnings, deltas, or counterfactual rows that a promoted surface expects, downstream proof can reject the slice without re-deriving the packet by hand.

The receipt verifier also fails closed when the canonical successor registry or staged queue drifts away from the package id, work-task id, title, owned surfaces, or frontier id for `145.1`.

Counterfactual packets stay bounded and deterministic:

- they normalize their own explain trace
- they carry their own source anchors and evidence
- they may include legality and delta posture
- they only admit promoted outcome kinds: `why`, `why-not`, and `what-if`
- overflow is explicit through `CounterfactualOverflowCount`

This keeps optional narration, presenter, or follow-up surfaces subordinate to core-owned packet truth instead of becoming calculation authority.

Verification commands:

- `CHUMMER_CORE_ENGINE_TEST_FILTER=explain-value-packets dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false`
- `python3 tests/test_explain_value_packet_receipt.py`
- `python3 scripts/verify-explain-value-packets.py --repo-root . --out .codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json`
