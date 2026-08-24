from __future__ import annotations

import hashlib
import importlib.util
import json
import shutil
import stat
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = REPO_ROOT / "scripts/ai/public-runtime-package-handoff.py"
MODULE_SPEC = importlib.util.spec_from_file_location("public_runtime_package_handoff", SCRIPT_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError("public runtime package handoff module is unavailable")
handoff = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_SPEC.name] = handoff
MODULE_SPEC.loader.exec_module(handoff)


class PublicRuntimePackageHandoffTests(unittest.TestCase):
    COMMIT = "a" * 40
    SOURCE_DIGEST = "b" * 64

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="public-runtime-handoff-test-")
        self.root = Path(self.temporary.name)
        self.repo = self.root / "repo"
        self.bundle = self.root / "bundle"
        (self.repo / "eng").mkdir(parents=True)
        (self.bundle / "packages").mkdir(parents=True)
        self.lock = json.loads(
            (REPO_ROOT / "eng/runtime-package-plane.lock.json").read_text(encoding="utf-8")
        )
        self._write_json(self.repo / "eng/runtime-package-plane.lock.json", self.lock)
        self._write_json(self.bundle / handoff.LOCK_NAME, self.lock)
        self.rows = []
        for index, spec in enumerate(self.lock["packages"]):
            package_name = f"{spec['id']}.{self.lock['package_version']}.nupkg"
            package_bytes = f"exact-test-package:{index}:{spec['id']}".encode("utf-8")
            (self.bundle / "packages" / package_name).write_bytes(package_bytes)
            self.rows.append(
                {
                    "id": spec["id"],
                    "version": self.lock["package_version"],
                    "repository": self.lock["runtime_source"]["repository"],
                    "source_commit": self.lock["runtime_source"]["commit"],
                    "project": spec["project"],
                    "assembly": spec["assembly"],
                    "target_framework": spec["target_framework"],
                    "dependencies": spec["dependencies"],
                    "file_name": package_name,
                    "sha256": hashlib.sha256(package_bytes).hexdigest(),
                    "size_bytes": len(package_bytes),
                }
            )
        lock_bytes = (self.bundle / handoff.LOCK_NAME).read_bytes()
        self.inventory = {
            "contract": "chummer-core.runtime-package-inventory/v1",
            "package_plane_lock_sha256": hashlib.sha256(lock_bytes).hexdigest(),
            "package_recipe_commit": self.COMMIT,
            "package_version": self.lock["package_version"],
            "packages": self.rows,
            "runtime_source_commit": self.lock["runtime_source"]["commit"],
        }
        inventory_path = self.bundle / handoff.INVENTORY_NAME
        self._write_json(inventory_path, self.inventory)
        inventory_digest = hashlib.sha256(inventory_path.read_bytes()).hexdigest()
        no_siblings = {
            "contract": "chummer-core.no-siblings-package-plane/v3",
            "generated_at_utc": "2026-08-24T00:00:00Z",
            "status": "pass",
            "core_commit": self.COMMIT,
            "package_plane_lock_sha256": "c" * 64,
            "package_inventory_sha256": "d" * 64,
            "candidate_package_inventory_sha256": "e" * 64,
            "candidate_runtime_package_inventory_sha256": "f" * 64,
            "runtime_package_inventory_sha256": inventory_digest,
            "runtime_package_plane_lock_sha256": hashlib.sha256(lock_bytes).hexdigest(),
            "runtime_source_commit": self.lock["runtime_source"]["commit"],
            "package_recipe_commit": self.COMMIT,
            "package_version": "owner-test-version",
            "candidate_package_version": self.lock["package_version"],
            "locked_packages": [],
            "resolved_owner_contracts": [
                dict(row, role="current_core_runtime_candidate") for row in self.rows
            ],
            "no_sibling_directories": True,
            "isolated_package_cache": True,
            "package_source_mapping": {
                "Chummer.*": "locked-owner-contracts",
                "other": "https://api.nuget.org/v3/index.json",
            },
            **{key: "pass" for key in handoff.PASS_CLAIMS},
        }
        self._write_json(self.bundle / handoff.NO_SIBLINGS_RECEIPT_NAME, no_siblings)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def _write_json(path: Path, payload: object) -> None:
        path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    def _prepare(self, name: str = "out") -> tuple[Path, Path]:
        return handoff.prepare(
            repo_root=self.repo,
            bundle_dir=self.bundle,
            output_dir=self.root / name,
            repository=handoff.REPOSITORY,
            commit=self.COMMIT,
            ref=handoff.MAIN_REF,
            event_name="push",
            source_artifact_id=9528212865,
            source_artifact_digest=f"sha256:{self.SOURCE_DIGEST}",
        )

    def _release_fixture(self) -> tuple[Path, Path, Path, Path, Path, dict]:
        archive, receipt_path = self._prepare()
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        downloads = self.root / "downloads"
        downloads.mkdir()
        downloaded_archive = downloads / archive.name
        downloaded_receipt = downloads / receipt_path.name
        shutil.copyfile(archive, downloaded_archive)
        shutil.copyfile(receipt_path, downloaded_receipt)
        receipt_bytes = receipt_path.read_bytes()
        metadata = {
            "tag_name": receipt["release_tag"],
            "target_commitish": self.COMMIT,
            "draft": False,
            "prerelease": False,
            "assets": [
                {
                    "name": archive.name,
                    "state": "uploaded",
                    "size": archive.stat().st_size,
                    "digest": f"sha256:{hashlib.sha256(archive.read_bytes()).hexdigest()}",
                },
                {
                    "name": receipt_path.name,
                    "state": "uploaded",
                    "size": len(receipt_bytes),
                    "digest": f"sha256:{hashlib.sha256(receipt_bytes).hexdigest()}",
                },
            ],
        }
        metadata_path = self.root / "release.json"
        self._write_json(metadata_path, metadata)
        tag_path = self.root / "tag.json"
        self._write_json(
            tag_path,
            {
                "ref": f"refs/tags/{receipt['release_tag']}",
                "object": {"type": "commit", "sha": self.COMMIT},
            },
        )
        return metadata_path, tag_path, receipt_path, downloaded_archive, downloaded_receipt, metadata

    def test_prepare_is_byte_deterministic_and_stored_only(self) -> None:
        archive_one, receipt_one = self._prepare("out-one")
        archive_two, receipt_two = self._prepare("out-two")
        self.assertEqual(archive_one.read_bytes(), archive_two.read_bytes())
        self.assertEqual(receipt_one.read_bytes(), receipt_two.read_bytes())
        with zipfile.ZipFile(archive_one) as archive:
            infos = archive.infolist()
            self.assertEqual(len(infos), 11)
            self.assertEqual(len(infos), len({info.filename.casefold() for info in infos}))
            for info in infos:
                self.assertEqual(info.compress_type, zipfile.ZIP_STORED)
                self.assertEqual(info.date_time, (1980, 1, 1, 0, 0, 0))
                self.assertEqual(info.create_system, 3)
                self.assertEqual(stat.S_IMODE(info.external_attr >> 16), 0o644)

    def test_prepare_receipts_exact_actions_artifact_and_member_bytes(self) -> None:
        archive, receipt_path = self._prepare()
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        self.assertEqual(receipt["commit"], self.COMMIT)
        self.assertEqual(receipt["source_actions_artifact"]["id"], 9528212865)
        self.assertEqual(receipt["source_actions_artifact"]["sha256"], self.SOURCE_DIGEST)
        self.assertEqual(receipt["bundle"]["sha256"], hashlib.sha256(archive.read_bytes()).hexdigest())
        self.assertEqual(receipt["bundle"]["size_bytes"], archive.stat().st_size)
        self.assertEqual(receipt["bundle"]["member_count"], 11)

    def test_prepare_rejects_non_main_or_non_push_context(self) -> None:
        for repository, ref, event in (
            ("attacker/fork", handoff.MAIN_REF, "push"),
            (handoff.REPOSITORY, "refs/heads/feature", "push"),
            (handoff.REPOSITORY, handoff.MAIN_REF, "pull_request"),
        ):
            with self.subTest(repository=repository, ref=ref, event=event):
                with self.assertRaisesRegex(handoff.PublicHandoffError, "exact Core main push"):
                    handoff.prepare(
                        repo_root=self.repo,
                        bundle_dir=self.bundle,
                        output_dir=self.root / f"reject-{len(list(self.root.iterdir()))}",
                        repository=repository,
                        commit=self.COMMIT,
                        ref=ref,
                        event_name=event,
                        source_artifact_id=1,
                        source_artifact_digest=self.SOURCE_DIGEST,
                    )

    def test_prepare_rejects_package_byte_drift(self) -> None:
        package = next((self.bundle / "packages").iterdir())
        package.write_bytes(package.read_bytes() + b"drift")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "package bytes differ"):
            self._prepare()

    def test_prepare_rejects_symlinked_package(self) -> None:
        package = next((self.bundle / "packages").iterdir())
        target = self.root / "foreign.nupkg"
        target.write_bytes(package.read_bytes())
        package.unlink()
        package.symlink_to(target)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "bounded regular file"):
            self._prepare()

    def test_prepare_rejects_extra_bundle_member(self) -> None:
        (self.bundle / "foreign.txt").write_text("foreign", encoding="utf-8")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "root members differ"):
            self._prepare()

    def test_prepare_never_overwrites_existing_output(self) -> None:
        existing = self.root / "out"
        existing.mkdir()
        marker = existing / "preserve"
        marker.write_text("preserve", encoding="utf-8")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "new directory"):
            self._prepare()
        self.assertEqual(marker.read_text(encoding="utf-8"), "preserve")

    def test_public_unauthenticated_release_proof_is_accepted(self) -> None:
        metadata, tag, receipt, archive, downloaded_receipt, _ = self._release_fixture()
        handoff.verify_public_release(
            release_metadata_path=metadata,
            tag_metadata_path=tag,
            receipt_path=receipt,
            downloaded_bundle_path=archive,
            downloaded_receipt_path=downloaded_receipt,
        )

    def test_public_release_rejects_extra_asset(self) -> None:
        metadata_path, tag, receipt, archive, downloaded_receipt, metadata = self._release_fixture()
        metadata["assets"].append(
            {"name": "foreign", "state": "uploaded", "size": 1, "digest": "sha256:" + "0" * 64}
        )
        self._write_json(metadata_path, metadata)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "exactly two"):
            handoff.verify_public_release(
                release_metadata_path=metadata_path,
                tag_metadata_path=tag,
                receipt_path=receipt,
                downloaded_bundle_path=archive,
                downloaded_receipt_path=downloaded_receipt,
            )

    def test_public_release_rejects_remote_byte_drift(self) -> None:
        metadata, tag, receipt, archive, downloaded_receipt, _ = self._release_fixture()
        archive.write_bytes(archive.read_bytes() + b"drift")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "byte authority differs"):
            handoff.verify_public_release(
                release_metadata_path=metadata,
                tag_metadata_path=tag,
                receipt_path=receipt,
                downloaded_bundle_path=archive,
                downloaded_receipt_path=downloaded_receipt,
            )

    def test_public_release_rejects_moved_tag_target(self) -> None:
        metadata_path, tag, receipt, archive, downloaded_receipt, metadata = self._release_fixture()
        metadata["target_commitish"] = "0" * 40
        self._write_json(metadata_path, metadata)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "target or posture"):
            handoff.verify_public_release(
                release_metadata_path=metadata_path,
                tag_metadata_path=tag,
                receipt_path=receipt,
                downloaded_bundle_path=archive,
                downloaded_receipt_path=downloaded_receipt,
            )

    def test_public_release_rejects_moved_git_ref(self) -> None:
        metadata, tag_path, receipt, archive, downloaded_receipt, _ = self._release_fixture()
        tag = json.loads(tag_path.read_text(encoding="utf-8"))
        tag["object"]["sha"] = "0" * 40
        self._write_json(tag_path, tag)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "point directly"):
            handoff.verify_public_release(
                release_metadata_path=metadata,
                tag_metadata_path=tag_path,
                receipt_path=receipt,
                downloaded_bundle_path=archive,
                downloaded_receipt_path=downloaded_receipt,
            )


class PublicRuntimePackageWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow_text = (REPO_ROOT / ".github/workflows/package-plane.yml").read_text(
            encoding="utf-8"
        )
        cls.publisher = cls.workflow_text.split(
            "  publish-public-runtime-package-handoff:\n", 1
        )[1]

    def test_build_job_remains_read_only_and_publication_write_is_isolated(self) -> None:
        prefix = self.workflow_text.split("jobs:\n", 1)[0]
        build = self.workflow_text.split("  no-siblings:\n", 1)[1].split(
            "  publish-public-runtime-package-handoff:\n", 1
        )[0]
        self.assertIn("permissions:\n  contents: read", prefix)
        self.assertNotIn("    permissions:", build)
        self.assertIn("    permissions:\n      actions: read\n      contents: write", self.publisher)
        self.assertIn("persist-credentials: false", self.publisher)

    def test_publication_is_main_push_only_and_has_no_cross_repo_secret(self) -> None:
        self.assertIn("github.event_name == 'push'", self.publisher)
        self.assertIn("github.ref == 'refs/heads/main'", self.publisher)
        self.assertIn("github.repository == 'ArchonMegalon/chummer6-core'", self.publisher)
        self.assertNotIn("CHUMMER_CORE_ARTIFACT_READ_TOKEN", self.workflow_text)

    def test_publication_never_overwrites_and_proves_anonymous_readback(self) -> None:
        publication, anonymous = self.publisher.split(
            "      - name: Prove the published release is anonymously readable and byte exact\n",
            1,
        )
        self.assertIn("release already exists; overwrite is forbidden", publication)
        self.assertIn("tag already exists; overwrite or retarget is forbidden", publication)
        self.assertIn("gh release create", publication)
        self.assertNotIn("--clobber", publication)
        self.assertNotIn("Authorization", anonymous)
        self.assertNotIn("GH_TOKEN", anonymous)
        self.assertIn("verify-public-release", anonymous)


if __name__ == "__main__":
    unittest.main()
