#!/usr/bin/env python3

from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
LOCK_PATH = REPO_ROOT / "eng/package-plane.lock.json"
BOOTSTRAP_PATH = REPO_ROOT / "scripts/ai/bootstrap-owner-contracts-feed.py"


def load_bootstrap_module():
    spec = importlib.util.spec_from_file_location("bootstrap_owner_contracts_feed", BOOTSTRAP_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("unable to import owner-contract bootstrap")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


MODULE = load_bootstrap_module()


PACKAGE_CONSUMERS = {
    "Chummer.Hub.Registry.Contracts": (
        "Chummer.Application/Chummer.Application.csproj",
        "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
        "Chummer.GmCharacterEdits/Chummer.GmCharacterEdits.csproj",
        "Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj",
        "Chummer.Tests/Chummer.Tests.csproj",
    ),
    "Chummer.Run.Contracts": (
        "Chummer.Application/Chummer.Application.csproj",
        "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
        "Chummer.GmCharacterEdits/Chummer.GmCharacterEdits.csproj",
        "Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj",
        "Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj",
        "Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj",
        "Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj",
        "Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj",
        "Chummer.Tests/Chummer.Tests.csproj",
    ),
}

LOCKED_OWNER_SWITCHED_CONSUMERS = (
    "Chummer.Application/Chummer.Application.csproj",
    "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
    "Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj",
    "Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj",
    "Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj",
    "Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj",
)
HUB_OWNER_SWITCHED_CONSUMERS = {
    "Chummer.Application/Chummer.Application.csproj",
    "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
}

LOCAL_OWNER_PROJECT_CONDITION = (
    "'$(ChummerUseLocalCompatibilityTree)' == 'true' and "
    "'$(ChummerUseLockedOwnerContractPackages)' != 'true'"
)
LOCKED_OWNER_PACKAGE_CONDITION = (
    "'$(ChummerUseLocalCompatibilityTree)' != 'true' or "
    "'$(ChummerUseLockedOwnerContractPackages)' == 'true'"
)


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


class PackagePlaneTests(unittest.TestCase):
    def setUp(self) -> None:
        self.payload = json.loads(LOCK_PATH.read_text(encoding="utf-8"))

    def test_lock_matches_sdk_and_immutable_package_authorities(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        global_json = json.loads((REPO_ROOT / "global.json").read_text(encoding="utf-8"))
        self.assertEqual(global_json["sdk"]["version"], lock.dotnet_sdk)
        self.assertEqual(tuple(row.package_id for row in lock.packages), MODULE.EXPECTED_PACKAGE_IDS)
        self.assertNotIn("local", lock.package_version)
        for row in lock.packages:
            self.assertRegex(row.commit, r"^[0-9a-f]{40}$")
            self.assertTrue(row.repository.startswith("https://github.com/ArchonMegalon/"))

    def test_owner_pack_restore_uses_staged_feed_and_exact_lock_versions(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        staged_feed = Path("/tmp/chummer-owner-contracts-staged")
        arguments = MODULE._owner_pack_restore_args(lock, staged_feed=staged_feed)

        self.assertEqual(
            f"-p:RestoreSources={staged_feed};{lock.approved_remote_source}",
            arguments[0],
        )
        self.assertIn("-p:RestoreAdditionalProjectSources=", arguments)
        self.assertIn("-p:RestoreLockedMode=false", arguments)
        self.assertIn("-p:RestorePackagesWithLockFile=true", arguments)
        for property_name in (
            "ChummerEngineContractsPackageVersion",
            "ChummerHubRegistryContractsPackageVersion",
            "ChummerRunContractsPackageVersion",
            "ChummerOwnerContractsPackageVersion",
            "ChummerPackagePlaneVersion",
        ):
            self.assertIn(
                f"-p:{property_name}={lock.package_version}",
                arguments,
            )

    def test_owner_pack_restore_rejects_ambiguous_staged_feed_path(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        with self.assertRaisesRegex(MODULE.PackagePlaneError, "exact restore source"):
            MODULE._owner_pack_restore_args(
                lock,
                staged_feed=Path("/tmp/staged;https://example.invalid/index.json"),
            )

    def test_owner_contract_consumers_use_only_pinned_package_references(self) -> None:
        version_properties = {
            "Chummer.Hub.Registry.Contracts": "$(ChummerHubRegistryContractsPackageVersion)",
            "Chummer.Run.Contracts": "$(ChummerRunContractsPackageVersion)",
        }
        expected_by_file: dict[str, set[str]] = {}
        for package_id, consumers in PACKAGE_CONSUMERS.items():
            for consumer in consumers:
                expected_by_file.setdefault(consumer, set()).add(package_id)

        for relative_path, expected_ids in expected_by_file.items():
            root = ET.parse(REPO_ROOT / relative_path).getroot()
            references = [
                element
                for element in root.iter()
                if local_name(element.tag) in {"Reference", "PackageReference"}
                and element.attrib.get("Include") in version_properties
            ]
            observed_ids = {element.attrib["Include"] for element in references}
            self.assertEqual(expected_ids, observed_ids, relative_path)
            for reference in references:
                package_id = reference.attrib["Include"]
                self.assertEqual("PackageReference", local_name(reference.tag), relative_path)
                self.assertEqual(version_properties[package_id], reference.attrib.get("Version"), relative_path)

    def test_no_project_can_resolve_owner_contracts_from_sibling_binaries(self) -> None:
        forbidden_fragments = ("chummer-hub-registry", "chummer.run-services")
        tracked_projects = subprocess.check_output(
            ["git", "ls-files", "--", "*.csproj"],
            cwd=REPO_ROOT,
            text=True,
        ).splitlines()
        for relative_project in tracked_projects:
            project = REPO_ROOT / relative_project
            root = ET.parse(project).getroot()
            for element in root.iter():
                if local_name(element.tag) != "HintPath":
                    continue
                normalized = (element.text or "").replace("\\", "/").lower()
                self.assertFalse(
                    any(fragment in normalized for fragment in forbidden_fragments),
                    f"sibling HintPath survived in {project.relative_to(REPO_ROOT)}: {normalized}",
                )

    def test_msbuild_version_property_matches_lock(self) -> None:
        props = ET.parse(REPO_ROOT / "Directory.Build.props").getroot()
        values = {
            local_name(element.tag): (element.text or "").strip()
            for element in props.iter()
        }
        self.assertEqual(self.payload["package_version"], values["ChummerOwnerContractsPackageVersion"])
        self.assertEqual(
            "$(ChummerOwnerContractsPackageVersion)",
            values["ChummerHubRegistryContractsPackageVersion"],
        )
        self.assertEqual(
            "$(ChummerOwnerContractsPackageVersion)",
            values["ChummerRunContractsPackageVersion"],
        )
        self.assertEqual("false", values["ChummerUseLockedOwnerContractPackages"])

    def test_locked_owner_mode_selects_packages_across_local_production_graph(self) -> None:
        for relative_path in LOCKED_OWNER_SWITCHED_CONSUMERS:
            project = ET.parse(REPO_ROOT / relative_path).getroot()
            run_project_condition = None
            run_package_condition = None
            hub_project_condition = None
            hub_package_condition = None

            for item_group in project:
                includes = {
                    element.attrib.get("Include")
                    for element in item_group
                }
                if "$(ChummerLocalRunContractsProject)" in includes:
                    run_project_condition = item_group.attrib.get("Condition")
                if "Chummer.Run.Contracts" in includes:
                    run_package_condition = item_group.attrib.get("Condition")
                if "$(ChummerLocalHubRegistryContractsProject)" in includes:
                    hub_project_condition = item_group.attrib.get("Condition")
                if "Chummer.Hub.Registry.Contracts" in includes:
                    hub_package_condition = item_group.attrib.get("Condition")

            with self.subTest(project=relative_path):
                self.assertEqual(LOCAL_OWNER_PROJECT_CONDITION, run_project_condition)
                self.assertEqual(LOCKED_OWNER_PACKAGE_CONDITION, run_package_condition)
                if relative_path in HUB_OWNER_SWITCHED_CONSUMERS:
                    self.assertEqual(
                        LOCAL_OWNER_PROJECT_CONDITION,
                        hub_project_condition,
                    )
                    self.assertEqual(
                        LOCKED_OWNER_PACKAGE_CONDITION,
                        hub_package_condition,
                    )

    def test_engine_contract_package_declares_gplv3_license_metadata(self) -> None:
        root = ET.parse(REPO_ROOT / "Chummer.Contracts/Chummer.Contracts.csproj").getroot()
        values = {
            local_name(element.tag): (element.text or "").strip()
            for element in root.iter()
        }
        self.assertEqual("GPL-3.0-only", values["PackageLicenseExpression"])

    def test_gm_edit_runtime_package_is_core_owned_and_store_backed(self) -> None:
        root = ET.parse(
            REPO_ROOT / "Chummer.GmCharacterEdits/Chummer.GmCharacterEdits.csproj"
        ).getroot()
        values = {
            local_name(element.tag): (element.text or "").strip()
            for element in root.iter()
        }
        self.assertEqual(
            "Chummer.Engine.GmCharacterEdits", values["PackageId"]
        )
        self.assertEqual("GPL-3.0-only", values["PackageLicenseExpression"])
        factory = (
            REPO_ROOT
            / "Chummer.GmCharacterEdits/CoreGmCharacterEditGatewayFactory.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("new FileWorkspaceStore(stateRoot)", factory)
        self.assertNotIn("InMemoryWorkspaceStore", factory)
        self.assertIn("Directory.Exists(stateRoot)", factory)
        self.assertIn("FileAttributes.ReparsePoint", factory)

    def test_lock_rejects_branch_or_tag_authority(self) -> None:
        payload = copy.deepcopy(self.payload)
        payload["packages"][0]["commit"] = "main"
        with self.assertRaisesRegex(MODULE.PackagePlaneError, "exact lowercase 40-character SHA"):
            MODULE.validate_lock_payload(payload)

    def test_lock_rejects_mutable_or_ranged_package_versions(self) -> None:
        for version in ("1.*", "[1.0.0,)", "latest", "01.0.0"):
            payload = copy.deepcopy(self.payload)
            payload["package_version"] = version
            with self.subTest(version=version), self.assertRaisesRegex(
                MODULE.PackagePlaneError, "exact SemVer"
            ):
                MODULE.validate_lock_payload(payload)

    def test_lock_rejects_project_path_escape(self) -> None:
        payload = copy.deepcopy(self.payload)
        payload["packages"][0]["project"] = "../outside.csproj"
        with self.assertRaisesRegex(MODULE.PackagePlaneError, "contained relative path"):
            MODULE.validate_lock_payload(payload)

    def test_lock_rejects_package_set_drift(self) -> None:
        payload = copy.deepcopy(self.payload)
        payload["packages"][-1]["id"] = payload["packages"][0]["id"]
        with self.assertRaisesRegex(MODULE.PackagePlaneError, "exact ordered package plane"):
            MODULE.validate_lock_payload(payload)

    def test_package_validator_rejects_internal_dependency_drift(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        spec = lock.packages[-1]
        with tempfile.TemporaryDirectory() as temporary_directory:
            feed = Path(temporary_directory)
            package = feed / f"{spec.package_id}.{lock.package_version}.nupkg"
            nuspec = f"""<?xml version="1.0"?>
<package>
  <metadata>
    <id>{spec.package_id}</id>
    <version>{lock.package_version}</version>
    <repository type="git" url="{spec.repository}" commit="{spec.commit}" />
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="Chummer.Engine.Contracts" version="999.0.0" />
      </group>
    </dependencies>
  </metadata>
</package>
"""
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr(f"{spec.package_id}.nuspec", nuspec)
                archive.writestr(f"lib/net10.0/{spec.package_id}.dll", b"not-an-assembly")
            with self.assertRaisesRegex(MODULE.PackagePlaneError, "dependency drift"):
                MODULE.validate_package(feed, spec, lock.package_version)

    def test_nupkg_canonicalization_removes_opc_and_zip_nondeterminism(self) -> None:
        def write_package(
            path: Path,
            *,
            core_token: str,
            relationship_token: str,
            timestamp: tuple[int, int, int, int, int, int],
            reverse: bool,
        ) -> None:
            core_name = (
                f"{MODULE.CORE_PROPERTIES_PREFIX}{core_token}"
                f"{MODULE.CORE_PROPERTIES_SUFFIX}"
            )
            relationships = f"""<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="{MODULE.PACKAGE_RELATIONSHIPS_NAMESPACE}">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/Example.nuspec" Id="RMANIFEST" />
  <Relationship Type="{MODULE.CORE_PROPERTIES_RELATIONSHIP_TYPE}" Target="/{core_name}" Id="{relationship_token}" />
</Relationships>""".encode("utf-8")
            members = [
                (MODULE.PACKAGE_RELATIONSHIPS_PATH, relationships),
                ("Example.nuspec", b"<package><metadata><id>Example</id></metadata></package>"),
                ("lib/net10.0/Example.dll", b"deterministic-assembly"),
                (core_name, b"<coreProperties><version>1.0.0</version></coreProperties>"),
                ("[Content_Types].xml", b"<Types />"),
            ]
            if reverse:
                members.reverse()
            with zipfile.ZipFile(path, "w") as archive:
                for name, content in members:
                    info = zipfile.ZipInfo(name, timestamp)
                    archive.writestr(info, content)

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = root / "first.nupkg"
            second = root / "second.nupkg"
            write_package(
                first,
                core_token="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                relationship_token="RFIRST",
                timestamp=(2026, 7, 20, 10, 0, 0),
                reverse=False,
            )
            write_package(
                second,
                core_token="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                relationship_token="RSECOND",
                timestamp=(2026, 7, 20, 10, 1, 0),
                reverse=True,
            )

            first_digest = MODULE.canonicalize_nupkg(first)
            second_digest = MODULE.canonicalize_nupkg(second)
            self.assertEqual(first_digest, second_digest)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            self.assertEqual(first_digest, MODULE.canonicalize_nupkg(first))

            with zipfile.ZipFile(first) as archive:
                infos = archive.infolist()
                names = [info.filename for info in infos]
                self.assertEqual(names, sorted(names, key=lambda value: (value.casefold(), value)))
                self.assertTrue(
                    any(
                        name.startswith(MODULE.CORE_PROPERTIES_PREFIX)
                        and name.endswith(MODULE.CORE_PROPERTIES_SUFFIX)
                        and len(Path(name).stem) == 64
                        for name in names
                    )
                )
                self.assertTrue(
                    all(info.date_time == MODULE.CANONICAL_ZIP_TIMESTAMP for info in infos)
                )

    def test_exact_head_checkout_is_recreated_when_tracked_or_untracked_bytes_are_dirty(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "source"
            workspace = root / "workspace"
            source.mkdir()
            subprocess.run(["git", "init", "--quiet"], cwd=source, check=True)
            subprocess.run(
                ["git", "config", "user.email", "package-plane@example.invalid"],
                cwd=source,
                check=True,
            )
            subprocess.run(
                ["git", "config", "user.name", "Package Plane Test"],
                cwd=source,
                check=True,
            )
            (source / "authority.txt").write_text("trusted\n", encoding="utf-8")
            subprocess.run(["git", "add", "authority.txt"], cwd=source, check=True)
            subprocess.run(["git", "commit", "--quiet", "-m", "authority"], cwd=source, check=True)
            commit = subprocess.check_output(
                ["git", "rev-parse", "HEAD"], cwd=source, text=True
            ).strip()
            spec = MODULE.PackageSpec(
                "Chummer.Engine.Contracts",
                str(source.resolve()),
                commit,
                "checkout",
                "Chummer.Contracts/Chummer.Contracts.csproj",
            )

            checkout = MODULE.ensure_exact_checkout(workspace, spec)
            (checkout / "authority.txt").write_text("malicious\n", encoding="utf-8")
            (checkout / "untracked.dll").write_bytes(b"malicious")
            recreated = MODULE.ensure_exact_checkout(workspace, spec)

            self.assertEqual("trusted\n", (recreated / "authority.txt").read_text(encoding="utf-8"))
            self.assertFalse((recreated / "untracked.dll").exists())
            self.assertEqual(
                "",
                subprocess.check_output(
                    ["git", "status", "--porcelain=v1", "--untracked-files=all"],
                    cwd=recreated,
                    text=True,
                ).strip(),
            )

    def test_inventory_rejects_malicious_dll_with_valid_package_metadata(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        lock_sha256 = hashlib.sha256(LOCK_PATH.read_bytes()).hexdigest()

        def write_package(path: Path, package_spec, assembly_bytes: bytes) -> None:
            dependencies = "".join(
                f'<dependency id="{dependency}" version="{lock.package_version}" />'
                for dependency in MODULE.EXPECTED_INTERNAL_DEPENDENCIES[package_spec.package_id]
            )
            nuspec = f"""<?xml version="1.0"?>
<package>
  <metadata>
    <id>{package_spec.package_id}</id>
    <version>{lock.package_version}</version>
    <repository type="git" url="{package_spec.repository}" commit="{package_spec.commit}" />
    {f'<license type="expression">{MODULE.EXPECTED_LICENSE_EXPRESSIONS[package_spec.package_id]}</license>' if package_spec.package_id in MODULE.EXPECTED_LICENSE_EXPRESSIONS else ''}
    <dependencies><group targetFramework="net10.0">{dependencies}</group></dependencies>
  </metadata>
</package>
"""
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr(f"{package_spec.package_id}.nuspec", nuspec)
                archive.writestr(
                    f"lib/net10.0/{package_spec.package_id}.dll",
                    assembly_bytes,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            feed = Path(temporary_directory)
            for package_spec in lock.packages:
                write_package(
                    feed / f"{package_spec.package_id}.{lock.package_version}.nupkg",
                    package_spec,
                    b"A" * 64,
                )
            inventory = MODULE._inventory_payload(
                lock,
                feed=feed,
                lock_sha256=lock_sha256,
            )
            MODULE._atomic_write_json(feed / MODULE.INVENTORY_FILE_NAME, inventory)
            MODULE.validate_feed_inventory(feed, lock, lock_sha256)

            unlisted = feed / "Newtonsoft.Json.13.0.3.nupkg"
            unlisted.write_bytes(b"unlisted package bytes")
            with self.assertRaisesRegex(MODULE.PackagePlaneError, "exact locked file set"):
                MODULE.validate_feed_inventory(feed, lock, lock_sha256)
            unlisted.unlink()

            inventory["unbound_field"] = "must fail closed"
            MODULE._atomic_write_json(feed / MODULE.INVENTORY_FILE_NAME, inventory)
            with self.assertRaisesRegex(MODULE.PackagePlaneError, "exact top-level fields"):
                MODULE.validate_feed_inventory(feed, lock, lock_sha256)
            inventory.pop("unbound_field")
            MODULE._atomic_write_json(feed / MODULE.INVENTORY_FILE_NAME, inventory)

            target_spec = lock.packages[0]
            write_package(
                feed / f"{target_spec.package_id}.{lock.package_version}.nupkg",
                target_spec,
                b"B" * 64,
            )
            with self.assertRaisesRegex(MODULE.PackagePlaneError, "byte digest mismatch"):
                MODULE.validate_feed_inventory(feed, lock, lock_sha256)

    def test_engine_package_validator_rejects_missing_license_metadata(self) -> None:
        lock = MODULE.validate_lock_payload(self.payload)
        spec = lock.packages[0]
        with tempfile.TemporaryDirectory() as temporary_directory:
            feed = Path(temporary_directory)
            package = feed / f"{spec.package_id}.{lock.package_version}.nupkg"
            nuspec = f"""<?xml version="1.0"?>
<package>
  <metadata>
    <id>{spec.package_id}</id>
    <version>{lock.package_version}</version>
    <repository type="git" url="{spec.repository}" commit="{spec.commit}" />
  </metadata>
</package>
"""
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr(f"{spec.package_id}.nuspec", nuspec)
                archive.writestr(f"lib/net10.0/{spec.package_id}.dll", b"not-an-assembly")
            with self.assertRaisesRegex(MODULE.PackagePlaneError, "license mismatch"):
                MODULE.validate_package(feed, spec, lock.package_version)

    def test_normal_build_does_not_build_ambient_sibling_projects(self) -> None:
        build_script = (REPO_ROOT / "scripts/ai/build.sh").read_text(encoding="utf-8")
        self.assertNotIn("../chummer-hub-registry", build_script)
        self.assertNotIn("../chummer.run-services", build_script)
        bootstrap = (REPO_ROOT / "scripts/ai/bootstrap-contracts-feed.sh").read_text(encoding="utf-8")
        self.assertIn("bootstrap-owner-contracts-feed.py", bootstrap)
        self.assertNotIn("CHUMMER_FORCE_OWNER_CONTRACTS_BOOTSTRAP", bootstrap)

    def test_no_siblings_ci_is_automatic_and_package_mapped(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(encoding="utf-8")
        verifier = (REPO_ROOT / "scripts/ai/verify-no-siblings-package-plane.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("pull_request:", workflow)
        self.assertIn("verify-no-siblings-package-plane.sh", workflow)
        self.assertIn('<package pattern="Chummer.*" />', verifier)
        self.assertIn("--no-cache", verifier)
        self.assertIn("no_sibling_directories", verifier)
        self.assertIn("normal_local_engine_dependency_graph", verifier)
        self.assertIn("package_inventory_sha256", verifier)
        self.assertIn(
            'candidate_version_prefix="0.0.0-packageplane.candidate"',
            verifier,
        )
        self.assertIn(
            'candidate_version="$candidate_version_prefix.sha${candidate_commit:0:12}"',
            verifier,
        )
        self.assertIn("chummer-core.candidate-engine-contract-package-inventory/v1", verifier)
        self.assertIn('"role": "current_core_candidate"', verifier)
        self.assertIn('"locked_engine_baseline_not_selected"', verifier)
        self.assertIn('"locked_owner_dependency"', verifier)
        self.assertIn(
            '"-p:ChummerEngineContractsPackageVersion=$candidate_version"',
            verifier,
        )
        candidate_pack = verifier.index(
            'dotnet pack "$consumer_root/Chummer.Contracts/Chummer.Contracts.csproj"'
        )
        isolated_restore = verifier.index(
            'dotnet restore "$consumer_root/Chummer.CoreEngine.sln"'
        )
        self.assertLess(candidate_pack, isolated_restore)
        self.assertNotIn(
            '-p:PackageVersion="$locked_version"',
            verifier[candidate_pack:],
        )


if __name__ == "__main__":
    unittest.main()
