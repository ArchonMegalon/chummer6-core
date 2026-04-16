#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"
project_path="Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj"
skip_build=0
build_args=()
contracts_package_version="${CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION:-0.0.0-local}"

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
  "$(dirname "$0")/bootstrap-contracts-feed.sh"
  dotnet build "$project_path" --nologo -m:1 -p:ChummerEngineContractsPackageVersion="$contracts_package_version" "${build_args[@]}"
fi

python3 "$repo_root/tests/test_engine_proof_pack_generator.py"
python3 "$repo_root/scripts/generate-engine-proof-pack.py" --repo-root "$repo_root" --check >/dev/null

dotnet "Chummer.CoreEngine.Tests/bin/Debug/$target_framework/Chummer.CoreEngine.Tests.dll"
