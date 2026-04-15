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
- filesystem resolution for the queue proof anchors so stale or moved closeout evidence cannot keep the package marked passed
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
The checked-in receipt is also treated as a reproducible artifact: `tests/test_engine_proof_pack_generator.py` rebuilds the payload from repo-local evidence and compares it to `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`, ignoring only `generated_at`.

## Verification

The generator unit tests prove fail-closed behavior for missing evidence symbols, missing executable benchmark workloads, missing adjacent import oracles, release-channel promoted tuple drift, release-channel artifact shelf drift, successor registry or queue tokens that only appear on another milestone/package row, missing successor frontier id, unassigned successor queue allowed paths or owned surfaces, non-resolving successor queue proof anchors, core-task completion evidence that only appears on a later milestone-104 task, and checked-in receipt drift from generator output:

```bash
python3 tests/test_engine_proof_pack_generator.py
```

The core engine test harness enforces the generated proof pack shape and required coverage after regenerating the receipt:

```bash
dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1
dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll
```

The benchmark budget command listed in the proof pack remains the release command for measured workload budget enforcement.
