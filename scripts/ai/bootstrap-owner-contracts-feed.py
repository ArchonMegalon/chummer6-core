#!/usr/bin/env python3
"""Build and validate the immutable owner-contract package plane.

The owner repositories are fetched at the exact commits in
eng/package-plane.lock.json. No branch or tag is accepted as authority.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


LOCK_CONTRACT = "chummer-core.package-plane-lock/v1"
INVENTORY_CONTRACT = "chummer-core.owner-contract-package-inventory/v1"
INVENTORY_FILE_NAME = "chummer-owner-contracts.inventory.json"
EXPECTED_PACKAGE_IDS = (
    "Chummer.Engine.Contracts",
    "Chummer.Hub.Registry.Contracts",
    "Chummer.Play.Contracts",
    "Chummer.Run.Contracts",
)
EXPECTED_INTERNAL_DEPENDENCIES = {
    "Chummer.Engine.Contracts": (),
    "Chummer.Hub.Registry.Contracts": (),
    "Chummer.Play.Contracts": (),
    "Chummer.Run.Contracts": (
        "Chummer.Engine.Contracts",
        "Chummer.Hub.Registry.Contracts",
        "Chummer.Play.Contracts",
    ),
}
EXPECTED_LICENSE_EXPRESSIONS = {
    "Chummer.Engine.Contracts": "GPL-3.0-only",
}
SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
VERSION_PATTERN = re.compile(
    r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)
HTTPS_GITHUB_PATTERN = re.compile(
    r"^https://github\.com/ArchonMegalon/[A-Za-z0-9._-]+\.git$"
)


class PackagePlaneError(RuntimeError):
    """Raised when immutable package-plane authority cannot be proven."""


@dataclass(frozen=True)
class PackageSpec:
    package_id: str
    repository: str
    commit: str
    checkout_directory: str
    project: str


@dataclass(frozen=True)
class PackagePlaneLock:
    dotnet_sdk: str
    package_version: str
    approved_remote_source: str
    packages: tuple[PackageSpec, ...]


def _required_string(payload: dict[str, Any], key: str) -> str:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip() or value != value.strip():
        raise PackagePlaneError(f"{key} must be a non-empty canonical string")
    return value


def _safe_relative_path(raw: str, label: str) -> str:
    path = PurePosixPath(raw)
    if path.is_absolute() or not path.parts or any(part in {"", ".", ".."} for part in path.parts):
        raise PackagePlaneError(f"{label} must be a contained relative path")
    if "\\" in raw:
        raise PackagePlaneError(f"{label} must use canonical forward slashes")
    return raw


def validate_lock_payload(payload: Any) -> PackagePlaneLock:
    if not isinstance(payload, dict):
        raise PackagePlaneError("package-plane lock must be a JSON object")
    if payload.get("contract") != LOCK_CONTRACT:
        raise PackagePlaneError(f"package-plane lock contract must be {LOCK_CONTRACT}")

    dotnet_sdk = _required_string(payload, "dotnet_sdk")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", dotnet_sdk):
        raise PackagePlaneError("dotnet_sdk must be an exact three-part version")

    package_version = _required_string(payload, "package_version")
    if not VERSION_PATTERN.fullmatch(package_version):
        raise PackagePlaneError("package_version must be one exact SemVer value")

    approved_remote_source = _required_string(payload, "approved_remote_source")
    if approved_remote_source != "https://api.nuget.org/v3/index.json":
        raise PackagePlaneError("approved_remote_source must be the canonical HTTPS NuGet.org v3 index")

    package_rows = payload.get("packages")
    if not isinstance(package_rows, list):
        raise PackagePlaneError("packages must be a list")

    packages: list[PackageSpec] = []
    repository_authority: dict[str, tuple[str, str]] = {}
    for index, raw_row in enumerate(package_rows):
        if not isinstance(raw_row, dict):
            raise PackagePlaneError(f"packages[{index}] must be an object")
        package_id = _required_string(raw_row, "id")
        repository = _required_string(raw_row, "repository")
        commit = _required_string(raw_row, "commit")
        checkout_directory = _safe_relative_path(
            _required_string(raw_row, "checkout_directory"),
            f"packages[{index}].checkout_directory",
        )
        project = _safe_relative_path(
            _required_string(raw_row, "project"),
            f"packages[{index}].project",
        )
        if "/" in checkout_directory:
            raise PackagePlaneError("checkout_directory must be one directory name")
        if not HTTPS_GITHUB_PATTERN.fullmatch(repository):
            raise PackagePlaneError("repository must be an allowlisted ArchonMegalon HTTPS GitHub URL")
        if not SHA_PATTERN.fullmatch(commit):
            raise PackagePlaneError("commit must be an exact lowercase 40-character SHA")
        authority = repository_authority.setdefault(checkout_directory, (repository, commit))
        if authority != (repository, commit):
            raise PackagePlaneError("one checkout_directory cannot name multiple repository authorities")
        packages.append(PackageSpec(package_id, repository, commit, checkout_directory, project))

    ids = tuple(package.package_id for package in packages)
    if ids != EXPECTED_PACKAGE_IDS:
        raise PackagePlaneError(
            "packages must contain the exact ordered package plane: " + ", ".join(EXPECTED_PACKAGE_IDS)
        )
    if len(set(ids)) != len(ids):
        raise PackagePlaneError("package ids must be unique")

    return PackagePlaneLock(dotnet_sdk, package_version, approved_remote_source, tuple(packages))


def load_lock(path: Path) -> PackagePlaneLock:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PackagePlaneError(f"unable to read package-plane lock {path}: {exc}") from exc
    return validate_lock_payload(payload)


def _run(command: Iterable[str], *, cwd: Path | None = None) -> str:
    result = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    if result.returncode != 0:
        rendered = " ".join(command)
        raise PackagePlaneError(f"command failed ({result.returncode}): {rendered}\n{result.stdout}")
    return result.stdout


def _contained_child(root: Path, child: Path) -> Path:
    resolved_root = root.resolve()
    resolved_child = child.resolve(strict=False)
    if resolved_child == resolved_root or resolved_root not in resolved_child.parents:
        raise PackagePlaneError(f"unsafe package-plane path outside {resolved_root}: {resolved_child}")
    return resolved_child


def _remove_checkout(workspace: Path, checkout: Path) -> None:
    contained = _contained_child(workspace, checkout)
    if contained.is_symlink() or contained.is_file():
        contained.unlink()
    elif contained.is_dir():
        shutil.rmtree(contained)


def ensure_exact_checkout(workspace: Path, spec: PackageSpec) -> Path:
    checkout = workspace / spec.checkout_directory
    # Never reuse an existing checkout. Matching HEAD/origin metadata does not
    # prove that tracked files, untracked files, or build outputs are clean.
    if checkout.exists() or checkout.is_symlink():
        _remove_checkout(workspace, checkout)

    checkout.parent.mkdir(parents=True, exist_ok=True)
    _run(("git", "init", str(checkout)))
    _run(("git", "remote", "add", "origin", spec.repository), cwd=checkout)
    _run(("git", "fetch", "--depth=1", "origin", spec.commit), cwd=checkout)
    _run(("git", "checkout", "--detach", spec.commit), cwd=checkout)
    observed = _run(("git", "rev-parse", "HEAD"), cwd=checkout).strip()
    if observed != spec.commit:
        raise PackagePlaneError(f"checkout digest mismatch for {spec.package_id}: {observed}")
    origin = _run(("git", "remote", "get-url", "origin"), cwd=checkout).strip()
    if origin != spec.repository:
        raise PackagePlaneError(f"checkout origin mismatch for {spec.package_id}: {origin}")
    status = _run(
        ("git", "status", "--porcelain=v1", "--untracked-files=all"),
        cwd=checkout,
    ).strip()
    if status:
        raise PackagePlaneError(f"fresh checkout is dirty for {spec.package_id}:\n{status}")
    return checkout


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _find_child_text(root: ET.Element, name: str) -> str:
    for element in root.iter():
        if _local_name(element.tag) == name and element.text:
            return element.text.strip()
    return ""


def _find_repository_metadata(root: ET.Element) -> tuple[str, str]:
    for element in root.iter():
        if _local_name(element.tag) == "repository":
            return (
                (element.attrib.get("url") or "").strip(),
                (element.attrib.get("commit") or "").strip(),
            )
    return "", ""


def _find_license_metadata(root: ET.Element) -> tuple[str, str]:
    for element in root.iter():
        if _local_name(element.tag) == "license":
            return (
                (element.attrib.get("type") or "").strip(),
                (element.text or "").strip(),
            )
    return "", ""


def _internal_dependencies(root: ET.Element) -> tuple[tuple[str, str], ...]:
    dependencies: list[tuple[str, str]] = []
    for element in root.iter():
        if _local_name(element.tag) != "dependency":
            continue
        package_id = (element.attrib.get("id") or "").strip()
        if package_id.startswith("Chummer."):
            dependencies.append((package_id, (element.attrib.get("version") or "").strip()))
    return tuple(sorted(dependencies))


def package_path(feed: Path, package_id: str, version: str) -> Path:
    expected = f"{package_id}.{version}.nupkg".lower()
    matches = [candidate for candidate in feed.glob("*.nupkg") if candidate.name.lower() == expected]
    if len(matches) != 1:
        raise PackagePlaneError(f"feed must contain exactly one {package_id} {version} package")
    return matches[0]


def validate_package(feed: Path, spec: PackageSpec, version: str) -> Path:
    path = package_path(feed, spec.package_id, version)
    try:
        with zipfile.ZipFile(path) as archive:
            names = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
            if len(names) != 1:
                raise PackagePlaneError(f"{path.name} must contain exactly one nuspec")
            root = ET.fromstring(archive.read(names[0]))
    except (OSError, zipfile.BadZipFile, ET.ParseError) as exc:
        raise PackagePlaneError(f"invalid package {path}: {exc}") from exc
    if _find_child_text(root, "id") != spec.package_id:
        raise PackagePlaneError(f"package id mismatch in {path.name}")
    if _find_child_text(root, "version") != version:
        raise PackagePlaneError(f"package version mismatch in {path.name}")
    repository_url, repository_commit = _find_repository_metadata(root)
    if repository_url != spec.repository:
        raise PackagePlaneError(
            f"package repository URL mismatch in {path.name}: {repository_url or '<missing>'}"
        )
    if repository_commit != spec.commit:
        raise PackagePlaneError(
            f"package repository commit mismatch in {path.name}: {repository_commit or '<missing>'}"
        )
    expected_license = EXPECTED_LICENSE_EXPRESSIONS.get(spec.package_id)
    if expected_license is not None:
        license_type, license_value = _find_license_metadata(root)
        if (license_type, license_value) != ("expression", expected_license):
            raise PackagePlaneError(
                f"package license mismatch in {path.name}: "
                f"{license_type or '<missing>'}/{license_value or '<missing>'}"
            )
    expected_dependencies = tuple(
        sorted((dependency, version) for dependency in EXPECTED_INTERNAL_DEPENDENCIES[spec.package_id])
    )
    if _internal_dependencies(root) != expected_dependencies:
        raise PackagePlaneError(f"internal dependency drift in {path.name}")
    expected_assembly = f"lib/net10.0/{spec.package_id}.dll".lower()
    with zipfile.ZipFile(path) as archive:
        if expected_assembly not in {name.lower() for name in archive.namelist()}:
            raise PackagePlaneError(f"expected contract assembly is missing from {path.name}")
    return path


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _inventory_path(feed: Path) -> Path:
    return feed / INVENTORY_FILE_NAME


def _expected_feed_entry_names(lock: PackagePlaneLock) -> set[str]:
    return {
        INVENTORY_FILE_NAME,
        *(f"{spec.package_id}.{lock.package_version}.nupkg" for spec in lock.packages),
    }


def _assert_exact_feed_entries(feed: Path, lock: PackagePlaneLock) -> None:
    expected = _expected_feed_entry_names(lock)
    observed = {entry.name for entry in feed.iterdir()}
    if observed != expected:
        missing = sorted(expected - observed)
        unexpected = sorted(observed - expected)
        details: list[str] = []
        if missing:
            details.append("missing=" + ",".join(missing))
        if unexpected:
            details.append("unexpected=" + ",".join(unexpected))
        raise PackagePlaneError(
            "feed must contain the exact locked file set (" + "; ".join(details) + ")"
        )


def _inventory_payload(
    lock: PackagePlaneLock,
    *,
    feed: Path,
    lock_sha256: str,
) -> dict[str, Any]:
    packages: list[dict[str, Any]] = []
    for spec in lock.packages:
        path = validate_package(feed, spec, lock.package_version)
        packages.append(
            {
                "id": spec.package_id,
                "version": lock.package_version,
                "repository": spec.repository,
                "commit": spec.commit,
                "project": spec.project,
                "file_name": path.name,
                "sha256": _sha256(path),
                "size_bytes": path.stat().st_size,
            }
        )
    return {
        "contract": INVENTORY_CONTRACT,
        "package_plane_lock_sha256": lock_sha256,
        "package_version": lock.package_version,
        "packages": packages,
    }


def _atomic_write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    temporary_path = Path(temporary_name)
    try:
        with os.fdopen(handle, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        if temporary_path.exists():
            temporary_path.unlink()


def validate_feed_inventory(
    feed: Path,
    lock: PackagePlaneLock,
    lock_sha256: str,
) -> str:
    _assert_exact_feed_entries(feed, lock)
    inventory_path = _inventory_path(feed)
    try:
        inventory_bytes = inventory_path.read_bytes()
        payload = json.loads(inventory_bytes)
    except (OSError, json.JSONDecodeError) as exc:
        raise PackagePlaneError(f"unable to read package inventory {inventory_path}: {exc}") from exc
    expected_top_level_keys = {
        "contract",
        "package_plane_lock_sha256",
        "package_version",
        "packages",
    }
    if not isinstance(payload, dict) or set(payload) != expected_top_level_keys:
        raise PackagePlaneError("package inventory must contain the exact top-level fields")
    if payload.get("contract") != INVENTORY_CONTRACT:
        raise PackagePlaneError(f"package inventory contract must be {INVENTORY_CONTRACT}")
    if payload.get("package_plane_lock_sha256") != lock_sha256:
        raise PackagePlaneError("package inventory does not bind the exact package-plane lock")
    if payload.get("package_version") != lock.package_version:
        raise PackagePlaneError("package inventory version does not match the package-plane lock")
    rows = payload.get("packages")
    if not isinstance(rows, list) or len(rows) != len(lock.packages):
        raise PackagePlaneError("package inventory must contain the exact locked package set")

    for spec, row in zip(lock.packages, rows, strict=True):
        if not isinstance(row, dict):
            raise PackagePlaneError(f"package inventory row for {spec.package_id} must be an object")
        expected_row_keys = {
            "id",
            "version",
            "repository",
            "commit",
            "project",
            "file_name",
            "sha256",
            "size_bytes",
        }
        if set(row) != expected_row_keys:
            raise PackagePlaneError(
                f"package inventory row for {spec.package_id} must contain the exact fields"
            )
        expected_file_name = f"{spec.package_id}.{lock.package_version}.nupkg"
        expected_metadata = {
            "id": spec.package_id,
            "version": lock.package_version,
            "repository": spec.repository,
            "commit": spec.commit,
            "project": spec.project,
            "file_name": expected_file_name,
        }
        for key, expected in expected_metadata.items():
            if row.get(key) != expected:
                raise PackagePlaneError(
                    f"package inventory {key} mismatch for {spec.package_id}: "
                    f"{row.get(key)!r}"
                )
        expected_sha256 = row.get("sha256")
        if not isinstance(expected_sha256, str) or not SHA256_PATTERN.fullmatch(expected_sha256):
            raise PackagePlaneError(f"package inventory SHA256 is invalid for {spec.package_id}")
        expected_size = row.get("size_bytes")
        if not isinstance(expected_size, int) or isinstance(expected_size, bool) or expected_size <= 0:
            raise PackagePlaneError(f"package inventory size is invalid for {spec.package_id}")
        path = validate_package(feed, spec, lock.package_version)
        if path.stat().st_size != expected_size:
            raise PackagePlaneError(f"package byte size mismatch for {spec.package_id}")
        if _sha256(path) != expected_sha256:
            raise PackagePlaneError(f"package byte digest mismatch for {spec.package_id}")

    return hashlib.sha256(inventory_bytes).hexdigest()


def _promote_feed(
    lock: PackagePlaneLock,
    *,
    staged_feed: Path,
    feed: Path,
    inventory: dict[str, Any],
) -> None:
    feed.mkdir(parents=True, exist_ok=True)
    for spec in lock.packages:
        source = package_path(staged_feed, spec.package_id, lock.package_version)
        target = feed / source.name
        handle, temporary_name = tempfile.mkstemp(prefix=f".{target.name}.", dir=feed)
        os.close(handle)
        temporary_path = Path(temporary_name)
        try:
            shutil.copyfile(source, temporary_path)
            os.replace(temporary_path, target)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()
        for candidate in feed.glob("*.nupkg"):
            if candidate != target and candidate.name.lower() == target.name.lower():
                candidate.unlink()

    expected_entries = _expected_feed_entry_names(lock)
    for candidate in feed.iterdir():
        if candidate.name in expected_entries:
            continue
        if candidate.is_file() and candidate.suffix.lower() in {".nupkg", ".snupkg"}:
            candidate.unlink()
            continue
        raise PackagePlaneError(f"unexpected non-package entry in owner-contract feed: {candidate.name}")

    # The inventory is the current pointer and is advanced only after every
    # package has been promoted. A partial promotion therefore fails closed.
    _atomic_write_json(_inventory_path(feed), inventory)


def build_feed(
    lock: PackagePlaneLock,
    *,
    lock_sha256: str,
    feed: Path,
    workspace: Path,
    package_root: Path,
    dotnet: str,
) -> str:
    feed.parent.mkdir(parents=True, exist_ok=True)
    workspace.mkdir(parents=True, exist_ok=True)
    package_root.mkdir(parents=True, exist_ok=True)

    repository_specs: dict[str, PackageSpec] = {}
    for spec in lock.packages:
        repository_specs.setdefault(spec.checkout_directory, spec)
    with tempfile.TemporaryDirectory(prefix="owner-contracts-feed-", dir=feed.parent) as staged_dir:
        staged_feed = Path(staged_dir)
        with tempfile.TemporaryDirectory(
            prefix="owner-contracts-packages-", dir=package_root
        ) as isolated_package_root_name:
            isolated_package_root = Path(isolated_package_root_name)
            for spec in repository_specs.values():
                ensure_exact_checkout(workspace, spec)

            for spec in lock.packages:
                project = workspace / spec.checkout_directory / Path(spec.project)
                if not project.is_file():
                    raise PackagePlaneError(f"locked project is missing: {project}")
                pack_command = [
                    dotnet,
                    "pack",
                    str(project),
                    "--configuration",
                    "Release",
                    "--nologo",
                    "-m:1",
                    f"-p:PackageVersion={lock.package_version}",
                    f"-p:Version={lock.package_version}",
                    f"-p:RepositoryCommit={spec.commit}",
                    f"-p:RepositoryUrl={spec.repository}",
                    "-p:PublishRepositoryUrl=true",
                    "-p:ContinuousIntegrationBuild=true",
                    "-p:UseSharedCompilation=false",
                    f"-p:RestorePackagesPath={isolated_package_root}",
                ]
                expected_license = EXPECTED_LICENSE_EXPRESSIONS.get(spec.package_id)
                if expected_license is not None:
                    pack_command.append(f"-p:PackageLicenseExpression={expected_license}")
                pack_command.extend(
                    (
                        "--output",
                        str(staged_feed),
                    )
                )
                output = _run(
                    pack_command,
                    cwd=workspace / spec.checkout_directory,
                )
                sys.stdout.write(output)
                validate_package(staged_feed, spec, lock.package_version)

        inventory = _inventory_payload(lock, feed=staged_feed, lock_sha256=lock_sha256)
        _promote_feed(lock, staged_feed=staged_feed, feed=feed, inventory=inventory)
    return validate_feed_inventory(feed, lock, lock_sha256)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--feed", type=Path)
    parser.add_argument("--workspace", type=Path)
    parser.add_argument("--package-root", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--print-version", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    lock_path = (args.lock or repo_root / "eng/package-plane.lock.json").resolve()
    lock = load_lock(lock_path)
    lock_sha256 = _sha256(lock_path)
    if args.print_version:
        print(lock.package_version)
        return 0
    if args.feed is None:
        raise PackagePlaneError("--feed is required unless --print-version is used")
    feed = args.feed.resolve()
    if args.validate_only:
        inventory_sha256 = validate_feed_inventory(feed, lock, lock_sha256)
        print(
            f"owner-contract-package-plane: ok ({len(lock.packages)} packages; "
            f"inventory {inventory_sha256})"
        )
        return 0
    workspace = (args.workspace or repo_root / ".tmp/ai/package-plane/sources").resolve()
    package_root = (args.package_root or repo_root / ".tmp/ai/package-plane/nuget-packages").resolve()
    inventory_sha256 = build_feed(
        lock,
        lock_sha256=lock_sha256,
        feed=feed,
        workspace=workspace,
        package_root=package_root,
        dotnet=args.dotnet,
    )
    print(
        f"owner-contract-package-plane: ok ({len(lock.packages)} packages; "
        f"inventory {inventory_sha256})"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except PackagePlaneError as exc:
        print(f"owner-contract-package-plane: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
