#!/usr/bin/env python3
"""Build and verify the immutable, content-only Core artifact consumed by Android."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


LOCK_CONTRACT = "chummer-core.android-content-plane-lock/v1"
LAYOUT_CONTRACT = "chummer-core.android-content-artifact/v1"
INVENTORY_CONTRACT = "chummer-core.android-content-inventory/v1"
RECEIPT_CONTRACT = "chummer-core.android-content-receipt/v1"
CONTENT_DIGEST_CONTRACT = "chummer-core.android-content-digest/v1"
SOURCE_REPOSITORY = "ArchonMegalon/chummer6-core"
SOURCE_COMMIT = "bc08228d3ce06410ca97ada63a5af41a2eaa91bf"
SOURCE_TREE = "b376960723c63743c50e8b6878c212b1b2fc8d3c"
CONTENT_FILE_COUNT = 110
CONTENT_BYTE_COUNT = 17_371_170
CONTENT_DIGEST = "95b0653a54fc16c15b5283562e426e214db67c7b58c7e21c124d8d396a7c2f3d"
EXPECTED_ROOTS = (
    {
        "sourcePath": "Chummer/data",
        "artifactPath": "content/data",
        "tree": "ec73ac4f3887c7cf9cdad831c11723d63237902d",
    },
    {
        "sourcePath": "Chummer/lang",
        "artifactPath": "content/lang",
        "tree": "02f6049b25bfb83cf385f8668289922d7168d516",
    },
)
EXPECTED_LICENSES = (
    {
        "sourcePath": "LICENSE",
        "artifactPath": "licenses/LICENSE",
        "blob": "f288702d2fa16d3cdf0035b15a9fcbc552cd88e7",
        "size": 35_149,
        "sha256": "3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986",
    },
    {
        "sourcePath": "Chummer/LICENSE.txt",
        "artifactPath": "licenses/Chummer-LICENSE.txt",
        "blob": "fe1341954b9cddf07ff4acf97180855b4c5d7111",
        "size": 32_425,
        "sha256": "ed5cfe0fa58d086a63c181125c129a2ac77a355aba3fa77c3b2aa408830a8fc9",
    },
)
EXPECTED_ARTIFACT = {
    "layout": LAYOUT_CONTRACT,
    "inventoryContract": INVENTORY_CONTRACT,
    "inventoryPath": "content/manifest.json",
    "receiptContract": RECEIPT_CONTRACT,
    "receiptPath": "authority/producer-receipt.json",
    "receiptSealPath": "authority/producer-receipt.json.sha256",
    "memberCount": 115,
}
EXPECTED_LIMITS = {
    "maxContentFiles": 256,
    "maxContentBytes": 33_554_432,
    "maxFileBytes": 4_194_304,
    "maxManifestBytes": 1_048_576,
    "maxPathUtf8Bytes": 240,
}
PRODUCER_INPUT_PATHS = (
    ".github/workflows/android-content-plane.yml",
    "eng/android-content-plane.lock.json",
    "scripts/ai/android-content-plane.py",
    "tests/test_android_content_plane.py",
)
FORBIDDEN_SUFFIXES = (
    ".dll",
    ".nupkg",
    ".pdb",
    ".deps.json",
    ".runtimeconfig.json",
)
HEX40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
POSITIVE_INTEGER = re.compile(r"^[1-9][0-9]*$")
WINDOWS_RESERVED = {
    "con",
    "prn",
    "aux",
    "nul",
    *(f"com{index}" for index in range(1, 10)),
    *(f"lpt{index}" for index in range(1, 10)),
}


class ContentPlaneError(RuntimeError):
    """The content artifact cannot be proven exact."""


@dataclass(frozen=True)
class SourceFile:
    path: str
    mode: str
    blob: str
    size: int
    sha256: str
    value: bytes

    def inventory_row(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "mode": self.mode,
            "blob": self.blob,
            "size": self.size,
            "sha256": self.sha256,
        }


@dataclass(frozen=True)
class Authority:
    lock: dict[str, Any]
    lock_sha256: str
    producer_commit: str
    producer_tree: str
    producer_inputs: tuple[dict[str, Any], ...]
    content_files: tuple[SourceFile, ...]
    licenses: tuple[SourceFile, ...]


def _require_exact_keys(value: Any, expected: set[str], label: str) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != expected:
        actual = sorted(value) if isinstance(value, dict) else type(value).__name__
        raise ContentPlaneError(
            f"{label} fields differ: expected={sorted(expected)!r}, actual={actual!r}"
        )
    return value


def _object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ContentPlaneError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise ContentPlaneError(f"non-standard JSON constant: {value}")


def _read_json_bytes(value: bytes, label: str) -> dict[str, Any]:
    try:
        decoded = value.decode("utf-8")
        parsed = json.loads(
            decoded,
            object_pairs_hook=_object_without_duplicates,
            parse_constant=_reject_json_constant,
        )
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ContentPlaneError(f"{label} is not strict UTF-8 JSON: {error}") from error
    if not isinstance(parsed, dict):
        raise ContentPlaneError(f"{label} root must be an object")
    return parsed


def _read_regular_file(path: Path, label: str) -> bytes:
    try:
        metadata = path.lstat()
    except OSError as error:
        raise ContentPlaneError(f"{label} is unavailable: {path}: {error}") from error
    if not stat.S_ISREG(metadata.st_mode) or path.is_symlink():
        raise ContentPlaneError(f"{label} must be one regular non-symlink file: {path}")
    try:
        return path.read_bytes()
    except OSError as error:
        raise ContentPlaneError(f"{label} cannot be read: {path}: {error}") from error


def _sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _git(repo_root: Path, *arguments: str, input_value: bytes | None = None) -> bytes:
    try:
        return subprocess.run(
            ["git", "-C", str(repo_root), *arguments],
            input=input_value,
            check=True,
            capture_output=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError) as error:
        stderr = getattr(error, "stderr", b"") or b""
        detail = stderr.decode("utf-8", errors="replace").strip()
        raise ContentPlaneError(
            f"git {' '.join(arguments)} failed" + (f": {detail}" if detail else "")
        ) from error


def _validate_path(value: Any, allowed_roots: tuple[str, ...], max_utf8_bytes: int) -> str:
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ContentPlaneError(f"artifact path is not canonical: {value!r}")
    if value != unicodedata.normalize("NFC", value):
        raise ContentPlaneError(f"artifact path is not NFC: {value!r}")
    if any(unicodedata.category(character).startswith("C") for character in value):
        raise ContentPlaneError(f"artifact path contains a control character: {value!r}")
    if len(value.encode("utf-8")) > max_utf8_bytes:
        raise ContentPlaneError(f"artifact path exceeds its UTF-8 limit: {value!r}")
    path = PurePosixPath(value)
    if (
        path.is_absolute()
        or path.as_posix() != value
        or len(path.parts) < 2
        or path.parts[0] not in allowed_roots
        or any(
            segment in {"", ".", ".."}
            or segment != segment.strip()
            or segment.rstrip(". ") != segment
            or segment.split(".", 1)[0].casefold() in WINDOWS_RESERVED
            for segment in path.parts
        )
    ):
        raise ContentPlaneError(f"artifact path is unsafe: {value!r}")
    return value


def _validate_unique_paths(paths: Iterable[str], label: str) -> None:
    exact: set[str] = set()
    folded: set[str] = set()
    previous: str | None = None
    for path in paths:
        if path in exact:
            raise ContentPlaneError(f"{label} contains a duplicate path: {path}")
        key = unicodedata.normalize("NFC", path).casefold()
        if key in folded:
            raise ContentPlaneError(f"{label} contains a case/NFC-colliding path: {path}")
        if previous is not None and previous >= path:
            raise ContentPlaneError(f"{label} paths are not ordinally sorted")
        exact.add(path)
        folded.add(key)
        previous = path


def validate_lock(lock: dict[str, Any]) -> None:
    _require_exact_keys(
        lock,
        {
            "contract",
            "sourceRepository",
            "sourceCommit",
            "sourceTree",
            "roots",
            "contentFileCount",
            "contentByteCount",
            "contentDigest",
            "licenses",
            "artifact",
            "limits",
            "producerInputPaths",
        },
        "content lock",
    )
    expected_scalars = {
        "contract": LOCK_CONTRACT,
        "sourceRepository": SOURCE_REPOSITORY,
        "sourceCommit": SOURCE_COMMIT,
        "sourceTree": SOURCE_TREE,
        "contentFileCount": CONTENT_FILE_COUNT,
        "contentByteCount": CONTENT_BYTE_COUNT,
        "contentDigest": CONTENT_DIGEST,
    }
    for key, expected in expected_scalars.items():
        if type(lock.get(key)) is not type(expected) or lock.get(key) != expected:
            raise ContentPlaneError(f"content lock {key} is not exact")
    if lock.get("roots") != list(EXPECTED_ROOTS):
        raise ContentPlaneError("content lock roots are not exact")
    if lock.get("licenses") != list(EXPECTED_LICENSES):
        raise ContentPlaneError("content lock licenses are not exact")
    if lock.get("artifact") != EXPECTED_ARTIFACT:
        raise ContentPlaneError("content lock artifact layout is not exact")
    if lock.get("limits") != EXPECTED_LIMITS:
        raise ContentPlaneError("content lock limits are not exact")
    if lock.get("producerInputPaths") != list(PRODUCER_INPUT_PATHS):
        raise ContentPlaneError("content lock producer input paths are not exact")
    if EXPECTED_ARTIFACT["memberCount"] != CONTENT_FILE_COUNT + len(EXPECTED_LICENSES) + 3:
        raise ContentPlaneError("content lock artifact member count is internally inconsistent")


def load_lock(lock_path: Path) -> tuple[dict[str, Any], str]:
    value = _read_regular_file(lock_path, "Android content lock")
    lock = _read_json_bytes(value, "Android content lock")
    validate_lock(lock)
    return lock, _sha256(value)


def _parse_tree_record(record: bytes, label: str) -> tuple[str, str, str, int, str]:
    try:
        metadata, raw_path = record.split(b"\t", 1)
        mode, object_type, object_id, raw_size = metadata.decode("ascii").split()
        path = raw_path.decode("utf-8")
        size = int(raw_size)
    except (ValueError, UnicodeDecodeError) as error:
        raise ContentPlaneError(f"{label} tree record is malformed") from error
    return mode, object_type, object_id, size, path


def _content_digest(files: Iterable[SourceFile]) -> str:
    digest = hashlib.sha256()
    digest.update(f"{CONTENT_DIGEST_CONTRACT}\n".encode("utf-8"))
    for label, value in (
        ("sourceCommit", SOURCE_COMMIT),
        ("sourceTree", SOURCE_TREE),
        ("dataTree", EXPECTED_ROOTS[0]["tree"]),
        ("langTree", EXPECTED_ROOTS[1]["tree"]),
    ):
        digest.update(f"{label}={value}\n".encode("utf-8"))
    for file in files:
        digest.update(
            f"{file.path}\0{file.mode}\0{file.blob}\0{file.size}\0{file.sha256}\n".encode(
                "utf-8"
            )
        )
    return digest.hexdigest()


def _read_source_blob(repo_root: Path, object_id: str, expected_size: int, label: str) -> bytes:
    if not HEX40.fullmatch(object_id):
        raise ContentPlaneError(f"{label} Git object id is not canonical")
    value = _git(repo_root, "cat-file", "blob", object_id)
    if len(value) != expected_size:
        raise ContentPlaneError(f"{label} Git object size differs from ls-tree")
    if value.startswith(b"version https://git-lfs.github.com/spec/v1\n"):
        raise ContentPlaneError(f"{label} is an unresolved Git LFS pointer")
    return value


def collect_content(repo_root: Path, lock: dict[str, Any]) -> tuple[SourceFile, ...]:
    source_commit = lock["sourceCommit"]
    object_type = _git(repo_root, "cat-file", "-t", source_commit).decode("ascii").strip()
    if object_type != "commit":
        raise ContentPlaneError("content source authority is not a commit")
    actual_tree = _git(repo_root, "show", "-s", "--format=%T", source_commit).decode().strip()
    if actual_tree != lock["sourceTree"]:
        raise ContentPlaneError("content source tree differs from the lock")
    for root in lock["roots"]:
        tree = _git(repo_root, "rev-parse", f"{source_commit}:{root['sourcePath']}").decode().strip()
        if tree != root["tree"] or _git(repo_root, "cat-file", "-t", tree).strip() != b"tree":
            raise ContentPlaneError(f"content root tree differs: {root['sourcePath']}")

    raw = _git(
        repo_root,
        "ls-tree",
        "-rz",
        "-l",
        source_commit,
        "--",
        *(root["sourcePath"] for root in lock["roots"]),
    )
    result: list[SourceFile] = []
    for record in raw.split(b"\0"):
        if not record:
            continue
        mode, object_type, object_id, size, source_path = _parse_tree_record(
            record, "content source"
        )
        if mode != "100644" or object_type != "blob":
            raise ContentPlaneError(f"content source member is not a regular 100644 blob: {source_path}")
        matching = [
            root for root in lock["roots"] if source_path.startswith(f"{root['sourcePath']}/")
        ]
        if len(matching) != 1:
            raise ContentPlaneError(f"content source member lies outside its exact roots: {source_path}")
        manifest_path = source_path.removeprefix("Chummer/")
        _validate_path(manifest_path, ("data", "lang"), lock["limits"]["maxPathUtf8Bytes"])
        if size > lock["limits"]["maxFileBytes"]:
            raise ContentPlaneError(f"content source member exceeds its size limit: {manifest_path}")
        value = _read_source_blob(repo_root, object_id, size, manifest_path)
        result.append(
            SourceFile(manifest_path, mode, object_id, size, _sha256(value), value)
        )
    result.sort(key=lambda item: item.path)
    _validate_unique_paths((item.path for item in result), "content inventory")
    if len(result) != lock["contentFileCount"] or len(result) > lock["limits"]["maxContentFiles"]:
        raise ContentPlaneError("content source file count differs from the lock")
    byte_count = sum(item.size for item in result)
    if byte_count != lock["contentByteCount"] or byte_count > lock["limits"]["maxContentBytes"]:
        raise ContentPlaneError("content source byte count differs from the lock")
    if _content_digest(result) != lock["contentDigest"]:
        raise ContentPlaneError("content source digest differs from the lock")
    return tuple(result)


def collect_licenses(repo_root: Path, lock: dict[str, Any]) -> tuple[SourceFile, ...]:
    result: list[SourceFile] = []
    for expected in lock["licenses"]:
        raw = _git(
            repo_root,
            "ls-tree",
            "-z",
            "-l",
            lock["sourceCommit"],
            "--",
            expected["sourcePath"],
        )
        records = [record for record in raw.split(b"\0") if record]
        if len(records) != 1:
            raise ContentPlaneError(f"legal source member is ambiguous: {expected['sourcePath']}")
        mode, object_type, object_id, size, source_path = _parse_tree_record(
            records[0], "legal source"
        )
        if (
            source_path != expected["sourcePath"]
            or mode != "100644"
            or object_type != "blob"
            or object_id != expected["blob"]
            or size != expected["size"]
        ):
            raise ContentPlaneError(f"legal source authority differs: {expected['sourcePath']}")
        artifact_path = _validate_path(
            expected["artifactPath"], ("licenses",), lock["limits"]["maxPathUtf8Bytes"]
        )
        value = _read_source_blob(repo_root, object_id, size, artifact_path)
        digest = _sha256(value)
        if digest != expected["sha256"]:
            raise ContentPlaneError(f"legal source digest differs: {expected['sourcePath']}")
        result.append(SourceFile(artifact_path, mode, object_id, size, digest, value))
    result.sort(key=lambda item: item.path)
    _validate_unique_paths((item.path for item in result), "legal inventory")
    return tuple(result)


def _producer_input_authority(repo_root: Path, producer_commit: str) -> tuple[dict[str, Any], ...]:
    rows: list[dict[str, Any]] = []
    for relative in PRODUCER_INPUT_PATHS:
        path = repo_root / relative
        working = _read_regular_file(path, f"producer input {relative}")
        raw = _git(repo_root, "ls-tree", "-z", "-l", producer_commit, "--", relative)
        records = [record for record in raw.split(b"\0") if record]
        if len(records) != 1:
            raise ContentPlaneError(f"producer input is not tracked exactly once: {relative}")
        mode, object_type, object_id, size, tracked_path = _parse_tree_record(
            records[0], "producer input"
        )
        if tracked_path != relative or mode != "100644" or object_type != "blob":
            raise ContentPlaneError(f"producer input is not a regular tracked blob: {relative}")
        committed = _read_source_blob(repo_root, object_id, size, relative)
        if working != committed:
            raise ContentPlaneError(f"producer input differs from HEAD: {relative}")
        rows.append({"path": relative, "size": size, "sha256": _sha256(committed)})
    return tuple(rows)


def build_authority(
    repo_root: Path,
    lock_path: Path,
    expected_producer_commit: str | None = None,
) -> Authority:
    try:
        root_metadata = repo_root.lstat()
    except OSError as error:
        raise ContentPlaneError(f"producer repository is unavailable: {repo_root}: {error}") from error
    if not stat.S_ISDIR(root_metadata.st_mode) or repo_root.is_symlink():
        raise ContentPlaneError("producer repository root must be a regular non-symlink directory")
    _require_safe_existing_directory(repo_root, "producer repository")
    expected_lock_path = repo_root / "eng/android-content-plane.lock.json"
    if lock_path != expected_lock_path:
        raise ContentPlaneError("content lock path must be the exact producer input path")
    lock, lock_sha256 = load_lock(lock_path)
    producer_commit = _git(repo_root, "rev-parse", "HEAD").decode("ascii").strip()
    if not HEX40.fullmatch(producer_commit):
        raise ContentPlaneError("producer HEAD is not a canonical full commit")
    if expected_producer_commit is not None:
        if not HEX40.fullmatch(expected_producer_commit) or producer_commit != expected_producer_commit:
            raise ContentPlaneError("producer HEAD differs from the expected workflow commit")
    producer_tree = _git(repo_root, "show", "-s", "--format=%T", producer_commit).decode().strip()
    if not HEX40.fullmatch(producer_tree):
        raise ContentPlaneError("producer tree is not canonical")
    ancestor = subprocess.run(
        ["git", "-C", str(repo_root), "merge-base", "--is-ancestor", SOURCE_COMMIT, producer_commit],
        check=False,
        capture_output=True,
    )
    if ancestor.returncode != 0:
        raise ContentPlaneError("content source commit is not an ancestor of the producer")
    producer_inputs = _producer_input_authority(repo_root, producer_commit)
    content_files = collect_content(repo_root, lock)
    licenses = collect_licenses(repo_root, lock)
    return Authority(
        lock,
        lock_sha256,
        producer_commit,
        producer_tree,
        producer_inputs,
        content_files,
        licenses,
    )


def _source_authority(authority: Authority) -> dict[str, Any]:
    return {
        "repository": authority.lock["sourceRepository"],
        "commit": authority.lock["sourceCommit"],
        "tree": authority.lock["sourceTree"],
        "roots": authority.lock["roots"],
    }


def _producer_authority(authority: Authority) -> dict[str, Any]:
    return {
        "repository": authority.lock["sourceRepository"],
        "commit": authority.producer_commit,
        "tree": authority.producer_tree,
    }


def _json_bytes(value: dict[str, Any]) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def build_inventory(authority: Authority) -> tuple[dict[str, Any], bytes]:
    inventory = {
        "contract": INVENTORY_CONTRACT,
        "layout": LAYOUT_CONTRACT,
        "sourceAuthority": _source_authority(authority),
        "producerAuthority": _producer_authority(authority),
        "content": {
            "digestContract": CONTENT_DIGEST_CONTRACT,
            "digest": authority.lock["contentDigest"],
            "fileCount": len(authority.content_files),
            "byteCount": sum(file.size for file in authority.content_files),
            "files": [file.inventory_row() for file in authority.content_files],
        },
        "licenses": [file.inventory_row() for file in authority.licenses],
    }
    value = _json_bytes(inventory)
    if len(value) > authority.lock["limits"]["maxManifestBytes"]:
        raise ContentPlaneError("content inventory exceeds its manifest size limit")
    return inventory, value


def _positive_integer(value: str | None, label: str) -> int:
    if value is None or not POSITIVE_INTEGER.fullmatch(value):
        raise ContentPlaneError(f"{label} must be a positive canonical integer")
    return int(value)


def build_artifact_members(
    authority: Authority,
    run_id: str,
    run_attempt: str,
) -> tuple[dict[str, bytes], dict[str, Any], dict[str, Any]]:
    run_id_value = _positive_integer(run_id, "run id")
    run_attempt_value = _positive_integer(run_attempt, "run attempt")
    inventory, inventory_bytes = build_inventory(authority)
    inventory_path = authority.lock["artifact"]["inventoryPath"]
    receipt_path = authority.lock["artifact"]["receiptPath"]
    seal_path = authority.lock["artifact"]["receiptSealPath"]
    artifact_name = (
        f"chummer-core-android-content-{authority.producer_commit}-{run_id_value}-{run_attempt_value}"
    )
    receipt = {
        "contract": RECEIPT_CONTRACT,
        "status": "pass",
        "layout": LAYOUT_CONTRACT,
        "sourceAuthority": _source_authority(authority),
        "producerAuthority": {
            **_producer_authority(authority),
            "runId": run_id_value,
            "runAttempt": run_attempt_value,
            "artifactName": artifact_name,
            "inputFiles": list(authority.producer_inputs),
        },
        "lock": {
            "path": "eng/android-content-plane.lock.json",
            "sha256": authority.lock_sha256,
        },
        "inventory": {
            "path": inventory_path,
            "size": len(inventory_bytes),
            "sha256": _sha256(inventory_bytes),
        },
        "content": {
            "digest": authority.lock["contentDigest"],
            "fileCount": len(authority.content_files),
            "byteCount": sum(file.size for file in authority.content_files),
        },
        "licenses": {
            "fileCount": len(authority.licenses),
            "byteCount": sum(file.size for file in authority.licenses),
        },
        "artifactMemberCount": authority.lock["artifact"]["memberCount"],
        "forbiddenRuntimeMemberCount": 0,
    }
    receipt_bytes = _json_bytes(receipt)
    receipt_name = PurePosixPath(receipt_path).name
    seal_bytes = f"{_sha256(receipt_bytes)}  {receipt_name}\n".encode("ascii")
    members: dict[str, bytes] = {
        f"content/{file.path}": file.value for file in authority.content_files
    }
    members.update({file.path: file.value for file in authority.licenses})
    members[inventory_path] = inventory_bytes
    members[receipt_path] = receipt_bytes
    members[seal_path] = seal_bytes
    if len(members) != authority.lock["artifact"]["memberCount"]:
        raise ContentPlaneError("generated artifact member count differs from the lock")
    return dict(sorted(members.items())), inventory, receipt


def _require_safe_existing_directory(path: Path, label: str) -> None:
    if not path.is_absolute():
        raise ContentPlaneError(f"{label} must be absolute")
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        try:
            metadata = current.lstat()
        except OSError as error:
            raise ContentPlaneError(f"{label} ancestor is unavailable: {current}: {error}") from error
        if stat.S_ISLNK(metadata.st_mode) or not stat.S_ISDIR(metadata.st_mode):
            raise ContentPlaneError(f"{label} ancestor is not a regular directory: {current}")


class _DescriptorExport:
    def __init__(self, export_dir: Path) -> None:
        if not export_dir.is_absolute() or export_dir.name in {"", ".", ".."}:
            raise ContentPlaneError("export directory must be one absolute child path")
        if export_dir.exists() or export_dir.is_symlink():
            raise ContentPlaneError("export directory must not already exist")
        _require_safe_existing_directory(export_dir.parent, "export parent")
        flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
        self.parent_fd = os.open(export_dir.parent, flags)
        self.root_fd = -1
        self.name = export_dir.name
        try:
            os.mkdir(self.name, 0o755, dir_fd=self.parent_fd)
            self.root_fd = os.open(self.name, flags, dir_fd=self.parent_fd)
            os.fchmod(self.root_fd, 0o755)
        except Exception:
            os.close(self.parent_fd)
            raise

    def close(self) -> None:
        if self.root_fd >= 0:
            os.fsync(self.root_fd)
            os.close(self.root_fd)
            self.root_fd = -1
        if self.parent_fd >= 0:
            os.fsync(self.parent_fd)
            os.close(self.parent_fd)
            self.parent_fd = -1

    def _directory(self, parts: tuple[str, ...]) -> int:
        flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
        descriptor = os.dup(self.root_fd)
        try:
            for part in parts:
                try:
                    os.mkdir(part, 0o755, dir_fd=descriptor)
                except FileExistsError:
                    pass
                child = os.open(part, flags, dir_fd=descriptor)
                os.fchmod(child, 0o755)
                os.close(descriptor)
                descriptor = child
            return descriptor
        except Exception:
            os.close(descriptor)
            raise

    def write(self, relative: str, value: bytes) -> None:
        path = PurePosixPath(relative)
        parent = self._directory(tuple(path.parts[:-1]))
        flags = (
            os.O_WRONLY
            | os.O_CREAT
            | os.O_EXCL
            | getattr(os, "O_NOFOLLOW", 0)
        )
        try:
            descriptor = os.open(path.name, flags, 0o644, dir_fd=parent)
            try:
                os.fchmod(descriptor, 0o644)
                view = memoryview(value)
                while view:
                    written = os.write(descriptor, view)
                    if written <= 0:
                        raise ContentPlaneError(f"short write while exporting {relative}")
                    view = view[written:]
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
            os.fsync(parent)
        finally:
            os.close(parent)


def export_artifact(
    authority: Authority,
    export_dir: Path,
    run_id: str,
    run_attempt: str,
) -> dict[str, Any]:
    members, _, receipt = build_artifact_members(authority, run_id, run_attempt)
    writer = _DescriptorExport(export_dir)
    try:
        for relative, value in members.items():
            _validate_path(
                relative,
                ("content", "licenses", "authority"),
                authority.lock["limits"]["maxPathUtf8Bytes"],
            )
            writer.write(relative, value)
    finally:
        writer.close()
    verify_export(authority, export_dir, run_id, run_attempt)
    return receipt


def _walk_export(export_dir: Path) -> tuple[set[str], set[str]]:
    try:
        root_metadata = export_dir.lstat()
    except OSError as error:
        raise ContentPlaneError(f"export directory is unavailable: {error}") from error
    if not stat.S_ISDIR(root_metadata.st_mode) or export_dir.is_symlink():
        raise ContentPlaneError("export root must be a regular non-symlink directory")
    if stat.S_IMODE(root_metadata.st_mode) != 0o755:
        raise ContentPlaneError("export root mode is not 0755")
    files: set[str] = set()
    directories: set[str] = set()
    for root, directory_names, file_names in os.walk(export_dir, followlinks=False):
        root_path = Path(root)
        for name in directory_names:
            path = root_path / name
            metadata = path.lstat()
            if stat.S_ISLNK(metadata.st_mode) or not stat.S_ISDIR(metadata.st_mode):
                raise ContentPlaneError(f"artifact directory member is unsafe: {path}")
            if stat.S_IMODE(metadata.st_mode) != 0o755:
                raise ContentPlaneError(f"artifact directory mode is not 0755: {path}")
            directories.add(path.relative_to(export_dir).as_posix())
        for name in file_names:
            path = root_path / name
            metadata = path.lstat()
            if stat.S_ISLNK(metadata.st_mode) or not stat.S_ISREG(metadata.st_mode):
                raise ContentPlaneError(f"artifact file member is not regular: {path}")
            if metadata.st_nlink != 1:
                raise ContentPlaneError(f"artifact file member is hard-linked: {path}")
            if stat.S_IMODE(metadata.st_mode) != 0o644:
                raise ContentPlaneError(f"artifact file mode is not 0644: {path}")
            files.add(path.relative_to(export_dir).as_posix())
    return files, directories


def _read_export_member(export_dir: Path, relative: str) -> bytes:
    flags_dir = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
    flags_file = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(export_dir, flags_dir)
    try:
        parts = PurePosixPath(relative).parts
        for part in parts[:-1]:
            child = os.open(part, flags_dir, dir_fd=descriptor)
            os.close(descriptor)
            descriptor = child
        member = os.open(parts[-1], flags_file, dir_fd=descriptor)
        try:
            before = os.fstat(member)
            if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1:
                raise ContentPlaneError(f"artifact member is not an independent regular file: {relative}")
            chunks: list[bytes] = []
            while True:
                chunk = os.read(member, 1024 * 1024)
                if not chunk:
                    break
                chunks.append(chunk)
            after = os.fstat(member)
            if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) != (
                after.st_dev,
                after.st_ino,
                after.st_size,
                after.st_mtime_ns,
            ):
                raise ContentPlaneError(f"artifact member changed while read: {relative}")
            return b"".join(chunks)
        finally:
            os.close(member)
    finally:
        os.close(descriptor)


def verify_export(
    authority: Authority,
    export_dir: Path,
    run_id: str,
    run_attempt: str,
) -> dict[str, Any]:
    expected, inventory, receipt = build_artifact_members(authority, run_id, run_attempt)
    actual_files, actual_directories = _walk_export(export_dir)
    expected_files = set(expected)
    if actual_files != expected_files:
        raise ContentPlaneError(
            "artifact members differ "
            f"(missing={sorted(expected_files - actual_files)!r}, "
            f"extra={sorted(actual_files - expected_files)!r})"
        )
    expected_directories = {
        PurePosixPath(path).parent.as_posix()
        for path in expected_files
        if PurePosixPath(path).parent.as_posix() != "."
    }
    expected_directories |= {
        parent.as_posix()
        for path in tuple(expected_directories)
        for parent in PurePosixPath(path).parents
        if parent.as_posix() != "."
    }
    if actual_directories != expected_directories:
        raise ContentPlaneError("artifact directories differ from the exact layout")
    _validate_unique_paths(sorted(actual_files), "artifact layout")
    if any(path.casefold().endswith(FORBIDDEN_SUFFIXES) for path in actual_files):
        raise ContentPlaneError("artifact contains a forbidden runtime assembly/package member")
    for relative, expected_value in expected.items():
        actual = _read_export_member(export_dir, relative)
        if len(actual) != len(expected_value) or _sha256(actual) != _sha256(expected_value):
            raise ContentPlaneError(f"artifact member bytes differ: {relative}")
    inventory_value = _read_export_member(export_dir, authority.lock["artifact"]["inventoryPath"])
    if _read_json_bytes(inventory_value, "content inventory") != inventory:
        raise ContentPlaneError("content inventory object differs after export")
    receipt_value = _read_export_member(export_dir, authority.lock["artifact"]["receiptPath"])
    if _read_json_bytes(receipt_value, "content receipt") != receipt:
        raise ContentPlaneError("content receipt object differs after export")
    seal_value = _read_export_member(export_dir, authority.lock["artifact"]["receiptSealPath"])
    expected_seal = f"{_sha256(receipt_value)}  producer-receipt.json\n".encode("ascii")
    if seal_value != expected_seal:
        raise ContentPlaneError("content receipt seal differs")
    return receipt


def _summary(authority: Authority) -> dict[str, Any]:
    _, inventory_bytes = build_inventory(authority)
    return {
        "status": "pass",
        "contract": LOCK_CONTRACT,
        "sourceCommit": SOURCE_COMMIT,
        "sourceTree": SOURCE_TREE,
        "producerCommit": authority.producer_commit,
        "producerTree": authority.producer_tree,
        "contentFileCount": len(authority.content_files),
        "contentByteCount": sum(file.size for file in authority.content_files),
        "contentDigest": CONTENT_DIGEST,
        "inventorySha256": _sha256(inventory_bytes),
        "artifactMemberCount": EXPECTED_ARTIFACT["memberCount"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--export-dir", type=Path)
    parser.add_argument("--expected-producer-commit")
    parser.add_argument("--run-id")
    parser.add_argument("--run-attempt")
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--check", action="store_true")
    actions.add_argument("--export", action="store_true")
    actions.add_argument("--verify-export", action="store_true")
    args = parser.parse_args()

    try:
        repo_root = args.repo_root.absolute()
        lock_path = (args.lock or repo_root / "eng/android-content-plane.lock.json").absolute()
        authority = build_authority(repo_root, lock_path, args.expected_producer_commit)
        if args.check:
            payload: dict[str, Any] = _summary(authority)
        else:
            if args.export_dir is None:
                raise ContentPlaneError("--export-dir is required for export verification")
            export_dir = args.export_dir.absolute()
            if args.export:
                payload = export_artifact(authority, export_dir, args.run_id, args.run_attempt)
            else:
                payload = verify_export(authority, export_dir, args.run_id, args.run_attempt)
        print(json.dumps(payload, indent=2, sort_keys=True))
        return 0
    except ContentPlaneError as error:
        print(f"android-content-plane: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
