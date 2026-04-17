# Next90 M104 Core Proof Pack Closeout

Package: `next90-m104-core-proof-pack`
Frontier: `3227666051`
Milestone: `104`
Owner: `chummer6-core`

## Closed Scope

The core-owned successor slice is closed when all of these remain true:

- `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` reports `status=passed`.
- `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` carries top-level `package_id=next90-m104-core-proof-pack`, `frontier_id=3227666051`, and `milestone_id=104`.
- `successor_wave_authority` reports `status=passed`.
- The Fleet queue mirror and design-owned queue each contain exactly one `next90-m104-core-proof-pack` row.
- The Fleet queue mirror and design-owned queue package rows keep matching proof lists; `queue_mirror_parity_status` remains `passed`.
- The Fleet queue mirror and design-owned queue proof lists contain no duplicated proof item.
- That row remains `status: complete`, keeps `frontier_id: 3227666051`, and keeps `landed_commit: 00800059`.
- The row keeps only the assigned allowed paths: `src`, `tests`, `docs`, and `scripts`.
- The row keeps only the assigned owned surfaces: `engine_proof_pack` and `import_oracle_discipline`.
- Queue proof anchors resolve inside `/docker/chummercomplete/chummer-core-engine`.
- Every absolute `/docker/chummercomplete/...` proof path in either queue row resolves inside `/docker/chummercomplete/chummer-core-engine`; added sibling-repo or missing package-local proof paths fail closed.
- Local commit proof includes `5a649e57`, `c01dfa10`, `1a98d904`, `af67ecfd`, `870be707`, `498dff3d`, `ecbb466c`, `a2c8ad9f`, `2c98f61c`, `2e4e8e81`, `b5d46938`, and `c1300863`, the M104 proof pack handoff, queue-mirror parity guard, and current proof-guard anchors.
- Registry and queue evidence do not cite task-local telemetry, task-local telemetry field names such as `frontier_briefs`, `first_commands`, `status_query_supported`, `polling_disabled`, or `slice_summary`, active-run handoff files, active-run handoff field labels, operator telemetry, operator status snippets, supervisor-status or ETA helpers, supervisor helper loops, or active-run helper command output as release proof. The queue rule applies to both the Fleet mirror and the design-owned queue row.
- Registry, queue, closeout, and import-certification evidence do not cite implementation-only retry prompt fragments, including previous-attempt or previous attempt text, as release proof.
- Registry and queue evidence do not cite retry-orientation prompt fragments such as exact-command orientation, current steering focus, direct-read handoff context, writable scope roots, or stop-report templates as release proof.
- Registry, queue, closeout, and import-certification evidence do not cite copied worker-run prohibitions such as "do not run supervisor status or eta helpers" as release proof.
- Import-certification command and evidence lists remain exact release-bound receipts: only `dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release` and `core-engine-tests: ok` may satisfy the source receipt.
- Import-certification coverage counts and `coverage_percent` remain JSON integers, not string-encoded numbers, floats, booleans, under-100-percent summaries, or totals that omit the adjacent Genesis and CommLink6 oracle rows; performance-budget thresholds remain JSON numeric values.
- The same proof-hygiene ban applies after percent-decoding and HTML unescaping, after URL form-decoding, and after separator normalization, so encoded or separator-obfuscated active-run paths, task-local telemetry, or supervisor-helper evidence cannot close the package.
- Release-channel desktop tuple and artifact ids remain unique, so promoted desktop proof cannot pass by hiding a stale duplicate route or shelf receipt.
- Release-channel artifact and desktop route-truth rows remain structured objects with non-empty ids, so malformed shelf rows cannot be ignored while promoted tuple proof stays green.

## Do Not Reopen

Do not reopen this core package for adjacent M104 work. Explain-receipt UI surfaces belong to the UI-owned M104 package, and any later release-channel, desktop, or support packet drift belongs to its own owner package.

Future shards assigned this package should verify:

```bash
python3 scripts/generate-engine-proof-pack.py --check
python3 tests/test_engine_proof_pack_generator.py
dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1
dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll
dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json
```

If those checks pass and the canonical queue row still matches the closed scope above, the correct action is to advance a different open successor slice.
