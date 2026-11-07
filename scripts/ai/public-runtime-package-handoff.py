#!/usr/bin/env python3
"""Prepare and verify the public, secret-free Core runtime package handoff.

The GitHub release is a transport, not an authority.  Consumers must pin the
receipt and bundle byte digests in their own reviewed authority before use.

The existing v2 receipt is Actions-only.  Local v3 receipts explicitly carry
operator-declared execution evidence, not authenticated Actions provenance.
The exporter does not capture SDK identity or execution start/end times: the
operator must record those during the build in a local-execution/v1 JSON file.
Local preparation checks that declaration against Git and the exported bytes;
it cannot independently attest which SDK executed the build.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import os
import re
import stat
import subprocess
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any


CONTRACT = "chummer-core.runtime-package-public-handoff/v2"
LOCAL_CONTRACT = "chummer-core.runtime-package-public-handoff/v3"
LOCAL_EXECUTION_CONTRACT = "chummer-core.runtime-package-local-execution/v1"
ARCHIVE_CONTRACT = "chummer-core.runtime-package-public-handoff-zip/v1"
REPOSITORY = "ArchonMegalon/chummer6-core"
MAIN_REF = "refs/heads/main"
MAIN_BRANCH = "main"
WORKFLOW_PATH = ".github/workflows/package-plane.yml"
INVENTORY_NAME = "chummer-core-runtime-packages.inventory.json"
LOCK_NAME = "runtime-package-plane.lock.json"
NO_SIBLINGS_RECEIPT_NAME = "no-siblings.v3.receipt.json"
MAX_MEMBER_BYTES = 16 * 1024 * 1024
MAX_BUNDLE_BYTES = 32 * 1024 * 1024
MAX_ACTIONS_ARCHIVE_BYTES = 32 * 1024 * 1024
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
ARTIFACT_DIGEST_RE = re.compile(r"^(?:sha256:)?([0-9a-f]{64})$")
RECEIPT_KEYS = {
    "contract",
    "repository",
    "commit",
    "ref",
    "release_tag",
    "source_actions_artifact",
    "bundle",
    "receipt_asset_name",
}
LOCAL_RECEIPT_KEYS = (RECEIPT_KEYS - {"ref", "source_actions_artifact"}) | {
    "provenance_mode", "source_local_execution",
}
LOCAL_EXECUTION_KEYS = {
    "contract", "evidence_kind", "repository", "runtime_source_commit",
    "runtime_source_tree", "package_recipe_commit", "package_recipe_tree",
    "package_version", "started_at_utc", "completed_at_utc", "toolchain",
    "no_siblings_receipt_sha256", "no_siblings_generated_at_utc",
}
NO_SIBLINGS_RECEIPT_KEYS = {
    "contract",
    "generated_at_utc",
    "status",
    "core_commit",
    "package_plane_lock_sha256",
    "package_inventory_sha256",
    "candidate_package_inventory_sha256",
    "candidate_runtime_package_inventory_sha256",
    "runtime_package_inventory_sha256",
    "runtime_package_plane_lock_sha256",
    "runtime_source_commit",
    "package_recipe_commit",
    "package_version",
    "candidate_package_version",
    "locked_packages",
    "resolved_owner_contracts",
    "no_sibling_directories",
    "isolated_package_cache",
    "package_source_mapping",
    "normal_local_engine_dependency_graph",
    "build",
    "package_plane_runtime_test",
    "local_owner_isolation_tests",
    "candidate_engine_contract_pack",
    "candidate_gm_edit_runtime_pack",
    "candidate_gm_edit_runtime_consumer",
    "eight_package_runtime_plane",
}
PASS_CLAIMS = (
    "normal_local_engine_dependency_graph",
    "build",
    "package_plane_runtime_test",
    "local_owner_isolation_tests",
    "candidate_engine_contract_pack",
    "candidate_gm_edit_runtime_pack",
    "candidate_gm_edit_runtime_consumer",
    "eight_package_runtime_plane",
)


class PublicHandoffError(RuntimeError):
    pass


def _strict_json(raw: bytes | str, label: str) -> Any:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise PublicHandoffError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    def reject_nonfinite(value: str) -> Any:
        raise PublicHandoffError(f"{label} contains non-finite number {value}")

    def finite_float(value: str) -> float:
        parsed = float(value)
        if not math.isfinite(parsed):
            reject_nonfinite(value)
        return parsed

    try:
        return json.loads(
            raw,
            object_pairs_hook=reject_duplicates,
            parse_constant=reject_nonfinite,
            parse_float=finite_float,
        )
    except PublicHandoffError:
        raise
    except (TypeError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PublicHandoffError(f"{label} is invalid JSON: {exc}") from exc


def _canonical_json(payload: Any) -> bytes:
    return (json.dumps(payload, indent=2, sort_keys=True) + "\n").encode("utf-8")


def _sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def _require_commit(commit: str) -> None:
    if not COMMIT_RE.fullmatch(commit):
        raise PublicHandoffError("handoff commit must be one lowercase full SHA")


def release_tag(commit: str) -> str:
    _require_commit(commit)
    return f"core-runtime-package-plane-{commit}"


def bundle_asset_name(commit: str) -> str:
    _require_commit(commit)
    return f"chummer-core-runtime-package-plane-{commit}.zip"


def receipt_asset_name(commit: str) -> str:
    _require_commit(commit)
    return f"chummer-core-runtime-package-plane-{commit}.public-handoff.json"


def actions_artifact_name(commit: str) -> str:
    _require_commit(commit)
    return f"chummer-core-runtime-package-plane-{commit}"


def _safe_member_name(name: str) -> None:
    path = PurePosixPath(name)
    if (
        not name
        or path.is_absolute()
        or "\\" in name
        or any(part in {"", ".", ".."} for part in path.parts)
    ):
        raise PublicHandoffError(f"unsafe runtime bundle member: {name!r}")


def _read_bound_file(
    path: Path,
    label: str,
    *,
    maximum_bytes: int = MAX_MEMBER_BYTES,
) -> bytes:
    try:
        before = path.lstat()
    except OSError as exc:
        raise PublicHandoffError(f"cannot inspect {label}: {exc}") from exc
    if (
        not stat.S_ISREG(before.st_mode)
        or before.st_nlink != 1
        or before.st_size <= 0
        or before.st_size > maximum_bytes
    ):
        raise PublicHandoffError(f"{label} is not one bounded regular file")
    descriptor = -1
    try:
        descriptor = os.open(
            path,
            os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0),
        )
        opened = os.fstat(descriptor)
        identity = (
            opened.st_dev,
            opened.st_ino,
            opened.st_mode,
            opened.st_nlink,
            opened.st_size,
            opened.st_mtime_ns,
            opened.st_ctime_ns,
        )
        before_identity = (
            before.st_dev,
            before.st_ino,
            before.st_mode,
            before.st_nlink,
            before.st_size,
            before.st_mtime_ns,
            before.st_ctime_ns,
        )
        if identity != before_identity:
            raise PublicHandoffError(f"{label} changed while opening")
        chunks: list[bytes] = []
        total = 0
        while total <= maximum_bytes:
            chunk = os.read(descriptor, min(1024 * 1024, maximum_bytes + 1 - total))
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
        after = os.fstat(descriptor)
        after_identity = (
            after.st_dev,
            after.st_ino,
            after.st_mode,
            after.st_nlink,
            after.st_size,
            after.st_mtime_ns,
            after.st_ctime_ns,
        )
        if after_identity != identity or total != opened.st_size:
            raise PublicHandoffError(f"{label} changed while reading")
        return b"".join(chunks)
    except OSError as exc:
        raise PublicHandoffError(f"cannot read {label}: {exc}") from exc
    finally:
        if descriptor >= 0:
            os.close(descriptor)


def _directory_names(path: Path, label: str) -> set[str]:
    try:
        metadata = path.lstat()
        if not stat.S_ISDIR(metadata.st_mode):
            raise PublicHandoffError(f"{label} is not a real directory")
        names = {entry.name for entry in os.scandir(path)}
    except PublicHandoffError:
        raise
    except OSError as exc:
        raise PublicHandoffError(f"cannot inspect {label}: {exc}") from exc
    if len(names) != len({name.casefold() for name in names}):
        raise PublicHandoffError(f"{label} contains case-colliding members")
    return names


def _positive_int(value: Any, label: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise PublicHandoffError(f"{label} must be one positive integer")
    return value


def _utc_timestamp(value: Any, label: str) -> str:
    if not isinstance(value, str):
        raise PublicHandoffError(f"{label} must be one UTC timestamp")
    try:
        datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError as exc:
        raise PublicHandoffError(f"{label} must be one UTC timestamp") from exc
    return value


def _expected_workflow_ref(repository: str, ref: str) -> str:
    return f"{repository}/{WORKFLOW_PATH}@{ref}"


def _validate_actions_source(
    *,
    artifact_metadata_path: Path,
    run_metadata_path: Path,
    actions_archive_path: Path,
    repository: str,
    commit: str,
    ref: str,
    event_name: str,
    source_artifact_id: int,
    source_artifact_digest: str,
    run_id: int,
    run_attempt: int,
    workflow_ref: str,
    workflow_sha: str,
    head_tree: str,
) -> tuple[bytes, dict[str, Any]]:
    """Authenticate one current-run artifact before parsing any archive member."""

    _require_commit(commit)
    _require_commit(workflow_sha)
    _require_commit(head_tree)
    source_artifact_id = _positive_int(source_artifact_id, "source artifact ID")
    run_id = _positive_int(run_id, "workflow run ID")
    run_attempt = _positive_int(run_attempt, "workflow run attempt")
    digest_match = ARTIFACT_DIGEST_RE.fullmatch(source_artifact_digest)
    if (
        repository != REPOSITORY
        or ref != MAIN_REF
        or event_name != "push"
        or workflow_ref != _expected_workflow_ref(repository, ref)
        or workflow_sha != commit
        or digest_match is None
    ):
        raise PublicHandoffError(
            "Actions artifact authority is allowed only for the exact Core main push workflow"
        )

    archive_raw = _read_bound_file(
        actions_archive_path,
        "raw Actions artifact archive",
        maximum_bytes=MAX_ACTIONS_ARCHIVE_BYTES,
    )
    archive_sha256 = _sha256(archive_raw)
    if digest_match.group(1) != archive_sha256:
        raise PublicHandoffError("raw Actions artifact digest differs from upload authority")

    artifact = _strict_json(
        _read_bound_file(artifact_metadata_path, "authenticated Actions artifact metadata"),
        "authenticated Actions artifact metadata",
    )
    run = _strict_json(
        _read_bound_file(run_metadata_path, "authenticated Actions run metadata"),
        "authenticated Actions run metadata",
    )
    if not isinstance(artifact, dict) or not isinstance(run, dict):
        raise PublicHandoffError("authenticated Actions metadata shape differs")

    api_root = f"https://api.github.com/repos/{repository}"
    artifact_api_url = f"{api_root}/actions/artifacts/{source_artifact_id}"
    artifact_archive_url = f"{artifact_api_url}/zip"
    run_attempt_api_url = f"{api_root}/actions/runs/{run_id}/attempts/{run_attempt}"
    artifact_run = artifact.get("workflow_run")
    if not isinstance(artifact_run, dict):
        raise PublicHandoffError("authenticated artifact omits workflow-run authority")
    repository_id = _positive_int(artifact_run.get("repository_id"), "artifact repository ID")
    head_repository_id = _positive_int(
        artifact_run.get("head_repository_id"),
        "artifact head repository ID",
    )
    if (
        artifact.get("id") != source_artifact_id
        or artifact.get("name") != actions_artifact_name(commit)
        or artifact.get("url") != artifact_api_url
        or artifact.get("archive_download_url") != artifact_archive_url
        or artifact.get("expired") is not False
        or artifact.get("size_in_bytes") != len(archive_raw)
        or artifact.get("digest") != f"sha256:{archive_sha256}"
        or artifact_run.get("id") != run_id
        or artifact_run.get("head_branch") != MAIN_BRANCH
        or artifact_run.get("head_sha") != commit
        or repository_id != head_repository_id
    ):
        raise PublicHandoffError("authenticated Actions artifact metadata differs")
    created_at = _utc_timestamp(artifact.get("created_at"), "artifact creation time")
    expires_at = _utc_timestamp(artifact.get("expires_at"), "artifact expiry time")

    run_repository = run.get("repository")
    head_repository = run.get("head_repository")
    head_commit = run.get("head_commit")
    if not all(isinstance(value, dict) for value in (run_repository, head_repository, head_commit)):
        raise PublicHandoffError("authenticated run omits repository or head-commit authority")
    workflow_id = _positive_int(run.get("workflow_id"), "workflow ID")
    observed_run_attempt = _positive_int(run.get("run_attempt"), "observed workflow run attempt")
    if (
        run.get("id") != run_id
        or observed_run_attempt != run_attempt
        or run.get("event") != event_name
        or run.get("head_branch") != MAIN_BRANCH
        or run.get("head_sha") != commit
        or run.get("path") != WORKFLOW_PATH
        or run_repository.get("id") != repository_id
        or run_repository.get("full_name") != repository
        or head_repository.get("id") != head_repository_id
        or head_repository.get("full_name") != repository
        or head_commit.get("id") != commit
        or head_commit.get("tree_id") != head_tree
    ):
        raise PublicHandoffError("authenticated Actions run metadata differs")

    source_authority = {
        "id": source_artifact_id,
        "name": actions_artifact_name(commit),
        "sha256": archive_sha256,
        "size_bytes": len(archive_raw),
        "authenticated_metadata": {
            "api_url": artifact_api_url,
            "archive_download_url": artifact_archive_url,
            "created_at_utc": created_at,
            "expires_at_utc": expires_at,
            "repository_id": repository_id,
            "head_repository_id": head_repository_id,
        },
        "workflow_run": {
            "id": run_id,
            "attempt": run_attempt,
            "attempt_api_url": run_attempt_api_url,
            "workflow_id": workflow_id,
            "workflow_ref": workflow_ref,
            "workflow_sha": workflow_sha,
            "event": event_name,
            "head_branch": MAIN_BRANCH,
            "head_sha": commit,
            "head_tree": head_tree,
            "repository": repository,
        },
    }
    return archive_raw, source_authority


def _snapshot_actions_archive(archive_raw: bytes) -> dict[str, bytes]:
    """Read an already-authenticated outer archive into one immutable byte snapshot."""

    try:
        with zipfile.ZipFile(io.BytesIO(archive_raw), "r", allowZip64=False) as archive:
            infos = archive.infolist()
            if len(infos) != 11:
                raise PublicHandoffError("Actions artifact must contain exactly 11 files")
            names = [info.filename for info in infos]
            if len(names) != len(set(names)) or len(names) != len({name.casefold() for name in names}):
                raise PublicHandoffError("Actions artifact contains duplicate or case-colliding names")
            snapshot: dict[str, bytes] = {}
            total = 0
            for info in infos:
                _safe_member_name(info.filename)
                mode = info.external_attr >> 16
                if (
                    info.is_dir()
                    or info.create_system != 3
                    or stat.S_IFMT(mode) != stat.S_IFREG
                    or stat.S_IMODE(mode) != 0o644
                    or info.compress_type != zipfile.ZIP_STORED
                    or info.flag_bits & 0x1
                    or info.file_size <= 0
                    or info.file_size > MAX_MEMBER_BYTES
                    or info.compress_size != info.file_size
                ):
                    raise PublicHandoffError(
                        f"Actions artifact member posture differs: {info.filename}"
                    )
                total += info.file_size
                if total > MAX_BUNDLE_BYTES:
                    raise PublicHandoffError("Actions artifact exceeds aggregate byte authority")
                raw = archive.read(info)
                if len(raw) != info.file_size:
                    raise PublicHandoffError(
                        f"Actions artifact member size differs: {info.filename}"
                    )
                snapshot[info.filename] = raw
            return snapshot
    except PublicHandoffError:
        raise
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError) as exc:
        raise PublicHandoffError(f"Actions artifact archive is invalid: {exc}") from exc


def _validate_bundle(
    repo_root: Path,
    bundle_dir: Path,
    commit: str,
) -> tuple[dict[str, bytes], dict[str, Any]]:
    _require_commit(commit)
    repo_lock_path = repo_root / "eng" / LOCK_NAME
    repo_lock_bytes = _read_bound_file(repo_lock_path, "checked runtime package lock")
    lock = _strict_json(repo_lock_bytes, "checked runtime package lock")
    if not isinstance(lock, dict) or lock.get("contract") != "chummer-core.runtime-package-plane-lock/v1":
        raise PublicHandoffError("checked runtime package lock contract differs")
    package_specs = lock.get("packages")
    if not isinstance(package_specs, list) or len(package_specs) != 8:
        raise PublicHandoffError("checked runtime package lock must contain eight packages")

    expected_root = {"packages", INVENTORY_NAME, LOCK_NAME, NO_SIBLINGS_RECEIPT_NAME}
    if _directory_names(bundle_dir, "runtime bundle") != expected_root:
        raise PublicHandoffError("runtime bundle root members differ from exact authority")
    artifact_lock_bytes = _read_bound_file(bundle_dir / LOCK_NAME, "artifact runtime package lock")
    if artifact_lock_bytes != repo_lock_bytes:
        raise PublicHandoffError("artifact runtime package lock differs from checked commit")

    inventory_bytes = _read_bound_file(bundle_dir / INVENTORY_NAME, "runtime package inventory")
    inventory = _strict_json(inventory_bytes, "runtime package inventory")
    if not isinstance(inventory, dict) or set(inventory) != {
        "contract",
        "package_plane_lock_sha256",
        "package_recipe_commit",
        "package_version",
        "packages",
        "runtime_source_commit",
    }:
        raise PublicHandoffError("runtime package inventory shape differs")
    if (
        inventory.get("contract") != "chummer-core.runtime-package-inventory/v1"
        or inventory.get("package_plane_lock_sha256") != _sha256(repo_lock_bytes)
        or inventory.get("package_recipe_commit") != commit
        or inventory.get("package_version") != lock.get("package_version")
        or inventory.get("runtime_source_commit") != (lock.get("runtime_source") or {}).get("commit")
    ):
        raise PublicHandoffError("runtime package inventory authority differs")
    rows = inventory.get("packages")
    if not isinstance(rows, list) or len(rows) != len(package_specs):
        raise PublicHandoffError("runtime package inventory row count differs")
    expected_row_keys = {
        "id",
        "version",
        "repository",
        "source_commit",
        "project",
        "assembly",
        "target_framework",
        "dependencies",
        "file_name",
        "sha256",
        "size_bytes",
    }
    package_names: set[str] = set()
    members: dict[str, bytes] = {
        INVENTORY_NAME: inventory_bytes,
        LOCK_NAME: artifact_lock_bytes,
    }
    packages_dir = bundle_dir / "packages"
    for spec, row in zip(package_specs, rows, strict=True):
        if not isinstance(spec, dict) or not isinstance(row, dict) or set(row) != expected_row_keys:
            raise PublicHandoffError("runtime package inventory row shape differs")
        package_name = row.get("file_name")
        if not isinstance(package_name, str):
            raise PublicHandoffError("runtime package inventory file name is invalid")
        _safe_member_name(package_name)
        expected_metadata = {
            "id": spec.get("id"),
            "version": lock.get("package_version"),
            "repository": (lock.get("runtime_source") or {}).get("repository"),
            "source_commit": (lock.get("runtime_source") or {}).get("commit"),
            "project": spec.get("project"),
            "assembly": spec.get("assembly"),
            "target_framework": spec.get("target_framework"),
            "dependencies": spec.get("dependencies"),
            "file_name": f"{spec.get('id')}.{lock.get('package_version')}.nupkg",
        }
        if any(row.get(key) != value for key, value in expected_metadata.items()):
            raise PublicHandoffError(f"runtime package metadata differs: {spec.get('id')}")
        if (
            not SHA256_RE.fullmatch(str(row.get("sha256", "")))
            or not isinstance(row.get("size_bytes"), int)
            or isinstance(row.get("size_bytes"), bool)
            or row["size_bytes"] <= 0
            or row["size_bytes"] > MAX_MEMBER_BYTES
        ):
            raise PublicHandoffError(f"runtime package byte authority is invalid: {package_name}")
        package_names.add(package_name)
        raw = _read_bound_file(packages_dir / package_name, f"runtime package {package_name}")
        if len(raw) != row["size_bytes"] or _sha256(raw) != row["sha256"]:
            raise PublicHandoffError(f"runtime package bytes differ: {package_name}")
        members[f"packages/{package_name}"] = raw
    if _directory_names(packages_dir, "runtime packages directory") != package_names:
        raise PublicHandoffError("runtime package members differ from exact inventory")

    no_siblings_bytes = _read_bound_file(
        bundle_dir / NO_SIBLINGS_RECEIPT_NAME,
        "no-siblings receipt",
    )
    no_siblings = _strict_json(no_siblings_bytes, "no-siblings receipt")
    if (
        not isinstance(no_siblings, dict)
        or set(no_siblings) != NO_SIBLINGS_RECEIPT_KEYS
        or no_siblings.get("contract") != "chummer-core.no-siblings-package-plane/v3"
    ):
        raise PublicHandoffError("no-siblings receipt shape differs")
    try:
        datetime.strptime(no_siblings["generated_at_utc"], "%Y-%m-%dT%H:%M:%SZ")
    except (KeyError, TypeError, ValueError) as exc:
        raise PublicHandoffError("no-siblings receipt timestamp is invalid") from exc
    if (
        no_siblings.get("status") != "pass"
        or no_siblings.get("core_commit") != commit
        or no_siblings.get("package_recipe_commit") != commit
        or no_siblings.get("runtime_source_commit") != inventory["runtime_source_commit"]
        or no_siblings.get("candidate_package_version") != inventory["package_version"]
        or no_siblings.get("runtime_package_inventory_sha256") != _sha256(inventory_bytes)
        or no_siblings.get("runtime_package_plane_lock_sha256") != _sha256(repo_lock_bytes)
        or no_siblings.get("no_sibling_directories") is not True
        or no_siblings.get("isolated_package_cache") is not True
        or any(no_siblings.get(key) != "pass" for key in PASS_CLAIMS)
    ):
        raise PublicHandoffError("no-siblings receipt claims differ from bundle authority")
    runtime_rows = [dict(row, role="current_core_runtime_candidate") for row in rows]
    resolved_rows = no_siblings.get("resolved_owner_contracts")
    if (
        not isinstance(resolved_rows, list)
        or resolved_rows[: len(runtime_rows)] != runtime_rows
    ):
        raise PublicHandoffError("no-siblings receipt does not bind every runtime package")
    members[NO_SIBLINGS_RECEIPT_NAME] = no_siblings_bytes
    if sum(len(raw) for raw in members.values()) > MAX_BUNDLE_BYTES:
        raise PublicHandoffError("runtime bundle exceeds aggregate byte bound")
    return members, inventory


def _deterministic_zip(members: dict[str, bytes]) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_STORED, allowZip64=False) as archive:
        for name in sorted(members, key=lambda value: (value.casefold(), value)):
            _safe_member_name(name)
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.compress_type = zipfile.ZIP_STORED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(info, members[name])
    raw = output.getvalue()
    if not raw or len(raw) > MAX_BUNDLE_BYTES:
        raise PublicHandoffError("public runtime bundle archive exceeds byte bound")
    return raw


def _validate_local_source(source: Any, commit: str) -> tuple[datetime, datetime]:
    if (
        not isinstance(source, dict)
        or set(source) != LOCAL_EXECUTION_KEYS
        or source.get("contract") != LOCAL_EXECUTION_CONTRACT
        or source.get("evidence_kind") != "operator-declared"
        or source.get("repository") != REPOSITORY
        or source.get("package_recipe_commit") != commit
    ):
        raise PublicHandoffError("local execution provenance shape or authority differs")
    for key in ("runtime_source_commit", "runtime_source_tree", "package_recipe_commit", "package_recipe_tree"):
        if not isinstance(source[key], str) or not COMMIT_RE.fullmatch(source[key]):
            raise PublicHandoffError(f"local execution {key} must be one full Git SHA")
    toolchain = source["toolchain"]
    if (
        not isinstance(toolchain, dict)
        or set(toolchain) != {"dotnet_sdk_version"}
        or not isinstance(toolchain["dotnet_sdk_version"], str)
        or len(toolchain["dotnet_sdk_version"]) > 80
        or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", toolchain["dotnet_sdk_version"])
        or not isinstance(source["package_version"], str)
        or not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]{0,199}", source["package_version"])
        or not isinstance(source["no_siblings_receipt_sha256"], str)
        or not SHA256_RE.fullmatch(source["no_siblings_receipt_sha256"])
    ):
        raise PublicHandoffError("local execution toolchain, version or receipt digest is invalid")
    times = []
    for key in ("started_at_utc", "no_siblings_generated_at_utc", "completed_at_utc"):
        value = _utc_timestamp(source[key], f"local execution {key}")
        parsed = datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
        if parsed.strftime("%Y-%m-%dT%H:%M:%SZ") != value:
            raise PublicHandoffError("local execution timestamp is not canonical UTC")
        times.append(parsed)
    start, generated, end = times
    if not start <= generated <= end:
        raise PublicHandoffError("no-siblings receipt is outside the declared local execution window")
    return start, end


def _validate_local_bundle_binding(
    source: dict[str, Any], members: dict[str, bytes], inventory: dict[str, Any],
) -> None:
    no_siblings = _strict_json(members[NO_SIBLINGS_RECEIPT_NAME], "local no-siblings receipt")
    lock = _strict_json(members[LOCK_NAME], "local runtime package lock")
    sdk = lock.get("dotnet_sdk")
    if (
        any(source[key] != inventory[key] for key in (
            "runtime_source_commit", "package_recipe_commit", "package_version",
        ))
        or source["no_siblings_receipt_sha256"] != _sha256(members[NO_SIBLINGS_RECEIPT_NAME])
        or source["no_siblings_generated_at_utc"] != no_siblings["generated_at_utc"]
        or not isinstance(sdk, dict)
        or source["toolchain"]["dotnet_sdk_version"] != sdk.get("version")
        or (lock.get("runtime_source") or {}).get("repository")
        != f"https://github.com/{REPOSITORY}.git"
    ):
        raise PublicHandoffError("local execution provenance differs from exported bundle")


def _git(repo_root: Path, *args: str) -> bytes:
    try:
        return subprocess.run(
            ["git", "--no-replace-objects", "--no-optional-locks", "-C", str(repo_root), *args],
            check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30,
        ).stdout
    except (OSError, subprocess.SubprocessError) as exc:
        raise PublicHandoffError("cannot verify local execution Git identity") from exc


def _validate_local_checkout(repo_root: Path, source: dict[str, Any], lock_bytes: bytes) -> None:
    commit = source["package_recipe_commit"]
    if (
        _git(repo_root, "rev-parse", "--show-toplevel").strip() != os.fsencode(repo_root.resolve())
        or _git(repo_root, "rev-parse", "HEAD").strip() != commit.encode("ascii")
        or _git(repo_root, "status", "--porcelain", "--untracked-files=all")
        or _git(repo_root, "show", f"{commit}:eng/{LOCK_NAME}") != lock_bytes
    ):
        raise PublicHandoffError("local execution requires the clean exact recipe checkout")
    for prefix in ("runtime_source", "package_recipe"):
        revision = source[f"{prefix}_commit"]
        if (
            _git(repo_root, "cat-file", "-t", revision).strip() != b"commit"
            or _git(repo_root, "rev-parse", f"{revision}^{{tree}}").strip()
            != source[f"{prefix}_tree"].encode("ascii")
        ):
            raise PublicHandoffError("local execution source or recipe tree differs from Git")
    _git(repo_root, "merge-base", "--is-ancestor", source["runtime_source_commit"], commit)


def prepare_from_local_bundle(
    *, repo_root: Path, bundle_dir: Path, local_provenance_path: Path,
    output_dir: Path, commit: str,
) -> tuple[Path, Path]:
    """Seal an existing export; never run a build or manufacture execution evidence.

    The local declaration must use exactly LOCAL_EXECUTION_KEYS, with
    evidence_kind=operator-declared and toolchain={"dotnet_sdk_version": "..."}.
    Record start/end around the existing no-siblings verifier/exporter. Exact
    unchanged evidence remains reusable regardless of age. Git identities and
    all bundle bytes are checked,
    while SDK identity and execution times remain operator assertions.
    """
    _require_commit(commit)
    source = _strict_json(
        _read_bound_file(local_provenance_path, "local execution provenance"),
        "local execution provenance",
    )
    _, end = _validate_local_source(source, commit)
    now = datetime.now(timezone.utc)
    if end > now:
        raise PublicHandoffError("local execution evidence is in the future")
    members, inventory = _validate_bundle(repo_root, bundle_dir, commit)
    _validate_local_bundle_binding(source, members, inventory)
    _validate_local_checkout(repo_root, source, members[LOCK_NAME])
    return _write_public_assets(
        members=members, output_dir=output_dir, repository=REPOSITORY,
        commit=commit, source_local_execution=source,
    )


def _member_rows(members: dict[str, bytes]) -> list[dict[str, Any]]:
    return [
        {"path": name, "sha256": _sha256(members[name]), "size_bytes": len(members[name])}
        for name in sorted(members, key=lambda value: (value.casefold(), value))
    ]


def _write_public_assets(
    *,
    members: dict[str, bytes],
    output_dir: Path,
    repository: str,
    commit: str,
    ref: str | None = None,
    source_actions_artifact: dict[str, Any] | None = None,
    source_local_execution: dict[str, Any] | None = None,
) -> tuple[Path, Path]:
    if (source_actions_artifact is None) == (source_local_execution is None):
        raise PublicHandoffError("public handoff requires exactly one provenance mode")
    if source_local_execution is not None and ref is not None:
        raise PublicHandoffError("local execution cannot claim an Actions ref")
    archive_bytes = _deterministic_zip(members)
    archive_name = bundle_asset_name(commit)
    manifest_name = receipt_asset_name(commit)
    member_rows = _member_rows(members)
    payload = {
        "contract": CONTRACT,
        "repository": repository,
        "commit": commit,
        "ref": ref,
        "release_tag": release_tag(commit),
        "source_actions_artifact": source_actions_artifact,
        "bundle": {
            "contract": ARCHIVE_CONTRACT,
            "asset_name": archive_name,
            "sha256": _sha256(archive_bytes),
            "size_bytes": len(archive_bytes),
            "member_count": len(member_rows),
            "uncompressed_size_bytes": sum(row["size_bytes"] for row in member_rows),
            "members": member_rows,
        },
        "receipt_asset_name": manifest_name,
    }
    if source_local_execution is not None:
        payload["contract"] = LOCAL_CONTRACT
        payload["provenance_mode"] = "local"
        payload["source_local_execution"] = source_local_execution
        del payload["ref"]
        del payload["source_actions_artifact"]
    receipt_bytes = _canonical_json(payload)
    try:
        output_dir.mkdir(mode=0o755)
        archive_path = output_dir / archive_name
        receipt_path = output_dir / manifest_name
        with archive_path.open("xb") as stream:
            stream.write(archive_bytes)
        with receipt_path.open("xb") as stream:
            stream.write(receipt_bytes)
    except (FileExistsError, OSError) as exc:
        raise PublicHandoffError(f"public handoff output must be a new directory: {exc}") from exc
    return archive_path, receipt_path


def prepare_from_actions_artifact(
    *,
    repo_root: Path,
    artifact_metadata_path: Path,
    run_metadata_path: Path,
    actions_archive_path: Path,
    output_dir: Path,
    repository: str,
    commit: str,
    ref: str,
    event_name: str,
    source_artifact_id: int,
    source_artifact_digest: str,
    run_id: int,
    run_attempt: int,
    workflow_ref: str,
    workflow_sha: str,
    head_tree: str,
) -> tuple[Path, Path]:
    archive_raw, source_authority = _validate_actions_source(
        artifact_metadata_path=artifact_metadata_path,
        run_metadata_path=run_metadata_path,
        actions_archive_path=actions_archive_path,
        repository=repository,
        commit=commit,
        ref=ref,
        event_name=event_name,
        source_artifact_id=source_artifact_id,
        source_artifact_digest=source_artifact_digest,
        run_id=run_id,
        run_attempt=run_attempt,
        workflow_ref=workflow_ref,
        workflow_sha=workflow_sha,
        head_tree=head_tree,
    )
    snapshot = _snapshot_actions_archive(archive_raw)
    try:
        with tempfile.TemporaryDirectory(prefix="chummer-core-actions-artifact-") as temporary:
            bundle_dir = Path(temporary) / "bundle"
            bundle_dir.mkdir(mode=0o700)
            for name, raw in snapshot.items():
                destination = bundle_dir / PurePosixPath(name)
                destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                with destination.open("xb") as stream:
                    stream.write(raw)
            members, _ = _validate_bundle(repo_root, bundle_dir, commit)
    except PublicHandoffError:
        raise
    except OSError as exc:
        raise PublicHandoffError(f"cannot materialize authenticated artifact snapshot: {exc}") from exc
    return _write_public_assets(
        members=members,
        output_dir=output_dir,
        repository=repository,
        commit=commit,
        ref=ref,
        source_actions_artifact=source_authority,
    )


def _load_public_receipt(path: Path) -> tuple[bytes, dict[str, Any]]:
    raw = _read_bound_file(path, "public handoff receipt")
    payload = _strict_json(raw, "public handoff receipt")
    if isinstance(payload, dict) and payload.get("contract") == LOCAL_CONTRACT:
        _validate_local_public_receipt(payload)
        if raw != _canonical_json(payload):
            raise PublicHandoffError("public handoff receipt is not canonical JSON")
        return raw, payload
    if not isinstance(payload, dict) or set(payload) != RECEIPT_KEYS:
        raise PublicHandoffError("public handoff receipt shape differs")
    commit = payload.get("commit")
    if not isinstance(commit, str):
        raise PublicHandoffError("public handoff receipt commit is invalid")
    _require_commit(commit)
    source = payload.get("source_actions_artifact")
    bundle = payload.get("bundle")
    authenticated_metadata = source.get("authenticated_metadata") if isinstance(source, dict) else None
    workflow_run = source.get("workflow_run") if isinstance(source, dict) else None
    if (
        payload.get("contract") != CONTRACT
        or payload.get("repository") != REPOSITORY
        or payload.get("ref") != MAIN_REF
        or payload.get("release_tag") != release_tag(commit)
        or payload.get("receipt_asset_name") != receipt_asset_name(commit)
        or not isinstance(source, dict)
        or set(source)
        != {
            "id",
            "name",
            "sha256",
            "size_bytes",
            "authenticated_metadata",
            "workflow_run",
        }
        or not isinstance(source.get("id"), int)
        or isinstance(source.get("id"), bool)
        or source["id"] <= 0
        or source.get("name") != actions_artifact_name(commit)
        or not SHA256_RE.fullmatch(str(source.get("sha256", "")))
        or not isinstance(source.get("size_bytes"), int)
        or isinstance(source.get("size_bytes"), bool)
        or source["size_bytes"] <= 0
        or source["size_bytes"] > MAX_ACTIONS_ARCHIVE_BYTES
        or not isinstance(authenticated_metadata, dict)
        or set(authenticated_metadata)
        != {
            "api_url",
            "archive_download_url",
            "created_at_utc",
            "expires_at_utc",
            "repository_id",
            "head_repository_id",
        }
        or authenticated_metadata.get("api_url")
        != f"https://api.github.com/repos/{REPOSITORY}/actions/artifacts/{source.get('id')}"
        or authenticated_metadata.get("archive_download_url")
        != f"https://api.github.com/repos/{REPOSITORY}/actions/artifacts/{source.get('id')}/zip"
        or not isinstance(authenticated_metadata.get("repository_id"), int)
        or isinstance(authenticated_metadata.get("repository_id"), bool)
        or authenticated_metadata["repository_id"] <= 0
        or authenticated_metadata.get("head_repository_id")
        != authenticated_metadata.get("repository_id")
        or not isinstance(workflow_run, dict)
        or set(workflow_run)
        != {
            "id",
            "attempt",
            "attempt_api_url",
            "workflow_id",
            "workflow_ref",
            "workflow_sha",
            "event",
            "head_branch",
            "head_sha",
            "head_tree",
            "repository",
        }
        or not isinstance(workflow_run.get("id"), int)
        or isinstance(workflow_run.get("id"), bool)
        or workflow_run["id"] <= 0
        or not isinstance(workflow_run.get("attempt"), int)
        or isinstance(workflow_run.get("attempt"), bool)
        or workflow_run["attempt"] <= 0
        or workflow_run.get("attempt_api_url")
        != (
            f"https://api.github.com/repos/{REPOSITORY}/actions/runs/"
            f"{workflow_run.get('id')}/attempts/{workflow_run.get('attempt')}"
        )
        or not isinstance(workflow_run.get("workflow_id"), int)
        or isinstance(workflow_run.get("workflow_id"), bool)
        or workflow_run["workflow_id"] <= 0
        or workflow_run.get("workflow_ref") != _expected_workflow_ref(REPOSITORY, MAIN_REF)
        or workflow_run.get("workflow_sha") != commit
        or workflow_run.get("event") != "push"
        or workflow_run.get("head_branch") != MAIN_BRANCH
        or workflow_run.get("head_sha") != commit
        or not COMMIT_RE.fullmatch(str(workflow_run.get("head_tree", "")))
        or workflow_run.get("repository") != REPOSITORY
        or not isinstance(bundle, dict)
        or set(bundle) != {
            "contract",
            "asset_name",
            "sha256",
            "size_bytes",
            "member_count",
            "uncompressed_size_bytes",
            "members",
        }
        or bundle.get("contract") != ARCHIVE_CONTRACT
        or bundle.get("asset_name") != bundle_asset_name(commit)
        or not SHA256_RE.fullmatch(str(bundle.get("sha256", "")))
    ):
        raise PublicHandoffError("public handoff receipt authority differs")
    _utc_timestamp(authenticated_metadata["created_at_utc"], "receipt artifact creation time")
    _utc_timestamp(authenticated_metadata["expires_at_utc"], "receipt artifact expiry time")
    if raw != _canonical_json(payload):
        raise PublicHandoffError("public handoff receipt is not canonical JSON")
    return raw, payload


def _validate_local_public_receipt(payload: dict[str, Any]) -> None:
    if set(payload) != LOCAL_RECEIPT_KEYS or payload.get("provenance_mode") != "local":
        raise PublicHandoffError("local public handoff receipt shape or mode differs")
    commit = payload["commit"]
    if not isinstance(commit, str):
        raise PublicHandoffError("local public handoff commit is invalid")
    _require_commit(commit)
    source = payload["source_local_execution"]
    _, completed = _validate_local_source(source, commit)
    if completed > datetime.now(timezone.utc):
        raise PublicHandoffError("local public handoff execution is in the future")
    bundle = payload["bundle"]
    if (
        payload["repository"] != REPOSITORY
        or payload["release_tag"] != release_tag(commit)
        or payload["receipt_asset_name"] != receipt_asset_name(commit)
        or not isinstance(bundle, dict)
        or set(bundle) != {
            "contract", "asset_name", "sha256", "size_bytes", "member_count",
            "uncompressed_size_bytes", "members",
        }
        or bundle["contract"] != ARCHIVE_CONTRACT
        or bundle["asset_name"] != bundle_asset_name(commit)
        or not isinstance(bundle["sha256"], str)
        or not SHA256_RE.fullmatch(bundle["sha256"])
        or not isinstance(bundle["members"], list)
    ):
        raise PublicHandoffError("local public handoff bundle authority differs")
    for key in ("size_bytes", "member_count", "uncompressed_size_bytes"):
        _positive_int(bundle[key], f"local bundle {key}")
    rows = bundle["members"]
    paths: list[str] = []
    total = 0
    receipt_digest = None
    for row in rows:
        if (
            not isinstance(row, dict) or set(row) != {"path", "sha256", "size_bytes"}
            or not isinstance(row["path"], str) or not isinstance(row["sha256"], str)
            or not SHA256_RE.fullmatch(row["sha256"])
        ):
            raise PublicHandoffError("local bundle member authority is invalid")
        _safe_member_name(row["path"])
        size = _positive_int(row["size_bytes"], "local bundle member size")
        if size > MAX_MEMBER_BYTES:
            raise PublicHandoffError("local bundle member exceeds byte bound")
        total += size
        paths.append(row["path"])
        if row["path"] == NO_SIBLINGS_RECEIPT_NAME:
            receipt_digest = row["sha256"]
    if (
        bundle["member_count"] != 11 or len(rows) != 11
        or len({path.casefold() for path in paths}) != 11
        or paths != sorted(paths, key=lambda value: (value.casefold(), value))
        or not {INVENTORY_NAME, LOCK_NAME, NO_SIBLINGS_RECEIPT_NAME} <= set(paths)
        or any(not re.fullmatch(r"packages/[^/]+\.nupkg", path) for path in paths
               if path not in {INVENTORY_NAME, LOCK_NAME, NO_SIBLINGS_RECEIPT_NAME})
        or total != bundle["uncompressed_size_bytes"] or total > MAX_BUNDLE_BYTES
        or bundle["size_bytes"] > MAX_BUNDLE_BYTES
        or receipt_digest != source["no_siblings_receipt_sha256"]
    ):
        raise PublicHandoffError("local bundle members differ from receipt authority")


def _verify_local_public_bundle(raw: bytes, receipt: dict[str, Any]) -> None:
    # Both archive modes use the same exact 11-file, stored-only ZIP posture.
    snapshot = _snapshot_actions_archive(raw)
    if _member_rows(snapshot) != receipt["bundle"]["members"] or _deterministic_zip(snapshot) != raw:
        raise PublicHandoffError("local public bundle members differ from receipt")
    try:
        with tempfile.TemporaryDirectory(prefix="chummer-core-local-readback-") as temporary:
            root = Path(temporary)
            bundle = root / "bundle"
            for name, data in snapshot.items():
                destination = bundle / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(data)
            (root / "eng").mkdir()
            (root / "eng" / LOCK_NAME).write_bytes(snapshot[LOCK_NAME])
            # At readback the pinned receipt supplies byte authority, not a checkout.
            members, inventory = _validate_bundle(root, bundle, receipt["commit"])
            _validate_local_bundle_binding(receipt["source_local_execution"], members, inventory)
    except OSError as exc:
        raise PublicHandoffError(f"cannot validate local bundle readback: {exc}") from exc


def _validate_direct_tag_metadata(tag_metadata: Any, tag: str, commit: str) -> None:
    if (
        not isinstance(tag_metadata, dict)
        or tag_metadata.get("ref") != f"refs/tags/{tag}"
        or not isinstance(tag_metadata.get("object"), dict)
        or tag_metadata["object"].get("type") != "commit"
        or tag_metadata["object"].get("sha") != commit
    ):
        raise PublicHandoffError("public Git tag does not point directly to the exact commit")


def verify_public_release(
    *,
    release_metadata_path: Path,
    tag_metadata_path: Path,
    receipt_path: Path,
    downloaded_bundle_path: Path,
    downloaded_receipt_path: Path,
) -> None:
    expected_receipt_bytes, receipt = _load_public_receipt(receipt_path)
    metadata = _strict_json(
        _read_bound_file(release_metadata_path, "public release metadata"),
        "public release metadata",
    )
    if not isinstance(metadata, dict):
        raise PublicHandoffError("public release metadata shape differs")
    commit = receipt["commit"]
    if (
        metadata.get("tag_name") != receipt["release_tag"]
        or metadata.get("draft") is not False
        or metadata.get("prerelease") is not False
    ):
        raise PublicHandoffError("public release tag or posture differs")
    tag_metadata = _strict_json(
        _read_bound_file(tag_metadata_path, "public tag metadata"),
        "public tag metadata",
    )
    _validate_direct_tag_metadata(tag_metadata, receipt["release_tag"], commit)
    assets = metadata.get("assets")
    if not isinstance(assets, list) or len(assets) != 2:
        raise PublicHandoffError("public release must contain exactly two assets")
    by_name: dict[str, dict[str, Any]] = {}
    for asset in assets:
        if not isinstance(asset, dict) or not isinstance(asset.get("name"), str):
            raise PublicHandoffError("public release asset metadata is invalid")
        if asset["name"] in by_name:
            raise PublicHandoffError("public release contains duplicate asset names")
        by_name[asset["name"]] = asset
    expected_names = {receipt["bundle"]["asset_name"], receipt["receipt_asset_name"]}
    if set(by_name) != expected_names:
        raise PublicHandoffError("public release asset names differ")

    downloaded_bundle = _read_bound_file(downloaded_bundle_path, "downloaded public bundle")
    downloaded_receipt = _read_bound_file(downloaded_receipt_path, "downloaded public receipt")
    expected = {
        receipt["bundle"]["asset_name"]: (
            receipt["bundle"]["sha256"],
            receipt["bundle"]["size_bytes"],
            downloaded_bundle,
        ),
        receipt["receipt_asset_name"]: (
            _sha256(expected_receipt_bytes),
            len(expected_receipt_bytes),
            downloaded_receipt,
        ),
    }
    for name, (digest, size_bytes, downloaded) in expected.items():
        asset = by_name[name]
        if (
            asset.get("state") != "uploaded"
            or asset.get("size") != size_bytes
            or asset.get("digest") != f"sha256:{digest}"
            or len(downloaded) != size_bytes
            or _sha256(downloaded) != digest
        ):
            raise PublicHandoffError(f"public release asset byte authority differs: {name}")
    if downloaded_receipt != expected_receipt_bytes:
        raise PublicHandoffError("downloaded public receipt differs byte-for-byte")
    if receipt["contract"] == LOCAL_CONTRACT:
        _verify_local_public_bundle(downloaded_bundle, receipt)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subcommands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = subcommands.add_parser("prepare-from-actions-artifact")
    prepare_parser.add_argument("--repo-root", type=Path, required=True)
    prepare_parser.add_argument("--artifact-metadata", type=Path, required=True)
    prepare_parser.add_argument("--run-metadata", type=Path, required=True)
    prepare_parser.add_argument("--actions-archive", type=Path, required=True)
    prepare_parser.add_argument("--output-dir", type=Path, required=True)
    prepare_parser.add_argument("--repository", required=True)
    prepare_parser.add_argument("--commit", required=True)
    prepare_parser.add_argument("--ref", required=True)
    prepare_parser.add_argument("--event-name", required=True)
    prepare_parser.add_argument("--source-artifact-id", type=int, required=True)
    prepare_parser.add_argument("--source-artifact-digest", required=True)
    prepare_parser.add_argument("--run-id", type=int, required=True)
    prepare_parser.add_argument("--run-attempt", type=int, required=True)
    prepare_parser.add_argument("--workflow-ref", required=True)
    prepare_parser.add_argument("--workflow-sha", required=True)
    prepare_parser.add_argument("--head-tree", required=True)

    local_parser = subcommands.add_parser("prepare-from-local-bundle")
    local_parser.add_argument("--repo-root", type=Path, required=True)
    local_parser.add_argument("--bundle-dir", type=Path, required=True)
    local_parser.add_argument("--local-provenance", type=Path, required=True)
    local_parser.add_argument("--output-dir", type=Path, required=True)
    local_parser.add_argument("--commit", required=True)

    verify_parser = subcommands.add_parser("verify-public-release")
    verify_parser.add_argument("--release-metadata", type=Path, required=True)
    verify_parser.add_argument("--tag-metadata", type=Path, required=True)
    verify_parser.add_argument("--receipt", type=Path, required=True)
    verify_parser.add_argument("--downloaded-bundle", type=Path, required=True)
    verify_parser.add_argument("--downloaded-receipt", type=Path, required=True)

    tag_parser = subcommands.add_parser("verify-tag-ref")
    tag_parser.add_argument("--tag-metadata", type=Path, required=True)
    tag_parser.add_argument("--tag", required=True)
    tag_parser.add_argument("--commit", required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if args.command == "prepare-from-actions-artifact":
            archive, receipt = prepare_from_actions_artifact(
                repo_root=args.repo_root.resolve(),
                artifact_metadata_path=args.artifact_metadata.resolve(),
                run_metadata_path=args.run_metadata.resolve(),
                actions_archive_path=args.actions_archive.resolve(),
                output_dir=args.output_dir.resolve(),
                repository=args.repository,
                commit=args.commit,
                ref=args.ref,
                event_name=args.event_name,
                source_artifact_id=args.source_artifact_id,
                source_artifact_digest=args.source_artifact_digest,
                run_id=args.run_id,
                run_attempt=args.run_attempt,
                workflow_ref=args.workflow_ref,
                workflow_sha=args.workflow_sha,
                head_tree=args.head_tree,
            )
            print(f"public runtime package bundle: {archive}")
            print(f"public runtime package receipt: {receipt}")
        elif args.command == "prepare-from-local-bundle":
            archive, receipt = prepare_from_local_bundle(
                repo_root=args.repo_root.absolute(),
                bundle_dir=args.bundle_dir.absolute(),
                local_provenance_path=args.local_provenance.absolute(),
                output_dir=args.output_dir.absolute(),
                commit=args.commit,
            )
            print(f"public runtime package bundle: {archive}")
            print(f"public runtime package receipt: {receipt}")
        elif args.command == "verify-public-release":
            verify_public_release(
                release_metadata_path=args.release_metadata.resolve(),
                tag_metadata_path=args.tag_metadata.resolve(),
                receipt_path=args.receipt.resolve(),
                downloaded_bundle_path=args.downloaded_bundle.resolve(),
                downloaded_receipt_path=args.downloaded_receipt.resolve(),
            )
            print("public runtime package release: verified without credentials")
        else:
            _require_commit(args.commit)
            tag_metadata = _strict_json(
                _read_bound_file(args.tag_metadata.resolve(), "created tag metadata"),
                "created tag metadata",
            )
            _validate_direct_tag_metadata(tag_metadata, args.tag, args.commit)
            print("public runtime package tag: exact lightweight commit ref")
    except PublicHandoffError as exc:
        print(f"public-runtime-package-handoff: {exc}", file=os.sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
