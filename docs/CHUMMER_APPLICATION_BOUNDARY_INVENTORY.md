# Chummer.Application Boundary Inventory

Last reviewed: 2026-03-22

Purpose: close `WL-106` by making `Chummer.Application` folder ownership explicit and by splitting migration candidates into deterministic-engine seams versus compatibility-only seams.

## Boundary classes

- `deterministic-engine seam`: belongs in `chummer6-core` because it computes deterministic mechanics, runtime, explain, or reducer-safe projections.
- `compatibility-only seam`: retained in this repo only as transitional cargo; target owner is another repo lane and extraction work should stay explicit.

## Per-folder inventory

| Folder | Class | Why | Owner lane | Split candidate |
|---|---|---|---|---|
| `Content/` | deterministic-engine seam | Runtime lock, rule pack/profile registry, install/runtime fingerprint services back deterministic engine truth. | `chummer6-core` (`E0` purification, `E1` runtime DTO canon) | Keep in active core boundary. |
| `Session/` | deterministic-engine seam (with compat residue) | Replay/projection/validator logic is deterministic session semantics; `NotImplementedSessionService` is compatibility scaffold. | `chummer6-core` (`E3` session reducer canon) | Keep projection/validator + owner-scoped semantics; quarantine or migrate `NotImplemented*` scaffolds with hosted/mobile consumers. |
| `Explain/` | deterministic-engine seam | Explain hook composition and normalization are engine explain provenance seams. | `chummer6-core` (`E2` explain canon) | Keep in active core boundary. |
| `Validation/` | deterministic-engine seam | Validation summary/failure-envelope construction is deterministic and localization-key safe. | `chummer6-core` (`E2` explain/validation canon) | Keep in active core boundary. |
| `BuildLab/` | deterministic-engine seam | Build-variant scoring/projection logic is deterministic mechanics support. | `chummer6-core` (`E6` Build Lab backend) | Keep in active core boundary. |
| `Journal/` | deterministic-engine seam | Journal/ledger/timeline projection ordering and diagnostics are deterministic projection primitives. | `chummer6-core` (`E5` explain backend completion) | Keep in active core boundary. |
| `Simulation/` | deterministic-engine seam | Heat/awareness/favor/healing computations are deterministic simulation math. | `chummer6-core` (`E0` purification) | Keep in active core boundary. |
| `Seeds/` | deterministic-engine seam | Seed generation services are deterministic rule-support outputs. | `chummer6-core` (`E0` purification) | Keep in active core boundary. |
| `Owners/` | deterministic-engine seam | `IOwnerContextAccessor` is a neutral owner-scope input seam used by deterministic services. | `chummer6-core` (`E0` purification) | Keep as core-owned abstraction only. |
| `LifeModules/` | deterministic-engine seam | Catalog interface maps to ruleset capability data and does not own presentation transport. | `chummer6-core` (`E4` ruleset ABI stabilization) | Keep in core; implement via ruleset-owned providers only. |
| `AI/` | compatibility-only seam | Contains provider-routing, transport, recap/media scaffolds, and many `NotImplemented*` compatibility stubs; this is hosted orchestration, not engine truth. | `chummer6-hub` / `chummer6-media-factory` (`C1b`, `E3`, `E4`) | Extract hosted AI orchestration interfaces/services out of `Chummer.Application`; leave only deterministic prompt/explain helpers if still required by core. |
| `Hub/` | compatibility-only seam | Publication/catalog/review/moderation services reference hub/presentation contract families and model hosted workflows. | `chummer6-hub` and `chummer6-hub-registry` (`E2`, `E2b`) | Migrate hub catalog/review/publish/moderation service contracts and stores to hosted owner repos; keep core as downstream consumer of package contracts only. |
| `Tools/` | compatibility-only seam | Shell preferences/session/roster/tool catalog surfaces are presentation/workbench concerns, not deterministic mechanics. | `chummer6-ui` / `chummer6-mobile` (`B`, `E0`) | Extract shell/tool preference APIs from core boundary; preserve only engine-safe export abstractions needed by deterministic lanes. |
| `Workspaces/` | compatibility-only seam (mixed) | Workspace import/export/session document lifecycle is product-shell behavior; only ruleset codec abstraction is engine-adjacent. | `chummer6-ui` / `chummer6-mobile` with core codec contract support (`B`, `E0`) | Split: keep ruleset codec contracts needed for deterministic parsing; migrate persistence/import session orchestration services to shell repos. |
| `Characters/` | compatibility-only seam | Character query/metadata interface family is application/query orchestration, not deterministic rules math implementation. | `chummer6-ui` / `chummer6-mobile` (`B`, `E0`) | Move interface ownership to consuming heads, keeping contract DTOs package-owned in canonical contract repos. |

## Immediate split queue candidates

| Candidate | Scope | Class | Owner lane |
|---|---|---|---|
| `WL-106.1` | Extract `Chummer.Application/AI` hosted-provider orchestration and media scaffolds behind hub/media-factory-owned adapters. | compatibility-only seam | `chummer6-hub` (`C1b`) + `chummer6-media-factory` (`E4`) |
| `WL-106.2` | Migrate `Chummer.Application/Hub` catalog/review/publication/moderation service ownership to hosted owner repos. | compatibility-only seam | `chummer6-hub` + `chummer6-hub-registry` (`E2`, `E2b`) |
| `WL-106.3` | Split `Workspaces/` into deterministic codec contract seam (core) versus shell persistence/import orchestration (ui/mobile). | mixed seam | `chummer6-core` + `chummer6-ui` + `chummer6-mobile` (`E0`) |
| `WL-106.4` | Remove presentation shell tool/session preference ownership from `Tools/` and relocate to workbench/play heads. | compatibility-only seam | `chummer6-ui` / `chummer6-mobile` (`B`) |
| `WL-106.5` | Move `Characters/` query/metadata orchestration interfaces to consumer heads while retaining DTO package canon. | compatibility-only seam | `chummer6-ui` / `chummer6-mobile` (`B`) |

## Boundary guard intent

- Active deterministic boundary remains `Content`, `Session` (deterministic subset), `Explain`, `Validation`, `BuildLab`, `Journal`, `Simulation`, `Seeds`, `Owners`, and `LifeModules`.
- Compatibility-only folders listed above should not gain new deterministic math/reducer authority.
- Future extraction PRs should reference the `WL-106.x` candidate id they are closing.
