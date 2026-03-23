#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"

contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"
contracts_feed_root="${CHUMMER_ENGINE_CONTRACTS_FEED:-$repo_root/.tmp/ai/local-nuget}"

mkdir -p "$contracts_feed_root"

dotnet pack "$repo_root/Chummer.Contracts/Chummer.Contracts.csproj" \
  --configuration Debug \
  --nologo \
  -m:1 \
  -p:PackageVersion="$contracts_package_version" \
  -o "$contracts_feed_root"
