#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_dir/_env.sh"

lock_path="$repo_root/eng/package-plane.lock.json"
bootstrap_script="$repo_root/scripts/ai/bootstrap-owner-contracts-feed.py"
feed_root="$repo_root/.tmp/ai/local-nuget"
inventory_name="chummer-owner-contracts.inventory.json"
receipt_path="${CHUMMER_PACKAGE_PLANE_RECEIPT:-$repo_root/.artifacts/package-plane/no-siblings.generated.json}"
locked_version="$(python3 "$bootstrap_script" --repo-root "$repo_root" --print-version)"

bash "$script_dir/bootstrap-contracts-feed.sh"
python3 "$bootstrap_script" \
  --repo-root "$repo_root" \
  --feed "$feed_root" \
  --validate-only

# Prove the normal developer graph is coherent: the local Engine project at
# 0.0.0-local must win while Registry/Play/Run resolve as the locked packages.
dotnet restore "$repo_root/Chummer.Application/Chummer.Application.csproj" \
  --force \
  --no-cache \
  --nologo \
  -m:1 \
  -p:RuntimeIdentifiers=linux-x64 \
  -p:UseSharedCompilation=false

python3 - "$repo_root/Chummer.Application/obj/project.assets.json" "$locked_version" <<'PY'
import json
import sys
from pathlib import Path

assets_path = Path(sys.argv[1])
locked_version = sys.argv[2]
payload = json.loads(assets_path.read_text(encoding="utf-8"))
libraries = payload.get("libraries") or {}
expected = {
    "Chummer.Engine.Contracts/0.0.0-local": "project",
    f"Chummer.Hub.Registry.Contracts/{locked_version}": "package",
    f"Chummer.Play.Contracts/{locked_version}": "package",
    f"Chummer.Run.Contracts/{locked_version}": "package",
}
for identity, expected_type in expected.items():
    observed = libraries.get(identity)
    if not isinstance(observed, dict) or observed.get("type") != expected_type:
        raise SystemExit(
            f"normal dependency graph mismatch for {identity}: {observed!r}"
        )
owner_identities = {
    identity for identity in libraries
    if identity.split("/", 1)[0] in {row.split("/", 1)[0] for row in expected}
}
if owner_identities != set(expected):
    raise SystemExit(f"unexpected normal owner-contract identities: {sorted(owner_identities)}")
for log in payload.get("logs") or []:
    if log.get("code") == "NU1605" or str(log.get("level", "")).lower() == "error":
        raise SystemExit(f"normal dependency graph restore error: {log}")
print("normal-owner-contract-graph: ok (local Engine + locked owner packages)")
PY

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/chummer-core-package-plane.XXXXXX")"
cleanup() {
  case "$temporary_root" in
    "${TMPDIR:-/tmp}"/chummer-core-package-plane.*) rm -rf "$temporary_root" ;;
    *) echo "refusing to remove unexpected package-plane path: $temporary_root" >&2 ;;
  esac
}
trap cleanup EXIT

consumer_parent="$temporary_root/consumer"
consumer_root="$consumer_parent/chummer6-core"
isolated_feed="$temporary_root/feed"
isolated_packages="$temporary_root/packages"
isolated_cli_home="$temporary_root/dotnet-cli"
nuget_config="$temporary_root/NuGet.Config"
pack_output="$temporary_root/pack"
mkdir -p "$consumer_parent" "$isolated_feed" "$isolated_packages" "$isolated_cli_home" "$pack_output"

python3 - "$feed_root/$inventory_name" "$feed_root" "$isolated_feed" <<'PY'
import json
import shutil
import sys
from pathlib import Path

inventory_path, source_root, target_root = map(Path, sys.argv[1:4])
payload = json.loads(inventory_path.read_text(encoding="utf-8"))
for row in payload["packages"]:
    expected = row["file_name"].lower()
    matches = [path for path in source_root.glob("*.nupkg") if path.name.lower() == expected]
    if len(matches) != 1:
        raise SystemExit(f"expected exactly one inventoried package for {row['id']}")
    shutil.copy2(matches[0], target_root / matches[0].name)
shutil.copy2(inventory_path, target_root / inventory_path.name)
PY

python3 "$bootstrap_script" \
  --repo-root "$repo_root" \
  --feed "$isolated_feed" \
  --validate-only

git clone --quiet --no-hardlinks "$repo_root" "$consumer_root"
git -C "$consumer_root" checkout --quiet --detach "$(git -C "$repo_root" rev-parse HEAD)"

test ! -e "$consumer_parent/chummer-hub-registry"
test ! -e "$consumer_parent/chummer.run-services"
test ! -e "$consumer_parent/chummer-core-engine"

cat >"$nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="locked-owner-contracts" value="$isolated_feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="locked-owner-contracts">
      <package pattern="Chummer.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

export DOTNET_CLI_HOME="$isolated_cli_home"
export NUGET_PACKAGES="$isolated_packages"
missing_local_project="$temporary_root/no-local-contracts-project.csproj"
common_properties=(
  "-p:ChummerLocalContractsProject=$missing_local_project"
  "-p:UseChummerEngineContractsLocalFeed=false"
  "-p:RestoreAdditionalProjectSources="
  "-p:ChummerEngineContractsPackageVersion=$locked_version"
  "-p:ChummerOwnerContractsPackageVersion=$locked_version"
  "-p:RuntimeIdentifiers=linux-x64"
  "-p:UseSharedCompilation=false"
)

dotnet restore "$consumer_root/Chummer.CoreEngine.sln" \
  --configfile "$nuget_config" \
  --packages "$isolated_packages" \
  --no-cache \
  -m:1 \
  "${common_properties[@]}"

dotnet build "$consumer_root/Chummer.CoreEngine.sln" \
  --configuration Release \
  --no-restore \
  --nologo \
  -m:1 \
  "${common_properties[@]}"

CHUMMER_CORE_ENGINE_TEST_FILTER=sr5-core-providers \
  dotnet "$consumer_root/Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll"

dotnet restore "$consumer_root/Chummer.Tests/Chummer.Tests.csproj" \
  --configfile "$nuget_config" \
  --packages "$isolated_packages" \
  --no-cache \
  -m:1 \
  -p:TargetFramework=net10.0 \
  "${common_properties[@]}"

local_owner_filter='FullyQualifiedName~Import_with_local_single_user_scope_routes_through_the_unscoped_store_lane|FullyQualifiedName~Import_with_blank_owner_scope_remains_rejected|FullyQualifiedName~Raw_local_single_user_owner_value_cannot_enter_the_trusted_local_lane|FullyQualifiedName~Import_with_named_owner_scopes_keeps_two_owner_and_local_lanes_isolated|FullyQualifiedName~Workspace_service_owner_scoped_sentinels_cannot_reach_local_state'
dotnet test "$consumer_root/Chummer.Tests/Chummer.Tests.csproj" \
  --configuration Release \
  --framework net10.0 \
  --no-restore \
  --nologo \
  -m:1 \
  --filter "$local_owner_filter" \
  "${common_properties[@]}"

dotnet pack "$consumer_root/Chummer.Contracts/Chummer.Contracts.csproj" \
  --configuration Release \
  --no-build \
  --no-restore \
  --nologo \
  -m:1 \
  -p:PackageVersion="$locked_version" \
  "${common_properties[@]}" \
  --output "$pack_output"

python3 - \
  "$consumer_root" \
  "$isolated_packages" \
  "$lock_path" \
  "$isolated_feed/$inventory_name" \
  "$receipt_path" <<'PY'
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

consumer_root, package_root, lock_path, inventory_path, receipt_path = map(Path, sys.argv[1:6])
expected_package_root = package_root.resolve()
asset_files = sorted(consumer_root.glob("**/obj/project.assets.json"))
if not asset_files:
    raise SystemExit("no project.assets.json files were produced")
observed_libraries = set()
for asset_file in asset_files:
    payload = json.loads(asset_file.read_text(encoding="utf-8"))
    package_folders = list((payload.get("packageFolders") or {}).keys())
    if len(package_folders) != 1 or Path(package_folders[0]).resolve() != expected_package_root:
        raise SystemExit(f"ambient NuGet package root detected in {asset_file}: {package_folders}")
    observed_libraries.update((payload.get("libraries") or {}).keys())

lock_bytes = lock_path.read_bytes()
lock = json.loads(lock_bytes)
inventory_bytes = inventory_path.read_bytes()
inventory = json.loads(inventory_bytes)
version = lock["package_version"]
for row in lock["packages"]:
    identity = f"{row['id']}/{version}"
    if identity not in observed_libraries:
        raise SystemExit(f"locked package was not restored: {identity}")

commit = subprocess.check_output(
    ["git", "-C", str(consumer_root), "rev-parse", "HEAD"], text=True
).strip()
receipt = {
    "contract": "chummer-core.no-siblings-package-plane/v1",
    "generated_at_utc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    "status": "pass",
    "core_commit": commit,
    "package_plane_lock_sha256": hashlib.sha256(lock_bytes).hexdigest(),
    "package_inventory_sha256": hashlib.sha256(inventory_bytes).hexdigest(),
    "package_version": version,
    "packages": [
        {
            "id": row["id"],
            "sha256": row["sha256"],
            "size_bytes": row["size_bytes"],
        }
        for row in inventory["packages"]
    ],
    "no_sibling_directories": True,
    "isolated_package_cache": True,
    "package_source_mapping": {
        "Chummer.*": "locked-owner-contracts",
        "other": "https://api.nuget.org/v3/index.json",
    },
    "normal_local_engine_dependency_graph": "pass",
    "build": "pass",
    "package_plane_runtime_test": "pass",
    "local_owner_isolation_tests": "pass",
    "pack": "pass",
}
receipt_path.parent.mkdir(parents=True, exist_ok=True)
receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
print(f"no-siblings-package-plane: ok ({len(asset_files)} assets files)")
print(f"receipt: {receipt_path}")
PY
