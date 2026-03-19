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
- legacy env-driven direct AI provider transport is now compatibility-only rather than part of the active headless-core boundary
- explain canon, runtime-bundle canon, restore/replay hardening, migration certification, and legacy-root quarantine are materially closed

## Go deeper

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
bash scripts/ai/verify.sh
```
