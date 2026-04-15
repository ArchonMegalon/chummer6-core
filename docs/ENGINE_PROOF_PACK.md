# Engine Proof Pack

## Purpose

`ENGINE_PROOF_PACK.generated.json` is the release-bound core proof contract for milestone `104`.
It keeps engine trust evidence machine-readable so desktop release polish cannot outrun mechanical confidence.

## Artifact

- path: `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`
- contract: `chummer6-core.engine_proof_pack`

## Required coverage

The proof pack must fail closed unless it includes:

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

## Verification

The core engine test harness enforces the proof pack shape and required coverage:

```bash
dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release
```

The benchmark budget command listed in the proof pack remains the release command for measured workload budget enforcement.
