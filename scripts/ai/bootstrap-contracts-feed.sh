#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"

contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"
contracts_feed_root="${CHUMMER_ENGINE_CONTRACTS_FEED:-$repo_root/.tmp/ai/local-nuget}"
contracts_cache_root="${NUGET_PACKAGES:-$repo_root/.tmp/nuget/packages}"
contracts_cache_path="$contracts_cache_root/chummer.engine.contracts/$contracts_package_version"

mkdir -p "$contracts_feed_root"

dotnet build "$repo_root/Chummer.Contracts/Chummer.Contracts.csproj" \
  --configuration Debug \
  --nologo \
  -m:1 \
  -p:PackageVersion="$contracts_package_version"

dotnet pack "$repo_root/Chummer.Contracts/Chummer.Contracts.csproj" \
  --configuration Debug \
  --nologo \
  --no-build \
  -m:1 \
  -p:PackageVersion="$contracts_package_version" \
  -o "$contracts_feed_root"

# The local feed intentionally reuses a stable 0.0.0-local version. Drop the cached
# package payload so the next restore actually consumes the freshly packed contracts.
rm -rf "$contracts_cache_path"
