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
- finishing `A1` means making that physical cleanup visible, not just winning arguments about architecture
- package canon is already `Chummer.Engine.Contracts`, while source namespaces remain compatibility-first under `Chummer.Contracts.*`
- hosted package seams such as `Chummer.Run.Contracts` and `Chummer.Hub.Registry.Contracts` are no longer source-owned here

## Go deeper

- `.codex-design/product/ARCHITECTURE.md`
- `docs/CONTRACT_BOUNDARY_MAP.md`
- `docs/ENGINE_BOUNDARY.md`
- `.codex-design/review/REVIEW_CONTEXT.md`

## Verification

Run:

```bash
bash scripts/ai/verify.sh
```
