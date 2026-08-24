from __future__ import annotations

import copy
import importlib.util
import json
import sys
import tempfile
import unittest
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
        altered["packages"][-1]["dependencies"].remove("Chummer.Application")
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "dependencies drifted"):
            runtime.validate_lock_payload(altered)

    def test_project_recipe_digest_is_fail_closed(self) -> None:
        altered = copy.deepcopy(self.lock)
        altered["packages"][0]["project_sha256"] = "0" * 64
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "order, ownership"):
            runtime.validate_lock_payload(altered)

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
        return external.get(dependency, runtime.PACKAGE_VERSION)

    def _nuspec(
        self,
        package,
        *,
        dependencies: tuple[str, ...] | None = None,
        source_commit: str | None = None,
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
        groups = ET.SubElement(metadata, "dependencies")
        group = ET.SubElement(groups, "group", {"targetFramework": "net10.0"})
        for dependency in dependencies if dependencies is not None else package.dependencies:
            ET.SubElement(
                group,
                "dependency",
                {"id": dependency, "version": self._version(dependency)},
            )
        return ET.tostring(root, encoding="utf-8", xml_declaration=True)

    def _write_package(
        self,
        package,
        *,
        dependencies: tuple[str, ...] | None = None,
        source_commit: str | None = None,
        extra_dll: str | None = None,
        extra_payload: str | None = None,
    ) -> None:
        package_path = self.feed / f"{package.package_id}.{runtime.PACKAGE_VERSION}.nupkg"
        entries = {
            f"{package.package_id}.nuspec": self._nuspec(
                package,
                dependencies=dependencies,
                source_commit=source_commit,
            ),
            f"lib/net10.0/{package.assembly}": b"immutable-test-assembly",
        }
        if extra_dll is not None:
            entries[f"lib/net10.0/{extra_dll}"] = b"foreign-test-assembly"
        if extra_payload is not None:
            entries[extra_payload] = b"foreign-test-payload"
        with zipfile.ZipFile(package_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name in sorted(entries, key=lambda value: (value.casefold(), value)):
                archive.writestr(name, entries[name])

    def test_exact_thin_package_graph_is_accepted(self) -> None:
        rows = runtime.inspect_packages(self.feed)
        self.assertEqual([row["id"] for row in rows], [row.package_id for row in runtime.PACKAGE_SPECS])
        self.assertEqual({row["assembly"] for row in rows}, {row.assembly for row in runtime.PACKAGE_SPECS})

    def test_fat_gm_package_is_rejected(self) -> None:
        gm = runtime.PACKAGE_SPECS[-1]
        self._write_package(gm, extra_dll="Chummer.Application.dll")
        with self.assertRaisesRegex(runtime.RuntimePackagePlaneError, "must own only"):
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


if __name__ == "__main__":
    unittest.main()
