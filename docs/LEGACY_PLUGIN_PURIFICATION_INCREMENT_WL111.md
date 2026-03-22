# Legacy Plugin Purification Increment (`WL-111`)

Last reviewed: 2026-03-22

Purpose: execute the next helper/plugin residual purification increment from `docs/HELPER_TOOLING_RESIDUAL_BACKLOG.md` for `WL-108.1` and `WL-108.3` without widening hosted-contract ownership.

## Scope executed in this increment

| Backlog ID | Increment outcome | Evidence |
|---|---|---|
| `WL-108.1` | `Plugins/ChummerHub.Client` remains explicit compatibility-only cargo and is kept outside active core-engine solutions and project-coupling lanes. | `Chummer.CoreEngine.sln` and repo-root `Chummer.sln` stay free of plugin project entries; active core boundary guardrails in `Chummer.CoreEngine.Tests/Program.cs` now fail if active-solution projects couple to `Plugins/ChummerHub.Client`. |
| `WL-108.3` | Legacy `Chummer/Plugins` loader seams are put on an explicit retirement gate with parity-first extraction proof; no new deterministic-engine authority is allowed through this path. | Guardrails fail if active-solution projects couple to `Chummer/Plugins`; this doc plus `WORKLIST.md` keep the retirement lane explicit and bounded to compatibility purification. |

## Retirement gate (`WL-108.3`)

`Chummer/Plugins` remains legacy oracle cargo only until extraction parity proof is complete.

Required proof before physical removal/quarantine of loader seams:

1. Legacy parity checks no longer require runtime plugin loader paths from `Chummer/Plugins`.
2. Any retained plugin compatibility behavior is represented by explicit compatibility docs/tests, not by active engine boundary coupling.
3. `bash scripts/ai/verify.sh` remains green with `Chummer/Plugins` guarded as non-active-solution cargo.

## Ownership guardrail

This increment does not reintroduce hosted contract authority into `chummer6-core`.
`Chummer.Engine.Contracts` package-boundary ownership and hosted-owner package seams remain unchanged.
