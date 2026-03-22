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
| WL-112 | done | P2 | Execute next residual core purification + hardening evidence cycle: keep `Plugins/SamplePlugin` compatibility-only (`WL-108.2`), keep repo helper/git-env utilities operational-only (`WL-108.4`/`WL-108.5`), and revalidate that observability/DR + migration-certification closure evidence remains verifier-backed without reopening hosted contract ownership. | agent | Closed 2026-03-22: added `docs/LEGACY_PLUGIN_AND_HELPER_OPERATIONAL_EVIDENCE_WL112.md`, extended verifier guardrails for `WL-108.2`/`WL-108.4`/`WL-108.5`, and kept `F1`/`F2` closure evidence verifier-backed without reopening hosted contract authority. |
| WL-109 | done | P1 | Implement structured observability hardening for the active API + head flows (`MIG-091`) with correlation-id propagation and verification coverage. | agent | Closed 2026-03-22: `SessionApiResult`/`SessionNotImplementedReceipt` now carry structured observability envelopes (`operation`, `correlationId`, `traceId`, metrics seam + tags), `OwnerScopedSessionService` and `NotImplementedSessionService` emit those envelopes across active session API flows, and `Chummer.CoreEngine.Tests` regression coverage locks the contract seams. |
| WL-110 | done | P2 | Define and enforce workspace/session retention + cleanup operational policy (`MIG-093`) with runbook and automation hooks. | agent | Closed 2026-03-22: published `docs/WORKSPACE_RETENTION_POLICY.md`, added `RUNBOOK_MODE=retention-cleanup` + `RUNBOOK_MODE=retention-cleanup-smoke` to `scripts/runbook.sh`, and extended `scripts/ai/verify.sh` to enforce retention policy/runbook evidence. |
| WL-111 | done | P2 | Execute next legacy cargo purification increment from published compatibility backlog (`WL-108.1` and `WL-108.3`). | agent | Closed 2026-03-22: executed compatibility-freeze + retirement-gate increment with verifier guardrails that keep active core solutions decoupled from `Plugins/ChummerHub.Client` and `Chummer/Plugins`, with evidence in `docs/LEGACY_PLUGIN_PURIFICATION_INCREMENT_WL111.md`. |
| WL-106 | done | P1 | Publish `Chummer.Application` boundary inventory and split candidates into deterministic-engine vs compatibility-only seams. | agent | Closed 2026-03-22: published `docs/CHUMMER_APPLICATION_BOUNDARY_INVENTORY.md` with per-folder boundary class, owner lane, and explicit `WL-106.x` split candidates separating deterministic-engine seams from compatibility-only seams. |
| WL-107 | done | P1 | Add verifier guardrails that block browser infrastructure coupling from active engine projects and `Chummer.Application` ownership drift. | agent | Closed 2026-03-22: `Chummer.CoreEngine.Tests` now fails when active-solution projects add browser-infrastructure source/project coupling, compile `Chummer.Application` source directly, or introduce unsanctioned `Chummer.Application` project-reference ownership drift. |
| WL-108 | done | P2 | Publish helper-tooling residual backlog for remaining repo-surface utilities and plugin-era helper flows. | agent | Closed 2026-03-22: `docs/HELPER_TOOLING_RESIDUAL_BACKLOG.md` now inventories residual helper/plugin surfaces, assigns `keep/remove/migrate` disposition, and maps follow-through to milestone-backed lanes (`A0.5.7`, `F3`, `WL-092`, `WL-100`, `WL-D038`). |
| WL-098 | done | P1 | Revalidate temporary contract source-project leak deletion guardrails and refresh closure evidence. | agent | Revalidated 2026-03-21: `.sln`/`.csproj` sweep again found no `Chummer.Presentation.Contracts` or `Chummer.RunServices.Contracts` references, `bash scripts/ai/verify.sh` passed, and queue publication was refreshed to avoid reopening closed implementation slices. |
| WL-099 | done | P1 | Materialize remaining uncovered cross-repo contract reset scope into milestone-mapped runnable lanes. | agent | Closed 2026-03-19: the design canon no longer carries `A1`/`D1` as open, and the live worklist now reflects purification-only follow-through instead of pretending contract-reset scope still lacks milestone mapping. |
| WL-100 | done | P1 | Collapse repo-body root drift by moving non-deterministic legacy app/plugin and browser infrastructure into quarantine packages or explicit compatibility lanes. | agent | Closed 2026-03-19: `docs/LEGACY_ROOT_SURFACE_INVENTORY.md` now makes the remaining broad roots explicit compatibility-only cargo, and `scripts/ai/verify.sh` keeps the active engine boundary separate from those roots. |
| WL-103 | done | P1 | Quarantine or remove repo-local third-party AI transport and credential-routing ownership so `WL-D020` can honestly converge on hub-only adapter authority. | agent | Closed 2026-03-19: `AddChummerHeadlessCore(...)` now defaults to neutral credential/transport catalogs plus `NotImplementedAiProviderTransportClient`, the old env/http provider path is fenced behind explicit `AddLegacyEnvironmentAiTransportCompatibility(...)`, and verification now enforces that split. |
| WL-101 | done | P1 | Close `F1` for core by publishing restore/runbook evidence, replay-safety drills, and operator-facing hardening proof around deterministic runtime bundles. | agent | Closed 2026-03-19: `docs/CORE_RUNTIME_RESTORE_RUNBOOK.md` now binds restore/replay proof to `scripts/runbook.sh`, `scripts/migration-loop.sh`, `DualHeadAcceptanceTests`, and `MigrationComplianceTests`, and `scripts/ai/verify.sh` keeps that evidence present. |
| WL-102 | done | P1 | Close `F2` for core by certifying import/export and regression behavior against the `chummer5a` legacy corpus. | agent | Closed 2026-03-19: `docs/LEGACY_MIGRATION_CERTIFICATION.md` now ties the `chummer5a` oracle, parity oracle, migration loop, audit-compliance path, and core compliance/acceptance suites into one explicit certification lane, with verifier-backed evidence kept in-repo. |
| WL-105 | done | P1 | Publish milestone-mapped runnable closure evidence for the residual core scope statement (observability/DR hardening, migration certification, legacy cargo purification; not hosted contract ownership). | agent | Closed 2026-03-21: scope is already executable and closed through `WL-101` (`F1`), `WL-102` (`F2`), and `WL-100`/`WL-091` (`F3` purification evidence), so queue publication now stays empty instead of reopening hosted-contract slices. Revalidated 2026-03-22: removed stale residual-scope prompts from `.codex-studio/published/QUEUE.generated.yaml` to keep publication aligned with this closed lane. Revalidated 2026-03-22 (system re-entry): cleared stale queue overlays reopening already-closed residual scope plus `Chummer.Application`/browser/helper follow-through (`WL-106`/`WL-107`/`WL-108`/`WL-111`). Revalidated 2026-03-22 (auditor candidates `4317` and `44367`, first published 2026-03-21): scope remains fully materialized and closed; `.codex-studio/published/QUEUE.generated.yaml` stays `items: []` to avoid duplicating completed work. |
| WL-104 | done | P1 | Close temporary contract-source ambiguity for owned contract namespaces (`Engine` vs `Chummer.Engine.Contracts` vs old `Chummer.Contracts`) with migration commands and verifier guards. | agent | Closed 2026-03-19: boundary-map, sibling-owner package references, repo-local hosted-contract mirror deletion, and session-semantic verify guards now make the package boundary executable instead of aspirational. |
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
| WL-098 | done | A0.5.11 follow-through | Revalidated 2026-03-21 via `.sln`/`.csproj` contract-leak sweep plus passing `bash scripts/ai/verify.sh`; queue publication now stays aligned with this closed lane. |
| WL-099 | done | A0.5/A1/D1 follow-through | The contract-canon scope is now closed in design truth, so this row remains only as historical proof that the milestone mapping was made explicit before closure. |
| WL-105 | done | F1/F2/F3 follow-through | Residual-scope publication is now explicitly mapped to already closed hardening (`WL-101`), migration certification (`WL-102`), and purification lanes (`WL-100`, `WL-091`) so queue overlays do not re-open hosted-contract ownership work. |
| WL-111 | done | F3 follow-through | Legacy plugin/helper purification increment now has verifier-backed compatibility freeze and retirement-gate evidence for `WL-108.1` and `WL-108.3` without reopening hosted-contract ownership. |
| WL-112 | done | F follow-through | Residual helper/plugin and utility follow-through for `WL-108.2`/`WL-108.4`/`WL-108.5` is now verifier-backed via `docs/LEGACY_PLUGIN_AND_HELPER_OPERATIONAL_EVIDENCE_WL112.md` and `scripts/ai/verify.sh`, while `F1`/`F2` closure evidence remains intact. |
| WL-089 | done | A0.5.4 follow-through | The presentation-contract authority closure runnable lane is closed but still named here so verifier parity does not drift. |
| WL-090 | done | A0.5.5 follow-through | The run-service contract authority closure runnable lane is closed but still named here so verifier parity does not drift. |
| WL-091 | done | A0.5.6 follow-through | The browser infrastructure authority closure runnable lane is closed but still named here so verifier parity does not drift. |

## Current repo truth

- Repo-local live queue: none (last residual non-hosted purification/evidence cycle `WL-112` closed on 2026-03-22).
- Contract, explain, runtime-bundle canon, and migration-certification closure evidence remain materially closed (`WL-101`, `WL-102`, `WL-105`).
- Boundary warning: broad legacy roots still exist physically; purification remains an explicit compatibility-governance lane instead of implicit ownership drift.
- Hosted contract ownership remains closed and is intentionally excluded from the active queue.

## Historical log

- Full reconciliation history, queue-overlay drift, and repeated re-entry proof now live in `RECONCILIATION_LOG.md`.
