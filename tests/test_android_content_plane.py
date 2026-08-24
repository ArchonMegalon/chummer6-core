from __future__ import annotations

import copy
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts/ai/android-content-plane.py"
LOCK_PATH = REPO_ROOT / "eng/android-content-plane.lock.json"
WORKFLOW_PATH = REPO_ROOT / ".github/workflows/android-content-plane.yml"


def load_script():
    spec = importlib.util.spec_from_file_location("android_content_plane", SCRIPT_PATH)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


plane = load_script()


class AndroidContentPlaneTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.lock, cls.lock_sha256 = plane.load_lock(LOCK_PATH)
        cls.content = plane.collect_content(REPO_ROOT, cls.lock)
        cls.licenses = plane.collect_licenses(REPO_ROOT, cls.lock)
        cls.authority = plane.Authority(
            cls.lock,
            cls.lock_sha256,
            "1" * 40,
            "2" * 40,
            tuple(
                {"path": path, "size": 1, "sha256": "3" * 64}
                for path in plane.PRODUCER_INPUT_PATHS
            ),
            cls.content,
            cls.licenses,
        )

    def temporary_directory(self) -> tempfile.TemporaryDirectory[str]:
        tmpfs = Path("/dev/shm")
        return tempfile.TemporaryDirectory(dir=tmpfs if tmpfs.is_dir() else None)

    def test_exact_bc082_source_authority_and_content_digest(self) -> None:
        self.assertEqual(110, len(self.content))
        self.assertEqual(17_371_170, sum(item.size for item in self.content))
        self.assertEqual({"100644"}, {item.mode for item in self.content})
        self.assertEqual(plane.CONTENT_DIGEST, plane._content_digest(self.content))
        self.assertEqual(
            {"data": 94, "lang": 16},
            {
                root: sum(item.path.startswith(f"{root}/") for item in self.content)
                for root in ("data", "lang")
            },
        )
        largest = max(self.content, key=lambda item: item.size)
        self.assertEqual("lang/fr-fr_data.xml", largest.path)
        self.assertEqual(1_955_664, largest.size)

    def test_exact_legal_authority_is_auxiliary_not_runtime_content(self) -> None:
        self.assertEqual(2, len(self.licenses))
        self.assertEqual(67_574, sum(item.size for item in self.licenses))
        self.assertEqual(
            ["licenses/Chummer-LICENSE.txt", "licenses/LICENSE"],
            [item.path for item in self.licenses],
        )
        self.assertFalse(any(item.path.startswith("licenses/") for item in self.content))

    def test_lock_rejects_weakened_or_ambiguous_authority(self) -> None:
        mutations = (
            ("sourceCommit", "0" * 40),
            ("sourceTree", "0" * 40),
            ("contentFileCount", 111),
            ("contentByteCount", 17_371_171),
            ("contentDigest", "0" * 64),
        )
        for key, value in mutations:
            with self.subTest(key=key):
                candidate = copy.deepcopy(self.lock)
                candidate[key] = value
                with self.assertRaises(plane.ContentPlaneError):
                    plane.validate_lock(candidate)

        candidate = copy.deepcopy(self.lock)
        candidate["limits"]["maxContentBytes"] += 1
        with self.assertRaises(plane.ContentPlaneError):
            plane.validate_lock(candidate)
        candidate = copy.deepcopy(self.lock)
        candidate["producerInputPaths"].pop()
        with self.assertRaises(plane.ContentPlaneError):
            plane.validate_lock(candidate)

    def test_strict_json_rejects_duplicate_keys(self) -> None:
        with self.assertRaisesRegex(plane.ContentPlaneError, "duplicate JSON key"):
            plane._read_json_bytes(b'{"contract":"a","contract":"b"}', "hostile")
        with self.assertRaisesRegex(plane.ContentPlaneError, "non-standard JSON constant"):
            plane._read_json_bytes(b'{"size":NaN}', "hostile")

    def test_path_policy_rejects_traversal_case_nfc_controls_and_reserved_names(self) -> None:
        hostile = (
            "data/../lang/en-us.xml",
            "data\\actions.xml",
            "data/C:/actions.xml",
            "/data/actions.xml",
            "data//actions.xml",
            "data/actions.xml ",
            "data/con.xml",
            "data/action\n.xml",
            "data/e\u0301.xml",
        )
        for path in hostile:
            with self.subTest(path=path), self.assertRaises(plane.ContentPlaneError):
                plane._validate_path(path, ("data", "lang"), 240)
        with self.assertRaisesRegex(plane.ContentPlaneError, "case/NFC-colliding"):
            plane._validate_unique_paths(
                ["data/ACTIONS.xml", "data/actions.xml"], "hostile inventory"
            )

    def test_export_has_exact_115_member_content_only_layout_and_revalidates(self) -> None:
        with self.temporary_directory() as temporary:
            export = Path(temporary) / "artifact"
            receipt = plane.export_artifact(self.authority, export, "7", "2")
            verified = plane.verify_export(self.authority, export, "7", "2")
            observed = {
                path.relative_to(export).as_posix()
                for path in export.rglob("*")
                if path.is_file()
            }
            self.assertEqual(115, len(observed))
            self.assertEqual(110, sum(path.startswith("content/data/") or path.startswith("content/lang/") for path in observed))
            self.assertEqual(receipt, verified)
            self.assertEqual(0, receipt["forbiddenRuntimeMemberCount"])
            self.assertFalse(any(path.casefold().endswith(plane.FORBIDDEN_SUFFIXES) for path in observed))

    def test_export_refuses_an_existing_or_symlinked_target(self) -> None:
        with self.temporary_directory() as temporary:
            existing = Path(temporary) / "existing"
            existing.mkdir()
            with self.assertRaisesRegex(plane.ContentPlaneError, "must not already exist"):
                plane.export_artifact(self.authority, existing, "1", "1")
            link = Path(temporary) / "link"
            link.symlink_to(existing, target_is_directory=True)
            with self.assertRaisesRegex(plane.ContentPlaneError, "must not already exist"):
                plane.export_artifact(self.authority, link, "1", "1")

    def test_final_revalidation_rejects_tamper_extra_assembly_missing_license_and_symlink(self) -> None:
        mutations = ("tamper", "assembly", "license", "symlink")
        for mutation in mutations:
            with self.subTest(mutation=mutation), self.temporary_directory() as temporary:
                export = Path(temporary) / "artifact"
                plane.export_artifact(self.authority, export, "9", "1")
                if mutation == "tamper":
                    target = export / "content/data/actions.xml"
                    target.write_bytes(target.read_bytes() + b"hostile")
                elif mutation == "assembly":
                    (export / "authority/Injected.dll").write_bytes(b"assembly")
                elif mutation == "license":
                    (export / "licenses/LICENSE").unlink()
                else:
                    target = export / "licenses/LICENSE"
                    target.unlink()
                    target.symlink_to(export / "content/data/actions.xml")
                with self.assertRaises(plane.ContentPlaneError):
                    plane.verify_export(self.authority, export, "9", "1")

    def test_final_revalidation_rejects_hardlinks_and_extra_empty_directories(self) -> None:
        with self.temporary_directory() as temporary:
            export = Path(temporary) / "artifact"
            plane.export_artifact(self.authority, export, "11", "3")
            license_path = export / "licenses/LICENSE"
            license_path.unlink()
            os.link(export / "content/data/actions.xml", license_path)
            with self.assertRaisesRegex(plane.ContentPlaneError, "hard-linked"):
                plane.verify_export(self.authority, export, "11", "3")
        with self.temporary_directory() as temporary:
            export = Path(temporary) / "artifact"
            plane.export_artifact(self.authority, export, "11", "3")
            extra = export / "content/extra-empty"
            extra.mkdir(mode=0o755)
            extra.chmod(0o755)
            with self.assertRaisesRegex(plane.ContentPlaneError, "directories differ"):
                plane.verify_export(self.authority, export, "11", "3")

    def test_final_revalidation_rejects_a_weakened_root_mode(self) -> None:
        with self.temporary_directory() as temporary:
            export = Path(temporary) / "artifact"
            plane.export_artifact(self.authority, export, "13", "1")
            export.chmod(0o775)
            with self.assertRaisesRegex(plane.ContentPlaneError, "root mode"):
                plane.verify_export(self.authority, export, "13", "1")

    def test_descriptor_snapshot_rejects_mutations_after_the_initial_walk(self) -> None:
        def file_mode(export: Path) -> None:
            (export / "content/data/actions.xml").chmod(0o666)

        def file_bytes(export: Path) -> None:
            target = export / "content/data/actions.xml"
            value = target.read_bytes()
            target.write_bytes(bytes((value[0] ^ 1,)) + value[1:])

        def file_size(export: Path) -> None:
            target = export / "content/data/actions.xml"
            target.write_bytes(target.read_bytes() + b"hostile")

        def directory_mode(export: Path) -> None:
            (export / "content/data").chmod(0o775)

        def directory_membership(export: Path) -> None:
            extra = export / "content/data/hostile-empty"
            extra.mkdir(mode=0o755)
            extra.chmod(0o755)

        mutations = {
            "file-mode": file_mode,
            "file-bytes": file_bytes,
            "file-size": file_size,
            "directory-mode": directory_mode,
            "directory-membership": directory_membership,
        }
        for label, mutate in mutations.items():
            with self.subTest(mutation=label), self.temporary_directory() as temporary:
                export = Path(temporary) / "artifact"
                plane.export_artifact(self.authority, export, "17", "2")
                original_walk = plane._walk_export

                def mutate_after_walk(path: Path):
                    snapshot = original_walk(path)
                    mutate(path)
                    return snapshot

                with mock.patch.object(plane, "_walk_export", side_effect=mutate_after_walk):
                    with self.assertRaises(plane.ContentPlaneError):
                        plane.verify_export(self.authority, export, "17", "2")

    def test_receipt_is_source_producer_inventory_run_and_layout_bound(self) -> None:
        members, inventory, receipt = plane.build_artifact_members(self.authority, "23", "4")
        self.assertEqual(115, len(members))
        self.assertEqual(plane.SOURCE_COMMIT, inventory["sourceAuthority"]["commit"])
        self.assertEqual("1" * 40, inventory["producerAuthority"]["commit"])
        self.assertEqual(23, receipt["producerAuthority"]["runId"])
        self.assertEqual(4, receipt["producerAuthority"]["runAttempt"])
        self.assertEqual(self.lock_sha256, receipt["lock"]["sha256"])
        self.assertEqual(110, receipt["content"]["fileCount"])
        self.assertEqual(17_371_170, receipt["content"]["byteCount"])
        self.assertEqual(2, receipt["licenses"]["fileCount"])
        self.assertEqual(115, receipt["artifactMemberCount"])
        seal = members["authority/producer-receipt.json.sha256"].decode("ascii")
        self.assertRegex(seal, r"^[0-9a-f]{64}  producer-receipt\.json\n$")

    def test_workflow_is_content_only_least_privilege_and_pinned(self) -> None:
        workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("permissions:\n  contents: read", workflow)
        self.assertIn("persist-credentials: false", workflow)
        self.assertIn("fetch-depth: 0", workflow)
        self.assertIn(
            "actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5",
            workflow,
        )
        self.assertIn(
            "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02",
            workflow,
        )
        self.assertIn(
            "actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093",
            workflow,
        )
        self.assertIn("--expected-producer-commit \"${GITHUB_SHA}\"", workflow)
        self.assertIn("CONTENT_ARTIFACT_ID", workflow)
        self.assertIn("CONTENT_ARTIFACT_DIGEST", workflow)
        self.assertIn(
            "artifact-ids: ${{ steps.upload-android-content.outputs.artifact-id }}",
            workflow,
        )
        self.assertIn("merge-multiple: true", workflow)
        self.assertIn("post-upload verification root already exists", workflow)
        self.assertEqual(2, workflow.count("--verify-export"))
        for forbidden in (
            "dotnet ",
            "curl ",
            "wget ",
            "nuget",
            "runtime-package-plane",
            "verify-no-siblings-package-plane",
        ):
            self.assertNotIn(forbidden, workflow.casefold())

    def test_lock_has_no_static_producer_commit_or_guessed_artifact_identity(self) -> None:
        raw = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
        self.assertNotIn("producerCommit", raw)
        self.assertNotIn("artifactId", raw["artifact"])
        self.assertNotIn("artifactDigest", raw["artifact"])
        self.assertEqual(list(plane.PRODUCER_INPUT_PATHS), raw["producerInputPaths"])


if __name__ == "__main__":
    unittest.main()
