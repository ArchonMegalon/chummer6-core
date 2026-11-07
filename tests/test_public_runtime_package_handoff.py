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
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch


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
    HEAD_TREE = "b" * 40
    ARTIFACT_ID = 9528212865
    RUN_ID = 32748122851
    RUN_ATTEMPT = 1
    WORKFLOW_ID = 1234567
    REPOSITORY_ID = 7654321

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

    def _actions_inputs(self, name: str) -> tuple[Path, Path, Path, str]:
        archive_path = self.root / f"{name}-actions-artifact.zip"
        member_paths = [
            handoff.INVENTORY_NAME,
            handoff.LOCK_NAME,
            handoff.NO_SIBLINGS_RECEIPT_NAME,
            *(f"packages/{row['file_name']}" for row in self.rows),
        ]
        with zipfile.ZipFile(
            archive_path,
            "w",
            compression=zipfile.ZIP_STORED,
            allowZip64=False,
        ) as archive:
            for member_path in sorted(member_paths):
                info = zipfile.ZipInfo(member_path, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.compress_type = zipfile.ZIP_STORED
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                archive.writestr(info, (self.bundle / member_path).read_bytes())
        archive_bytes = archive_path.read_bytes()
        archive_digest = hashlib.sha256(archive_bytes).hexdigest()
        api_root = f"https://api.github.com/repos/{handoff.REPOSITORY}"
        artifact_metadata_path = self.root / f"{name}-artifact-metadata.json"
        self._write_json(
            artifact_metadata_path,
            {
                "id": self.ARTIFACT_ID,
                "name": handoff.actions_artifact_name(self.COMMIT),
                "size_in_bytes": len(archive_bytes),
                "url": f"{api_root}/actions/artifacts/{self.ARTIFACT_ID}",
                "archive_download_url": f"{api_root}/actions/artifacts/{self.ARTIFACT_ID}/zip",
                "expired": False,
                "created_at": "2026-08-24T17:00:00Z",
                "expires_at": "2026-08-29T17:00:00Z",
                "digest": f"sha256:{archive_digest}",
                "workflow_run": {
                    "id": self.RUN_ID,
                    "repository_id": self.REPOSITORY_ID,
                    "head_repository_id": self.REPOSITORY_ID,
                    "head_branch": handoff.MAIN_BRANCH,
                    "head_sha": self.COMMIT,
                },
            },
        )
        run_metadata_path = self.root / f"{name}-run-metadata.json"
        self._write_json(
            run_metadata_path,
            {
                "id": self.RUN_ID,
                "run_attempt": self.RUN_ATTEMPT,
                "workflow_id": self.WORKFLOW_ID,
                "event": "push",
                "head_branch": handoff.MAIN_BRANCH,
                "head_sha": self.COMMIT,
                "path": handoff.WORKFLOW_PATH,
                "repository": {
                    "id": self.REPOSITORY_ID,
                    "full_name": handoff.REPOSITORY,
                },
                "head_repository": {
                    "id": self.REPOSITORY_ID,
                    "full_name": handoff.REPOSITORY,
                },
                "head_commit": {"id": self.COMMIT, "tree_id": self.HEAD_TREE},
            },
        )
        return artifact_metadata_path, run_metadata_path, archive_path, archive_digest

    def _prepare(
        self,
        name: str = "out",
        *,
        repository: str = handoff.REPOSITORY,
        ref: str = handoff.MAIN_REF,
        event_name: str = "push",
    ) -> tuple[Path, Path]:
        artifact_metadata, run_metadata, actions_archive, archive_digest = self._actions_inputs(name)
        return self._prepare_inputs(
            name,
            artifact_metadata,
            run_metadata,
            actions_archive,
            archive_digest,
            repository=repository,
            ref=ref,
            event_name=event_name,
        )

    def _prepare_inputs(
        self,
        name: str,
        artifact_metadata: Path,
        run_metadata: Path,
        actions_archive: Path,
        archive_digest: str,
        *,
        repository: str = handoff.REPOSITORY,
        ref: str = handoff.MAIN_REF,
        event_name: str = "push",
    ) -> tuple[Path, Path]:
        return handoff.prepare_from_actions_artifact(
            repo_root=self.repo,
            artifact_metadata_path=artifact_metadata,
            run_metadata_path=run_metadata,
            actions_archive_path=actions_archive,
            output_dir=self.root / name,
            repository=repository,
            commit=self.COMMIT,
            ref=ref,
            event_name=event_name,
            source_artifact_id=self.ARTIFACT_ID,
            source_artifact_digest=f"sha256:{archive_digest}",
            run_id=self.RUN_ID,
            run_attempt=self.RUN_ATTEMPT,
            workflow_ref=handoff._expected_workflow_ref(handoff.REPOSITORY, handoff.MAIN_REF),
            workflow_sha=self.COMMIT,
            head_tree=self.HEAD_TREE,
        )

    def _refresh_artifact_byte_authority(self, metadata_path: Path, archive_path: Path) -> str:
        raw = archive_path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata["size_in_bytes"] = len(raw)
        metadata["digest"] = f"sha256:{digest}"
        self._write_json(metadata_path, metadata)
        return digest

    def _release_fixture(self, *, local: bool = False) -> tuple[Path, Path, Path, Path, Path, dict]:
        archive, receipt_path = self._prepare_local() if local else self._prepare()
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

    def _local_inputs(self) -> tuple[Path, dict]:
        end = datetime.now(timezone.utc).replace(microsecond=0) - timedelta(seconds=1)
        generated = end - timedelta(seconds=1)
        start = end - timedelta(minutes=5)
        stamp = lambda value: value.strftime("%Y-%m-%dT%H:%M:%SZ")
        receipt_path = self.bundle / handoff.NO_SIBLINGS_RECEIPT_NAME
        receipt = json.loads(receipt_path.read_bytes())
        receipt["generated_at_utc"] = stamp(generated)
        self._write_json(receipt_path, receipt)
        source = {
            "contract": handoff.LOCAL_EXECUTION_CONTRACT,
            "evidence_kind": "operator-declared",
            "repository": handoff.REPOSITORY,
            "runtime_source_commit": self.lock["runtime_source"]["commit"],
            "runtime_source_tree": "c" * 40,
            "package_recipe_commit": self.COMMIT,
            "package_recipe_tree": self.HEAD_TREE,
            "package_version": self.lock["package_version"],
            "started_at_utc": stamp(start),
            "completed_at_utc": stamp(end),
            "toolchain": {"dotnet_sdk_version": self.lock["dotnet_sdk"]["version"]},
            "no_siblings_receipt_sha256": hashlib.sha256(receipt_path.read_bytes()).hexdigest(),
            "no_siblings_generated_at_utc": stamp(generated),
        }
        path = self.root / "local-provenance.json"
        self._write_json(path, source)
        return path, source

    def _local_git(self, repo_root: Path, *args: str) -> bytes:
        self.assertEqual(repo_root, self.repo)
        responses = {
            ("rev-parse", "--show-toplevel"): str(self.repo).encode(),
            ("rev-parse", "HEAD"): self.COMMIT.encode(),
            ("status", "--porcelain", "--untracked-files=all"): b"",
            ("show", f"{self.COMMIT}:eng/{handoff.LOCK_NAME}"): (self.repo / "eng" / handoff.LOCK_NAME).read_bytes(),
            ("cat-file", "-t", self.COMMIT): b"commit",
            ("cat-file", "-t", self.lock["runtime_source"]["commit"]): b"commit",
            ("rev-parse", f"{self.COMMIT}^{{tree}}"): self.HEAD_TREE.encode(),
            ("rev-parse", self.lock["runtime_source"]["commit"] + "^{tree}"): b"c" * 40,
            ("merge-base", "--is-ancestor", self.lock["runtime_source"]["commit"], self.COMMIT): b"",
        }
        return responses[args]

    def _prepare_local(self, name: str = "out", provenance: Path | None = None) -> tuple[Path, Path]:
        if provenance is None:
            provenance, _ = self._local_inputs()
        with patch.object(handoff, "_git", side_effect=self._local_git):
            return handoff.prepare_from_local_bundle(
                repo_root=self.repo, bundle_dir=self.bundle, local_provenance_path=provenance,
                output_dir=self.root / name, commit=self.COMMIT,
            )

    def test_local_prepare_preserves_exact_bytes_without_actions_claims(self) -> None:
        provenance, source = self._local_inputs()
        archive, receipt_path = self._prepare_local("local-one", provenance)
        archive_two, receipt_two = self._prepare_local("local-two", provenance)
        actions_archive, actions_receipt = self._prepare("actions")
        self.assertEqual(archive.read_bytes(), archive_two.read_bytes())
        self.assertEqual(receipt_path.read_bytes(), receipt_two.read_bytes())
        self.assertEqual(archive.read_bytes(), actions_archive.read_bytes())
        _, receipt = handoff._load_public_receipt(receipt_path)
        self.assertEqual(receipt["contract"], handoff.LOCAL_CONTRACT)
        self.assertEqual(receipt["provenance_mode"], "local")
        self.assertEqual(receipt["source_local_execution"], source)
        self.assertNotIn("source_actions_artifact", receipt)
        self.assertNotIn("ref", receipt)
        _, actions = handoff._load_public_receipt(actions_receipt)
        self.assertEqual(actions["contract"], handoff.CONTRACT)
        self.assertEqual(set(actions), handoff.RECEIPT_KEYS)
        with zipfile.ZipFile(archive) as zipped:
            for name in zipped.namelist():
                self.assertEqual(zipped.read(name), (self.bundle / name).read_bytes())

    def test_local_prepare_rejects_inconsistent_missing_or_actions_provenance(self) -> None:
        path, source = self._local_inputs()
        cases = []
        for key, value in (
            ("package_recipe_commit", "f" * 40), ("runtime_source_commit", "f" * 40),
            ("package_recipe_tree", "f" * 40), ("runtime_source_tree", "f" * 40),
            ("package_version", "1.2.3"), ("no_siblings_receipt_sha256", "f" * 64),
            ("toolchain", {"dotnet_sdk_version": "1.2.3"}),
            ("repository", "foreign/repo"), ("evidence_kind", "authenticated"),
            ("run_id", self.RUN_ID), ("started_at_utc", source["completed_at_utc"]),
            ("toolchain", {"dotnet_sdk_version": True}),
        ):
            cases.append(dict(source, **{key: value}))
        cases.extend({key: value for key, value in source.items() if key != omitted}
                     for omitted in source)
        for index, candidate in enumerate(cases):
            with self.subTest(index=index):
                self._write_json(path, candidate)
                with self.assertRaises(handoff.PublicHandoffError):
                    self._prepare_local(f"bad-{index}", path)
                self.assertFalse((self.root / f"bad-{index}").exists())

    def test_local_prepare_rejects_future_or_mismatched_receipt_time(self) -> None:
        path, source = self._local_inputs()
        future = dict(source)
        for key in ("started_at_utc", "completed_at_utc", "no_siblings_generated_at_utc"):
            stamp = datetime.strptime(future[key], "%Y-%m-%dT%H:%M:%SZ")
            future[key] = (stamp + timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        self._write_json(path, future)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "in the future"):
            self._prepare_local("future-time", path)
        source["no_siblings_generated_at_utc"] = source["completed_at_utc"]
        self._write_json(path, source)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "differs from exported bundle"):
            self._prepare_local("mismatched-time", path)

    def test_local_prepare_reuses_unchanged_evidence_after_two_days(self) -> None:
        path, source = self._local_inputs()
        for key in ("started_at_utc", "completed_at_utc", "no_siblings_generated_at_utc"):
            stamp = datetime.strptime(source[key], "%Y-%m-%dT%H:%M:%SZ")
            source[key] = (stamp - timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        receipt_path = self.bundle / handoff.NO_SIBLINGS_RECEIPT_NAME
        receipt = json.loads(receipt_path.read_bytes())
        receipt["generated_at_utc"] = source["no_siblings_generated_at_utc"]
        self._write_json(receipt_path, receipt)
        source["no_siblings_receipt_sha256"] = hashlib.sha256(receipt_path.read_bytes()).hexdigest()
        self._write_json(path, source)
        archive, receipt = self._prepare_local("retained-evidence", path)
        _, accepted = handoff._load_public_receipt(receipt)
        self.assertEqual(accepted["source_local_execution"], source)
        self.assertTrue(archive.is_file())

    def test_local_prepare_checks_clean_checkout_and_git_identity(self) -> None:
        path, _ = self._local_inputs()
        for args, value in (
            (("rev-parse", "HEAD"), b"f" * 40),
            (("status", "--porcelain", "--untracked-files=all"), b" M source.py"),
            (("show", f"{self.COMMIT}:eng/{handoff.LOCK_NAME}"), b"drift"),
            (("cat-file", "-t", self.COMMIT), b"tag"),
        ):
            with self.subTest(args=args), patch.object(
                handoff, "_git", side_effect=lambda root, *command: value if command == args
                else self._local_git(root, *command),
            ):
                with self.assertRaises(handoff.PublicHandoffError):
                    handoff.prepare_from_local_bundle(
                        repo_root=self.repo, bundle_dir=self.bundle, local_provenance_path=path,
                        output_dir=self.root / "bad-git", commit=self.COMMIT,
                    )
        self.assertFalse((self.root / "bad-git").exists())

    def test_local_prepare_reuses_byte_layout_and_absent_output_guards(self) -> None:
        path, _ = self._local_inputs()
        self._prepare_local("out", path)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "new directory"):
            self._prepare_local("out", path)
        package = self.bundle / "packages" / self.rows[0]["file_name"]
        package.write_bytes(b"tampered")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "bytes differ"):
            self._prepare_local("drift", path)
        package.unlink()
        package.symlink_to(path)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "bounded regular file"):
            self._prepare_local("symlink", path)

    def test_local_public_readback_accepts_local_and_rejects_moved_tag_and_byte_drift(self) -> None:
        metadata, tag, receipt, archive, downloaded, _ = self._release_fixture(local=True)
        args = dict(release_metadata_path=metadata, tag_metadata_path=tag, receipt_path=receipt,
                    downloaded_bundle_path=archive, downloaded_receipt_path=downloaded)
        handoff.verify_public_release(**args)
        original_tag = tag.read_bytes()
        self._write_json(tag, {"ref": f"refs/tags/{handoff.release_tag(self.COMMIT)}",
                               "object": {"type": "commit", "sha": "f" * 40}})
        with self.assertRaisesRegex(handoff.PublicHandoffError, "exact commit"):
            handoff.verify_public_release(**args)
        tag.write_bytes(original_tag)
        archive.write_bytes(archive.read_bytes() + b"tamper")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "byte authority differs"):
            handoff.verify_public_release(**args)

    def test_local_public_receipt_rejects_cross_mode_and_digest_tampering(self) -> None:
        _, path = self._prepare_local()
        original = json.loads(path.read_bytes())
        for key, value in (("provenance_mode", "actions"), ("contract", handoff.CONTRACT),
                           ("ref", handoff.MAIN_REF), ("source_actions_artifact", {})):
            with self.subTest(key=key):
                path.write_bytes(handoff._canonical_json(dict(original, **{key: value})))
                with self.assertRaises(handoff.PublicHandoffError):
                    handoff._load_public_receipt(path)
        original["source_local_execution"]["no_siblings_receipt_sha256"] = "f" * 64
        path.write_bytes(handoff._canonical_json(original))
        with self.assertRaisesRegex(handoff.PublicHandoffError, "members differ"):
            handoff._load_public_receipt(path)
        _, actions_path = self._prepare("actions")
        actions = json.loads(actions_path.read_bytes())
        actions["source_local_execution"] = original["source_local_execution"]
        actions_path.write_bytes(handoff._canonical_json(actions))
        with self.assertRaisesRegex(handoff.PublicHandoffError, "shape differs"):
            handoff._load_public_receipt(actions_path)

    def test_local_prepare_requires_passing_receipt_and_all_eight_packages(self) -> None:
        provenance, source = self._local_inputs()
        receipt_path = self.bundle / handoff.NO_SIBLINGS_RECEIPT_NAME
        receipt = json.loads(receipt_path.read_bytes())
        original = receipt_path.read_bytes()
        receipt["eight_package_runtime_plane"] = "fail"
        self._write_json(receipt_path, receipt)
        source["no_siblings_receipt_sha256"] = hashlib.sha256(receipt_path.read_bytes()).hexdigest()
        self._write_json(provenance, source)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "claims differ"):
            self._prepare_local("failed-receipt", provenance)
        receipt_path.write_bytes(original)
        gm_row = next(row for row in self.rows if row["id"] == "Chummer.Engine.GmCharacterEdits")
        (self.bundle / "packages" / gm_row["file_name"]).unlink()
        with self.assertRaisesRegex(handoff.PublicHandoffError, "cannot inspect runtime package"):
            self._prepare_local("seven-packages", provenance)

    def test_local_public_readback_rejects_provenance_rebound_away_from_bundle(self) -> None:
        archive, path = self._prepare_local()
        payload = json.loads(path.read_bytes())
        for key, value in (("runtime_source_commit", "f" * 40), ("package_version", "1.2.3"),
                           ("toolchain", {"dotnet_sdk_version": "1.2.3"})):
            with self.subTest(key=key):
                altered = json.loads(json.dumps(payload))
                altered["source_local_execution"][key] = value
                with self.assertRaisesRegex(handoff.PublicHandoffError, "differs from exported bundle"):
                    handoff._verify_local_public_bundle(archive.read_bytes(), altered)

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
        source = receipt["source_actions_artifact"]
        self.assertEqual(source["id"], self.ARTIFACT_ID)
        self.assertEqual(source["workflow_run"]["id"], self.RUN_ID)
        self.assertEqual(source["workflow_run"]["attempt"], self.RUN_ATTEMPT)
        self.assertEqual(source["workflow_run"]["head_sha"], self.COMMIT)
        self.assertEqual(source["workflow_run"]["head_tree"], self.HEAD_TREE)
        self.assertEqual(source["workflow_run"]["workflow_id"], self.WORKFLOW_ID)
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
                    self._prepare(
                        f"reject-{len(list(self.root.iterdir()))}",
                        repository=repository,
                        ref=ref,
                        event_name=event,
                    )

    def test_prepare_rejects_package_byte_drift(self) -> None:
        package = next((self.bundle / "packages").iterdir())
        package.write_bytes(package.read_bytes() + b"drift")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "package bytes differ"):
            self._prepare()

    def test_raw_actions_digest_is_checked_before_invalid_zip_is_parsed(self) -> None:
        metadata, run, archive, digest = self._actions_inputs("raw-drift")
        archive.write_bytes(b"not a ZIP and not the authenticated bytes")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "raw Actions artifact digest differs"):
            self._prepare_inputs("raw-drift-out", metadata, run, archive, digest)

    def test_prepare_rejects_artifact_metadata_from_another_run(self) -> None:
        metadata_path, run, archive, digest = self._actions_inputs("foreign-run")
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata["workflow_run"]["id"] = self.RUN_ID + 1
        self._write_json(metadata_path, metadata)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "artifact metadata differs"):
            self._prepare_inputs("foreign-run-out", metadata_path, run, archive, digest)

    def test_prepare_rejects_authenticated_run_head_tree_drift(self) -> None:
        metadata, run_path, archive, digest = self._actions_inputs("tree-drift")
        run = json.loads(run_path.read_text(encoding="utf-8"))
        run["head_commit"]["tree_id"] = "0" * 40
        self._write_json(run_path, run)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "run metadata differs"):
            self._prepare_inputs("tree-drift-out", metadata, run_path, archive, digest)

    def test_prepare_rejects_compressed_outer_member_even_with_matching_digest(self) -> None:
        metadata, run, archive_path, _ = self._actions_inputs("compressed-member")
        with zipfile.ZipFile(archive_path, "r") as archive:
            members = [(info.filename, archive.read(info)) for info in archive.infolist()]
        with zipfile.ZipFile(
            archive_path,
            "w",
            compression=zipfile.ZIP_DEFLATED,
            allowZip64=False,
        ) as archive:
            for name, raw in members:
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                archive.writestr(info, raw)
        digest = self._refresh_artifact_byte_authority(metadata, archive_path)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "member posture differs"):
            self._prepare_inputs("compressed-member-out", metadata, run, archive_path, digest)

    def test_prepare_rejects_symlinked_package(self) -> None:
        package = next((self.bundle / "packages").iterdir())
        target = self.root / "foreign.nupkg"
        target.write_bytes(package.read_bytes())
        package.unlink()
        package.symlink_to(target)
        with self.assertRaisesRegex(handoff.PublicHandoffError, "bounded regular file"):
            handoff._validate_bundle(self.repo, self.bundle, self.COMMIT)

    def test_prepare_rejects_extra_bundle_member(self) -> None:
        (self.bundle / "foreign.txt").write_text("foreign", encoding="utf-8")
        with self.assertRaisesRegex(handoff.PublicHandoffError, "root members differ"):
            handoff._validate_bundle(self.repo, self.bundle, self.COMMIT)

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

    def test_public_release_ignores_unused_target_commitish_for_precreated_tag(self) -> None:
        metadata_path, tag, receipt, archive, downloaded_receipt, metadata = self._release_fixture()
        metadata["target_commitish"] = "main"
        self._write_json(metadata_path, metadata)
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

    def test_direct_tag_verifier_rejects_annotated_tag_objects(self) -> None:
        with self.assertRaisesRegex(handoff.PublicHandoffError, "point directly"):
            handoff._validate_direct_tag_metadata(
                {
                    "ref": f"refs/tags/{handoff.release_tag(self.COMMIT)}",
                    "object": {"type": "tag", "sha": self.COMMIT},
                },
                handoff.release_tag(self.COMMIT),
                self.COMMIT,
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
        self.assertIn('"${api_root}/git/refs"', publication)
        self.assertIn("409|422", publication)
        self.assertIn("tag already exists or conflicted; overwrite is forbidden", publication)
        self.assertIn("gh release create", publication)
        self.assertIn("--verify-tag", publication)
        self.assertNotIn("--clobber", publication)
        self.assertNotIn("Authorization", anonymous)
        self.assertNotIn("GH_TOKEN", anonymous)
        self.assertIn("verify-public-release", anonymous)

    def test_source_artifact_is_fetched_by_numeric_id_and_bound_to_current_run(self) -> None:
        self.assertIn("SOURCE_ARTIFACT_ID: ${{ needs.no-siblings.outputs.runtime-artifact-id }}", self.publisher)
        self.assertIn("/actions/artifacts/${SOURCE_ARTIFACT_ID}", self.publisher)
        self.assertIn("/actions/runs/${GITHUB_RUN_ID}/attempts/${GITHUB_RUN_ATTEMPT}", self.publisher)
        self.assertIn("${artifact_api}/zip", self.publisher)
        self.assertIn("prepare-from-actions-artifact", self.publisher)
        self.assertIn('--run-id "${GITHUB_RUN_ID}"', self.publisher)
        self.assertIn('--run-attempt "${GITHUB_RUN_ATTEMPT}"', self.publisher)
        self.assertIn('--workflow-ref "${GITHUB_WORKFLOW_REF}"', self.publisher)
        self.assertIn('--workflow-sha "${GITHUB_WORKFLOW_SHA}"', self.publisher)
        self.assertNotIn("actions/download-artifact", self.publisher)


if __name__ == "__main__":
    unittest.main()
