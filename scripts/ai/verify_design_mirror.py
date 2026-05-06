#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import sys
from collections import Counter
from pathlib import Path

import yaml

from sync_design_mirror import DESIGN_ROOT, REPO_ROOT, expand_product_sources, load_manifest, relative_product_target


QUEUE_PATH = REPO_ROOT / ".codex-studio" / "published" / "QUEUE.generated.yaml"


def get_core_mirror(manifest: dict[str, object]) -> dict[str, object]:
    mirrors = manifest.get("mirrors") or []
    if not isinstance(mirrors, list):
        raise ValueError("sync_manifest_mirrors_not_list")

    mirror = next((item for item in mirrors if isinstance(item, dict) and item.get("repo") == "chummer6-core"), None)
    if mirror is None:
        raise ValueError("sync_manifest_missing_core_mirror")
    return mirror


def collect_stale_paths(manifest: dict[str, object], mirror: dict[str, object]) -> list[str]:
    product_target = str(mirror.get("product_target") or ".codex-design/product").strip()
    product_sources = expand_product_sources(manifest, mirror)
    duplicate_basenames = {
        name
        for name, count in Counter(Path(source).name for source in product_sources).items()
        if count > 1
    }

    stale: list[str] = []
    expected_product_files: set[Path] = set()

    for source_rel in product_sources:
        source = DESIGN_ROOT / source_rel
        destination = REPO_ROOT / relative_product_target(source_rel, duplicate_basenames, product_target)
        expected_product_files.add(destination.relative_to(REPO_ROOT))
        if not destination.is_file() or source.read_bytes() != destination.read_bytes():
            stale.append(str(destination.relative_to(REPO_ROOT)))

    product_root = REPO_ROOT / product_target
    if product_root.is_dir():
        for destination in sorted(item for item in product_root.rglob("*") if item.is_file()):
            repo_relative = destination.relative_to(REPO_ROOT)
            if repo_relative not in expected_product_files:
                stale.append(str(repo_relative))

    repo_source = DESIGN_ROOT / str(mirror.get("repo_source") or "").strip()
    repo_target = REPO_ROOT / str(mirror.get("repo_target") or ".codex-design/repo/IMPLEMENTATION_SCOPE.md").strip()
    if repo_source.is_file() and (not repo_target.is_file() or repo_source.read_bytes() != repo_target.read_bytes()):
        stale.append(str(repo_target.relative_to(REPO_ROOT)))

    review_source = DESIGN_ROOT / str(mirror.get("review_source") or "").strip()
    review_target = REPO_ROOT / str(mirror.get("review_target") or ".codex-design/review/REVIEW_CONTEXT.md").strip()
    if review_source.is_file() and (not review_target.is_file() or review_source.read_bytes() != review_target.read_bytes()):
        stale.append(str(review_target.relative_to(REPO_ROOT)))

    return stale


def load_queue_items() -> list[dict[str, object]]:
    if not QUEUE_PATH.is_file():
        return []
    data = yaml.safe_load(QUEUE_PATH.read_text(encoding="utf-8")) or {}
    if not isinstance(data, dict):
        raise ValueError("queue_yaml_not_object")
    items = data.get("items") or []
    if not isinstance(items, list):
        raise ValueError("queue_items_not_list")
    return [item for item in items if isinstance(item, dict)]


def verify_audit_row(items: list[dict[str, object]], stale_paths: list[str]) -> list[str]:
    errors: list[str] = []
    matching_rows = [
        item for item in items
        if item.get("package_id") == "audit-task-11707"
        or item.get("audit_finding_key") == "project.design_mirror_missing_or_stale"
    ]

    if len(matching_rows) > 1:
        errors.append(f"duplicate_audit_task_11707_rows={len(matching_rows)}")
        return errors

    if not stale_paths:
        if matching_rows:
            errors.append("mirror_clean_but_audit_task_11707_queue_row_still_published")
        return errors

    if not matching_rows:
        errors.append("mirror_stale_without_audit_task_11707_queue_row")
        return errors

    row = matching_rows[0]
    if row.get("audit_scope_id") != "core":
        errors.append("audit_task_11707_scope_not_core")
    if row.get("allowed_paths") != [".codex-design"]:
        errors.append("audit_task_11707_allowed_paths_drift")
    if row.get("owned_surfaces") != ["design_mirror:core"]:
        errors.append("audit_task_11707_owned_surfaces_drift")

    source_items = row.get("source_items") or []
    if not isinstance(source_items, list) or not source_items:
        errors.append("audit_task_11707_missing_source_items")
        return errors

    repo_prefix = str(REPO_ROOT) + "/.codex-design/"
    bad_source_items = [
        str(item)
        for item in source_items
        if not isinstance(item, str)
        or not item.startswith(repo_prefix)
        or "/.." in item
    ]
    if bad_source_items:
        errors.append(f"audit_task_11707_invalid_source_items={bad_source_items}")
        return errors

    expected_source_items = sorted(str(REPO_ROOT / rel_path) for rel_path in stale_paths if rel_path.startswith(".codex-design/"))
    actual_source_items = sorted(str(item) for item in source_items)
    if actual_source_items != expected_source_items:
        errors.append(f"audit_task_11707_source_items_mismatch expected={expected_source_items} actual={actual_source_items}")

    return errors


def main() -> int:
    manifest = load_manifest()
    mirror = get_core_mirror(manifest)
    stale_paths = collect_stale_paths(manifest, mirror)
    queue_items = load_queue_items()
    queue_errors = verify_audit_row(queue_items, stale_paths)

    if queue_errors:
        print(f"stale_paths={len(stale_paths)} queue_errors={len(queue_errors)}")
        for rel_path in stale_paths:
            print(f"stale {rel_path}")
        for error in queue_errors:
            print(f"error {error}")
        return 1

    if stale_paths:
        print(f"stale_paths={len(stale_paths)} queue_errors=0 bounded_queue_row=1")
        for rel_path in stale_paths:
            print(f"stale {rel_path}")
        return 0

    queue_digest = hashlib.sha1(QUEUE_PATH.read_bytes()).hexdigest() if QUEUE_PATH.is_file() else "missing"
    print(f"stale_paths=0 queue_errors=0 queue_sha1={queue_digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
