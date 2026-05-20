#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

project_path="${1:-Chummer.Tests/Chummer.Tests.csproj}"
shift || true

case "${OS:-$(uname -s)}" in
  Windows_NT)
    echo "[native-matrix] windows host detected"
    CHUMMER_MATRIX_REQUIRE_WINDOWS_EXECUTION=1 \
      bash "$SCRIPT_DIR/test-matrix.sh" "$project_path" "$@"
    ;;
  Darwin)
    echo "[native-matrix] macOS host detected"
    bash "$SCRIPT_DIR/test-matrix.sh" "$project_path" "$@"
    ;;
  *)
    echo "[native-matrix] this wrapper is for native Windows/macOS hosts only" >&2
    echo "[native-matrix] use scripts/ai/test-matrix.sh on Linux for compile-only Windows coverage" >&2
    exit 1
    ;;
esac
