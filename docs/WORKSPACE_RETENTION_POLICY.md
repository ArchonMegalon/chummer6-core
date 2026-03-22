# Workspace and Session Retention/Cleanup Policy

Purpose: provide the `MIG-093` operational policy for workspace/session retention, cleanup, and recovery evidence.

## Scope

This policy governs filesystem retention for operational workspace/session state owned by this repo's runbook/test paths.

It does not redefine deterministic engine truth or hosted persistence ownership.

## Policy

- Workspace/session runbook state is retained for `14` days by default.
- Runbook logs are retained for `30` days by default.
- Cleanup only targets known runbook state roots and log roots.
- Cleanup is deterministic and age-based (`mtime`), not name-based.
- Cleanup has a dry-run mode and must print candidate paths before deletion.
- Cleanup never deletes outside configured roots.

## Canonical roots

Default state roots:

- `.tmp/dotnet-cli-home`
- `.tmp/downloads-smoke`
- `.tmp/retention-cleanup-smoke`

Default log roots:

- `.tmp/*.log`
- `$XDG_RUNTIME_DIR/*.log` when runbook logs are stored there
- `$RUNBOOK_LOG_DIR/*.log` when explicitly configured

## Recovery posture

- Cleanup is bounded to stale artifacts and leaves active/new files untouched.
- Operators can preserve artifacts by increasing retention days for a run.
- Deletion activity is summarized in runbook logs for audit/replay evidence.

## Evidence commands

Run from repo root:

```bash
RUNBOOK_MODE=retention-cleanup RETENTION_CLEANUP_DRY_RUN=1 bash scripts/runbook.sh
RUNBOOK_MODE=retention-cleanup-smoke bash scripts/runbook.sh
```

Acceptance signals:

- policy summary prints configured retention windows and roots
- dry-run reports candidate count without deleting files
- smoke run proves stale fixture deletion and recent fixture retention
