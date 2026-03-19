# Legacy Migration Certification

Purpose: keep the core share of `F2` explicit and executable.

`chummer5a` remains the legacy oracle for migration confidence. This certification lane exists to prove that import/export and parity behavior stay grounded in that oracle instead of relying on architecture intent alone.

## Certification inputs

- `chummer5a` regression corpus and compatibility fixtures
- `docs/PARITY_ORACLE.json`
- `docs/MIGRATION_BACKLOG.md`
- `scripts/migration-loop.sh`
- `scripts/audit-compliance.sh`
- `Chummer.Tests/Compliance/MigrationComplianceTests.cs`
- `Chummer.Tests/Presentation/DualHeadAcceptanceTests.cs`
- `Chummer.Tests/Compliance/ArchitectureGuardrailTests.cs`

## Required command set

Run from the repo root:

```bash
bash scripts/migration-loop.sh 1
bash scripts/audit-compliance.sh
docker compose --profile test run --build --rm chummer-tests \
  dotnet test Chummer.Tests/Chummer.Tests.csproj -c Release -f net10.0 -p:TargetFramework=net10.0 \
  --filter "FullyQualifiedName~ArchitectureGuardrailTests|FullyQualifiedName~MigrationComplianceTests|FullyQualifiedName~DualHeadAcceptanceTests"
```

## Certification rule

Migration certification is only healthy when:

- import/export behavior remains compatible with the checked-in legacy oracle expectations
- parity and acceptance coverage continue to exercise the current active heads instead of retired legacy UI paths
- migration/compliance coverage stays runnable from the repo-standard command paths above
- no release claim skips the `chummer5a` corpus-backed proof path

## Current boundary

This lane certifies behavior, not architecture ownership:

- `chummer6-core` owns the current engine/runtime truth
- `chummer5a` remains the regression oracle
- the certification commands above are the required proof that the migration path still holds
