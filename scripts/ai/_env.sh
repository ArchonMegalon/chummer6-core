#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cache_root="${CHUMMER_AI_CACHE_ROOT:-$repo_root/.tmp/ai}"
default_solution_path="${CHUMMER_AI_DEFAULT_SOLUTION:-$repo_root/Chummer.CoreEngine.sln}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/.dotnet-cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/.nuget/packages}"
export TMPDIR="${TMPDIR:-$cache_root/tmp}"
mkdir -p /tmp/.dotnet/shm "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$TMPDIR"

has_explicit_dotnet_target() {
  local arg
  for arg in "$@"; do
    if [[ "$arg" != -* ]]; then
      return 0
    fi
  done

  return 1
}

dotnet_with_default_target() {
  local verb="$1"
  shift

  if has_explicit_dotnet_target "$@"; then
    dotnet "$verb" "$@"
    return
  fi

  dotnet "$verb" "$default_solution_path" "$@"
}
