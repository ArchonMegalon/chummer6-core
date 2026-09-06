from __future__ import annotations

import copy
import hashlib
import importlib.util
import io
import json
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts/ai/runtime-package-plane.py"
MODULE_SPEC = importlib.util.spec_from_file_location("runtime_package_plane", SCRIPT_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError("runtime package plane validator is unavailable")
runtime = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_SPEC.name] = runtime
MODULE_SPEC.loader.exec_module(runtime)


class RuntimePackageLockTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.lock_path = REPO_ROOT / "eng/runtime-package-plane.lock.json"
        cls.lock = runtime.load_lock(cls.lock_path)

    def test_checked_repository_matches_immutable_runtime_lock(self) -> None:
        runtime.validate_repository(REPO_ROOT, self.lock)

    def test_runtime_source_commit_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["runtime_source"]["commit"] = "0" * 40
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "runtime source authority"):
            runtime.validate_lock_payload(altered)

    def test_candidate_version_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["package_version"] += ".newer"
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "version is not exact"):
            runtime.validate_lock_payload(altered)

    def test_candidate_version_is_bound_to_runtime_source_prefix(self) -> None:
        self.assertEqual(
            f"0.0.0-packageplane.candidate.sh{runtime.SOURCE_COMMIT[:13]}",
            runtime.PACKAGE_VERSION,
        )

    def test_next_wave_candidate_is_bound_to_reviewed_semantic_commit(self) -> None:
        self.assertEqual("07a66baa25fb5c978097bd619591abd872613c06", runtime.SOURCE_COMMIT)
        self.assertEqual("0.0.0-packageplane.candidate.sh07a66baa25fb5", runtime.PACKAGE_VERSION)

    def test_finalization_members_are_bound_to_runtime_source(self) -> None:
        self.assertEqual(4, len(runtime.CREATION_FINALIZATION_AUTHORITY_PATHS))
        self.assertEqual(4, len(set(runtime.CREATION_FINALIZATION_AUTHORITY_PATHS)))
        self.assertIn("Chummer.Tests/Chummer.CreationFinalization.Tests.csproj",
                      runtime.CREATION_FINALIZATION_AUTHORITY_PATHS)
        for member in runtime.CREATION_FINALIZATION_AUTHORITY_PATHS:
            with self.subTest(member=member):
                runtime._run(
                    ("git", "cat-file", "-e", f"{runtime.SOURCE_COMMIT}:{member}"),
                    cwd=REPO_ROOT,
                )

    def test_after_run_reward_members_are_bound_to_runtime_source(self) -> None:
        self.assertEqual(11, len(runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS))
        self.assertEqual(11, len(set(runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS)))
        self.assertIn("Chummer.Contracts/Characters/CharacterAfterRunRewardContracts.cs",
                      runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS)
        self.assertIn("Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
                      runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS)
        self.assertIn("Chummer.Tests/CharacterAfterRunSettlementRulesTests.cs",
                      runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS)
        for member in runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS:
            with self.subTest(member=member):
                runtime._run(
                    ("git", "cat-file", "-e", f"{runtime.SOURCE_COMMIT}:{member}"),
                    cwd=REPO_ROOT,
                )

    def test_missing_finalization_or_reward_source_member_is_fail_closed(self) -> None:
        real_run = runtime._run
        for member in (*runtime.CREATION_FINALIZATION_AUTHORITY_PATHS,
                       *runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS):
            def missing_member(command, *, cwd):
                if tuple(command) == ("git", "cat-file", "-e", f"{runtime.SOURCE_COMMIT}:{member}"):
                    raise runtime.RuntimePackagePlaneError("missing anchored semantic member")
                return real_run(command, cwd=cwd)

            with self.subTest(member=member), mock.patch.object(runtime, "_run", side_effect=missing_member):
                with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "missing anchored semantic member"):
                    runtime.validate_repository(REPO_ROOT, self.lock)

    def test_finalization_and_reward_semantic_drift_cannot_hide_in_recipe(self) -> None:
        real_run = runtime._run
        for member in (*runtime.CREATION_FINALIZATION_AUTHORITY_PATHS,
                       *runtime.AFTER_RUN_REWARD_AUTHORITY_PATHS):
            def semantic_drift(command, *, cwd):
                if tuple(command) == ("git", "diff", "--name-only", runtime.SOURCE_COMMIT):
                    return "\n".join((*runtime.ALLOWED_RECIPE_DELTA, member))
                return real_run(command, cwd=cwd)

            with self.subTest(member=member), mock.patch.object(runtime, "_run", side_effect=semantic_drift):
                with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "package recipe delta differs"):
                    runtime.validate_repository(REPO_ROOT, self.lock)

    def test_recipe_delta_requires_all_and_only_the_four_authorized_files(self) -> None:
        self.assertEqual(
            ("eng/runtime-package-plane.lock.json", "scripts/ai/runtime-package-plane.py",
             "scripts/ai/verify-no-siblings-package-plane.sh", "tests/test_runtime_package_plane_authority.py"),
            runtime.ALLOWED_RECIPE_DELTA,
        )
        real_run = runtime._run
        for mutation in ("missing", "extra", "untracked"):
            def altered_delta(command, *, cwd):
                if tuple(command) == ("git", "diff", "--name-only", runtime.SOURCE_COMMIT):
                    files = list(runtime.ALLOWED_RECIPE_DELTA)
                    if mutation == "missing":
                        files.pop()
                    if mutation == "extra":
                        files.append("Directory.Build.props")
                    return "\n".join(files)
                if tuple(command) == ("git", "ls-files", "--others", "--exclude-standard"):
                    return "Chummer.Application/unreviewed.cs" if mutation == "untracked" else ""
                return real_run(command, cwd=cwd)

            with self.subTest(mutation=mutation), mock.patch.object(runtime, "_run", side_effect=altered_delta):
                with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "package recipe delta differs"):
                    runtime.validate_repository(REPO_ROOT, self.lock)

    def test_creation_activation_members_are_bound_to_runtime_source(self) -> None:
        self.assertEqual(8, len(runtime.CREATION_ACTIVATION_AUTHORITY_PATHS))
        for member in runtime.CREATION_ACTIVATION_AUTHORITY_PATHS:
            with self.subTest(member=member):
                runtime._run(
                    ("git", "cat-file", "-e", f"{runtime.SOURCE_COMMIT}:{member}"),
                    cwd=REPO_ROOT,
                )

    def test_creation_source_input_members_are_bound_to_runtime_source(self) -> None:
        self.assertEqual(2, len(runtime.CREATION_SOURCE_INPUT_AUTHORITY_PATHS))
        for member in runtime.CREATION_SOURCE_INPUT_AUTHORITY_PATHS:
            with self.subTest(member=member):
                runtime._run(
                    ("git", "cat-file", "-e", f"{runtime.SOURCE_COMMIT}:{member}"),
                    cwd=REPO_ROOT,
                )

    def test_sdk_archive_authority_is_fail_closed(self) -> None:
        for field, altered_value in (
            ("version", "10.0.104"),
            ("rid", "linux-arm64"),
            ("archive_url", "https://example.invalid/sdk.tar.gz"),
            ("archive_sha512", "0" * 128),
        ):
            with self.subTest(field=field):
                altered = copy.deepcopy(self.lock)
                altered["dotnet_sdk"][field] = altered_value
                with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "SDK is not exact"):
                    runtime.validate_lock_payload(altered)

    def test_runtime_lock_rejects_duplicate_keys(self) -> None:
        with tempfile.TemporaryDirectory(prefix="runtime-lock-duplicate-") as directory:
            path = Path(directory) / "lock.json"
            path.write_text(
                self.lock_path.read_text(encoding="utf-8").replace(
                    "{\n",
                    '{\n  "contract": "ambiguous",\n',
                    1,
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
                runtime.load_lock(path)

    def test_runtime_lock_rejects_nonfinite_numbers(self) -> None:
        for index, value in enumerate(("NaN", "1e999")):
            with self.subTest(value=value), tempfile.TemporaryDirectory(
                prefix="runtime-lock-nonfinite-"
            ) as directory:
                path = Path(directory) / f"lock-{index}.json"
                path.write_text(
                    self.lock_path.read_text(encoding="utf-8").replace(
                        '"contract": "chummer-core.runtime-package-plane-lock/v1"',
                        f'"contract": {value}',
                        1,
                    ),
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "non-finite JSON"):
                    runtime.load_lock(path)

    def test_external_owner_commit_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["external_owner_packages"][0]["commit"] = "0" * 40
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "external owner"):
            runtime.validate_lock_payload(altered)

    def test_package_order_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["packages"][1], altered["packages"][2] = (
            altered["packages"][2],
            altered["packages"][1],
        )
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "order, ownership"):
            runtime.validate_lock_payload(altered)

    def test_direct_dependency_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["packages"][-1]["dependencies"].pop(0)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "dependencies drifted"):
            runtime.validate_lock_payload(altered)

    def test_third_party_dependency_version_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["third_party_packages"][0]["version"] = "10.0.1"
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "third-party"):
            runtime.validate_lock_payload(altered)

    def test_project_recipe_digest_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["packages"][0]["project_sha256"] = "0" * 64
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "order, ownership"):
            runtime.validate_lock_payload(altered)

    def test_build_authority_byte_drift_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["build_authority_files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "build authority bytes drifted"):
            runtime.validate_repository(REPO_ROOT, altered)

    def test_allowed_recipe_delta_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["allowed_recipe_delta"].pop()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "allowed package recipe delta"):
            runtime.validate_lock_payload(altered)

    def test_canonicalizer_digest_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        canonicalizer = next(
            row
            for row in altered["build_authority_files"]
            if row["path"] == "scripts/ai/bootstrap-owner-contracts-feed.py"
        )
        canonicalizer["sha256"] = "0" * 64
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "canonicalizer bytes drifted"):
            runtime._canonicalizer(REPO_ROOT, altered)

    def test_gm_normal_project_semantics_cannot_be_exposed(self) -> None:
        gm = runtime.PACKAGE_SPECS[-1]
        root = ET.parse(REPO_ROOT / gm.project).getroot()
        project_reference = next(
            element for element in root.iter() if runtime._local_name(element.tag) == "ProjectReference"
        )
        private_assets = next(
            element
            for element in project_reference
            if runtime._local_name(element.tag) == "PrivateAssets"
        )
        project_reference.remove(private_assets)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "preserve normal PrivateAssets"):
            runtime._project_dependencies(REPO_ROOT, gm, root)


class RuntimePackageArchiveTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="runtime-package-plane-test-")
        self.feed = Path(self.temporary.name)
        for package in runtime.PACKAGE_SPECS:
            self._write_package(package)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def _version(dependency: str) -> str:
        external = {row[0]: row[1] for row in runtime.EXTERNAL_OWNER_PACKAGES}
        third_party = dict(runtime.THIRD_PARTY_PACKAGES)
        return external.get(dependency, third_party.get(dependency, runtime.PACKAGE_VERSION))

    def _nuspec(
        self,
        package,
        *,
        dependencies: tuple[str, ...] | None = None,
        source_commit: str | None = None,
        target_framework: str | None = None,
        omit_dependency_group: bool = False,
    ) -> bytes:
        root = ET.Element("package")
        metadata = ET.SubElement(root, "metadata")
        ET.SubElement(metadata, "id").text = package.package_id
        ET.SubElement(metadata, "version").text = runtime.PACKAGE_VERSION
        ET.SubElement(metadata, "license", {"type": "expression"}).text = runtime.LICENSE_EXPRESSION
        ET.SubElement(
            metadata,
            "repository",
            {
                "type": "git",
                "url": runtime.SOURCE_REPOSITORY,
                "commit": source_commit or runtime.SOURCE_COMMIT,
            },
        )
        selected_dependencies = dependencies if dependencies is not None else package.dependencies
        if not omit_dependency_group:
            groups = ET.SubElement(metadata, "dependencies")
            group = ET.SubElement(
                groups,
                "group",
                {"targetFramework": target_framework or runtime.TARGET_FRAMEWORK},
            )
            for dependency in selected_dependencies:
                ET.SubElement(
                    group,
                    "dependency",
                    {
                        "id": dependency,
                        "version": self._version(dependency),
                        "exclude": "Build,Analyzers",
                    },
                )
        return ET.tostring(root, encoding="utf-8", xml_declaration=True)

    def _write_package(
        self,
        package,
        *,
        dependencies: tuple[str, ...] | None = None,
        source_commit: str | None = None,
        target_framework: str | None = None,
        omit_dependency_group: bool = False,
        extra_dll: str | None = None,
        extra_payload: str | None = None,
    ) -> None:
        package_path = self.feed / f"{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg"
        runtime_assemblies = (
            runtime.GM_RUNTIME_ASSEMBLY_PATHS
            if package.package_id == "Chummer.Engine.GmCharacterEdits"
            else (f"lib/net10.0/{package.assembly}",)
        )
        entries = {
            f"{package.package_id}.nuspec": self._nuspec(
                package,
                dependencies=dependencies,
                source_commit=source_commit,
                target_framework=target_framework,
                omit_dependency_group=omit_dependency_group,
            ),
            **{
                assembly: b"immutable-test-assembly"
                for assembly in runtime_assemblies
            },
        }
        if extra_dll is not None:
            entries[f"lib/net10.0/{extra_dll}"] = b"foreign-test-assembly"
        if extra_payload is not None:
            entries[extra_payload] = b"foreign-test-payload"
        with zipfile.ZipFile(package_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name in sorted(entries, key=lambda value: (value.casefold(), value)):
                archive.writestr(name, entries[name])

    def test_exact_runtime_package_graph_is_accepted(self) -> None:
        rows = runtime.inspect_packages(self.feed)
        self.assertEqual([row["id"] for row in rows], [row.package_id for row in runtime.PACKAGE_SPECS])
        self.assertEqual({row["assembly"] for row in rows}, {row.assembly for row in runtime.PACKAGE_SPECS})

    def test_foreign_gm_runtime_assembly_is_rejected(self) -> None:
        gm = runtime.PACKAGE_SPECS[-1]
        self._write_package(gm, extra_dll="Foreign.dll")
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "assembly set differs"):
            runtime.inspect_packages(self.feed)

    def test_missing_direct_dependency_is_rejected(self) -> None:
        application = runtime.PACKAGE_SPECS[1]
        self._write_package(application, dependencies=application.dependencies[:-1])
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "dependency closure"):
            runtime.inspect_packages(self.feed)

    def test_foreign_runtime_payload_is_rejected(self) -> None:
        application = runtime.PACKAGE_SPECS[1]
        self._write_package(application, extra_payload="runtimes/linux/native/foreign.so")
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "unapproved package payloads"):
            runtime.inspect_packages(self.feed)

    def test_wrong_source_commit_is_rejected(self) -> None:
        application = runtime.PACKAGE_SPECS[1]
        self._write_package(application, source_commit="0" * 40)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "source provenance"):
            runtime.inspect_packages(self.feed)

    def test_wrong_dependency_framework_is_rejected(self) -> None:
        application = runtime.PACKAGE_SPECS[1]
        self._write_package(application, target_framework="net9.0")
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "dependency closure"):
            runtime.inspect_packages(self.feed)

    def test_contracts_empty_net10_group_is_required(self) -> None:
        contracts = runtime.PACKAGE_SPECS[0]
        self._write_package(contracts, omit_dependency_group=True)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "dependency closure"):
            runtime.inspect_packages(self.feed)

    def test_inventory_binds_every_package_byte(self) -> None:
        inventory = runtime.inventory_payload(REPO_ROOT, REPO_ROOT / "eng/runtime-package-plane.lock.json", self.feed)
        runtime._atomic_json(self.feed / runtime.INVENTORY_NAME, inventory)
        digest = runtime.validate_inventory(
            REPO_ROOT,
            REPO_ROOT / "eng/runtime-package-plane.lock.json",
            self.feed,
        )
        self.assertRegex(digest, r"^[0-9a-f]{64}$")
        inventory["packages"][0]["sha256"] = "0" * 64
        runtime._atomic_json(self.feed / runtime.INVENTORY_NAME, inventory)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "stale or altered"):
            runtime.validate_inventory(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
            )

    def _write_inventory_and_receipt(self) -> tuple[Path, dict]:
        lock_path = REPO_ROOT / "eng/runtime-package-plane.lock.json"
        inventory = runtime.inventory_payload(REPO_ROOT, lock_path, self.feed)
        inventory_path = self.feed / runtime.INVENTORY_NAME
        runtime._atomic_json(inventory_path, inventory)
        inventory_sha256 = hashlib.sha256(inventory_path.read_bytes()).hexdigest()
        owner_lock_path = REPO_ROOT / "eng/package-plane.lock.json"
        owner_lock_bytes = owner_lock_path.read_bytes()
        owner_lock = json.loads(owner_lock_bytes)
        owner_rows = []
        for spec in owner_lock["packages"]:
            file_name = f"{spec['id']}.{owner_lock['package_version']}.nupkg"
            package_path = self.feed / file_name
            package_path.write_bytes(f"owner-package:{spec['id']}".encode("utf-8"))
            owner_rows.append(
                {
                    "id": spec["id"],
                    "version": owner_lock["package_version"],
                    "repository": spec["repository"],
                    "commit": spec["commit"],
                    "project": spec["project"],
                    "file_name": file_name,
                    "sha256": hashlib.sha256(package_path.read_bytes()).hexdigest(),
                    "size_bytes": package_path.stat().st_size,
                }
            )
        owner_inventory = {
            "contract": "chummer-core.owner-contract-package-inventory/v1",
            "package_plane_lock_sha256": hashlib.sha256(owner_lock_bytes).hexdigest(),
            "package_version": owner_lock["package_version"],
            "packages": owner_rows,
        }
        owner_inventory_path = self.feed / runtime.OWNER_INVENTORY_NAME
        runtime._atomic_json(owner_inventory_path, owner_inventory)
        candidate_engine_path = self.feed / runtime.CANDIDATE_ENGINE_INVENTORY_NAME
        candidate_gm_path = self.feed / runtime.CANDIDATE_GM_INVENTORY_NAME
        runtime_rows = {row["id"]: row for row in inventory["packages"]}
        candidate_engine_row = runtime_rows["Chummer.Engine.Contracts"]
        candidate_gm_row = runtime_rows["Chummer.Engine.GmCharacterEdits"]
        runtime._atomic_json(
            candidate_engine_path,
            {
                "contract": "chummer-core.candidate-engine-contract-package-inventory/v2",
                "role": "current_core_candidate",
                "runtime_source_commit": runtime.SOURCE_COMMIT,
                "package_recipe_commit": inventory["package_recipe_commit"],
                "package": {
                    "id": candidate_engine_row["id"],
                    "version": candidate_engine_row["version"],
                    "repository": candidate_engine_row["repository"],
                    "commit": candidate_engine_row["source_commit"],
                    "project": candidate_engine_row["project"],
                    "file_name": candidate_engine_row["file_name"],
                    "sha256": candidate_engine_row["sha256"],
                    "size_bytes": candidate_engine_row["size_bytes"],
                },
            },
        )
        runtime._atomic_json(
            candidate_gm_path,
            {
                "contract": "chummer-core.candidate-gm-edit-runtime-package-inventory/v2",
                "role": "current_core_candidate",
                "runtime_source_commit": runtime.SOURCE_COMMIT,
                "package_recipe_commit": inventory["package_recipe_commit"],
                "package": {
                    "id": candidate_gm_row["id"],
                    "version": candidate_gm_row["version"],
                    "repository": candidate_gm_row["repository"],
                    "commit": candidate_gm_row["source_commit"],
                    "project": candidate_gm_row["project"],
                    "file_name": candidate_gm_row["file_name"],
                    "sha256": candidate_gm_row["sha256"],
                    "size_bytes": candidate_gm_row["size_bytes"],
                    "runtime_assemblies": list(runtime.GM_RUNTIME_ASSEMBLY_PATHS),
                },
            },
        )
        locked_rows = [
            {
                "id": row["id"],
                "version": row["version"],
                "sha256": row["sha256"],
                "size_bytes": row["size_bytes"],
                "role": (
                    "locked_engine_baseline_not_selected"
                    if row["id"] == "Chummer.Engine.Contracts"
                    else "locked_owner_dependency"
                ),
            }
            for row in owner_rows
        ]
        resolved_rows = [
            *(dict(row, role="current_core_runtime_candidate") for row in inventory["packages"]),
            *(
                {
                    "id": row["id"],
                    "version": row["version"],
                    "sha256": row["sha256"],
                    "size_bytes": row["size_bytes"],
                    "role": "locked_owner_dependency",
                }
                for row in owner_rows
                if row["id"] != "Chummer.Engine.Contracts"
            ),
        ]
        receipt = {
            "contract": "chummer-core.no-siblings-package-plane/v3",
            "generated_at_utc": "2026-08-24T12:00:00Z",
            "status": "pass",
            "core_commit": inventory["package_recipe_commit"],
            "package_plane_lock_sha256": hashlib.sha256(owner_lock_bytes).hexdigest(),
            "package_inventory_sha256": hashlib.sha256(owner_inventory_path.read_bytes()).hexdigest(),
            "candidate_package_inventory_sha256": hashlib.sha256(candidate_engine_path.read_bytes()).hexdigest(),
            "candidate_runtime_package_inventory_sha256": hashlib.sha256(candidate_gm_path.read_bytes()).hexdigest(),
            "runtime_package_inventory_sha256": inventory_sha256,
            "runtime_package_plane_lock_sha256": inventory["package_plane_lock_sha256"],
            "runtime_source_commit": inventory["runtime_source_commit"],
            "package_recipe_commit": inventory["package_recipe_commit"],
            "package_version": owner_lock["package_version"],
            "candidate_package_version": inventory["package_version"],
            "locked_packages": locked_rows,
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
            "candidate_gm_edit_runtime_pack": "pass",
            "candidate_gm_edit_runtime_consumer": "pass",
            "eight_package_runtime_plane": "pass",
            "resolved_owner_contracts": resolved_rows,
        }
        receipt_path = self.feed / "source-receipt.json"
        runtime._atomic_json(receipt_path, receipt)
        return receipt_path, receipt

    def _assert_pre_final_file_mutation_rejected(
        self,
        relative_name: str,
        *,
        same_size: bool,
        suffix: str,
    ) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_dir = self.feed / f"stable-read-{suffix}"

        def mutate_exported_file() -> None:
            target = export_dir / relative_name
            original = target.read_bytes()
            if same_size:
                replacement = bytes((original[0] ^ 0xFF,)) + original[1:]
            else:
                replacement = original + b"changed-size"
            target.write_bytes(replacement)

        with self.assertRaisesRegex(
            runtime.RuntimePackagePlaneError,
            "identity differs from authority|stable byte authority differs",
        ):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_dir,
                _before_final_reopen=mutate_exported_file,
            )

    def test_export_bundle_contains_only_exact_digest_bound_members(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        (self.feed / "unrelated-owner-package.nupkg").write_bytes(b"must-not-export")
        export_dir = self.feed / "export"
        inventory_digest, receipt_digest = runtime.export_bundle(
            REPO_ROOT,
            REPO_ROOT / "eng/runtime-package-plane.lock.json",
            self.feed,
            receipt_path,
            export_dir,
        )
        self.assertRegex(inventory_digest, r"^[0-9a-f]{64}$")
        self.assertRegex(receipt_digest, r"^[0-9a-f]{64}$")
        observed = {
            path.relative_to(export_dir).as_posix()
            for path in export_dir.rglob("*")
            if path.is_file()
        }
        expected = {
            runtime.INVENTORY_NAME,
            "runtime-package-plane.lock.json",
            "no-siblings.v3.receipt.json",
            *(
                f"packages/{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg"
                for package in runtime.PACKAGE_SPECS
            ),
        }
        self.assertEqual(observed, expected)

    def test_export_rejects_existing_destination(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_dir = self.feed / "existing-export"
        export_dir.mkdir()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "must not already exist"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_dir,
            )

    def test_export_rejects_stale_receipt(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        receipt["runtime_package_inventory_sha256"] = "0" * 64
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "receipt field is stale"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "stale-receipt-export",
            )

    def test_export_rejects_truncated_v3_receipt(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        receipt.pop("isolated_package_cache")
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "v3 receipt is required"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "truncated-receipt-export",
            )

    def test_export_rejects_foreign_resolved_owner_row(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        receipt["resolved_owner_contracts"].append(
            {
                "id": "Chummer.Foreign.Contracts",
                "version": "1.0.0",
                "sha256": "0" * 64,
                "size_bytes": 1,
                "role": "locked_owner_dependency",
            }
        )
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "resolved package rows"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "foreign-row-export",
            )

    def test_export_rejects_false_or_foreign_v3_claims(self) -> None:
        mutations = (
            ("no_sibling_directories", False),
            ("isolated_package_cache", False),
            (
                "package_source_mapping",
                {"Chummer.*": "nuget.org", "other": "https://api.nuget.org/v3/index.json"},
            ),
            ("build", "skipped"),
        )
        for index, (key, value) in enumerate(mutations):
            with self.subTest(key=key):
                receipt_path, receipt = self._write_inventory_and_receipt()
                receipt[key] = value
                runtime._atomic_json(receipt_path, receipt)
                with self.assertRaisesRegex(
                    runtime.RuntimePackagePlaneError,
                    "receipt field is stale|verifier claim is invalid",
                ):
                    runtime.export_bundle(
                        REPO_ROOT,
                        REPO_ROOT / "eng/runtime-package-plane.lock.json",
                        self.feed,
                        receipt_path,
                        self.feed / f"foreign-claim-export-{index}",
                    )

    def test_export_rejects_case_colliding_package_source(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        package = runtime.PACKAGE_SPECS[0]
        source = self.feed / f"{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg"
        collision = self.feed / source.name.lower()
        collision.write_bytes(source.read_bytes())
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "exactly one"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "collision-export",
            )

    def test_export_rejects_symlinked_parent_component(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        real_parent = self.feed / "real-export-parent"
        real_parent.mkdir()
        linked_parent = self.feed / "linked-export-parent"
        linked_parent.symlink_to(real_parent, target_is_directory=True)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "symlink component"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                linked_parent / "bundle",
            )

    def test_export_rejects_parent_swapped_after_open(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_parent = self.feed / "swappable-export-parent"
        moved_parent = self.feed / "original-export-parent"
        export_parent.mkdir()

        def swap_parent() -> None:
            export_parent.rename(moved_parent)
            export_parent.mkdir()

        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "parent changed"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_parent / "bundle",
                _after_parent_open=swap_parent,
            )
        self.assertFalse((export_parent / "bundle").exists())

    def test_export_rejects_export_directory_swapped_after_fsync(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_parent = self.feed / "export-child-swap-parent"
        export_parent.mkdir()
        export_dir = export_parent / "bundle"
        moved_export = export_parent / "held-bundle"

        def swap_export() -> None:
            export_dir.rename(moved_export)
            export_dir.mkdir()
            (export_dir / "attacker.txt").write_text("substituted", encoding="utf-8")

        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "export directory changed"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_dir,
                _before_final_reopen=swap_export,
            )
        self.assertEqual([path.name for path in export_dir.iterdir()], ["attacker.txt"])

    def test_export_rejects_packages_directory_swapped_after_fsync(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_dir = self.feed / "packages-child-swap-bundle"
        moved_packages = self.feed / "held-packages"

        def swap_packages() -> None:
            packages = export_dir / "packages"
            packages.rename(moved_packages)
            packages.mkdir()
            (packages / "attacker.nupkg").write_bytes(b"substituted")

        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "packages directory changed"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_dir,
                _before_final_reopen=swap_packages,
            )

    def test_export_stable_read_rejects_same_size_package_mutation(self) -> None:
        package = runtime.PACKAGE_SPECS[0]
        self._assert_pre_final_file_mutation_rejected(
            f"packages/{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg",
            same_size=True,
            suffix="package-same-size",
        )

    def test_export_stable_read_rejects_changed_size_package_mutation(self) -> None:
        package = runtime.PACKAGE_SPECS[0]
        self._assert_pre_final_file_mutation_rejected(
            f"packages/{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg",
            same_size=False,
            suffix="package-changed-size",
        )

    def test_export_stable_read_rejects_root_authority_mutations(self) -> None:
        names = (
            runtime.INVENTORY_NAME,
            "runtime-package-plane.lock.json",
            "no-siblings.v3.receipt.json",
        )
        for name in names:
            for same_size in (True, False):
                with self.subTest(name=name, same_size=same_size):
                    self._assert_pre_final_file_mutation_rejected(
                        name,
                        same_size=same_size,
                        suffix=f"{name}-{same_size}".replace(".", "-"),
                    )

    def test_export_stable_read_rejects_hardlinked_package(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        export_dir = self.feed / "stable-read-hardlink"
        package = runtime.PACKAGE_SPECS[0]
        relative_name = (
            f"packages/{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg"
        )

        def hardlink_exported_file() -> None:
            target = export_dir / relative_name
            held = self.feed / "held-hardlink-package.nupkg"
            target.rename(held)
            target.hardlink_to(held)

        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "identity differs"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                export_dir,
                _before_final_reopen=hardlink_exported_file,
            )

    def test_export_rejects_noncanonical_dotdot_parent(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        hop = self.feed / "hop"
        hop.mkdir()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "canonical absolute path"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                hop / ".." / "dotdot-bundle",
            )

    def test_export_rejects_self_consistent_arbitrary_candidate_inventory(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        candidate_path = self.feed / runtime.CANDIDATE_ENGINE_INVENTORY_NAME
        runtime._atomic_json(candidate_path, {"candidate": "engine"})
        receipt["candidate_package_inventory_sha256"] = hashlib.sha256(
            candidate_path.read_bytes()
        ).hexdigest()
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "schema or authority differs"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "arbitrary-candidate-export",
            )

    def test_export_rejects_wrong_gm_runtime_assembly_authority(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        candidate_path = self.feed / runtime.CANDIDATE_GM_INVENTORY_NAME
        candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
        candidate["package"]["runtime_assemblies"] = ["lib/net10.0/Foreign.dll"]
        runtime._atomic_json(candidate_path, candidate)
        receipt["candidate_runtime_package_inventory_sha256"] = hashlib.sha256(
            candidate_path.read_bytes()
        ).hexdigest()
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "schema or authority differs"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "wrong-gm-assembly-export",
            )

    def test_duplicate_key_receipt_is_rejected(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        receipt_path.write_text(
            receipt_path.read_text(encoding="utf-8").replace(
                "{\n",
                '{\n  "status": "fail",\n',
                1,
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "duplicate-receipt-export",
            )

    def test_duplicate_key_owner_inventory_is_rejected_even_when_receipt_hash_matches(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        owner_inventory_path = self.feed / runtime.OWNER_INVENTORY_NAME
        owner_inventory_path.write_text(
            owner_inventory_path.read_text(encoding="utf-8").replace(
                "{\n",
                '{\n  "contract": "ambiguous",\n',
                1,
            ),
            encoding="utf-8",
        )
        receipt["package_inventory_sha256"] = hashlib.sha256(
            owner_inventory_path.read_bytes()
        ).hexdigest()
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "duplicate-owner-inventory-export",
            )

    def test_duplicate_key_candidate_inventory_is_rejected(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        candidate_path = self.feed / runtime.CANDIDATE_ENGINE_INVENTORY_NAME
        candidate_path.write_text(
            candidate_path.read_text(encoding="utf-8").replace(
                "{\n",
                '{\n  "role": "ambiguous",\n',
                1,
            ),
            encoding="utf-8",
        )
        receipt["candidate_package_inventory_sha256"] = hashlib.sha256(
            candidate_path.read_bytes()
        ).hexdigest()
        runtime._atomic_json(receipt_path, receipt)
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "duplicate-candidate-export",
            )

    def test_duplicate_key_runtime_inventory_is_rejected(self) -> None:
        receipt_path, _ = self._write_inventory_and_receipt()
        inventory_path = self.feed / runtime.INVENTORY_NAME
        inventory_path.write_text(
            inventory_path.read_text(encoding="utf-8").replace(
                "{\n",
                '{\n  "contract": "ambiguous",\n',
                1,
            ),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
            runtime.export_bundle(
                REPO_ROOT,
                REPO_ROOT / "eng/runtime-package-plane.lock.json",
                self.feed,
                receipt_path,
                self.feed / "duplicate-runtime-inventory-export",
            )

    def test_duplicate_key_owner_lock_is_rejected(self) -> None:
        receipt_path, receipt = self._write_inventory_and_receipt()
        fake_repo = self.feed / "fake-owner-lock-repo"
        (fake_repo / "eng").mkdir(parents=True)
        owner_lock_path = fake_repo / "eng/package-plane.lock.json"
        owner_lock_path.write_text(
            (REPO_ROOT / "eng/package-plane.lock.json").read_text(encoding="utf-8").replace(
                "{\n",
                '{\n  "contract": "ambiguous",\n',
                1,
            ),
            encoding="utf-8",
        )
        owner_lock_sha256 = hashlib.sha256(owner_lock_path.read_bytes()).hexdigest()
        owner_inventory_path = self.feed / runtime.OWNER_INVENTORY_NAME
        owner_inventory = json.loads(owner_inventory_path.read_text(encoding="utf-8"))
        owner_inventory["package_plane_lock_sha256"] = owner_lock_sha256
        runtime._atomic_json(owner_inventory_path, owner_inventory)
        receipt["package_plane_lock_sha256"] = owner_lock_sha256
        receipt["package_inventory_sha256"] = hashlib.sha256(
            owner_inventory_path.read_bytes()
        ).hexdigest()
        runtime._atomic_json(receipt_path, receipt)
        runtime_inventory_path = self.feed / runtime.INVENTORY_NAME
        runtime_inventory = json.loads(runtime_inventory_path.read_text(encoding="utf-8"))
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate JSON key"):
            runtime._validate_receipt(
                fake_repo,
                self.feed,
                receipt_path,
                runtime_inventory,
                hashlib.sha256(runtime_inventory_path.read_bytes()).hexdigest(),
            )


class SdkArchiveAuthorityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="sdk-archive-authority-test-")
        self.root = Path(self.temporary.name)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def _archive(self, members: tuple[tuple[str, bytes], ...]) -> tuple[Path, str]:
        path = self.root / "sdk.tar.gz"
        with tarfile.open(path, mode="w:gz") as archive:
            for name, payload in members:
                info = tarfile.TarInfo(name)
                info.size = len(payload)
                info.mode = 0o755 if name == "dotnet" else 0o644
                archive.addfile(info, io.BytesIO(payload))
        return path, hashlib.sha512(path.read_bytes()).hexdigest()

    def _destination(self, name: str = "sdk-root") -> Path:
        return self.root / name

    def test_exact_archive_extracts_from_same_hashed_descriptor(self) -> None:
        archive, digest = self._archive((("dotnet", b"digest-bound-sdk"),))
        destination = self._destination()
        runtime._extract_digest_bound_tar_gz(archive, destination, digest)
        self.assertEqual((destination / "dotnet").read_bytes(), b"digest-bound-sdk")

    def test_wrong_archive_digest_is_rejected_before_extraction(self) -> None:
        archive, _ = self._archive((("dotnet", b"wrong-digest"),))
        destination = self._destination()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "SHA-512"):
            runtime._extract_digest_bound_tar_gz(archive, destination, "0" * 128)
        self.assertFalse(destination.exists())

    def test_parent_traversal_member_is_rejected(self) -> None:
        archive, digest = self._archive((("../escape", b"hostile"),))
        destination = self._destination()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "unsafe SDK archive member"):
            runtime._extract_digest_bound_tar_gz(archive, destination, digest)
        self.assertFalse((self.root / "escape").exists())

    def test_case_colliding_members_are_rejected(self) -> None:
        archive, digest = self._archive((("sdk/A.dll", b"a"), ("sdk/a.dll", b"b")))
        destination = self._destination()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "duplicate SDK archive member"):
            runtime._extract_digest_bound_tar_gz(archive, destination, digest)

    def test_source_inode_overwrite_after_snapshot_cannot_change_extracted_bytes(self) -> None:
        archive, digest = self._archive((("dotnet", b"digest-bound-sdk"),))
        replacement = self.root / "replacement.tar.gz"
        with tarfile.open(replacement, mode="w:gz") as stream:
            info = tarfile.TarInfo("attacker-tool")
            payload = b"unhashed replacement"
            info.size = len(payload)
            stream.addfile(info, io.BytesIO(payload))
        destination = self._destination()

        runtime._extract_digest_bound_tar_gz(
            archive,
            destination,
            digest,
            _after_snapshot=lambda: archive.write_bytes(replacement.read_bytes()),
        )

        self.assertEqual((destination / "dotnet").read_bytes(), b"digest-bound-sdk")
        self.assertFalse((destination / "attacker-tool").exists())

    def test_destination_swap_after_open_is_rejected_and_cannot_redirect_extraction(self) -> None:
        archive, digest = self._archive((("dotnet", b"digest-bound-sdk"),))
        destination = self._destination()
        moved = self.root / "held-sdk-root"
        external = self.root / "external-sdk-root"
        external.mkdir()

        def swap_destination() -> None:
            destination.rename(moved)
            destination.symlink_to(external, target_is_directory=True)

        with self.assertRaisesRegex(
            runtime.RuntimePackagePlaneError,
            "extraction directory changed",
        ):
            runtime._extract_digest_bound_tar_gz(
                archive,
                destination,
                digest,
                _after_destination_open=swap_destination,
            )
        self.assertEqual(list(external.iterdir()), [])
        self.assertEqual((moved / "dotnet").read_bytes(), b"digest-bound-sdk")

    def test_precreated_sdk_destination_is_rejected(self) -> None:
        archive, digest = self._archive((("dotnet", b"digest-bound-sdk"),))
        destination = self._destination()
        destination.mkdir()
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "must not already exist"):
            runtime._extract_digest_bound_tar_gz(archive, destination, digest)

    def test_sdk_snapshot_has_a_hard_size_bound(self) -> None:
        archive, digest = self._archive((("dotnet", b"two-bytes"),))
        original_limit = runtime.SDK_ARCHIVE_MAX_BYTES
        runtime.SDK_ARCHIVE_MAX_BYTES = 1
        try:
            with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "bounded snapshot"):
                runtime._extract_digest_bound_tar_gz(
                    archive,
                    self._destination(),
                    digest,
                )
        finally:
            runtime.SDK_ARCHIVE_MAX_BYTES = original_limit


class RuntimePackageWorkflowTests(unittest.TestCase):
    def test_workflow_upload_is_pinned_and_fail_closed(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(encoding="utf-8")
        self.assertIn(
            "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02",
            workflow,
        )
        self.assertNotIn("uses: actions/upload-artifact@v", workflow)
        self.assertIn("CHUMMER_RUNTIME_PACKAGE_EXPORT_DIR: ${{ runner.temp }}/", workflow)
        self.assertIn("if-no-files-found: error", workflow)
        self.assertIn("retention-days: 5", workflow)

    def test_sdk_archive_install_is_exact_and_installer_free(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(encoding="utf-8")
        self.assertIn(runtime.SDK_ARCHIVE_URL, workflow)
        self.assertIn(runtime.SDK_ARCHIVE_SHA512, workflow)
        self.assertIn("dotnet-sdk-10.0.103-linux-x64.tar.gz", workflow)
        self.assertIn("sha512sum --check --strict", workflow)
        self.assertIn("--extract-sdk-archive", workflow)
        self.assertNotIn("dotnet-install.sh", workflow)
        self.assertNotIn("core_dotnet_script", workflow)
        self.assertNotIn('mkdir --mode=0755 "${core_dotnet_root}"', workflow)
        validator = SCRIPT_PATH.read_text(encoding="utf-8")
        self.assertEqual(runtime.SDK_RID, "linux-x64")
        self.assertIn('Path(f"/proc/self/fd/{destination_descriptor}")', validator)
        self.assertIn("tempfile.TemporaryFile", validator)
        download = workflow.index(runtime.SDK_ARCHIVE_URL)
        digest = workflow.index("sha512sum --check --strict")
        extract = workflow.index("--extract-sdk-archive")
        executable = workflow.index('test -x "${core_dotnet_root}/dotnet"')
        self.assertLess(download, digest)
        self.assertLess(digest, extract)
        self.assertLess(extract, executable)

    def test_early_restores_use_only_the_locked_source_mapping_and_cache(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(
            encoding="utf-8"
        )
        self.assertIn(
            'core_nuget_config="${RUNNER_TEMP}/chummer-core-package-plane.NuGet.Config"',
            workflow,
        )
        self.assertIn(
            'core_nuget_packages="${RUNNER_TEMP}/chummer-core-package-plane-packages"',
            workflow,
        )
        self.assertIn("<clear />", workflow)
        self.assertIn('<package pattern="Chummer.*" />', workflow)
        self.assertIn('<package pattern="*" />', workflow)
        self.assertIn('echo "NUGET_PACKAGES=${core_nuget_packages}"', workflow)
        self.assertEqual(workflow.count('--configfile "${CHUMMER_CI_NUGET_CONFIG}"'), 2)
        self.assertEqual(workflow.count('--packages "${CHUMMER_CI_NUGET_PACKAGES}"'), 2)
        self.assertGreaterEqual(workflow.count("-p:RestoreAdditionalProjectSources="), 2)
        self.assertGreaterEqual(workflow.count("-p:RestoreFallbackFolders="), 2)
        self.assertGreaterEqual(workflow.count("-p:DisableImplicitNuGetFallbackFolder=true"), 2)
        self.assertEqual(workflow.count("--force"), 2)
        self.assertGreaterEqual(workflow.count("--no-cache"), 2)
        self.assertEqual(workflow.count("ambient package folder in"), 2)
        self.assertEqual(workflow.count("owner package identities drifted in"), 2)
        self.assertEqual(workflow.count("owner package source drifted in"), 2)
        self.assertEqual(workflow.count('/ ".nupkg.metadata"'), 2)
        restore_tests = workflow.index("Restore affected Core test graph from exact sources")
        restore_feature = workflow.index(
            "Restore deterministic feature-slice graph from exact sources"
        )
        revalidate = workflow.index("Revalidate locked owner feed after early test graphs")
        export = workflow.index("Verify immutable no-siblings package plane and export bundle")
        self.assertLess(restore_tests, restore_feature)
        self.assertLess(restore_feature, revalidate)
        self.assertLess(revalidate, export)

    def test_verifier_revalidates_before_receipt_and_export(self) -> None:
        verifier = (REPO_ROOT / "scripts/ai/verify-no-siblings-package-plane.sh").read_text(
            encoding="utf-8"
        )
        final_validate = verifier.rfind("--validate-feed")
        receipt = verifier.rfind('"$receipt_path" <<\'PY\'')
        export = verifier.rfind("--export-bundle")
        self.assertGreater(final_validate, 0)
        self.assertGreater(receipt, final_validate)
        self.assertGreater(export, receipt)


if __name__ == "__main__":
    unittest.main()
