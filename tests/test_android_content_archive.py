from __future__ import annotations

import hashlib
import importlib.util
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest import mock
import zipfile


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "android_content_archive_test", ROOT / "scripts/ai/android-content-archive.py"
)
archive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive)
plane = archive.load_plane(ROOT)


class ExactArchiveTransferTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / "archive.zip"
        self.env = {
            "GITHUB_REPOSITORY": "ArchonMegalon/chummer6-core",
            "CONTENT_ARTIFACT_ID": "15029786578", "GH_TOKEN": "test-token",
            "GH_DEBUG": "api", "DEBUG": "1",
        }
        self.sleep = mock.Mock()

    @staticmethod
    def response(status, body=b"", error=b""):
        def run(command, **kwargs):
            kwargs["stdout"].write(body)
            kwargs["stderr"].write(error)
            return subprocess.CompletedProcess(command, status)
        return run

    def download(self, run):
        return archive.download_archive(self.env, self.path, 3, run=run, sleep=self.sleep)

    def test_transient_archive_404_retries_the_exact_get_then_stops_on_success(self):
        results = iter([
            self.response(1, b"{}", b"gh: Not Found (HTTP 404)\n"),
            self.response(1, b"{}", b"gh: Not Found (HTTP 404)\n"),
            self.response(0, b"ZIP"),
        ])
        run = mock.Mock(side_effect=lambda *a, **k: next(results)(*a, **k))
        self.download(run)
        self.assertEqual(b"ZIP", self.path.read_bytes())
        self.assertEqual([mock.call(10)] * 2, self.sleep.call_args_list)
        for call in run.call_args_list:
            self.assertEqual([
                "gh", "api", "--hostname", "github.com", "--method", "GET",
                "repos/ArchonMegalon/chummer6-core/actions/artifacts/15029786578/zip",
            ], call.args[0])
            self.assertEqual(20, call.kwargs["timeout"])
            self.assertNotIn("GH_DEBUG", call.kwargs["env"])
            self.assertNotIn("DEBUG", call.kwargs["env"])
            self.assertNotIn("test-token", " ".join(call.args[0]))

    def test_immediate_transfer_needs_no_retry(self):
        run = mock.Mock(side_effect=self.response(0, b"ZIP"))
        self.download(run)
        run.assert_called_once()
        self.sleep.assert_not_called()

    def test_exact_archive_404_exhaustion_is_bounded(self):
        run = mock.Mock(side_effect=self.response(1, error=b"gh: Not Found (HTTP 404)\n"))
        with self.assertRaisesRegex(RuntimeError, "after 6 attempts"):
            self.download(run)
        self.assertEqual(6, run.call_count)
        self.assertEqual([mock.call(10)] * 5, self.sleep.call_args_list)
        self.assertFalse(self.path.exists())

    def test_unexpected_http_transport_or_byte_count_is_terminal_and_sanitized(self):
        cases = [self.response(1, error=f"gh: error (HTTP {status})\n".encode())
                 for status in (301, 401, 403, 410, 429, 500)]
        cases += [self.response(2, error=b"gh: Not Found (HTTP 404)\n"),
                  self.response(1, error=b"gh: https://untrusted.example/?token=private\n"),
                  self.response(0, b"oops")]
        for result in cases:
            with self.subTest(result=result):
                self.path.unlink(missing_ok=True)
                run = mock.Mock(side_effect=result)
                with self.assertRaises(RuntimeError) as caught:
                    self.download(run)
                self.assertNotIn("private", str(caught.exception))
                run.assert_called_once()
                self.sleep.assert_not_called()
        for failure in (subprocess.TimeoutExpired("gh", 20), OSError("private")):
            self.path.unlink(missing_ok=True)
            run = mock.Mock(side_effect=failure)
            with self.assertRaisesRegex(RuntimeError, "failed or timed out"):
                self.download(run)
            run.assert_called_once()
            self.sleep.assert_not_called()

    def test_invalid_id_repository_or_archive_size_never_executes_gh(self):
        for key, value in (("CONTENT_ARTIFACT_ID", "1/zip"),
                           ("GITHUB_REPOSITORY", "other/repository")):
            with self.subTest(key=key):
                env = dict(self.env, **{key: value})
                run = mock.Mock()
                with self.assertRaises(RuntimeError):
                    archive.download_archive(env, self.path, 3, run=run)
                run.assert_not_called()
        for size in (0, True, archive.MAX_ARCHIVE_BYTES + 1, None):
            run = mock.Mock()
            with self.assertRaises(RuntimeError):
                archive.download_archive(self.env, self.path, size, run=run)
            run.assert_not_called()


class CanonicalArchiveExtractionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        lock, lock_sha256 = plane.load_lock(ROOT / "eng/android-content-plane.lock.json")
        cls.authority = plane.Authority(
            lock, lock_sha256, "1" * 40, "2" * 40,
            tuple({"path": path, "size": 1, "sha256": "3" * 64}
                  for path in plane.PRODUCER_INPUT_PATHS),
            plane.collect_content(ROOT, lock), plane.collect_licenses(ROOT, lock),
        )
        cls.members, _, _ = plane.build_artifact_members(cls.authority, "45628688212", "1")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.path = self.root / "archive.zip"
        self.export = self.root / "content-export"

    def make_zip(self, changes=None, mode=stat.S_IFREG | 0o644):
        values = dict(self.members)
        if changes:
            changes(values)
        with zipfile.ZipFile(self.path, "w", compression=zipfile.ZIP_STORED) as target:
            for name, value in values.items():
                member = zipfile.ZipInfo(name)
                member.create_system = 3
                member.external_attr = mode << 16
                target.writestr(member, value)
        with self.path.open("rb") as stream:
            return hashlib.file_digest(stream, "sha256").hexdigest()

    def verify(self, digest, destination=None):
        return archive.verify_archive(
            plane, self.authority, self.path, digest, self.path.stat().st_size,
            destination or self.export, "45628688212", "1",
        )

    def test_expected_zip_digest_precedes_any_zip_open_or_extraction(self):
        self.path.write_bytes(b"this is not a ZIP")
        with mock.patch.object(archive.zipfile, "ZipFile") as opened:
            with self.assertRaisesRegex(RuntimeError, "SHA-256"):
                self.verify("0" * 64)
            opened.assert_not_called()
        self.assertFalse(self.export.exists())

    def test_exact_canonical_members_extract_with_strict_modes_and_final_verification(self):
        digest = self.make_zip()
        receipt = self.verify("sha256:" + digest)
        self.assertEqual(115, len(self.members))
        self.assertEqual(receipt, plane.verify_export(self.authority, self.export, "45628688212", "1"))
        for path in (self.export, *self.export.rglob("*")):
            self.assertEqual(0o755 if path.is_dir() else 0o644, stat.S_IMODE(path.stat().st_mode))
        self.assertEqual(set(self.members), {p.relative_to(self.export).as_posix()
                         for p in self.export.rglob("*") if p.is_file()})

    def test_untrusted_extra_traversal_and_missing_members_fail_before_writes(self):
        for name in ("../escape", "/escape", "content/a.dll", "content/a.nupkg", "signing/key.pfx"):
            with self.subTest(name=name):
                digest = self.make_zip(lambda values: values.update({name: b"x"}))
                with self.assertRaisesRegex(RuntimeError, "member set"):
                    self.verify(digest)
                self.assertFalse(self.export.exists())
        digest = self.make_zip(lambda values: values.pop(next(iter(values))))
        with self.assertRaisesRegex(RuntimeError, "member set"):
            self.verify(digest)
        self.assertFalse(self.export.exists())

    def test_links_executable_modes_oversize_and_duplicate_members_are_rejected(self):
        for mode in (stat.S_IFLNK | 0o644, stat.S_IFREG | 0o755):
            digest = self.make_zip(mode=mode)
            with self.assertRaisesRegex(RuntimeError, "mode, or expansion"):
                self.verify(digest)
            self.assertFalse(self.export.exists())
        name = next(iter(self.members))
        digest = self.make_zip(lambda values: values.update({name: values[name] + b"x"}))
        with self.assertRaisesRegex(RuntimeError, "mode, or expansion"):
            self.verify(digest)
        self.assertFalse(self.export.exists())
        self.make_zip()
        with zipfile.ZipFile(self.path, "a") as target:
            with self.assertWarns(UserWarning):
                target.writestr(name, self.members[name])
        with self.path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        with self.assertRaisesRegex(RuntimeError, "member set"):
            self.verify(digest)
        self.assertFalse(self.export.exists())

    def test_wrong_member_bytes_are_rejected(self):
        name = next(iter(self.members))
        original = self.members[name]
        digest = self.make_zip(lambda values: values.update({name: bytes([original[0] ^ 1]) + original[1:]}))
        with self.assertRaisesRegex(RuntimeError, "member bytes"):
            self.verify(digest)

    def test_existing_or_symlinked_destination_and_archive_are_rejected(self):
        digest = self.make_zip()
        self.export.mkdir()
        with self.assertRaises(plane.ContentPlaneError):
            self.verify(digest)
        self.export.rmdir()
        outside = self.root / "outside"
        outside.mkdir()
        self.export.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(plane.ContentPlaneError):
            self.verify(digest)
        self.assertEqual([], list(outside.iterdir()))
        self.export.unlink()
        real = self.root / "real.zip"
        self.path.rename(real)
        self.path.symlink_to(real)
        with self.assertRaises(OSError):
            self.verify(digest)
        self.assertFalse(self.export.exists())

    def test_descriptor_writer_rejects_a_symlink_in_the_extraction_tree(self):
        digest = self.make_zip()
        outside = self.root / "outside"
        outside.mkdir()
        original = plane._DescriptorExport.write

        def substitute_directory(writer, name, value):
            first = self.export / name.split("/")[0]
            first.symlink_to(outside, target_is_directory=True)
            return original(writer, name, value)

        with mock.patch.object(plane._DescriptorExport, "write", substitute_directory):
            with self.assertRaises(OSError):
                self.verify(digest)
        self.assertEqual([], list(outside.iterdir()))


if __name__ == "__main__":
    unittest.main()
