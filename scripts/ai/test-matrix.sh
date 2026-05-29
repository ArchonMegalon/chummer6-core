#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_env.sh"
cd "$repo_root"

project_path="${1:-Chummer.Tests/Chummer.Tests.csproj}"
shift || true
extra_args=("$@")

if [[ "$project_path" != /* ]]; then
  project_path="$repo_root/$project_path"
fi

linux_framework="net10.0"
windows_framework="net10.0-windows"

has_windows_desktop_runtime() {
  dotnet --list-runtimes | grep -Eq '^Microsoft\.WindowsDesktop\.App 10\.0\.'
}

run_linux_matrix() {
  echo "[matrix] restore linux target: $project_path ($linux_framework)"
  bash "$SCRIPT_DIR/restore.sh" "$project_path" -p:TargetFramework="$linux_framework"

  echo "[matrix] test linux target: $project_path ($linux_framework)"
  bash "$SCRIPT_DIR/test.sh" "$project_path" -f "$linux_framework" --no-restore "${extra_args[@]}"
}

run_windows_compile_matrix() {
  echo "[matrix] build windows target: $project_path ($windows_framework)"
  dotnet build "$project_path" \
    -c Debug \
    -f "$windows_framework" \
    -p:RestoreNoWarn=NU1701 \
    --nologo \
    -v:minimal \
    -clp:ErrorsOnly\;Summary \
    -warnAsMessage:NU1701 \
    -m:1
}

run_windows_execution_matrix() {
  if ! has_windows_desktop_runtime; then
    echo "[matrix] skip windows execution: Microsoft.WindowsDesktop.App 10.x is unavailable on this host" >&2
    if [[ "${CHUMMER_MATRIX_REQUIRE_WINDOWS_EXECUTION:-0}" == "1" ]]; then
      return 1
    fi
    return 0
  fi

  echo "[matrix] windows desktop runtime is available on this host"
}

run_linux_matrix
run_windows_compile_matrix
run_windows_execution_matrix

echo "[matrix] completed core-engine matrix for $project_path"
