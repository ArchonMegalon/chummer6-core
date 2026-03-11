#!/usr/bin/env bash
set -euo pipefail

echo "[audit] running migration compliance tests"
docker compose --profile test run --build --rm chummer-tests \
  dotnet test Chummer.Tests/Chummer.Tests.csproj -c Release -f net10.0 -p:TargetFramework=net10.0 --no-build --no-restore \
  --filter "FullyQualifiedName~MigrationComplianceTests" --logger "console;verbosity=minimal"

echo "[audit] running life-modules e2e data checks"
docker compose --profile test run --build --rm chummer-tests \
  dotnet test Chummer.Tests/Chummer.Tests.csproj -c Release -f net10.0 -p:TargetFramework=net10.0 --no-build --no-restore \
  --filter "FullyQualifiedName~LifeModulesEndToEndTests" --logger "console;verbosity=minimal"

echo "[audit] compliance checks passed"
