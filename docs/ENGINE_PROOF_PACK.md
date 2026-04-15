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
- queue closeout proof that the successor queue row is marked `status: complete`, cites landed commit `00800059`, and lists the proof anchors for this generated pack, generator, tests, and documentation
- row-scoped registry and queue validation so tokens from another successor milestone or package cannot satisfy `next90-m104-core-proof-pack`
- oracle suites: `creation`, `advancement`, `augment`, `matrix`, `magic`, `vehicle`, `source_toggle`, `amend_package`
- performance budget lanes: `load`, `explain`, `diff_apply`, `import`, `export_prep`
- import-oracle discipline status sourced from `IMPORT_PARITY_CERTIFICATION.generated.json`
- release commands with existing repo-local project and budget inputs
- evidence anchors that resolve to checked-in files; anchors with `::` must also resolve to a symbol or stable token in that file

All required performance lanes must resolve to named workloads in `Chummer.Benchmarks/workspace-benchmark-budgets.json`.
They must also resolve to executable workload evidence in `Chummer.Benchmarks/MigrationWorkspaceBenchmarks.cs`.
The proof generator fails closed when a required budget lane is missing from either the budget file or the executable benchmark workload source.

The import-oracle discipline lane requires named coverage for Chummer4, Chummer5a, Hero Lab Classic, Genesis, and CommLink6.

## Generation

From repo root:

```bash
python3 scripts/generate-engine-proof-pack.py
```

The generator treats the generated proof pack path as a planned output so a clean first run cannot fail only because `ENGINE_PROOF_PACK.generated.json` does not already exist.

## Verification

The generator unit tests prove fail-closed behavior for missing evidence symbols, missing executable benchmark workloads, missing adjacent import oracles, and successor registry or queue tokens that only appear on another milestone/package row:

```bash
python3 tests/test_engine_proof_pack_generator.py
```

The core engine test harness enforces the generated proof pack shape and required coverage after regenerating the receipt:

```bash
dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release
```

The benchmark budget command listed in the proof pack remains the release command for measured workload budget enforcement.
