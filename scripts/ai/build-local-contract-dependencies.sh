#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"

configuration="${1:-Debug}"
if [[ -z "$configuration" ]]; then
  echo "local contract dependency configuration must not be empty" >&2
  exit 2
fi

dotnet build \
  "$repo_root/../chummer-hub-registry/Chummer.Hub.Registry.Contracts/Chummer.Hub.Registry.Contracts.csproj" \
  --configuration "$configuration" \
  --nologo \
  -m:1

dotnet build \
  "$repo_root/../chummer.run-services/Chummer.Run.Contracts/Chummer.Run.Contracts.csproj" \
  --configuration "$configuration" \
  --nologo \
  -m:1
