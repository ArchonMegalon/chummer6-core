#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$script_dir/_env.sh"

lock_path="$repo_root/eng/package-plane.lock.json"
bootstrap_script="$repo_root/scripts/ai/bootstrap-owner-contracts-feed.py"
feed_root="$repo_root/.tmp/ai/local-nuget"
inventory_name="chummer-owner-contracts.inventory.json"
candidate_inventory_name="chummer-core-candidate-engine-contract.inventory.json"
candidate_version_prefix="0.0.0-packageplane.candidate"
candidate_id="Chummer.Engine.Contracts"
candidate_repository="https://github.com/ArchonMegalon/chummer6-core.git"
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
mkdir -p "$consumer_parent" "$isolated_feed" "$isolated_packages" "$isolated_cli_home"

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

# The lock still governs Registry/Play/Run and remains byte-for-byte intact in
# the isolated feed. Engine Contracts is the candidate owned by this checkout,
# so pack that exact commit separately and label it as candidate evidence. Its
# deterministic prerelease version sorts above the locked package minimum while
# remaining unmistakably non-release evidence. This keeps new Application code
# from compiling against the older locked baseline without suppressing NU1605
# or pretending that current source has already been published.
candidate_commit="$(git -C "$consumer_root" rev-parse HEAD)"
candidate_version="$candidate_version_prefix.sha${candidate_commit:0:12}"
candidate_origin="$(git -C "$repo_root" remote get-url origin)"
if [[ "$candidate_origin" != "$candidate_repository" \
   && "$candidate_origin" != "${candidate_repository%.git}" ]]; then
  echo "candidate Engine Contracts origin mismatch: $candidate_origin" >&2
  exit 1
fi

dotnet pack "$consumer_root/Chummer.Contracts/Chummer.Contracts.csproj" \
  --configuration Release \
  --nologo \
  -m:1 \
  -p:PackageVersion="$candidate_version" \
  -p:Version="$candidate_version" \
  -p:RepositoryCommit="$candidate_commit" \
  -p:RepositoryUrl="$candidate_repository" \
  -p:PublishRepositoryUrl=true \
  -p:ContinuousIntegrationBuild=true \
  -p:UseSharedCompilation=false \
  -p:RuntimeIdentifiers=linux-x64 \
  -p:RestoreConfigFile="$nuget_config" \
  -p:RestorePackagesPath="$isolated_packages" \
  --output "$isolated_feed"

python3 - \
  "$bootstrap_script" \
  "$isolated_feed" \
  "$candidate_inventory_name" \
  "$candidate_id" \
  "$candidate_version" \
  "$candidate_repository" \
  "$candidate_commit" <<'PY'
import hashlib
import importlib.util
import sys
from pathlib import Path

(
    bootstrap_path,
    feed,
    inventory_name,
    package_id,
    version,
    repository,
    commit,
) = sys.argv[1:8]
bootstrap_path = Path(bootstrap_path)
feed = Path(feed)
module_spec = importlib.util.spec_from_file_location(
    "candidate_engine_contract_package_plane",
    bootstrap_path,
)
if module_spec is None or module_spec.loader is None:
    raise SystemExit("unable to load owner-contract package validator")
module = importlib.util.module_from_spec(module_spec)
sys.modules[module_spec.name] = module
module_spec.loader.exec_module(module)

package_spec = module.PackageSpec(
    package_id,
    repository,
    commit,
    "current-core-checkout",
    "Chummer.Contracts/Chummer.Contracts.csproj",
)
package_path = module.validate_package(feed, package_spec, version)
module.canonicalize_nupkg(package_path)
module.validate_package(feed, package_spec, version)
package_bytes = package_path.read_bytes()
inventory = {
    "contract": "chummer-core.candidate-engine-contract-package-inventory/v1",
    "role": "current_core_candidate",
    "core_commit": commit,
    "package": {
        "id": package_id,
        "version": version,
        "repository": repository,
        "commit": commit,
        "project": "Chummer.Contracts/Chummer.Contracts.csproj",
        "file_name": package_path.name,
        "sha256": hashlib.sha256(package_bytes).hexdigest(),
        "size_bytes": len(package_bytes),
    },
}
module._atomic_write_json(feed / inventory_name, inventory)
print(
    "candidate-engine-contract-package: ok "
    f"({package_id} {version} at {commit}; "
    f"sha256 {inventory['package']['sha256']})"
)
PY

missing_local_project="$temporary_root/no-local-contracts-project.csproj"
common_properties=(
  "-p:ChummerLocalContractsProject=$missing_local_project"
  "-p:UseChummerEngineContractsLocalFeed=false"
  "-p:RestoreAdditionalProjectSources="
  "-p:ChummerEngineContractsPackageVersion=$candidate_version"
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

python3 - \
  "$consumer_root" \
  "$isolated_packages" \
  "$lock_path" \
  "$isolated_feed/$inventory_name" \
  "$isolated_feed/$candidate_inventory_name" \
  "$isolated_feed" \
  "$receipt_path" <<'PY'
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

(
    consumer_root,
    package_root,
    lock_path,
    inventory_path,
    candidate_inventory_path,
    isolated_feed,
    receipt_path,
) = map(Path, sys.argv[1:8])
expected_package_root = package_root.resolve()
asset_files = sorted(consumer_root.glob("**/obj/project.assets.json"))
if not asset_files:
    raise SystemExit("no project.assets.json files were produced")
observed_libraries = set()
observed_owner_types = {}
owner_ids = {
    "Chummer.Engine.Contracts",
    "Chummer.Hub.Registry.Contracts",
    "Chummer.Play.Contracts",
    "Chummer.Run.Contracts",
}
for asset_file in asset_files:
    payload = json.loads(asset_file.read_text(encoding="utf-8"))
    package_folders = list((payload.get("packageFolders") or {}).keys())
    if len(package_folders) != 1 or Path(package_folders[0]).resolve() != expected_package_root:
        raise SystemExit(f"ambient NuGet package root detected in {asset_file}: {package_folders}")
    libraries = payload.get("libraries") or {}
    observed_libraries.update(libraries)
    for identity, details in libraries.items():
        if identity.split("/", 1)[0] in owner_ids:
            observed_owner_types.setdefault(identity, set()).add(details.get("type"))

lock_bytes = lock_path.read_bytes()
lock = json.loads(lock_bytes)
inventory_bytes = inventory_path.read_bytes()
inventory = json.loads(inventory_bytes)
candidate_inventory_bytes = candidate_inventory_path.read_bytes()
candidate_inventory = json.loads(candidate_inventory_bytes)
version = lock["package_version"]
candidate = candidate_inventory["package"]
commit = subprocess.check_output(
    ["git", "-C", str(consumer_root), "rev-parse", "HEAD"], text=True
).strip()
if candidate_inventory.get("contract") != "chummer-core.candidate-engine-contract-package-inventory/v1":
    raise SystemExit("candidate Engine Contracts inventory contract is invalid")
if candidate_inventory.get("role") != "current_core_candidate":
    raise SystemExit("candidate Engine Contracts inventory role is invalid")
expected_candidate_version = f"0.0.0-packageplane.candidate.sha{commit[:12]}"
if (
    candidate.get("id") != "Chummer.Engine.Contracts"
    or candidate.get("version") != expected_candidate_version
):
    raise SystemExit("candidate Engine Contracts identity is invalid")
expected_candidate_metadata = {
    "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
    "commit": commit,
    "project": "Chummer.Contracts/Chummer.Contracts.csproj",
    "file_name": f"Chummer.Engine.Contracts.{expected_candidate_version}.nupkg",
}
for key, expected in expected_candidate_metadata.items():
    if candidate.get(key) != expected:
        raise SystemExit(
            f"candidate Engine Contracts {key} mismatch: {candidate.get(key)!r}"
        )
if candidate_inventory.get("core_commit") != commit:
    raise SystemExit("candidate Engine Contracts inventory is not bound to the consumer commit")
candidate_path = isolated_feed / candidate["file_name"]
candidate_bytes = candidate_path.read_bytes()
if len(candidate_bytes) != candidate.get("size_bytes"):
    raise SystemExit("candidate Engine Contracts byte size does not match its inventory")
if hashlib.sha256(candidate_bytes).hexdigest() != candidate.get("sha256"):
    raise SystemExit("candidate Engine Contracts digest does not match its inventory")

expected_owner_identities = {f"{candidate['id']}/{candidate['version']}"}
for row in lock["packages"]:
    if row["id"] == "Chummer.Engine.Contracts":
        continue
    identity = f"{row['id']}/{version}"
    if identity not in observed_libraries:
        raise SystemExit(f"locked package was not restored: {identity}")
    expected_owner_identities.add(identity)
observed_owner_identities = set(observed_owner_types)
if observed_owner_identities != expected_owner_identities:
    raise SystemExit(
        "isolated owner-contract selection mismatch: "
        f"expected {sorted(expected_owner_identities)}, "
        f"observed {sorted(observed_owner_identities)}"
    )
for identity, observed_types in observed_owner_types.items():
    if observed_types != {"package"}:
        raise SystemExit(
            f"isolated owner contract was not package-backed: {identity} -> "
            f"{sorted(str(value) for value in observed_types)}"
        )
locked_packages = []
resolved_packages = [dict(candidate, role="current_core_candidate")]
for row in inventory["packages"]:
    is_engine_baseline = row["id"] == "Chummer.Engine.Contracts"
    locked_packages.append(
        {
            "id": row["id"],
            "version": row["version"],
            "sha256": row["sha256"],
            "size_bytes": row["size_bytes"],
            "role": (
                "locked_engine_baseline_not_selected"
                if is_engine_baseline
                else "locked_owner_dependency"
            ),
        }
    )
    if not is_engine_baseline:
        resolved_packages.append(
            {
                "id": row["id"],
                "version": row["version"],
                "sha256": row["sha256"],
                "size_bytes": row["size_bytes"],
                "role": "locked_owner_dependency",
            }
        )

receipt = {
    "contract": "chummer-core.no-siblings-package-plane/v2",
    "generated_at_utc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    "status": "pass",
    "core_commit": commit,
    "package_plane_lock_sha256": hashlib.sha256(lock_bytes).hexdigest(),
    "package_inventory_sha256": hashlib.sha256(inventory_bytes).hexdigest(),
    "candidate_package_inventory_sha256": hashlib.sha256(candidate_inventory_bytes).hexdigest(),
    "package_version": version,
    "candidate_package_version": candidate["version"],
    "locked_packages": locked_packages,
    "resolved_owner_contracts": resolved_packages,
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
    "candidate_engine_contract_pack": "pass",
}
receipt_path.parent.mkdir(parents=True, exist_ok=True)
receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
print(f"no-siblings-package-plane: ok ({len(asset_files)} assets files)")
print(f"receipt: {receipt_path}")
PY
