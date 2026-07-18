# chummer6-core

Deterministic engine and rules truth for Chummer6.

## What this repo is

`chummer6-core` is the repo where the math stops bluffing.

It owns:

- engine runtime and reducer truth
- explain and provenance receipts
- runtime bundles and fingerprints
- engine-facing shared interfaces

## What this repo is not

This repo does not own:

- the workbench UX
- the player or GM shell
- hosted orchestration
- render-only media execution

## Current mission

The job here is purification by deletion and package canon:

- keep one canonical engine contract family
- strip away old cross-boundary ownership
- make the repo read unmistakably like engine truth
- consume hosted contract planes only from their owner repos

Current honesty clause:

- the engine boundary is directionally right
- the repo body is still heavier than it should be
- package canon is already `Chummer.Engine.Contracts`, while source namespaces remain compatibility-first under `Chummer.Contracts.*`
- hosted package seams such as `Chummer.Run.Contracts` and `Chummer.Hub.Registry.Contracts` are no longer source-owned here
- hosted package seams restore from the exact source commits in `eng/package-plane.lock.json`; sibling DLL outputs are not an active build input
- legacy env-driven direct AI provider transport is now compatibility-only rather than part of the active headless-core boundary
- explain canon, runtime-bundle canon, restore/replay hardening, migration certification, and legacy-root quarantine are materially closed

## Go deeper

Legacy root `chummer-core-engine.design*.md` files remain only as compatibility aliases. Use `.codex-design/*` as the live canon.

- `.codex-design/product/ARCHITECTURE.md`
- `docs/CONTRACT_BOUNDARY_MAP.md`
- `docs/EXPLAIN_AND_RUNTIME_CANON.md`
- `docs/CORE_RUNTIME_RESTORE_RUNBOOK.md`
- `docs/LEGACY_MIGRATION_CERTIFICATION.md`
- `docs/LEGACY_ROOT_SURFACE_INVENTORY.md`
- `docs/ENGINE_BOUNDARY.md`
- `.codex-design/review/REVIEW_CONTEXT.md`

## Verification

Run:

```bash
bash scripts/ai/verify-no-siblings-package-plane.sh
bash scripts/ai/verify.sh
bash scripts/ai/test-ruleset-depth.sh
bash scripts/ai/coverage.sh Chummer.Tests/Chummer.Tests.csproj
bash scripts/ai/test-matrix.sh Chummer.Tests/Chummer.Tests.csproj
```

The no-siblings lane creates an empty consumer checkout and NuGet cache, builds the owner-contract packages from the immutable commits in `eng/package-plane.lock.json`, and restores Core through that generated feed. It does not require or inspect ambient sibling repositories.

`scripts/ai/test-ruleset-depth.sh` is the fastest explicit ruleset-depth gate. It pins `Chummer.Tests` to Linux `net10.0`, covers the SR4/SR5/SR6 ruleset seam slice, runs the core executable audit, and writes `.codex-studio/published/RULESET_DEPTH_LINUX_GATE.generated.json`.

`scripts/ai/test-matrix.sh` is the host-aware entrypoint for the current engine matrix:

- always runs the Linux `net10.0` test lane
- always builds the `net10.0-windows` target
- only attempts Windows desktop execution when `Microsoft.WindowsDesktop.App 10.x` is available on the host

So from Linux you get truthful source/build coverage for the Windows target, while native Windows host execution remains an explicit final certification step instead of a hidden assumption.

Do not use a bare Linux `dotnet test Chummer.Tests/Chummer.Tests.csproj` as a release signal. That project is intentionally multi-targeted (`net10.0;net10.0-windows`), so Linux ruleset verification must either use `scripts/ai/test-ruleset-depth.sh`, `scripts/ai/test-matrix.sh`, or explicitly pass `-f net10.0`.

For final native-host certification use:

```bash
bash scripts/ai/test-native-host-matrix.sh Chummer.Tests/Chummer.Tests.csproj
```

That wrapper requires real Windows desktop execution on Windows hosts instead of silently accepting a compile-only pass.

`scripts/ai/coverage.sh` collects Linux `net10.0` coverage with the `XPlat Code Coverage` collector and writes a Cobertura summary JSON under `.artifacts/coverage/summary.json`.
