# Engine Proof Pack

## Purpose

`ENGINE_PROOF_PACK.generated.json` is the release-bound core proof contract for milestone `104`.
It keeps engine trust evidence machine-readable so desktop release polish cannot outrun mechanical confidence.

## Artifact

- path: `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`
- contract: `chummer6-core.engine_proof_pack`
- successor package: `next90-m104-core-proof-pack`
- successor frontier: `3227666051`

## Required coverage

The proof pack must fail closed unless it includes:

- successor-wave authority for milestone `104`, owned surfaces `engine_proof_pack` and `import_oracle_discipline`, and the canonical successor registry plus queue staging paths
- queue closeout proof that the successor queue row is marked `status: complete`, pins frontier `3227666051`, cites landed commit `00800059`, and lists the proof anchors for this generated pack, generator, tests, and documentation
- queue and registry closeout proof for commit `8dd516ef`, which makes failed generator runs exit nonzero while still writing diagnostic receipts
- queue and registry closeout proof for commit `c88178fa`, which proves design-owned queue scope drift fails closed
- queue and registry closeout proof for commit `769e7259`, which binds this completed package to the latest local guard chain
- queue and registry closeout proof for commit `d4b3b0ba`, which requires the current `769e7259` guard in the generated proof pack, unit tests, and documentation
- queue and registry closeout proof for commit `a2173476`, which requires the current `d4b3b0ba` guard in the generated proof pack, unit tests, and documentation
- queue and registry closeout proof for commit `4b124997`, which binds the proof-pack generator, tests, documentation, and checked-in receipt to active-run hygiene guard `4a56911d`
- queue and registry closeout proof for commit `b488d109`, which pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it
- queue and registry closeout proof for commit `b6fddf74`, which tightens the current M104 proof pack authority guard so future shards verify the latest closed package
- queue and registry closeout proof for commit `f6608678`, which tightens the latest M104 proof pack local guard so future shards verify the closed package
- queue and registry closeout proof for commit `a3cbb548`, which refreshes the checked-in M104 proof receipt after latest local guard tightening
- queue and registry closeout proof for commit `df0527b2`, which tightens the M104 proof pack receipt guard so future shards verify the latest closed package
- queue and registry closeout proof for commit `8574f63f`, which pins the M104 proof pack receipt guard
- queue and registry closeout proof for commit `6b3a662c`, which requires the current `8574f63f` guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `3b63478f`, which pins the current `6b3a662c` guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `cd30503f`, which pins the current `d2ee91a9` engine proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `e10f2739`, which pins the current `cd30503f` queue proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `3c242c2f`, which pins the current `f914ce6a` helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `ea449f7b`, which pins the current `c2872b40` queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `18365058`, which pins the current `ea449f7b` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `5031ee41`, which pins the current `18365058` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `cbce6a19`, which pins the current `5031ee41` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `71441924`, which pins the current `cbce6a19` queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `df1330b4`, which pins the latest `71441924` queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local git commit proof that the cited M104 guard commits, including guards `56048971`, `769e7259`, `d4b3b0ba`, `a2173476`, `dafc1205`, `65df3894`, `4a56911d`, `4b124997`, `2187db33`, `b488d109`, `b6fddf74`, `3b9a29c2`, `f6608678`, `a3cbb548`, `df0527b2`, `8574f63f`, `6b3a662c`, `3b63478f`, `31c75c02`, `ef46554c`, `0771b7ea`, `fdb6a273`, `d2ee91a9`, `cd30503f`, `e10f2739`, `e7d4270e`, `bbc877d7`, `56ff7283`, `7ae79416`, `a613bdb2`, `353921e7`, `9de2455b`, `d8e826a3`, `7a1f0e7c`, `d464cfab`, `a1a2d956`, `abf63719`, `bbc7fba8`, `a1a1d505`, `18d03556`, `77cb53cf`, `f914ce6a`, `3c242c2f`, `c2872b40`, `ea449f7b`, `18365058`, `5031ee41`, `cbce6a19`, `71441924`, and `df1330b4`, resolve in this repository before the release-bound proof pack can pass
- case-insensitive active-run proof hygiene, so registry or queue evidence for this closed package cannot cite active-run handoff files, operator telemetry, or active-run helper output as release proof
- design-owned queue closeout proof from `/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml`, in addition to the Fleet staging mirror, so Fleet-local staging cannot be the only authority that keeps the package closed
- design-owned queue scope proof that fails closed if the canonical queue row adds unassigned allowed paths or owned surfaces even when the Fleet staging mirror stays clean
- filesystem resolution for the queue proof anchors so stale or moved closeout evidence cannot keep the package marked passed
- package-local canonical proof anchors, so successor queue closeout cannot stay green by citing sibling package proof under `/docker/chummercomplete/...` instead of `chummer-core-engine`
- row-scoped queue authority for the assigned allowed paths: `src`, `tests`, `docs`, and `scripts`
- exact row-scoped queue authority for allowed paths and owned surfaces, so later queue edits cannot widen the package beyond `src`, `tests`, `docs`, `scripts`, `engine_proof_pack`, and `import_oracle_discipline` while keeping the proof pack green
- row-scoped registry and queue validation so tokens from another successor milestone or package cannot satisfy `next90-m104-core-proof-pack`
- task-scoped registry validation so completion and evidence for core tasks `104.1` and `104.2` cannot be satisfied by later milestone-104 tasks owned by another package
- oracle suites: `creation`, `advancement`, `augment`, `matrix`, `magic`, `vehicle`, `source_toggle`, `amend_package`
- performance budget lanes: `load`, `explain`, `diff_apply`, `import`, `export_prep`
- import-oracle discipline status sourced from `IMPORT_PARITY_CERTIFICATION.generated.json`
- release-channel binding sourced from `/docker/chummercomplete/chummer-hub-registry/.codex-studio/published/RELEASE_CHANNEL.generated.json`
- release commands with existing repo-local project and budget inputs
- evidence anchors that resolve to checked-in files; anchors with `::` must also resolve to a symbol or stable token in that file

All required performance lanes must resolve to named workloads in `Chummer.Benchmarks/workspace-benchmark-budgets.json`.
They must also resolve to executable workload evidence in `Chummer.Benchmarks/MigrationWorkspaceBenchmarks.cs`.
The proof generator fails closed when a required budget lane is missing from either the budget file or the executable benchmark workload source.

The import-oracle discipline lane requires named coverage for Chummer4, Chummer5a, Hero Lab Classic, Genesis, and CommLink6.

The release-channel binding requires the current release shelf to be `published`, `promoted_preview`, release-proof `passed`, and desktop tuple coverage `complete`.
It also fail-closes unless the promoted primary Avalonia installer tuples resolve for Linux, Windows, and macOS:

- `avalonia:linux:linux-x64`
- `avalonia:windows:win-x64`
- `avalonia:macos:osx-arm64`

Each required tuple must remain `routeRole=primary`, `promotionState=promoted`, `parityPosture=flagship_primary`, `updateEligibility=eligible`, `revokeState=not_revoked`, and `installPosture=installer_first`, with its artifact id present on the release shelf.

## Generation

From repo root:

```bash
python3 scripts/generate-engine-proof-pack.py
```

The generator treats the generated proof pack path as a planned output so a clean first run cannot fail only because `ENGINE_PROOF_PACK.generated.json` does not already exist. Other successor queue proof anchors must resolve on disk.
Both the Fleet staging queue and the design-owned staging queue must retain the completed `next90-m104-core-proof-pack` row with the same frontier, allowed paths, owned surfaces, landed commit, and proof anchors.
The checked-in receipt is also treated as a reproducible artifact: `tests/test_engine_proof_pack_generator.py` rebuilds the payload from repo-local evidence and compares it to `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`, ignoring only `generated_at`.
The generator still writes the diagnostic receipt when evidence is missing, but exits nonzero whenever the generated pack status is not `passed`.

## Verification

The generator unit tests prove fail-closed behavior for missing evidence symbols, missing executable benchmark workloads, missing adjacent import oracles, release-channel promoted tuple drift, release-channel artifact shelf drift, successor registry or queue tokens that only appear on another milestone/package row, missing successor frontier id, unassigned successor queue allowed paths or owned surfaces, non-resolving successor queue proof anchors, sibling package proof anchors under `/docker/chummercomplete/...`, case-insensitive active-run telemetry, handoff, or helper proof citations in registry and queue evidence, missing local closeout commits including the current `d2ee91a9` engine proof floor, the queue-cited `cd30503f` floor pin, the `e10f2739` queue proof floor, the `e7d4270e` queue proof floor guard, the `bbc877d7` proof floor guard, the `56ff7283` proof floor guard, the `7ae79416` proof floor guard, the `a613bdb2` engine proof pack guard, the `353921e7` engine proof pack guard floor, the `9de2455b` proof pack guard floor, the `d8e826a3` proof pack guard floor, the `7a1f0e7c` proof pack guard floor, the `d464cfab` proof pack guard floor, the `a1a2d956` proof pack local floor, the `abf63719` proof pack local floor, the `bbc7fba8` engine proof pack floor, the `a1a1d505` engine proof pack floor, the `18d03556` active-run helper hygiene floor, the `77cb53cf` helper hygiene proof floor, the `f914ce6a` helper hygiene proof floor pin, the `3c242c2f` helper hygiene queue citation, the `c2872b40` queue proof floor guard, the `ea449f7b` queue proof guard pin, the `18365058` queue proof guard floor, the `5031ee41` queue proof guard floor, the `cbce6a19` queue proof guard floor, the `71441924` queue proof floor, and the current `df1330b4` queue proof floor pin, design-owned queue authority and scope drift, core-task completion evidence that only appears on a later milestone-104 task, and checked-in receipt drift from generator output:

```bash
python3 tests/test_engine_proof_pack_generator.py
```

The core engine test harness enforces the generated proof pack shape and required coverage after regenerating the receipt:

```bash
dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1
dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll
```

The benchmark budget command listed in the proof pack remains the release command for measured workload budget enforcement.
