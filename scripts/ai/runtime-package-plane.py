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
import os
import re
import stat
import subprocess
import sys
import tempfile
import types
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


LOCK_CONTRACT = "chummer-core.runtime-package-plane-lock/v1"
INVENTORY_CONTRACT = "chummer-core.runtime-package-inventory/v1"
INVENTORY_NAME = "chummer-core-runtime-packages.inventory.json"
PACKAGE_VERSION = "0.0.0-packageplane.candidate.shabc08228d3ce0"
SOURCE_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-core.git"
SOURCE_COMMIT = "bc08228d3ce06410ca97ada63a5af41a2eaa91bf"
SDK_VERSION = "10.0.103"
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
        "cf5fc7f7f7d25c2ab20ba7719f3a60929cd78b205b9a43683b4a7048fcf0c19a",
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
        "7c2508ce3ee1c64338cc80df71e3a98487c5c8323db9bccb68e740f13f3db6a6",
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
ALLOWED_RECIPE_DELTA = (
    ".github/workflows/package-plane.yml",
    "Chummer.Application/Chummer.Application.csproj",
    "Chummer.Contracts/Chummer.Contracts.csproj",
    "Chummer.GmCharacterEdits/Chummer.GmCharacterEdits.csproj",
    "Chummer.Infrastructure/Chummer.Infrastructure.csproj",
    "Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj",
    "Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj",
    "Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj",
    "Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj",
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
    "scripts/ai/runtime-package-plane.py",
    "scripts/ai/verify-no-siblings-package-plane.sh",
)


class RuntimePackagePlaneError(RuntimeError):
    pass


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
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
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
    if payload["dotnet_sdk"] != SDK_VERSION:
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
        for element in root.iter():
            if _local_name(element.tag) == "BuildOutputInPackage":
                raise RuntimePackagePlaneError(f"{spec.package_id} bundles a foreign build output")
            if _local_name(element.tag) == "Target" and "RuntimeAssembl" in (
                element.attrib.get("Name") or ""
            ):
                raise RuntimePackagePlaneError(f"{spec.package_id} contains a runtime bundling target")


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
        expected_dll = f"lib/net10.0/{spec.assembly}"
        if dlls != [expected_dll]:
            raise RuntimePackagePlaneError(
                f"{spec.package_id} must own only {expected_dll}; observed {dlls}"
            )
        allowed_entries = {
            "_rels/.rels",
            "[Content_Types].xml",
            f"{spec.package_id}.nuspec",
            expected_dll,
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
        expected_framework = TARGET_FRAMEWORK if expected_dependencies else None
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
            json.dump(payload, stream, indent=2)
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
        observed = json.loads(raw)
    except (OSError, json.JSONDecodeError) as exc:
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


def _copy_bound_file(
    source: Path,
    target: Path,
    *,
    expected_sha256: str,
    expected_size: int,
) -> None:
    _require_regular_file(source, source.name)
    if source.stat().st_size != expected_size or _sha256(source) != expected_sha256:
        raise RuntimePackagePlaneError(f"source bytes drifted before export: {source.name}")
    try:
        descriptor = os.open(
            target,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            0o644,
        )
    except OSError as exc:
        raise RuntimePackagePlaneError(f"cannot create export file {target.name}: {exc}") from exc
    try:
        with os.fdopen(descriptor, "wb", closefd=True) as output, source.open("rb") as input_stream:
            for chunk in iter(lambda: input_stream.read(1024 * 1024), b""):
                output.write(chunk)
            output.flush()
            os.fsync(output.fileno())
    except BaseException:
        raise
    if target.stat().st_size != expected_size or _sha256(target) != expected_sha256:
        raise RuntimePackagePlaneError(f"exported bytes do not match authority: {target.name}")


def _validate_receipt(
    receipt_path: Path,
    inventory: dict[str, Any],
    inventory_sha256: str,
) -> tuple[dict[str, Any], str, int]:
    _require_regular_file(receipt_path, "no-siblings receipt")
    try:
        receipt_bytes = receipt_path.read_bytes()
        receipt = json.loads(receipt_bytes)
    except (OSError, json.JSONDecodeError) as exc:
        raise RuntimePackagePlaneError(f"no-siblings receipt is invalid: {exc}") from exc
    if not isinstance(receipt, dict) or receipt.get("contract") != "chummer-core.no-siblings-package-plane/v3":
        raise RuntimePackagePlaneError("no-siblings v3 receipt is required for export")
    required = {
        "status": "pass",
        "core_commit": inventory["package_recipe_commit"],
        "runtime_package_inventory_sha256": inventory_sha256,
        "runtime_package_plane_lock_sha256": inventory["package_plane_lock_sha256"],
        "runtime_source_commit": inventory["runtime_source_commit"],
        "package_recipe_commit": inventory["package_recipe_commit"],
        "candidate_package_version": inventory["package_version"],
        "eight_package_runtime_plane": "pass",
    }
    for key, expected in required.items():
        if receipt.get(key) != expected:
            raise RuntimePackagePlaneError(f"no-siblings receipt field is stale: {key}")
    expected_runtime_rows = [
        dict(row, role="current_core_runtime_candidate") for row in inventory["packages"]
    ]
    resolved = receipt.get("resolved_owner_contracts")
    if not isinstance(resolved, list):
        raise RuntimePackagePlaneError("no-siblings receipt lacks resolved package authority")
    observed_runtime_rows = [
        row
        for row in resolved
        if isinstance(row, dict) and row.get("role") == "current_core_runtime_candidate"
    ]
    if observed_runtime_rows != expected_runtime_rows:
        raise RuntimePackagePlaneError("no-siblings receipt runtime rows differ from inventory")
    return receipt, hashlib.sha256(receipt_bytes).hexdigest(), len(receipt_bytes)


def _validate_export_layout(export_dir: Path, expected_files: set[str]) -> None:
    observed_files: set[str] = set()
    observed_directories: set[str] = set()
    for root_name, directory_names, file_names in os.walk(export_dir, followlinks=False):
        root = Path(root_name)
        for directory_name in directory_names:
            path = root / directory_name
            if path.is_symlink():
                raise RuntimePackagePlaneError("runtime bundle contains a symlink directory")
            observed_directories.add(path.relative_to(export_dir).as_posix())
        for file_name in file_names:
            path = root / file_name
            _require_regular_file(path, "runtime bundle member")
            observed_files.add(path.relative_to(export_dir).as_posix())
    if observed_directories != {"packages"}:
        raise RuntimePackagePlaneError("runtime bundle directory layout is not exact")
    if observed_files != expected_files:
        missing = sorted(expected_files - observed_files)
        extra = sorted(observed_files - expected_files)
        raise RuntimePackagePlaneError(
            f"runtime bundle members differ (missing={missing}, extra={extra})"
        )
    casefolded = [name.casefold() for name in observed_files]
    if len(casefolded) != len(set(casefolded)):
        raise RuntimePackagePlaneError("runtime bundle contains case-colliding members")


def export_bundle(
    repo_root: Path,
    lock_path: Path,
    feed: Path,
    receipt_path: Path,
    export_dir: Path,
) -> tuple[str, str]:
    if not export_dir.is_absolute():
        raise RuntimePackagePlaneError("runtime bundle export path must be absolute")
    if export_dir.exists() or export_dir.is_symlink():
        raise RuntimePackagePlaneError("runtime bundle export path must not already exist")
    inventory_sha256 = validate_inventory(repo_root, lock_path, feed)
    inventory_path = feed / INVENTORY_NAME
    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
    _, receipt_sha256, receipt_size = _validate_receipt(
        receipt_path,
        inventory,
        inventory_sha256,
    )
    package_names = [row["file_name"] for row in inventory["packages"]]
    if len(package_names) != len(set(name.casefold() for name in package_names)):
        raise RuntimePackagePlaneError("runtime inventory contains case-colliding package names")

    try:
        export_dir.mkdir(mode=0o755, parents=False, exist_ok=False)
        packages_dir = export_dir / "packages"
        packages_dir.mkdir(mode=0o755, exist_ok=False)
    except OSError as exc:
        raise RuntimePackagePlaneError(f"cannot create runtime bundle export path: {exc}") from exc

    expected_files = {
        "chummer-core-runtime-packages.inventory.json",
        "runtime-package-plane.lock.json",
        "no-siblings.v3.receipt.json",
        *(f"packages/{name}" for name in package_names),
    }
    for row in inventory["packages"]:
        _copy_bound_file(
            _find_package(feed, row["id"]),
            packages_dir / row["file_name"],
            expected_sha256=row["sha256"],
            expected_size=row["size_bytes"],
        )
    _copy_bound_file(
        inventory_path,
        export_dir / INVENTORY_NAME,
        expected_sha256=inventory_sha256,
        expected_size=inventory_path.stat().st_size,
    )
    _copy_bound_file(
        lock_path,
        export_dir / "runtime-package-plane.lock.json",
        expected_sha256=inventory["package_plane_lock_sha256"],
        expected_size=lock_path.stat().st_size,
    )
    _copy_bound_file(
        receipt_path,
        export_dir / "no-siblings.v3.receipt.json",
        expected_sha256=receipt_sha256,
        expected_size=receipt_size,
    )
    _validate_export_layout(export_dir, expected_files)
    try:
        directory_descriptor = os.open(export_dir, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        packages_descriptor = os.open(
            packages_dir,
            os.O_RDONLY | getattr(os, "O_DIRECTORY", 0),
        )
        try:
            os.fsync(packages_descriptor)
            os.fsync(directory_descriptor)
        finally:
            os.close(packages_descriptor)
            os.close(directory_descriptor)
    except OSError as exc:
        raise RuntimePackagePlaneError(f"cannot flush runtime bundle directories: {exc}") from exc
    return inventory_sha256, receipt_sha256


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--feed", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--export-dir", type=Path)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument("--canonicalize-and-write-inventory", action="store_true")
    actions.add_argument("--validate-feed", action="store_true")
    actions.add_argument("--print-pack-rows", action="store_true")
    actions.add_argument("--export-bundle", action="store_true")
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
