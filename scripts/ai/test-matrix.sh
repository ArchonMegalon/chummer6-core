#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_env.sh"

project_path="${1:-Chummer.Tests/Chummer.Tests.csproj}"
shift || true
extra_args=("$@")

linux_framework="net10.0"
windows_framework="net10.0-windows"

has_windows_desktop_runtime() {
  dotnet --list-runtimes | grep -Eq '^Microsoft\.WindowsDesktop\.App 10\.0\.'
}

run_linux_matrix() {
  echo "[matrix] test linux target: $project_path ($linux_framework)"
  bash "$SCRIPT_DIR/test.sh" "$project_path" -f "$linux_framework" "${extra_args[@]}"
}

run_windows_compile_matrix() {
  echo "[matrix] build windows target: $project_path ($windows_framework)"
  bash "$SCRIPT_DIR/build.sh" "$project_path" -f "$windows_framework" --no-restore
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
