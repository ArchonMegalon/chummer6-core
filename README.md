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

## Go deeper

- `docs/ENGINE_BOUNDARY.md`
- `.codex-design/repo/IMPLEMENTATION_SCOPE.md`
- `.codex-design/review/REVIEW_CONTEXT.md`

## Verification

Run:

```bash
bash scripts/ai/verify.sh
```
