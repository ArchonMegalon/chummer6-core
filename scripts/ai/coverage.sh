#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_env.sh"

project_path="${1:-Chummer.Tests/Chummer.Tests.csproj}"
shift || true

framework="${CHUMMER_COVERAGE_FRAMEWORK:-net10.0}"
results_root="${CHUMMER_COVERAGE_RESULTS_ROOT:-$repo_root/.artifacts/coverage}"
summary_json="$results_root/summary.json"
runsettings_path="$SCRIPT_DIR/coverage.runsettings"
extra_args=("$@")

mkdir -p "$results_root"
rm -rf "$results_root"/*

echo "[coverage] restore $project_path ($framework)"
dotnet_with_default_target restore "$project_path" -p:TargetFramework="$framework"

echo "[coverage] run $project_path ($framework)"
dotnet_with_default_target test "$project_path" \
  --nologo \
  -m:1 \
  -f "$framework" \
  -p:TargetFramework="$framework" \
  --results-directory "$results_root" \
  --settings "$runsettings_path" \
  --collect:"XPlat Code Coverage" \
  "${extra_args[@]}"

python3 "$SCRIPT_DIR/coverage-summary.py" "$results_root" "$summary_json" "$repo_root"
echo "[coverage] summary written to $summary_json"
