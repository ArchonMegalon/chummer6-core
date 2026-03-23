# WL-113.2 Disposition: compatibility-first naming/path references

Date: 2026-03-23
Scope: classify the `WL-113.1` reference inventory into `keep-temporary-exception` versus `migrate-now-candidate` so `WL-113.3` can execute migration/guardrail work without reopening hosted-contract ownership.

## Decision rule

- `migrate-now-candidate`: active-boundary project/solution reference still points to `Chummer.Contracts/Chummer.Contracts.csproj` and should move to package/bootstrap posture (`Chummer.Engine.Contracts` via canonical feed or explicit compatibility tree).
- `keep-temporary-exception`: reference is in a compatibility-only root and may stay temporarily while that root remains quarantine cargo.
- Hosted-contract ownership (`Chummer.Run.Contracts`, `Chummer.Hub.Registry.Contracts`, and sibling hosted planes) is explicitly out of scope for this slice.

## Path-reference disposition (`.sln`/`.csproj`)

| File | Prior class (`WL-113.1`) | Disposition | Why |
|---|---|---|---|
| `Chummer.CoreEngine.sln` | active-core | migrate-now-candidate | Active engine solution should stop implying monorepo-relative source reference as default bootstrap path. |
| `Chummer.sln` | active-core | migrate-now-candidate | Root active solution mirrors active engine project set and should converge on package/bootstrap canon. |
| `Chummer.Application/Chummer.Application.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Core/Chummer.Core.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Infrastructure/Chummer.Infrastructure.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj` | active-core | migrate-now-candidate | Active boundary project should consume canonical package/bootstrap seam. |
| `Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj` | active-core | migrate-now-candidate | Active-boundary verification project should validate package/bootstrap posture, not source-path defaulting. |
| `Chummer/Chummer.csproj` | compatibility-only | keep-temporary-exception | Legacy app/oracle cargo remains quarantine-bound per `docs/LEGACY_ROOT_SURFACE_INVENTORY.md`. |
| `Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj` | compatibility-only | keep-temporary-exception | Browser infrastructure remains compatibility cargo, not active engine path. |
| `Chummer.Tests/Chummer.Tests.csproj` | compatibility-only | keep-temporary-exception | Mixed historical verification cargo remains compatibility-only. |

## Naming-reference disposition (repo guidance text)

| File | Reference | Disposition | Why |
|---|---|---|---|
| `README.md` | "`Chummer.Engine.Contracts` package canon; source namespaces remain compatibility-first under `Chummer.Contracts.*`" | keep-temporary-exception | This statement is currently accurate and documents intentional temporary namespace compatibility. |
| `docs/CONTRACT_BOUNDARY_MAP.md` | Source project path and `Chummer.Contracts.*` namespace compatibility notes | keep-temporary-exception | Canon describes current truth; migration follow-up should preserve this until source namespaces or bootstrap mechanics actually change. |

## Summary for `WL-113.3`

- `migrate-now-candidate`: 10 active-boundary path references.
- `keep-temporary-exception`: 3 compatibility-only path references + 2 documentation truth statements.
- Next slice (`WL-113.3`) should implement migration and verifier guardrails for the 10 active-boundary candidates while preserving explicit temporary exceptions and hosted-contract out-of-scope boundaries.
