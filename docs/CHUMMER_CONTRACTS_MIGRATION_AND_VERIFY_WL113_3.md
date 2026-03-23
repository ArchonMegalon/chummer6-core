# WL-113.3 Migration + Verify Follow-up: canonical `Chummer.Engine.Contracts` posture

Date: 2026-03-23
Scope: publish and execute runnable migration + verification follow-up so active-core bootstrap defaults to package posture (`Chummer.Engine.Contracts`) while preserving explicitly quarantined compatibility exceptions.

## Executed migration

Active-core migration completed for the 10 `WL-113.2` migrate-now candidates:

- Removed `Chummer.Contracts/Chummer.Contracts.csproj` from active solutions:
  - `Chummer.CoreEngine.sln`
  - `Chummer.sln`
- Replaced direct source `ProjectReference` with package `PackageReference` in active consumers:
  - `Chummer.Application/Chummer.Application.csproj`
  - `Chummer.Core/Chummer.Core.csproj`
  - `Chummer.Infrastructure/Chummer.Infrastructure.csproj`
  - `Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj`
  - `Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj`
  - `Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj`
  - `Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj`
  - `Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj`

## Bootstrap posture

`Chummer.Engine.Contracts` bootstrap is now runnable in-repo with a deterministic local feed flow:

- Canonical package version property: `$(ChummerEngineContractsPackageVersion)` (default `0.0.0-local`) in `Directory.Build.props`.
- Local feed property: `$(ChummerEngineContractsLocalFeed)` (default `<repo>/.tmp/ai/local-nuget`) in `Directory.Build.props`.
- `RestoreAdditionalProjectSources` includes that local feed (unless `UseChummerEngineContractsLocalFeed=false`).
- `scripts/ai/bootstrap-contracts-feed.sh` packs `Chummer.Contracts/Chummer.Contracts.csproj` as `Chummer.Engine.Contracts` into the local feed.
- `scripts/ai/build.sh`, `scripts/ai/restore.sh`, and `scripts/ai/test_core_engine.sh` now call the bootstrap script and pass `ChummerEngineContractsPackageVersion` so restore/build stays aligned.

## Compatibility exceptions kept explicit

These remain temporary source-reference exceptions per `WL-113.2` and are intentionally not migrated in this slice:

- `Chummer/Chummer.csproj`
- `Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj`
- `Chummer.Tests/Chummer.Tests.csproj`

## Verifier guardrails (`scripts/ai/verify.sh`)

`verify.sh` now enforces all of the following:

- Active solutions (`Chummer.CoreEngine.sln`, `Chummer.sln`) must not include `Chummer.Contracts/Chummer.Contracts.csproj`.
- Active contract consumers must not include source `ProjectReference` to `Chummer.Contracts.csproj`.
- Active contract consumers must include package reference:
  - `PackageReference Include="Chummer.Engine.Contracts" Version="$(ChummerEngineContractsPackageVersion)"`
- Compatibility exceptions above must still carry their source `ProjectReference` until retirement closure.
- Bootstrap config/scripts remain present and wired (`Directory.Build.props`, `scripts/ai/build.sh`, `scripts/ai/restore.sh`, `scripts/ai/test_core_engine.sh`).

## Runnable verification

Canonical command:

```bash
bash scripts/ai/verify.sh
```

Expected outcome for this slice: pass with package-first active-core posture plus explicit compatibility-only exceptions.
