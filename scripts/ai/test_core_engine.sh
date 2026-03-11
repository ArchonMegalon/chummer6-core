#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"
project_path="Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj"
skip_build=0
build_args=()

for arg in "$@"; do
  if [[ "$arg" == "--no-build" ]]; then
    skip_build=1
    continue
  fi

  build_args+=("$arg")
done

target_framework="$(
  grep -m 1 "<TargetFramework>" "$project_path" | sed 's:.*<TargetFramework>::; s:</TargetFramework>.*::'
)"

if [[ -z "$target_framework" ]]; then
  echo "Unable to determine TargetFramework from $project_path" >&2
  exit 1
fi

if [[ "$skip_build" -eq 0 ]]; then
  dotnet build "$project_path" --nologo -m:1 "${build_args[@]}"
fi

dotnet "Chummer.CoreEngine.Tests/bin/Debug/$target_framework/Chummer.CoreEngine.Tests.dll"
