# Helper-Tooling Residual Backlog

Purpose: close `WL-108` by publishing an explicit backlog for remaining repo-surface helper utilities and plugin-era helper flows that still exist as compatibility cargo in `chummer6-core`.

## Scope rule

These surfaces are not active deterministic-engine ownership. They remain only for compatibility, migration, or operator support while purification follow-through stays explicit.

## Residual helper/plugin surfaces

| Surface | Current posture | Disposition | Follow-through ID | Milestone tie |
|---|---|---|---|---|
| `Plugins/ChummerHub.Client/` | legacy plugin-era WinForms + OIDC + named-pipe helper flow | migrate | `WL-108.1` | `F3` via `WL-D038` compatibility-cargo governance |
| `Plugins/SamplePlugin/` | legacy sample plugin scaffold for compatibility testing/docs | keep | `WL-108.2` | `A0.5.7` via `WL-092` helper-retirement guardrail posture |
| `Chummer/Plugins/` | legacy plugin loader/control path in oracle app root | remove (after extraction parity evidence) | `WL-108.3` | `F3` via `WL-100` + `WL-D038` legacy-root purification |
| `scripts/repo_tool.sh` + `scripts/repo_inspect.sh` + `scripts/read_file.sh` + `scripts/find_text.sh` + `scripts/replace_text_literal.sh` | repo-surface helper utilities used for maintenance/operator loops | keep | `WL-108.4` | `F3` release-maintenance posture (non-engine utility lane) |
| `scripts/git_commit_repo_work.sh` + `scripts/git_status.sh` + `scripts/upsert_env_var.sh` | repo-surface execution helpers; operational convenience only | keep | `WL-108.5` | `F3` release-maintenance posture (non-engine utility lane) |

## Execution backlog

| ID | Task | Exit criteria | Owner lane |
|---|---|---|---|
| `WL-108.1` | Migrate `Plugins/ChummerHub.Client` flow ownership out of core runtime authority (or freeze as explicit legacy-only cargo with no active-solution coupling). | Core active-solution verification stays clean; plugin flow is either moved behind compatibility boundary docs/tests or extracted to owning repo. | compatibility purification (`F3`) |
| `WL-108.2` | Keep `Plugins/SamplePlugin` as compatibility sample only. | No active engine/runtime project reference from sample plugin roots; guardrails continue to treat this as non-engine cargo. | helper retirement follow-through (`A0.5.7`) |
| `WL-108.3` | Plan and execute retirement of legacy `Chummer/Plugins` loader seams after extraction parity proof. | Removal or quarantine lands with parity evidence proving no deterministic-engine regression. | legacy-root purification (`F3`) |
| `WL-108.4` | Maintain repo helper scripts as operator tooling only. | Scripts remain decoupled from deterministic-engine semantic ownership and stay out of active runtime paths. | release maintenance (`F3`) |
| `WL-108.5` | Keep git/env utility scripts bounded to devops workflows. | Utilities remain optional helper surfaces and do not become canonical runtime behavior. | release maintenance (`F3`) |

## Closure statement

`WL-108` is satisfied by making helper/plugin residual scope explicit, dispositioned (`keep/remove/migrate`), and tied to milestone-backed follow-through lanes (`A0.5.7`, `F3`, `WL-092`, `WL-100`, `WL-D038`) instead of leaving helper-tooling scope as implied queue prose.
