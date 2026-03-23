# WL-113.1 Inventory: `Chummer.Contracts.csproj` source references

Date: 2026-03-23
Scope: inventory all current `.sln` and `.csproj` references that still point to `Chummer.Contracts/Chummer.Contracts.csproj`, then classify each site as `active-core` or `compatibility-only`.

## Method

- Searched repo-local `.sln`, `.slnx`, and `.csproj` files with:
  - `rg -n --glob '*.sln' --glob '*.slnx' --glob '*.csproj' 'Chummer[./\\]Contracts[./\\]Chummer\.Contracts\.csproj|Chummer\.Contracts\.csproj'`
- Classified each match using active-boundary policy from:
  - `docs/LEGACY_ROOT_SURFACE_INVENTORY.md`
  - `Chummer.CoreEngine.sln` and `Chummer.sln`

## Results

Total reference sites: 13

| File | Reference type | Class | Notes |
|---|---|---|---|
| `Chummer.CoreEngine.sln` | solution project include | active-core | Active engine solution includes the contracts project as `Chummer.Engine.Contracts`. |
| `Chummer.sln` | solution project include | active-core | Root active solution mirrors `Chummer.CoreEngine.sln` active project set. |
| `Chummer.Application/Chummer.Application.csproj` | project reference | active-core | `Chummer.Application` is inside active boundary. |
| `Chummer.Core/Chummer.Core.csproj` | project reference | active-core | `Chummer.Core` is inside active boundary. |
| `Chummer.Infrastructure/Chummer.Infrastructure.csproj` | project reference | active-core | `Chummer.Infrastructure` is inside active boundary. |
| `Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj` | project reference | active-core | `Chummer.Rulesets.*` roots are active boundary. |
| `Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj` | project reference | active-core | `Chummer.Rulesets.*` roots are active boundary. |
| `Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj` | project reference | active-core | `Chummer.Rulesets.*` roots are active boundary. |
| `Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj` | project reference | active-core | `Chummer.Rulesets.*` roots are active boundary. |
| `Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj` | project reference | active-core | Test project is in the active solution and validates active boundary behavior. |
| `Chummer/Chummer.csproj` | project reference | compatibility-only | `Chummer/` is explicitly legacy compatibility cargo (`WL-100` inventory policy). |
| `Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj` | project reference | compatibility-only | Browser infrastructure root is explicitly compatibility cargo. |
| `Chummer.Tests/Chummer.Tests.csproj` | project reference | compatibility-only | `Chummer.Tests/` is explicitly mixed historical verification compatibility cargo. |

## Slice closure

- `WL-113.1` is complete: current reference inventory and class split are now explicit and repo-native.
- Follow-up remains in queue as `WL-113.2` (disposition: keep-temporary vs migrate-now) and `WL-113.3` (migration + verifier guardrails).
