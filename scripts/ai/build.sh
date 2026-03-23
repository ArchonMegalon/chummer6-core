#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"

contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"
"$(dirname "$0")/bootstrap-contracts-feed.sh"

dotnet build "$repo_root/Chummer.Contracts/Chummer.Contracts.csproj" --nologo -m:1 -p:PackageVersion="$contracts_package_version"
dotnet build "$repo_root/../chummer-hub-registry/Chummer.Hub.Registry.Contracts/Chummer.Hub.Registry.Contracts.csproj" --nologo -m:1
dotnet build "$repo_root/../chummer.run-services/Chummer.Run.Contracts/Chummer.Run.Contracts.csproj" --nologo -m:1

dotnet_with_default_target build "$@" --nologo -m:1 -p:ChummerEngineContractsPackageVersion="$contracts_package_version"
