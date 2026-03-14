# Worklist Queue

Purpose: keep the live repo-native queue readable. Historical queue churn and duplicate reconciliation notes now live in `RECONCILIATION_LOG.md`.

## Status Keys
- `queued`
- `in_progress`
- `blocked`
- `done`

## Queue
| ID | Status | Priority | Task | Owner | Notes |
|---|---|---|---|---|---|
| WL-086 | done | P1 | Keep non-engine authority cleanup explicit until safe package-only cutover exists. | agent | Closed 2026-03-13: the remaining presentation, run-service, browser-infrastructure, and helper-tool spillover slices were decomposed and then closed with regression guardrails instead of staying as one vague “trust me” row. |
| WL-089 | done | P1 | Remove presentation-owned contract authority from `Chummer.Contracts`. | agent | Closed 2026-03-13: presentation DTOs moved out of the engine-facing contract root and regression guards now block them from reappearing. |
| WL-090 | done | P1 | Keep hosted contract authority out of engine-owned source. | agent | Closed 2026-03-13: `Chummer.Run.Contracts` remains the hosted contract plane and the core verification harness now treats hosted DTO regrowth as a defect. |
| WL-091 | done | P2 | Keep browser-only infrastructure quarantined away from the active engine boundary. | agent | Closed 2026-03-13: browser infrastructure is still visible as legacy cargo, but it is no longer allowed back into the active engine-owned execution path. |
| WL-092 | done | P2 | Confirm retired helper tooling stays outside the engine mission. | agent | Closed 2026-03-11: retired helper roots stay out of the active repo body and verification blocks them from being restored as if they still belonged to engine truth. |
| WL-097 | done | P1 | Archive historical reconciliation churn out of the live worklist. | agent | Completed 2026-03-14: the old queue ledger was preserved in `RECONCILIATION_LOG.md`, and this file now reflects current repo truth instead of replaying every exhausted slice forever. |

## Milestone Closure Map

These rows stay explicit so the repo can prove milestone decomposition without dragging the whole historical queue back into the active section.

| ID | Status | Milestone | Closure note |
|---|---|---|---|
| WL-068 | done | Milestone A6: contract hardening | Completed via `WL-073`, `WL-074`, and `WL-075`. |
| WL-073 | done | A6.1 canonicalize runtime install and BuildKit DTO ownership | Closure remains verifier-guarded and package-canon safe. |
| WL-074 | done | A6.2 add normalization fixtures for runtime install, BuildKit, and runtime compatibility DTOs | Closure remains verifier-guarded and deterministic. |
| WL-075 | done | A6.3 harden session/runtime compatibility projection seams | Closure remains verifier-guarded and deterministic. |
| WL-069 | done | Milestone A7: Structured Explain API hardening | Completed via `WL-076`, `WL-077`, and `WL-078`. |
| WL-076 | done | A7.1 expose keyed disabled-reason payloads across explainable selection/filter surfaces | Closure remains verifier-guarded and localization-safe. |
| WL-077 | done | A7.2 lock explain provenance and evidence envelopes | Closure remains verifier-guarded and evidence-safe. |
| WL-078 | done | A7.3 add before/after runtime diff explain fixtures | Closure remains verifier-guarded and diff-safe. |
| WL-070 | done | Milestone A8: Runtime/RulePack determinism hardening | Completed via `WL-079`, `WL-080`, and `WL-081`. |
| WL-079 | done | A8.1 harden runtime fingerprint byte-stability across ordering variance | Closure remains verifier-guarded and deterministic. |
| WL-080 | done | A8.2 add compile-order and provider-binding determinism tests | Closure remains verifier-guarded and deterministic. |
| WL-081 | done | A8.3 harden RulePack dependency resolution ordering | Closure remains verifier-guarded and deterministic. |
| WL-071 | done | Milestone A9: backend integration primitives | Completed via `WL-082`, `WL-083`, and `WL-084`. |
| WL-082 | done | A9.1 add journal/ledger timeline projection primitives | Closure remains verifier-guarded and downstream-safe. |
| WL-083 | done | A9.2 add validation summary and failure-envelope primitives | Closure remains verifier-guarded and downstream-safe. |
| WL-084 | done | A9.3 add explain-hook composition seam for backend integrations | Closure remains verifier-guarded and downstream-safe. |
| WL-072 | done | delete temporary contract source projects after package cutover | Closure remains explicit: temporary source-project roots stay deleted and package-only cutover evidence is locked in verification. |
| WL-089 | done | A0.5.4 follow-through | The presentation-contract authority closure runnable lane is closed but still named here so verifier parity does not drift. |
| WL-090 | done | A0.5.5 follow-through | The run-service contract authority closure runnable lane is closed but still named here so verifier parity does not drift. |
| WL-091 | done | A0.5.6 follow-through | The browser infrastructure authority closure runnable lane is closed but still named here so verifier parity does not drift. |

## Current repo truth

- Repo-local live queue: empty
- Remaining program blockers still live in canonical design truth: `A1`, `D1`, and the broader `vnext-foundation` release track in `.codex-design/product/PROGRAM_MILESTONES.yaml`
- Boundary warning: root-level legacy app/plugin and browser cargo still make the repo body broader than the README mission; that is tracked as design canon, not hidden as fake local feature work

## Historical log

- Full reconciliation history, queue-overlay drift, and repeated re-entry proof now live in `RECONCILIATION_LOG.md`.
