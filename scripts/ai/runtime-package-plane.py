#!/usr/bin/env python3
"""Validate and receipt the exact Core runtime package plane.

The lock binds runtime source semantics to one Core commit.  A later packaging
recipe commit may add only package metadata and authority controls; changes to
runtime source fail closed until the source commit and candidate version move.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import stat
import subprocess
import sys
import tarfile
import tempfile
import types
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path, PurePosixPath
from typing import Any, Callable, Iterable


LOCK_CONTRACT = "chummer-core.runtime-package-plane-lock/v1"
INVENTORY_CONTRACT = "chummer-core.runtime-package-inventory/v1"
INVENTORY_NAME = "chummer-core-runtime-packages.inventory.json"
OWNER_INVENTORY_NAME = "chummer-owner-contracts.inventory.json"
CANDIDATE_ENGINE_INVENTORY_NAME = "chummer-core-candidate-engine-contract.inventory.json"
CANDIDATE_GM_INVENTORY_NAME = "chummer-core-candidate-gm-edit-runtime.inventory.json"
PACKAGE_VERSION = "0.0.0-packageplane.candidate.sh7599f9f5d460"
SOURCE_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-core.git"
SOURCE_COMMIT = "7599f9f5d46073b589612473472fccb445512fb1"
SDK_VERSION = "10.0.103"
SDK_RID = "linux-x64"
SDK_ARCHIVE_URL = (
    "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.103/"
    "dotnet-sdk-10.0.103-linux-x64.tar.gz"
)
SDK_ARCHIVE_SHA512 = (
    "bab94f13c57b2ac821d4924fe66084be9b44c41761ff7ff64522c8f7aba345659"
    "d31258401dcec31cc3cf6ccae1d012623075aca1c9b9165bcfe5ba9abda1c0c"
)
SDK_ARCHIVE_MAX_BYTES = 512 * 1024 * 1024
EXPORT_MEMBER_MAX_BYTES = 512 * 1024 * 1024
TARGET_FRAMEWORK = "net10.0"
LICENSE_EXPRESSION = "GPL-3.0-only"
EXTERNAL_OWNER_PACKAGES = (
    (
        "Chummer.Hub.Registry.Contracts",
        "0.0.0-packageplane.20260721.1",
        "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
        "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    ),
    (
        "Chummer.Play.Contracts",
        "0.0.0-packageplane.20260721.1",
        "https://github.com/ArchonMegalon/chummer6-hub.git",
        "7c1faef298fb9028e77069c2467686f92624566c",
    ),
    (
        "Chummer.Run.Contracts",
        "0.0.0-packageplane.20260721.1",
        "https://github.com/ArchonMegalon/chummer6-hub.git",
        "7c1faef298fb9028e77069c2467686f92624566c",
    ),
)
THIRD_PARTY_PACKAGES = (
    ("Microsoft.Extensions.DependencyInjection", "10.0.0"),
    ("SharpCompress", "0.50.1"),
)


@dataclass(frozen=True)
class PackageSpec:
    package_id: str
    project: str
    project_sha256: str
    assembly: str
    dependencies: tuple[str, ...]


PACKAGE_SPECS = (
    PackageSpec(
        "Chummer.Engine.Contracts",
        "Chummer.Contracts/Chummer.Contracts.csproj",
        "1ae056091372ae0fb353b983023cea521ac848b899fd8d3ca3d45e546f57707e",
        "Chummer.Engine.Contracts.dll",
        (),
    ),
    PackageSpec(
        "Chummer.Application",
        "Chummer.Application/Chummer.Application.csproj",
        "289b245ed773af33b114ceb9ed51e667801ff202f79ccee35a32ecc410da88fb",
        "Chummer.Application.dll",
        (
            "Chummer.Engine.Contracts",
            "Chummer.Hub.Registry.Contracts",
            "Chummer.Run.Contracts",
        ),
    ),
    PackageSpec(
        "Chummer.Rulesets.Hosting",
        "Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj",
        "b3e1145840a1767a92e6e7c42fa5e510249753b36973e75050d6eac198e17521",
        "Chummer.Rulesets.Hosting.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Run.Contracts",
            "Microsoft.Extensions.DependencyInjection",
        ),
    ),
    PackageSpec(
        "Chummer.Rulesets.Sr5",
        "Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj",
        "2f7f91916c55035d42d7e5bddd52e76379ad2d0bb6d6eb4ff7ac5c7bbbea9826",
        "Chummer.Rulesets.Sr5.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Run.Contracts",
            "Microsoft.Extensions.DependencyInjection",
        ),
    ),
    PackageSpec(
        "Chummer.Rulesets.Sr6",
        "Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj",
        "23023db965dbfbf1795a5d660f5d3d3bc0d12f17b0a164882e6910e1a25a1f1f",
        "Chummer.Rulesets.Sr6.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Run.Contracts",
            "Microsoft.Extensions.DependencyInjection",
        ),
    ),
    PackageSpec(
        "Chummer.Infrastructure",
        "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
        "e017c01931b664a99cf4d74d89f0e6ed07576c1de47dfa89b740eb972f877936",
        "Chummer.Infrastructure.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Hub.Registry.Contracts",
            "Chummer.Rulesets.Hosting",
            "Chummer.Rulesets.Sr5",
            "Chummer.Rulesets.Sr6",
            "Chummer.Run.Contracts",
            "Microsoft.Extensions.DependencyInjection",
            "SharpCompress",
        ),
    ),
    PackageSpec(
        "Chummer.Rulesets.Sr4",
        "Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj",
        "86eafdcdf1638c3651d5357acd7f99023ca97c5641d83432be2b3c12f3ba5fb5",
        "Chummer.Rulesets.Sr4.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Infrastructure",
            "Chummer.Run.Contracts",
            "Microsoft.Extensions.DependencyInjection",
        ),
    ),
    PackageSpec(
        "Chummer.Engine.GmCharacterEdits",
        "Chummer.GmCharacterEdits/Chummer.GmCharacterEdits.csproj",
        "527b68de82b36057747c55b124d4bcd89be6a3daee66856db5db8c986a44b641",
        "Chummer.Engine.GmCharacterEdits.dll",
        (
            "Chummer.Application",
            "Chummer.Engine.Contracts",
            "Chummer.Hub.Registry.Contracts",
            "Chummer.Infrastructure",
            "Chummer.Rulesets.Hosting",
            "Chummer.Rulesets.Sr5",
            "Chummer.Rulesets.Sr6",
            "Chummer.Run.Contracts",
        ),
    ),
)

SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
CORE_PROPERTIES_RE = re.compile(
    r"^package/services/metadata/core-properties/[0-9a-f]+\.psmdcp$"
)
PROJECT_VARIABLE_DEPENDENCIES = {
    "$(ChummerLocalContractsProject)": "Chummer.Engine.Contracts",
    "$(ChummerLocalHubRegistryContractsProject)": "Chummer.Hub.Registry.Contracts",
    "$(ChummerLocalRunContractsProject)": "Chummer.Run.Contracts",
}
GM_PACKAGE_PLANE_CONDITION = "'$(ChummerRuntimePackagePlane)' != 'true'"
GM_RUNTIME_ASSEMBLY_PATHS = (
    "lib/net10.0/Chummer.Application.dll",
    "lib/net10.0/Chummer.Engine.GmCharacterEdits.dll",
    "lib/net10.0/Chummer.Infrastructure.dll",
    "lib/net10.0/Chummer.Rulesets.Hosting.dll",
    "lib/net10.0/Chummer.Rulesets.Sr5.dll",
    "lib/net10.0/Chummer.Rulesets.Sr6.dll",
)
ALLOWED_RECIPE_DELTA = (
    "eng/runtime-package-plane.lock.json",
    "scripts/ai/runtime-package-plane.py",
    "scripts/ai/verify-no-siblings-package-plane.sh",
    "tests/test_runtime_package_plane_authority.py",
)
BUILD_AUTHORITY_PATHS = (
    ".github/workflows/package-plane.yml",
    "Chummer.CoreEngine.sln",
    "Directory.Build.props",
    "Directory.Build.targets",
    "eng/package-plane.lock.json",
    "global.json",
    "scripts/ai/_env.sh",
    "scripts/ai/bootstrap-contracts-feed.sh",
    "scripts/ai/bootstrap-owner-contracts-feed.py",
    "scripts/ai/public-runtime-package-handoff.py",
    "scripts/ai/runtime-package-plane.py",
    "scripts/ai/verify-no-siblings-package-plane.sh",
)


class RuntimePackagePlaneError(RuntimeError):
    pass


def _strict_json_loads(raw: bytes | str, label: str) -> Any:
    def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise RuntimePackagePlaneError(
                    f"{label} contains a duplicate JSON key: {key!r}"
                )
            result[key] = value
        return result

    def reject_nonfinite(value: str) -> Any:
        raise RuntimePackagePlaneError(
            f"{label} contains a non-finite JSON number: {value}"
        )

    def parse_finite_float(value: str) -> float:
        parsed = float(value)
        if not math.isfinite(parsed):
            reject_nonfinite(value)
        return parsed

    try:
        return json.loads(
            raw,
            object_pairs_hook=reject_duplicate_keys,
            parse_constant=reject_nonfinite,
            parse_float=parse_finite_float,
        )
    except RuntimePackagePlaneError:
        raise
    except (TypeError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimePackagePlaneError(f"{label} is invalid JSON: {exc}") from exc


def _require_canonical_absolute_path(path: Path, label: str) -> None:
    if not path.is_absolute() or ".." in path.parts:
        raise RuntimePackagePlaneError(f"{label} must be one canonical absolute path")
    normalized = Path(os.path.normpath(os.fspath(path)))
    if path != normalized:
        raise RuntimePackagePlaneError(f"{label} must be one canonical absolute path")


def _dependency_version(package_id: str) -> str:
    if package_id in {spec.package_id for spec in PACKAGE_SPECS}:
        return PACKAGE_VERSION
    owner_versions = {row[0]: row[1] for row in EXTERNAL_OWNER_PACKAGES}
    third_party_versions = dict(THIRD_PARTY_PACKAGES)
    return owner_versions.get(package_id, third_party_versions.get(package_id, ""))


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _extract_digest_bound_tar_gz(
    archive_path: Path,
    destination: Path,
    expected_sha512: str,
    *,
    _after_snapshot: Callable[[], None] | None = None,
    _after_destination_open: Callable[[], None] | None = None,
) -> None:
    _require_canonical_absolute_path(archive_path, "SDK archive path")
    _require_canonical_absolute_path(destination, "SDK extraction path")
    if destination.name in {"", ".", ".."}:
        raise RuntimePackagePlaneError("SDK extraction directory name is invalid")

    source_flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
    directory_flags = (
        os.O_RDONLY
        | getattr(os, "O_DIRECTORY", 0)
        | getattr(os, "O_NOFOLLOW", 0)
        | getattr(os, "O_CLOEXEC", 0)
    )
    try:
        source_descriptor = os.open(archive_path, source_flags)
    except OSError as exc:
        raise RuntimePackagePlaneError(f"SDK archive is unavailable: {exc}") from exc
    parent_descriptor = -1
    destination_descriptor = -1
    reopened_parent = -1
    reopened_destination = -1
    try:
        if not stat.S_ISREG(os.fstat(source_descriptor).st_mode):
            raise RuntimePackagePlaneError("SDK archive must be one regular non-symlink file")
        with os.fdopen(source_descriptor, "rb", closefd=True) as source, tempfile.TemporaryFile(
            mode="w+b"
        ) as snapshot:
            source_descriptor = -1
            digest = hashlib.sha512()
            size = 0
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                size += len(chunk)
                if size > SDK_ARCHIVE_MAX_BYTES:
                    raise RuntimePackagePlaneError(
                        "SDK archive exceeds the bounded snapshot authority"
                    )
                digest.update(chunk)
                snapshot.write(chunk)
            if digest.hexdigest() != expected_sha512:
                raise RuntimePackagePlaneError("SDK archive SHA-512 differs from authority")
            snapshot.flush()
            if os.fstat(snapshot.fileno()).st_size != size:
                raise RuntimePackagePlaneError("SDK archive snapshot size differs from authority")
            snapshot.seek(0)
            if _after_snapshot is not None:
                _after_snapshot()

            parent_descriptor = _open_absolute_directory(destination.parent)
            try:
                os.stat(destination.name, dir_fd=parent_descriptor, follow_symlinks=False)
            except FileNotFoundError:
                pass
            else:
                raise RuntimePackagePlaneError(
                    "SDK extraction directory must not already exist"
                )
            try:
                os.mkdir(destination.name, mode=0o755, dir_fd=parent_descriptor)
                destination_descriptor = os.open(
                    destination.name,
                    directory_flags,
                    dir_fd=parent_descriptor,
                )
            except OSError as exc:
                raise RuntimePackagePlaneError(
                    f"cannot create SDK extraction directory: {exc}"
                ) from exc
            if os.listdir(destination_descriptor):
                raise RuntimePackagePlaneError("SDK extraction directory is not empty")
            descriptor_path = Path(f"/proc/self/fd/{destination_descriptor}")
            try:
                descriptor_stat = descriptor_path.stat()
            except OSError as exc:
                raise RuntimePackagePlaneError(
                    f"SDK extraction descriptor path is unavailable: {exc}"
                ) from exc
            if (descriptor_stat.st_dev, descriptor_stat.st_ino) != (
                os.fstat(destination_descriptor).st_dev,
                os.fstat(destination_descriptor).st_ino,
            ):
                raise RuntimePackagePlaneError("SDK extraction descriptor path is not bound")
            if _after_destination_open is not None:
                _after_destination_open()

            with tarfile.open(fileobj=snapshot, mode="r:gz") as archive:
                members = archive.getmembers()
                if not members:
                    raise RuntimePackagePlaneError("SDK archive is empty")
                safe_members = []
                seen: set[str] = set()
                for member in members:
                    if member.name in {".", "./"} and member.isdir():
                        continue
                    member_path = PurePosixPath(member.name)
                    if (
                        member_path.is_absolute()
                        or not member_path.parts
                        or any(part in {"", ".", ".."} for part in member_path.parts)
                    ):
                        raise RuntimePackagePlaneError(
                            f"unsafe SDK archive member: {member.name!r}"
                        )
                    normalized = member_path.as_posix().casefold()
                    if normalized in seen:
                        raise RuntimePackagePlaneError(
                            f"duplicate SDK archive member: {member.name!r}"
                        )
                    seen.add(normalized)
                    filtered = tarfile.data_filter(member, descriptor_path)
                    if filtered is None:
                        raise RuntimePackagePlaneError(
                            f"SDK archive member was rejected: {member.name!r}"
                        )
                    safe_members.append(filtered)
                archive.extractall(descriptor_path, members=safe_members, filter="data")

            os.fsync(destination_descriptor)
            os.fsync(parent_descriptor)
            reopened_parent = _open_absolute_directory(destination.parent)
            if (
                os.fstat(reopened_parent).st_dev,
                os.fstat(reopened_parent).st_ino,
            ) != (
                os.fstat(parent_descriptor).st_dev,
                os.fstat(parent_descriptor).st_ino,
            ):
                raise RuntimePackagePlaneError("SDK extraction parent changed during extraction")
            try:
                reopened_destination = os.open(
                    destination.name,
                    directory_flags,
                    dir_fd=reopened_parent,
                )
            except OSError as exc:
                raise RuntimePackagePlaneError(
                    "SDK extraction directory changed during extraction"
                ) from exc
            if (
                os.fstat(reopened_destination).st_dev,
                os.fstat(reopened_destination).st_ino,
            ) != (
                os.fstat(destination_descriptor).st_dev,
                os.fstat(destination_descriptor).st_ino,
            ):
                raise RuntimePackagePlaneError(
                    "SDK extraction directory changed during extraction"
                )
    except (OSError, tarfile.TarError) as exc:
        raise RuntimePackagePlaneError(f"cannot safely extract SDK archive: {exc}") from exc
    finally:
        if reopened_destination >= 0:
            os.close(reopened_destination)
        if reopened_parent >= 0:
            os.close(reopened_parent)
        if destination_descriptor >= 0:
            os.close(destination_descriptor)
        if parent_descriptor >= 0:
            os.close(parent_descriptor)
        if source_descriptor >= 0:
            os.close(source_descriptor)


def _run(command: Iterable[str], *, cwd: Path) -> str:
    result = subprocess.run(
        tuple(command),
        cwd=cwd,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimePackagePlaneError(
            f"command failed ({result.returncode}): {' '.join(command)}\n{result.stdout}"
        )
    return result.stdout.strip()


def load_lock(path: Path) -> dict[str, Any]:
    try:
        payload = _strict_json_loads(path.read_bytes(), "runtime package lock")
    except (OSError, RuntimePackagePlaneError) as exc:
        raise RuntimePackagePlaneError(f"cannot read runtime package lock: {exc}") from exc
    validate_lock_payload(payload)
    return payload


def validate_lock_payload(payload: Any) -> None:
    if not isinstance(payload, dict) or set(payload) != {
        "contract",
        "dotnet_sdk",
        "package_version",
        "runtime_source",
        "allowed_recipe_delta",
        "build_authority_files",
        "external_owner_packages",
        "third_party_packages",
        "packages",
    }:
        raise RuntimePackagePlaneError("runtime package lock has an invalid top-level shape")
    if payload["contract"] != LOCK_CONTRACT:
        raise RuntimePackagePlaneError("runtime package lock contract is invalid")
    if payload["dotnet_sdk"] != {
        "version": SDK_VERSION,
        "rid": SDK_RID,
        "archive_url": SDK_ARCHIVE_URL,
        "archive_sha512": SDK_ARCHIVE_SHA512,
    }:
        raise RuntimePackagePlaneError("runtime package SDK is not exact")
    if payload["package_version"] != PACKAGE_VERSION:
        raise RuntimePackagePlaneError("runtime package version is not exact")
    if payload["runtime_source"] != {
        "repository": SOURCE_REPOSITORY,
        "commit": SOURCE_COMMIT,
    }:
        raise RuntimePackagePlaneError("runtime source authority is not exact")
    if payload["allowed_recipe_delta"] != list(ALLOWED_RECIPE_DELTA):
        raise RuntimePackagePlaneError("allowed package recipe delta is not exact")
    build_authority = payload["build_authority_files"]
    if not isinstance(build_authority, list) or any(
        not isinstance(row, dict) for row in build_authority
    ):
        raise RuntimePackagePlaneError("build authority files have an invalid shape")
    if [row.get("path") for row in build_authority] != list(BUILD_AUTHORITY_PATHS):
        raise RuntimePackagePlaneError("build authority file order is not exact")
    if any(
        set(row) != {"path", "sha256"}
        or not SHA256_RE.fullmatch(str(row.get("sha256", "")))
        for row in build_authority
    ):
        raise RuntimePackagePlaneError("build authority file digest is invalid")

    external = payload["external_owner_packages"]
    expected_external = [
        {"id": row[0], "version": row[1], "repository": row[2], "commit": row[3]}
        for row in EXTERNAL_OWNER_PACKAGES
    ]
    if external != expected_external:
        raise RuntimePackagePlaneError("external owner package authority is not exact")
    expected_third_party = [
        {"id": package_id, "version": version}
        for package_id, version in THIRD_PARTY_PACKAGES
    ]
    if payload["third_party_packages"] != expected_third_party:
        raise RuntimePackagePlaneError("third-party package authority is not exact")

    rows = payload["packages"]
    expected_rows = [
        {
            "id": spec.package_id,
            "project": spec.project,
            "project_sha256": spec.project_sha256,
            "assembly": spec.assembly,
            "target_framework": TARGET_FRAMEWORK,
            "dependencies": [
                {"id": dependency, "version": _dependency_version(dependency)}
                for dependency in spec.dependencies
            ],
        }
        for spec in PACKAGE_SPECS
    ]
    if rows != expected_rows:
        raise RuntimePackagePlaneError("runtime package order, ownership, or dependencies drifted")

    internal_ids = {spec.package_id for spec in PACKAGE_SPECS}
    external_ids = {row[0] for row in EXTERNAL_OWNER_PACKAGES}
    third_party_ids = {row[0] for row in THIRD_PARTY_PACKAGES}
    seen: set[str] = set()
    assemblies: set[str] = set()
    for spec in PACKAGE_SPECS:
        if spec.assembly.casefold() in assemblies:
            raise RuntimePackagePlaneError("one runtime assembly has multiple package owners")
        assemblies.add(spec.assembly.casefold())
        for dependency in spec.dependencies:
            if dependency not in internal_ids | external_ids | third_party_ids:
                raise RuntimePackagePlaneError(f"unknown package dependency: {dependency}")
            if dependency in internal_ids and dependency not in seen:
                raise RuntimePackagePlaneError(
                    f"runtime package order is not topological: {spec.package_id} -> {dependency}"
                )
        seen.add(spec.package_id)


def _project_dependencies(repo_root: Path, spec: PackageSpec, root: ET.Element) -> set[str]:
    project_path = repo_root / spec.project
    dependency_by_project = {
        (repo_root / candidate.project).resolve(): candidate.package_id
        for candidate in PACKAGE_SPECS
    }
    dependencies: set[str] = set()
    for element in root.iter():
        name = _local_name(element.tag)
        include = (element.attrib.get("Include") or "").strip()
        if name == "PackageReference" and include:
            dependencies.add(include)
        elif name == "ProjectReference":
            internal_reference = False
            if include in PROJECT_VARIABLE_DEPENDENCIES:
                dependencies.add(PROJECT_VARIABLE_DEPENDENCIES[include])
                internal_reference = True
            elif include and "$" not in include:
                resolved = (project_path.parent / include).resolve()
                dependency = dependency_by_project.get(resolved)
                if dependency is not None:
                    dependencies.add(dependency)
                    internal_reference = True
            private_assets = []
            attribute_private_assets = (element.attrib.get("PrivateAssets") or "").strip()
            if attribute_private_assets:
                private_assets.append(
                    (attribute_private_assets.lower(), (element.attrib.get("Condition") or "").strip())
                )
            private_assets.extend(
                (
                    (child.text or "").strip().lower(),
                    (child.attrib.get("Condition") or "").strip(),
                )
                for child in element
                if _local_name(child.tag) == "PrivateAssets" and (child.text or "").strip()
            )
            if not internal_reference:
                continue
            if spec.package_id == "Chummer.Engine.GmCharacterEdits":
                if private_assets != [("all", GM_PACKAGE_PLANE_CONDITION)]:
                    raise RuntimePackagePlaneError(
                        "GM project references must preserve normal PrivateAssets=all and "
                        "expose dependencies only in the explicit runtime package plane"
                    )
            elif any(value == "all" for value, _ in private_assets):
                raise RuntimePackagePlaneError(
                    f"{spec.package_id} hides an internal package dependency with PrivateAssets=all"
                )
    return dependencies


def _property_values(root: ET.Element, name: str) -> list[str]:
    return [
        (element.text or "").strip()
        for element in root.iter()
        if _local_name(element.tag) == name and (element.text or "").strip()
    ]


def validate_repository(repo_root: Path, lock: dict[str, Any]) -> None:
    validate_lock_payload(lock)
    if _run(("git", "cat-file", "-t", SOURCE_COMMIT), cwd=repo_root) != "commit":
        raise RuntimePackagePlaneError("runtime source commit is unavailable")
    _run(("git", "merge-base", "--is-ancestor", SOURCE_COMMIT, "HEAD"), cwd=repo_root)

    changed = _run(
        ("git", "diff", "--name-only", SOURCE_COMMIT),
        cwd=repo_root,
    ).splitlines()
    untracked = _run(
        (
            "git",
            "ls-files",
            "--others",
            "--exclude-standard",
        ),
        cwd=repo_root,
    ).splitlines()
    observed_recipe_delta = set(changed) | set(untracked)
    expected_recipe_delta = set(ALLOWED_RECIPE_DELTA)
    if observed_recipe_delta != expected_recipe_delta:
        raise RuntimePackagePlaneError(
            "package recipe delta differs from authority "
            f"(missing={sorted(expected_recipe_delta - observed_recipe_delta)}, "
            f"extra={sorted(observed_recipe_delta - expected_recipe_delta)})"
        )

    for row in lock["build_authority_files"]:
        authority_path = repo_root / row["path"]
        _require_regular_file(authority_path, f"build authority {row['path']}")
        if _sha256(authority_path) != row["sha256"]:
            raise RuntimePackagePlaneError(
                f"build authority bytes drifted: {row['path']}"
            )

    for spec in PACKAGE_SPECS:
        project_path = repo_root / spec.project
        if _sha256(project_path) != spec.project_sha256:
            raise RuntimePackagePlaneError(
                f"{spec.project} bytes differ from the immutable package recipe"
            )
        try:
            root = ET.parse(project_path).getroot()
        except (OSError, ET.ParseError) as exc:
            raise RuntimePackagePlaneError(f"cannot parse {spec.project}: {exc}") from exc
        package_ids = _property_values(root, "PackageId")
        if package_ids != [spec.package_id]:
            raise RuntimePackagePlaneError(f"{spec.project} does not own exact PackageId {spec.package_id}")
        versions = _property_values(root, "Version")
        if versions != ["0.0.0-local"]:
            raise RuntimePackagePlaneError(f"{spec.project} must retain the local source version")
        licenses = _property_values(root, "PackageLicenseExpression")
        if licenses != [LICENSE_EXPRESSION]:
            raise RuntimePackagePlaneError(f"{spec.project} lacks exact package license authority")
        descriptions = _property_values(root, "Description")
        if len(descriptions) != 1:
            raise RuntimePackagePlaneError(f"{spec.project} must declare one package description")
        assembly_names = _property_values(root, "AssemblyName")
        observed_assembly = (assembly_names[0] if assembly_names else project_path.stem) + ".dll"
        if observed_assembly != spec.assembly:
            raise RuntimePackagePlaneError(f"{spec.project} assembly identity drifted")
        dependencies = _project_dependencies(repo_root, spec, root)
        if dependencies != set(spec.dependencies):
            raise RuntimePackagePlaneError(
                f"{spec.project} dependency drift: expected {sorted(spec.dependencies)}, "
                f"observed {sorted(dependencies)}"
            )
        runtime_outputs = [
            element
            for element in root.iter()
            if _local_name(element.tag) == "BuildOutputInPackage"
        ]
        runtime_targets = [
            element
            for element in root.iter()
            if _local_name(element.tag) == "Target"
            and "RuntimeAssembl" in (element.attrib.get("Name") or "")
        ]
        if spec.package_id == "Chummer.Engine.GmCharacterEdits":
            if _property_values(root, "TargetsForTfmSpecificBuildOutput") != [
                "$(TargetsForTfmSpecificBuildOutput);IncludeCoreGmRuntimeAssemblies"
            ]:
                raise RuntimePackagePlaneError(
                    "GM runtime package target registration is not exact"
                )
            if len(runtime_outputs) != 1 or len(runtime_targets) != 1:
                raise RuntimePackagePlaneError(
                    "GM runtime package bundling authority is not exact"
                )
        elif runtime_outputs or runtime_targets:
            raise RuntimePackagePlaneError(
                f"{spec.package_id} contains an unauthorized runtime bundling target"
            )


def _canonicalizer(repo_root: Path, lock: dict[str, Any]):
    path = repo_root / "scripts/ai/bootstrap-owner-contracts-feed.py"
    expected = next(
        row["sha256"]
        for row in lock["build_authority_files"]
        if row["path"] == "scripts/ai/bootstrap-owner-contracts-feed.py"
    )
    _require_regular_file(path, "owner package canonicalizer")
    source = path.read_bytes()
    if hashlib.sha256(source).hexdigest() != expected:
        raise RuntimePackagePlaneError("owner package canonicalizer bytes drifted")
    module_name = "core_owner_package_canonicalizer"
    module = types.ModuleType(module_name)
    module.__file__ = str(path)
    sys.modules[module_name] = module
    exec(compile(source, str(path), "exec"), module.__dict__)
    return module


def _find_package(feed: Path, package_id: str) -> Path:
    expected = f"{package_id}.{PACKAGE_VERSION}.nupkg".casefold()
    matches = [path for path in feed.glob("*.nupkg") if path.name.casefold() == expected]
    if len(matches) != 1:
        raise RuntimePackagePlaneError(f"feed must contain exactly one {package_id} package")
    return matches[0]


def _nuspec_root(archive: zipfile.ZipFile, package_id: str) -> ET.Element:
    names = [name for name in archive.namelist() if name.casefold().endswith(".nuspec")]
    if len(names) != 1:
        raise RuntimePackagePlaneError(f"{package_id} must contain exactly one nuspec")
    try:
        return ET.fromstring(archive.read(names[0]))
    except ET.ParseError as exc:
        raise RuntimePackagePlaneError(f"{package_id} nuspec is invalid") from exc


def _text(root: ET.Element, name: str) -> str:
    values = [
        (element.text or "").strip()
        for element in root.iter()
        if _local_name(element.tag) == name
    ]
    return values[0] if len(values) == 1 else ""


def _repository(root: ET.Element) -> tuple[str, str]:
    rows = [element for element in root.iter() if _local_name(element.tag) == "repository"]
    if len(rows) != 1:
        return "", ""
    return (rows[0].attrib.get("url") or "").strip(), (
        rows[0].attrib.get("commit") or ""
    ).strip()


def _license(root: ET.Element) -> tuple[str, str]:
    rows = [element for element in root.iter() if _local_name(element.tag) == "license"]
    if len(rows) != 1:
        return "", ""
    return (rows[0].attrib.get("type") or "").strip(), (rows[0].text or "").strip()


def _dependency_group(root: ET.Element) -> tuple[str | None, dict[str, str]]:
    containers = [
        element for element in root.iter() if _local_name(element.tag) == "dependencies"
    ]
    if not containers:
        return None, {}
    if len(containers) != 1:
        raise RuntimePackagePlaneError("nuspec must contain at most one dependencies container")
    groups = [
        element for element in containers[0] if _local_name(element.tag) == "group"
    ]
    if len(groups) != 1 or len(list(containers[0])) != 1:
        raise RuntimePackagePlaneError("nuspec must contain one exact dependency group")
    group = groups[0]
    if set(group.attrib) != {"targetFramework"}:
        raise RuntimePackagePlaneError("nuspec dependency group metadata is invalid")
    result: dict[str, str] = {}
    for element in group:
        if _local_name(element.tag) != "dependency":
            raise RuntimePackagePlaneError("nuspec dependency group contains a foreign element")
        if set(element.attrib) != {"id", "version", "exclude"}:
            raise RuntimePackagePlaneError("nuspec dependency metadata is not exact")
        if (element.attrib.get("exclude") or "").strip() != "Build,Analyzers":
            raise RuntimePackagePlaneError("nuspec dependency exclusion metadata is not exact")
        package_id = (element.attrib.get("id") or "").strip()
        if package_id in result:
            raise RuntimePackagePlaneError(f"duplicate dependency in nuspec: {package_id}")
        result[package_id] = (element.attrib.get("version") or "").strip()
    return (group.attrib.get("targetFramework") or "").strip(), result


def inspect_packages(feed: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    assembly_owners: dict[str, str] = {}
    for spec in PACKAGE_SPECS:
        path = _find_package(feed, spec.package_id)
        try:
            with zipfile.ZipFile(path) as archive:
                names = archive.namelist()
                if len(names) != len(set(names)) or names != sorted(
                    names, key=lambda value: (value.casefold(), value)
                ):
                    raise RuntimePackagePlaneError(f"{spec.package_id} archive is not canonical")
                if any(
                    PurePosixPath(name).is_absolute()
                    or "\\" in name
                    or any(part in {"", ".", ".."} for part in PurePosixPath(name).parts)
                    for name in names
                ):
                    raise RuntimePackagePlaneError(f"{spec.package_id} archive path is unsafe")
                root = _nuspec_root(archive, spec.package_id)
                dlls = [
                    name for name in names
                    if name.startswith("lib/net10.0/") and name.casefold().endswith(".dll")
                ]
        except (OSError, zipfile.BadZipFile) as exc:
            raise RuntimePackagePlaneError(f"cannot inspect {path.name}: {exc}") from exc
        expected_dlls = (
            list(GM_RUNTIME_ASSEMBLY_PATHS)
            if spec.package_id == "Chummer.Engine.GmCharacterEdits"
            else [f"lib/net10.0/{spec.assembly}"]
        )
        if dlls != expected_dlls:
            raise RuntimePackagePlaneError(
                f"{spec.package_id} runtime assembly set differs from authority; "
                f"expected {expected_dlls}, observed {dlls}"
            )
        allowed_entries = {
            "_rels/.rels",
            "[Content_Types].xml",
            f"{spec.package_id}.nuspec",
            *expected_dlls,
        }
        if spec.package_id == "Chummer.Engine.Contracts":
            allowed_entries.add("README.md")
        unexpected_entries = {
            name
            for name in names
            if name not in allowed_entries and CORE_PROPERTIES_RE.fullmatch(name) is None
        }
        if unexpected_entries:
            raise RuntimePackagePlaneError(
                f"{spec.package_id} contains unapproved package payloads: "
                + ", ".join(sorted(unexpected_entries))
            )
        owner = assembly_owners.setdefault(spec.assembly.casefold(), spec.package_id)
        if owner != spec.package_id:
            raise RuntimePackagePlaneError(
                f"assembly {spec.assembly} is duplicated by {owner} and {spec.package_id}"
            )
        if _text(root, "id") != spec.package_id or _text(root, "version") != PACKAGE_VERSION:
            raise RuntimePackagePlaneError(f"{spec.package_id} nuspec identity is invalid")
        if _repository(root) != (SOURCE_REPOSITORY, SOURCE_COMMIT):
            raise RuntimePackagePlaneError(f"{spec.package_id} source provenance is invalid")
        if _license(root) != ("expression", LICENSE_EXPRESSION):
            raise RuntimePackagePlaneError(f"{spec.package_id} license is invalid")
        expected_dependencies = {
            dependency: _dependency_version(dependency) for dependency in spec.dependencies
        }
        observed_framework, observed_dependencies = _dependency_group(root)
        expected_framework = TARGET_FRAMEWORK
        if (
            observed_framework != expected_framework
            or observed_dependencies != expected_dependencies
        ):
            raise RuntimePackagePlaneError(f"{spec.package_id} nuspec dependency closure drifted")
        rows.append(
            {
                "id": spec.package_id,
                "version": PACKAGE_VERSION,
                "repository": SOURCE_REPOSITORY,
                "source_commit": SOURCE_COMMIT,
                "project": spec.project,
                "assembly": spec.assembly,
                "target_framework": TARGET_FRAMEWORK,
                "dependencies": [
                    {"id": dependency, "version": _dependency_version(dependency)}
                    for dependency in spec.dependencies
                ],
                "file_name": path.name,
                "sha256": _sha256(path),
                "size_bytes": path.stat().st_size,
            }
        )
    return rows


def inventory_payload(repo_root: Path, lock_path: Path, feed: Path) -> dict[str, Any]:
    recipe_commit = _run(("git", "rev-parse", "HEAD"), cwd=repo_root)
    if not COMMIT_RE.fullmatch(recipe_commit):
        raise RuntimePackagePlaneError("package recipe commit is invalid")
    return {
        "contract": INVENTORY_CONTRACT,
        "package_plane_lock_sha256": _sha256(lock_path),
        "package_version": PACKAGE_VERSION,
        "runtime_source_commit": SOURCE_COMMIT,
        "package_recipe_commit": recipe_commit,
        "packages": inspect_packages(feed),
    }


def _atomic_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", closefd=True) as stream:
            json.dump(payload, stream, indent=2, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        temporary.replace(path)
    finally:
        if temporary.exists():
            temporary.unlink()


def canonicalize_and_write_inventory(
    repo_root: Path,
    lock_path: Path,
    lock: dict[str, Any],
    feed: Path,
) -> str:
    module = _canonicalizer(repo_root, lock)
    for spec in PACKAGE_SPECS:
        module.canonicalize_nupkg(_find_package(feed, spec.package_id))
    payload = inventory_payload(repo_root, lock_path, feed)
    inventory_path = feed / INVENTORY_NAME
    _atomic_json(inventory_path, payload)
    return _sha256(inventory_path)


def validate_inventory(repo_root: Path, lock_path: Path, feed: Path) -> str:
    inventory_path = feed / INVENTORY_NAME
    try:
        raw = inventory_path.read_bytes()
        observed = _strict_json_loads(raw, "runtime package inventory")
    except (OSError, RuntimePackagePlaneError) as exc:
        raise RuntimePackagePlaneError(f"runtime package inventory is unavailable: {exc}") from exc
    expected = inventory_payload(repo_root, lock_path, feed)
    if observed != expected:
        raise RuntimePackagePlaneError("runtime package inventory is stale or altered")
    return hashlib.sha256(raw).hexdigest()


def _require_regular_file(path: Path, label: str) -> None:
    try:
        mode = path.lstat().st_mode
    except OSError as exc:
        raise RuntimePackagePlaneError(f"{label} is unavailable: {exc}") from exc
    if not stat.S_ISREG(mode) or path.is_symlink():
        raise RuntimePackagePlaneError(f"{label} must be one regular non-symlink file")


def _open_absolute_directory(path: Path) -> int:
    _require_canonical_absolute_path(path, "directory authority path")
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = -1
    try:
        descriptor = os.open(path.anchor, flags)
        for part in path.parts[1:]:
            next_descriptor = os.open(part, flags, dir_fd=descriptor)
            os.close(descriptor)
            descriptor = next_descriptor
        if not stat.S_ISDIR(os.fstat(descriptor).st_mode):
            raise RuntimePackagePlaneError("directory authority is not a directory")
        return descriptor
    except (OSError, RuntimePackagePlaneError) as exc:
        if descriptor >= 0:
            os.close(descriptor)
        if isinstance(exc, RuntimePackagePlaneError):
            raise
        raise RuntimePackagePlaneError(
            f"directory authority contains a missing, swapped, or symlink component: {path}"
        ) from exc


def _copy_bound_file_at(
    source: Path,
    directory_descriptor: int,
    target_name: str,
    *,
    expected_sha256: str,
    expected_size: int,
) -> None:
    _require_regular_file(source, source.name)
    if source.stat().st_size != expected_size or _sha256(source) != expected_sha256:
        raise RuntimePackagePlaneError(f"source bytes drifted before export: {source.name}")
    try:
        descriptor = os.open(
            target_name,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            0o644,
            dir_fd=directory_descriptor,
        )
    except OSError as exc:
        raise RuntimePackagePlaneError(f"cannot create export file {target_name}: {exc}") from exc
    with os.fdopen(descriptor, "wb", closefd=True) as output, source.open("rb") as input_stream:
        digest = hashlib.sha256()
        size = 0
        for chunk in iter(lambda: input_stream.read(1024 * 1024), b""):
            output.write(chunk)
            digest.update(chunk)
            size += len(chunk)
        output.flush()
        os.fsync(output.fileno())
        if (
            size != expected_size
            or digest.hexdigest() != expected_sha256
            or os.fstat(output.fileno()).st_size != expected_size
        ):
            raise RuntimePackagePlaneError(
                f"exported bytes do not match authority: {target_name}"
            )


def _candidate_inventory_digest(
    feed: Path,
    path: Path,
    runtime_inventory: dict[str, Any],
    *,
    gm_runtime: bool,
) -> str:
    label = "candidate GM runtime inventory" if gm_runtime else "candidate Engine inventory"
    _require_regular_file(path, label)
    try:
        raw = path.read_bytes()
    except OSError as exc:
        raise RuntimePackagePlaneError(f"{label} is unavailable: {exc}") from exc
    payload = _strict_json_loads(raw, label)
    spec = PACKAGE_SPECS[-1] if gm_runtime else PACKAGE_SPECS[0]
    package_path = _find_package(feed, spec.package_id)
    _require_regular_file(package_path, f"{label} package")
    package_size = package_path.stat().st_size
    if package_size <= 0:
        raise RuntimePackagePlaneError(f"{label} package is empty")
    expected_package: dict[str, Any] = {
        "id": spec.package_id,
        "version": PACKAGE_VERSION,
        "repository": SOURCE_REPOSITORY,
        "commit": SOURCE_COMMIT,
        "project": spec.project,
        "file_name": package_path.name,
        "sha256": _sha256(package_path),
        "size_bytes": package_size,
    }
    if gm_runtime:
        expected_package["runtime_assemblies"] = list(GM_RUNTIME_ASSEMBLY_PATHS)
    expected = {
        "contract": (
            "chummer-core.candidate-gm-edit-runtime-package-inventory/v2"
            if gm_runtime
            else "chummer-core.candidate-engine-contract-package-inventory/v2"
        ),
        "role": "current_core_candidate",
        "runtime_source_commit": SOURCE_COMMIT,
        "package_recipe_commit": runtime_inventory["package_recipe_commit"],
        "package": expected_package,
    }
    if payload != expected:
        raise RuntimePackagePlaneError(f"{label} schema or authority differs")
    matching_runtime_rows = [
        row for row in runtime_inventory.get("packages", [])
        if isinstance(row, dict) and row.get("id") == spec.package_id
    ]
    if len(matching_runtime_rows) != 1:
        raise RuntimePackagePlaneError(f"{label} lacks one matching runtime row")
    runtime_row = matching_runtime_rows[0]
    expected_runtime_binding = {
        "version": expected_package["version"],
        "repository": expected_package["repository"],
        "source_commit": expected_package["commit"],
        "project": expected_package["project"],
        "file_name": expected_package["file_name"],
        "sha256": expected_package["sha256"],
        "size_bytes": expected_package["size_bytes"],
    }
    if any(runtime_row.get(key) != value for key, value in expected_runtime_binding.items()):
        raise RuntimePackagePlaneError(f"{label} differs from unified runtime authority")
    return hashlib.sha256(raw).hexdigest()


def _validate_receipt(
    repo_root: Path,
    feed: Path,
    receipt_path: Path,
    inventory: dict[str, Any],
    inventory_sha256: str,
) -> tuple[dict[str, Any], str, int]:
    _require_regular_file(receipt_path, "no-siblings receipt")
    try:
        receipt_bytes = receipt_path.read_bytes()
        receipt = _strict_json_loads(receipt_bytes, "no-siblings receipt")
    except (OSError, RuntimePackagePlaneError) as exc:
        raise RuntimePackagePlaneError(f"no-siblings receipt is invalid: {exc}") from exc
    expected_keys = {
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
    if (
        not isinstance(receipt, dict)
        or set(receipt) != expected_keys
        or receipt.get("contract") != "chummer-core.no-siblings-package-plane/v3"
    ):
        raise RuntimePackagePlaneError("no-siblings v3 receipt is required for export")
    try:
        datetime.strptime(receipt["generated_at_utc"], "%Y-%m-%dT%H:%M:%SZ")
    except (TypeError, ValueError) as exc:
        raise RuntimePackagePlaneError("no-siblings receipt timestamp is invalid") from exc

    owner_lock_path = repo_root / "eng/package-plane.lock.json"
    _require_regular_file(owner_lock_path, "owner package-plane lock")
    owner_lock_bytes = owner_lock_path.read_bytes()
    owner_lock = _strict_json_loads(owner_lock_bytes, "owner package-plane lock")
    owner_inventory_path = feed / OWNER_INVENTORY_NAME
    _require_regular_file(owner_inventory_path, "owner package inventory")
    owner_inventory_bytes = owner_inventory_path.read_bytes()
    owner_inventory = _strict_json_loads(owner_inventory_bytes, "owner package inventory")
    if not isinstance(owner_lock, dict) or set(owner_lock) != {
        "contract",
        "dotnet_sdk",
        "package_version",
        "approved_remote_source",
        "packages",
    }:
        raise RuntimePackagePlaneError("owner package-plane lock shape is invalid")
    if not isinstance(owner_inventory, dict) or set(owner_inventory) != {
        "contract",
        "package_plane_lock_sha256",
        "package_version",
        "packages",
    }:
        raise RuntimePackagePlaneError("owner package inventory shape is invalid")
    owner_lock_sha256 = hashlib.sha256(owner_lock_bytes).hexdigest()
    owner_inventory_sha256 = hashlib.sha256(owner_inventory_bytes).hexdigest()
    if (
        owner_inventory.get("contract") != "chummer-core.owner-contract-package-inventory/v1"
        or owner_inventory.get("package_plane_lock_sha256") != owner_lock_sha256
        or owner_inventory.get("package_version") != owner_lock.get("package_version")
    ):
        raise RuntimePackagePlaneError("owner package inventory authority is stale")
    owner_specs = owner_lock.get("packages")
    owner_rows = owner_inventory.get("packages")
    if (
        not isinstance(owner_specs, list)
        or not isinstance(owner_rows, list)
        or len(owner_specs) != 4
        or len(owner_rows) != 4
    ):
        raise RuntimePackagePlaneError("owner package authority must contain exactly four rows")
    expected_owner_row_keys = {
        "id", "version", "repository", "commit", "project", "file_name", "sha256", "size_bytes"
    }
    for spec, row in zip(owner_specs, owner_rows, strict=True):
        if not isinstance(spec, dict) or not isinstance(row, dict) or set(row) != expected_owner_row_keys:
            raise RuntimePackagePlaneError("owner package inventory row shape is invalid")
        expected_metadata = {
            "id": spec.get("id"),
            "version": owner_lock["package_version"],
            "repository": spec.get("repository"),
            "commit": spec.get("commit"),
            "project": spec.get("project"),
            "file_name": f"{spec.get('id')}.{owner_lock['package_version']}.nupkg",
        }
        if any(row.get(key) != value for key, value in expected_metadata.items()):
            raise RuntimePackagePlaneError("owner package inventory metadata drifted")
        matches = [
            path for path in feed.glob("*.nupkg")
            if path.name.casefold() == row["file_name"].casefold()
        ]
        if len(matches) != 1 or matches[0].name != row["file_name"]:
            raise RuntimePackagePlaneError("owner package file authority is ambiguous")
        _require_regular_file(matches[0], "owner package")
        if (
            not isinstance(row.get("size_bytes"), int)
            or isinstance(row.get("size_bytes"), bool)
            or row["size_bytes"] <= 0
            or matches[0].stat().st_size != row["size_bytes"]
            or _sha256(matches[0]) != row.get("sha256")
        ):
            raise RuntimePackagePlaneError("owner package bytes differ from inventory")

    candidate_digests = {
        "candidate_package_inventory_sha256": _candidate_inventory_digest(
            feed,
            feed / CANDIDATE_ENGINE_INVENTORY_NAME,
            inventory,
            gm_runtime=False,
        ),
        "candidate_runtime_package_inventory_sha256": _candidate_inventory_digest(
            feed,
            feed / CANDIDATE_GM_INVENTORY_NAME,
            inventory,
            gm_runtime=True,
        ),
    }
    for receipt_key, digest in candidate_digests.items():
        if receipt.get(receipt_key) != digest:
            raise RuntimePackagePlaneError(f"no-siblings receipt field is stale: {receipt_key}")
    required = {
        "status": "pass",
        "core_commit": inventory["package_recipe_commit"],
        "package_plane_lock_sha256": owner_lock_sha256,
        "package_inventory_sha256": owner_inventory_sha256,
        "runtime_package_inventory_sha256": inventory_sha256,
        "runtime_package_plane_lock_sha256": inventory["package_plane_lock_sha256"],
        "runtime_source_commit": inventory["runtime_source_commit"],
        "package_recipe_commit": inventory["package_recipe_commit"],
        "candidate_package_version": inventory["package_version"],
        "package_version": owner_lock["package_version"],
        "no_sibling_directories": True,
        "isolated_package_cache": True,
        "package_source_mapping": {
            "Chummer.*": "locked-owner-contracts",
            "other": "https://api.nuget.org/v3/index.json",
        },
    }
    for key, expected in required.items():
        if receipt.get(key) != expected:
            raise RuntimePackagePlaneError(f"no-siblings receipt field is stale: {key}")
    expected_runtime_rows = [
        dict(row, role="current_core_runtime_candidate") for row in inventory["packages"]
    ]
    expected_locked_rows = [
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
    expected_resolved_rows = [
        *expected_runtime_rows,
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
    if receipt.get("locked_packages") != expected_locked_rows:
        raise RuntimePackagePlaneError("no-siblings receipt locked package rows are invalid")
    if receipt.get("resolved_owner_contracts") != expected_resolved_rows:
        raise RuntimePackagePlaneError("no-siblings receipt resolved package rows are invalid")
    for key in (
        "normal_local_engine_dependency_graph",
        "build",
        "package_plane_runtime_test",
        "local_owner_isolation_tests",
        "candidate_engine_contract_pack",
        "candidate_gm_edit_runtime_pack",
        "candidate_gm_edit_runtime_consumer",
        "eight_package_runtime_plane",
    ):
        if receipt.get(key) != "pass":
            raise RuntimePackagePlaneError(f"no-siblings receipt verifier claim is invalid: {key}")
    return receipt, hashlib.sha256(receipt_bytes).hexdigest(), len(receipt_bytes)


def _validate_export_layout_at(
    export_descriptor: int,
    packages_descriptor: int,
    expected_package_names: set[str],
) -> None:
    expected_root_names = {
        "packages",
        INVENTORY_NAME,
        "runtime-package-plane.lock.json",
        "no-siblings.v3.receipt.json",
    }
    root_names = set(os.listdir(export_descriptor))
    package_names = set(os.listdir(packages_descriptor))
    if root_names != expected_root_names or package_names != expected_package_names:
        raise RuntimePackagePlaneError("runtime bundle members differ from exact authority")
    for name in expected_root_names - {"packages"}:
        mode = os.stat(name, dir_fd=export_descriptor, follow_symlinks=False).st_mode
        if not stat.S_ISREG(mode):
            raise RuntimePackagePlaneError("runtime bundle root member is not regular")
    packages_mode = os.stat(
        "packages", dir_fd=export_descriptor, follow_symlinks=False
    ).st_mode
    if not stat.S_ISDIR(packages_mode):
        raise RuntimePackagePlaneError("runtime bundle packages member is not a directory")
    for name in expected_package_names:
        mode = os.stat(name, dir_fd=packages_descriptor, follow_symlinks=False).st_mode
        if not stat.S_ISREG(mode):
            raise RuntimePackagePlaneError("runtime package bundle member is not regular")
    all_names = [*expected_root_names, *expected_package_names]
    if len(all_names) != len({name.casefold() for name in all_names}):
        raise RuntimePackagePlaneError("runtime bundle contains case-colliding members")


def _stable_file_identity(metadata: os.stat_result) -> tuple[int, ...]:
    return (
        metadata.st_dev,
        metadata.st_ino,
        metadata.st_mode,
        metadata.st_nlink,
        metadata.st_size,
        metadata.st_mtime_ns,
        metadata.st_ctime_ns,
    )


def _verify_stable_bound_file_at(
    directory_descriptor: int,
    name: str,
    *,
    expected_sha256: str,
    expected_size: int,
) -> None:
    if (
        not name
        or "/" in name
        or "\\" in name
        or name in {".", ".."}
        or not SHA256_RE.fullmatch(expected_sha256)
        or not isinstance(expected_size, int)
        or isinstance(expected_size, bool)
        or expected_size <= 0
        or expected_size > EXPORT_MEMBER_MAX_BYTES
    ):
        raise RuntimePackagePlaneError(f"exported file authority is invalid: {name!r}")
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
    descriptor = -1
    try:
        path_before = os.stat(name, dir_fd=directory_descriptor, follow_symlinks=False)
        descriptor = os.open(name, flags, dir_fd=directory_descriptor)
        opened_before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(path_before.st_mode)
            or not stat.S_ISREG(opened_before.st_mode)
            or path_before.st_nlink != 1
            or opened_before.st_nlink != 1
            or _stable_file_identity(path_before) != _stable_file_identity(opened_before)
            or opened_before.st_size != expected_size
        ):
            raise RuntimePackagePlaneError(
                f"exported file identity differs from authority: {name}"
            )
        digest = hashlib.sha256()
        size = 0
        while size <= expected_size:
            chunk = os.read(
                descriptor,
                min(1024 * 1024, expected_size + 1 - size),
            )
            if not chunk:
                break
            digest.update(chunk)
            size += len(chunk)
        opened_after = os.fstat(descriptor)
        path_after = os.stat(name, dir_fd=directory_descriptor, follow_symlinks=False)
        if (
            _stable_file_identity(opened_after) != _stable_file_identity(opened_before)
            or _stable_file_identity(path_after) != _stable_file_identity(opened_before)
            or size != expected_size
            or digest.hexdigest() != expected_sha256
        ):
            raise RuntimePackagePlaneError(
                f"exported file stable byte authority differs: {name}"
            )
    except OSError as exc:
        raise RuntimePackagePlaneError(
            f"cannot verify exported file authority {name}: {exc}"
        ) from exc
    finally:
        if descriptor >= 0:
            os.close(descriptor)


def export_bundle(
    repo_root: Path,
    lock_path: Path,
    feed: Path,
    receipt_path: Path,
    export_dir: Path,
    *,
    _after_parent_open: Callable[[], None] | None = None,
    _before_final_reopen: Callable[[], None] | None = None,
) -> tuple[str, str]:
    _require_canonical_absolute_path(export_dir, "runtime bundle export path")
    if export_dir.name in {"", ".", ".."}:
        raise RuntimePackagePlaneError("runtime bundle export name is invalid")
    inventory_sha256 = validate_inventory(repo_root, lock_path, feed)
    inventory_path = feed / INVENTORY_NAME
    inventory = _strict_json_loads(
        inventory_path.read_bytes(),
        "runtime package inventory",
    )
    _, receipt_sha256, receipt_size = _validate_receipt(
        repo_root,
        feed,
        receipt_path,
        inventory,
        inventory_sha256,
    )
    package_names = [row["file_name"] for row in inventory["packages"]]
    if len(package_names) != len(set(name.casefold() for name in package_names)):
        raise RuntimePackagePlaneError("runtime inventory contains case-colliding package names")
    inventory_size = inventory_path.stat().st_size
    lock_size = lock_path.stat().st_size

    parent_descriptor = _open_absolute_directory(export_dir.parent)
    export_descriptor = -1
    packages_descriptor = -1
    reopened_parent = -1
    reopened_export = -1
    reopened_packages = -1
    directory_flags = (
        os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
    )
    try:
        if _after_parent_open is not None:
            _after_parent_open()
        try:
            os.stat(export_dir.name, dir_fd=parent_descriptor, follow_symlinks=False)
        except FileNotFoundError:
            pass
        else:
            raise RuntimePackagePlaneError(
                "runtime bundle export path must not already exist"
            )
        try:
            os.mkdir(export_dir.name, mode=0o755, dir_fd=parent_descriptor)
            export_descriptor = os.open(
                export_dir.name, directory_flags, dir_fd=parent_descriptor
            )
            os.mkdir("packages", mode=0o755, dir_fd=export_descriptor)
            packages_descriptor = os.open(
                "packages", directory_flags, dir_fd=export_descriptor
            )
        except OSError as exc:
            raise RuntimePackagePlaneError(
                f"cannot create runtime bundle export path: {exc}"
            ) from exc
        for row in inventory["packages"]:
            _copy_bound_file_at(
                _find_package(feed, row["id"]),
                packages_descriptor,
                row["file_name"],
                expected_sha256=row["sha256"],
                expected_size=row["size_bytes"],
            )
        _copy_bound_file_at(
            inventory_path,
            export_descriptor,
            INVENTORY_NAME,
            expected_sha256=inventory_sha256,
            expected_size=inventory_size,
        )
        _copy_bound_file_at(
            lock_path,
            export_descriptor,
            "runtime-package-plane.lock.json",
            expected_sha256=inventory["package_plane_lock_sha256"],
            expected_size=lock_size,
        )
        _copy_bound_file_at(
            receipt_path,
            export_descriptor,
            "no-siblings.v3.receipt.json",
            expected_sha256=receipt_sha256,
            expected_size=receipt_size,
        )
        _validate_export_layout_at(
            export_descriptor,
            packages_descriptor,
            set(package_names),
        )
        os.fsync(packages_descriptor)
        os.fsync(export_descriptor)
        os.fsync(parent_descriptor)
        if _before_final_reopen is not None:
            _before_final_reopen()
        reopened_parent = _open_absolute_directory(export_dir.parent)
        if (
            os.fstat(reopened_parent).st_dev,
            os.fstat(reopened_parent).st_ino,
        ) != (
            os.fstat(parent_descriptor).st_dev,
            os.fstat(parent_descriptor).st_ino,
        ):
            raise RuntimePackagePlaneError(
                "runtime bundle parent changed during export"
            )
        try:
            reopened_export = os.open(
                export_dir.name,
                directory_flags,
                dir_fd=reopened_parent,
            )
        except OSError as exc:
            raise RuntimePackagePlaneError(
                "runtime bundle export directory changed during export"
            ) from exc
        if (
            os.fstat(reopened_export).st_dev,
            os.fstat(reopened_export).st_ino,
        ) != (
            os.fstat(export_descriptor).st_dev,
            os.fstat(export_descriptor).st_ino,
        ):
            raise RuntimePackagePlaneError(
                "runtime bundle export directory changed during export"
            )
        try:
            reopened_packages = os.open(
                "packages",
                directory_flags,
                dir_fd=reopened_export,
            )
        except OSError as exc:
            raise RuntimePackagePlaneError(
                "runtime bundle packages directory changed during export"
            ) from exc
        if (
            os.fstat(reopened_packages).st_dev,
            os.fstat(reopened_packages).st_ino,
        ) != (
            os.fstat(packages_descriptor).st_dev,
            os.fstat(packages_descriptor).st_ino,
        ):
            raise RuntimePackagePlaneError(
                "runtime bundle packages directory changed during export"
            )
        _validate_export_layout_at(
            reopened_export,
            reopened_packages,
            set(package_names),
        )
        for row in inventory["packages"]:
            _verify_stable_bound_file_at(
                reopened_packages,
                row["file_name"],
                expected_sha256=row["sha256"],
                expected_size=row["size_bytes"],
            )
        for name, expected_sha256, expected_size in (
            (INVENTORY_NAME, inventory_sha256, inventory_size),
            (
                "runtime-package-plane.lock.json",
                inventory["package_plane_lock_sha256"],
                lock_size,
            ),
            ("no-siblings.v3.receipt.json", receipt_sha256, receipt_size),
        ):
            _verify_stable_bound_file_at(
                reopened_export,
                name,
                expected_sha256=expected_sha256,
                expected_size=expected_size,
            )
    finally:
        if reopened_packages >= 0:
            os.close(reopened_packages)
        if reopened_export >= 0:
            os.close(reopened_export)
        if reopened_parent >= 0:
            os.close(reopened_parent)
        if packages_descriptor >= 0:
            os.close(packages_descriptor)
        if export_descriptor >= 0:
            os.close(export_descriptor)
        os.close(parent_descriptor)
    return inventory_sha256, receipt_sha256


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--feed", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--export-dir", type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--destination", type=Path)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument("--canonicalize-and-write-inventory", action="store_true")
    actions.add_argument("--validate-feed", action="store_true")
    actions.add_argument("--print-pack-rows", action="store_true")
    actions.add_argument("--export-bundle", action="store_true")
    actions.add_argument("--extract-sdk-archive", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    lock_path = (args.lock or repo_root / "eng/runtime-package-plane.lock.json").resolve()
    lock = load_lock(lock_path)
    validate_repository(repo_root, lock)
    if args.print_pack_rows:
        for spec in PACKAGE_SPECS:
            print(f"{spec.package_id}\t{spec.project}")
        return 0
    if args.extract_sdk_archive:
        if args.archive is None or args.destination is None:
            raise RuntimePackagePlaneError(
                "--archive and --destination are required for SDK extraction"
            )
        _extract_digest_bound_tar_gz(
            args.archive.absolute(),
            args.destination.absolute(),
            SDK_ARCHIVE_SHA512,
        )
        print(
            f"runtime-package-sdk: ok ({SDK_VERSION}; {SDK_RID}; {SDK_ARCHIVE_SHA512})"
        )
        return 0
    if args.export_bundle:
        if args.feed is None or args.receipt is None or args.export_dir is None:
            raise RuntimePackagePlaneError(
                "--feed, --receipt, and --export-dir are required for export"
            )
        inventory_digest, receipt_digest = export_bundle(
            repo_root,
            lock_path,
            args.feed.resolve(),
            args.receipt.resolve(),
            args.export_dir,
        )
        print(
            "runtime-package-bundle: ok "
            f"({len(PACKAGE_SPECS)} packages; inventory {inventory_digest}; "
            f"receipt {receipt_digest})"
        )
        return 0
    if args.canonicalize_and_write_inventory or args.validate_feed:
        if args.feed is None:
            raise RuntimePackagePlaneError("--feed is required for feed operations")
        feed = args.feed.resolve()
        digest = (
            canonicalize_and_write_inventory(repo_root, lock_path, lock, feed)
            if args.canonicalize_and_write_inventory
            else validate_inventory(repo_root, lock_path, feed)
        )
        print(
            f"runtime-package-plane: ok ({len(PACKAGE_SPECS)} packages; inventory {digest})"
        )
        return 0
    print(f"runtime-package-plane-lock: ok ({len(PACKAGE_SPECS)} packages)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimePackagePlaneError as exc:
        print(f"runtime-package-plane: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
