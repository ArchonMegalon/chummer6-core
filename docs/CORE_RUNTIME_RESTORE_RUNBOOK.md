# Core Runtime Restore And Replay Runbook

Purpose: keep the core share of `F1` explicit and runnable.

This runbook is the operator-facing proof path for deterministic runtime bundles, replay-safe restore, and explain/runtime continuity after the contract-canon closure.
It also carries the operational retention/cleanup enforcement path for workspace/session runbook state (`MIG-093`).

## Scope

This drill covers:

- deterministic runtime-bundle recovery through the current engine/runtime seams
- replay-safe workspace/session restore against the active multi-head runtime
- explain/runtime continuity across core, hub, and mobile consumers
- restore/readiness commands that stay in the checked-in runbook path

It does not claim ownership of:

- hub-registry publication/install metadata restore
- media-factory asset/render restore
- hosted orchestration policy

## Canonical evidence anchors

- `docs/EXPLAIN_AND_RUNTIME_CANON.md`
- `docs/WORKSPACE_RETENTION_POLICY.md`
- `docs/SELF_HOSTED_DOWNLOADS_RUNBOOK.md`
- `scripts/runbook.sh`
- `scripts/migration-loop.sh`
- `Chummer.Tests/Presentation/DualHeadAcceptanceTests.cs`
- `Chummer.Tests/Compliance/MigrationComplianceTests.cs`

## Drill commands

Run from the repo root:

```bash
bash scripts/ai/verify.sh
RUNBOOK_MODE=local-tests TEST_NUGET_SOFT_FAIL=0 TEST_DISABLE_BUILD_SERVERS=1 TEST_MAX_CPU=1 bash scripts/runbook.sh
RUNBOOK_MODE=retention-cleanup RETENTION_CLEANUP_DRY_RUN=1 bash scripts/runbook.sh
RUNBOOK_MODE=retention-cleanup-smoke bash scripts/runbook.sh
bash scripts/migration-loop.sh 1
```

If the local restore cache is already warm, an offline follow-up is also valid:

```bash
RUNBOOK_MODE=local-tests TEST_NO_RESTORE=1 TEST_DISABLE_BUILD_SERVERS=1 TEST_MAX_CPU=1 bash scripts/runbook.sh
```

## Restore acceptance

The core `F1` lane is healthy when:

- runtime-bundle semantics remain aligned with `docs/EXPLAIN_AND_RUNTIME_CANON.md`
- `DualHeadAcceptanceTests` continue to prove replay-safe behavior across the active heads
- `MigrationComplianceTests` continue to prove restore, import/export, and parity safety under the current runtime
- `RUNBOOK_MODE=retention-cleanup` prints deterministic policy windows and stale-candidate evidence
- `RUNBOOK_MODE=retention-cleanup-smoke` proves stale fixture deletion while preserving recent fixtures
- `scripts/migration-loop.sh 1` completes without reintroducing drift between API, Blazor, Avalonia, and portal paths

If any of these fail, core-side observability/DR/replay safety is not closed.
