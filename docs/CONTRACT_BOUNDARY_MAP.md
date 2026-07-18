# Contract Boundary Map

Last reviewed: 2026-03-19

## Purpose

This repo still carries historical source namespaces like `Chummer.Contracts.*`, but the canonical package and assembly boundary is:

- source project: `Chummer.Contracts/Chummer.Contracts.csproj`
- assembly name: `Chummer.Engine.Contracts`
- package id: `Chummer.Engine.Contracts`

That split is intentional until a future namespace migration lands. Package ownership is already canonical even though source namespaces remain compatibility-first.

## Ownership Rules

- `Chummer.Engine.Contracts` is the only canonical engine/shared contract package.
- Engine runtime, explain, reducer, and semantic session DTOs live under `Chummer.Contracts`.
- `Chummer.Contracts.Session` is owned only by `Chummer.Contracts/Session`.
- Hosted orchestration contracts do not belong in this repo at all; they are consumed from sibling owner repos.
- Consumer repos may project or transport engine session state, but they must not redefine semantic event meaning.

## Compatibility Notes

- The older source namespace root remains `Chummer.Contracts.*`.
- The package boundary the rest of the program consumes is `Chummer.Engine.Contracts`.
- `Chummer.Run.Contracts` and `Chummer.Hub.Registry.Contracts` are owned by sibling repos and are consumed here as source-commit-locked NuGet packages.
- `eng/package-plane.lock.json` binds each owner package to an immutable repository commit and one package-plane version; the package bootstrap builds those exact sources into the existing repo-local feed.
- Active builds never resolve these hosted contracts through sibling `bin/` paths or ambient sibling checkouts.
- Legacy compatibility cargo elsewhere in this repo does not get to redefine engine session semantics.

## Verification Hooks

`bash scripts/ai/verify.sh` enforces:

- `docs/CONTRACT_BOUNDARY_MAP.md` exists
- no repo-local `Chummer.Run.Contracts` source mirror exists
- no active or compatibility project contains a sibling-repository `HintPath`
- every hosted-contract consumer pins `PackageReference` through the package-plane version properties
- the no-siblings lane restores into an empty package cache using only the generated locked feed plus the explicitly mapped NuGet.org source
- no file outside `Chummer.Contracts/Session` defines `namespace Chummer.Contracts.Session`
- no file outside `Chummer.Contracts/Session` defines `ISessionEvent`, `EffectAppliedEvent`, or `TrackerIncrementedEvent`
