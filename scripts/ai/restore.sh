#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"
contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"
"$(dirname "$0")/bootstrap-contracts-feed.sh"
dotnet_with_default_target restore "$@" -m:1 -p:ChummerEngineContractsPackageVersion="$contracts_package_version"
