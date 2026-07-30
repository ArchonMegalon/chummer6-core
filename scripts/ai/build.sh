#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"

contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"
"$(dirname "$0")/bootstrap-contracts-feed.sh"

build_configuration="Debug"
expect_configuration=0
for arg in "$@"; do
  if [[ "$expect_configuration" -eq 1 ]]; then
    build_configuration="$arg"
    expect_configuration=0
    continue
  fi
  case "$arg" in
    -c|--configuration)
      expect_configuration=1
      ;;
    -c=*|--configuration=*)
      build_configuration="${arg#*=}"
      ;;
  esac
done

dotnet build "$repo_root/Chummer.Contracts/Chummer.Contracts.csproj" --nologo -m:1 -p:PackageVersion="$contracts_package_version"
"$(dirname "$0")/build-local-contract-dependencies.sh" "$build_configuration"

dotnet_with_default_target build "$@" --nologo -m:1 -p:ChummerEngineContractsPackageVersion="$contracts_package_version"
