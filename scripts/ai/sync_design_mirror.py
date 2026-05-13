#!/usr/bin/env python3
from __future__ import annotations

import shutil
import sys
from collections import Counter
import json
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
DESIGN_ROOT = REPO_ROOT.parent / "chummer-design"
MANIFEST_PATH = DESIGN_ROOT / "products" / "chummer" / "sync" / "sync-manifest.yaml"
QUEUE_PATH = REPO_ROOT / ".codex-studio" / "published" / "QUEUE.generated.yaml"
WEEKLY_PRODUCT_PULSE_RELATIVE_PATH = Path("products/chummer/WEEKLY_PRODUCT_PULSE.generated.json")


def load_manifest() -> dict[str, object]:
    data = yaml.safe_load(MANIFEST_PATH.read_text(encoding="utf-8")) or {}
    if not isinstance(data, dict):
        raise ValueError("sync_manifest_not_object")
    return data


def expand_product_sources(manifest: dict[str, object], mirror: dict[str, object]) -> list[str]:
    groups = manifest.get("product_source_groups") or {}
    if not isinstance(groups, dict):
        raise ValueError("sync_manifest_product_source_groups_not_object")

    expanded: list[str] = []
    for group_name in mirror.get("product_groups") or []:
        items = groups.get(group_name)
        if not isinstance(items, list):
            raise ValueError(f"sync_manifest_product_group_not_list:{group_name}")
        expanded.extend(str(item or "").strip() for item in items)

    ordered: list[str] = []
    seen: set[str] = set()
    for source in expanded:
        if not source or source in seen:
            continue
        seen.add(source)
        ordered.append(source)
    return ordered


def relative_product_target(source_rel: str, duplicate_basenames: set[str], product_target: str) -> Path:
    source_path = Path(source_rel)
    parts = list(source_path.parts)
    if len(parts) >= 2 and parts[0] == "products" and parts[1] == "chummer":
        relative_source = Path(*parts[2:])
    elif source_path.name in duplicate_basenames:
        relative_source = source_path
    else:
        relative_source = Path(source_path.name)
    return Path(product_target) / relative_source


def files_match(source: Path, destination: Path) -> bool:
    if not destination.exists():
        return False

    if source.read_bytes() == destination.read_bytes():
        return True

    if source.as_posix().endswith(str(WEEKLY_PRODUCT_PULSE_RELATIVE_PATH)):
        try:
            source_payload = json.loads(source.read_text(encoding="utf-8"))
            destination_payload = json.loads(destination.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return False

        if isinstance(source_payload, dict) and isinstance(destination_payload, dict):
            source_payload = dict(source_payload)
            destination_payload = dict(destination_payload)
            source_payload.pop("generated_at", None)
            destination_payload.pop("generated_at", None)
            return source_payload == destination_payload

    return False


def copy_if_changed(source: Path, destination: Path) -> bool:
    if files_match(source, destination):
        return False
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    return True


def prune_product_root(product_root: Path, expected_rel_paths: set[Path]) -> list[Path]:
    removed: list[Path] = []
    if not product_root.exists():
        return removed

    for path in sorted((item for item in product_root.rglob("*") if item.is_file()), reverse=True):
        rel_path = path.relative_to(product_root)
        if rel_path in expected_rel_paths:
            continue
        path.unlink()
        removed.append(rel_path)

    for path in sorted((item for item in product_root.rglob("*") if item.is_dir()), reverse=True):
        try:
            path.rmdir()
        except OSError:
            continue

    return removed


def clear_design_mirror_audit_queue_row() -> bool:
    if not QUEUE_PATH.is_file():
        return False

    data = yaml.safe_load(QUEUE_PATH.read_text(encoding="utf-8")) or {}
    if not isinstance(data, dict):
        raise ValueError("queue_yaml_not_object")

    items = data.get("items") or []
    if not isinstance(items, list):
        raise ValueError("queue_items_not_list")

    filtered_items = [
        item for item in items
        if not (
            isinstance(item, dict)
            and (
                item.get("package_id") == "audit-task-11707"
                or item.get("audit_finding_key") == "project.design_mirror_missing_or_stale"
            )
        )
    ]

    if len(filtered_items) == len(items):
        return False

    data["items"] = filtered_items
    QUEUE_PATH.write_text(yaml.safe_dump(data, sort_keys=False), encoding="utf-8")
    return True


def main() -> int:
    manifest = load_manifest()
    mirrors = manifest.get("mirrors") or []
    if not isinstance(mirrors, list):
        raise ValueError("sync_manifest_mirrors_not_list")

    mirror = next((item for item in mirrors if isinstance(item, dict) and item.get("repo") == "chummer6-core"), None)
    if mirror is None:
        raise ValueError("sync_manifest_missing_core_mirror")

    product_target = str(mirror.get("product_target") or ".codex-design/product").strip()
    product_sources = expand_product_sources(manifest, mirror)
    duplicate_basenames = {
        name
        for name, count in Counter(Path(source).name for source in product_sources).items()
        if count > 1
    }

    changed: list[str] = []
    expected_product_rel_paths: set[Path] = set()

    for source_rel in product_sources:
        source = DESIGN_ROOT / source_rel
        if not source.is_file():
            raise FileNotFoundError(source)
        target_rel = relative_product_target(source_rel, duplicate_basenames, product_target)
        destination = REPO_ROOT / target_rel
        expected_product_rel_paths.add(target_rel.relative_to(product_target))
        if copy_if_changed(source, destination):
            changed.append(str(target_rel))

    repo_source = DESIGN_ROOT / str(mirror.get("repo_source") or "").strip()
    repo_target = REPO_ROOT / str(mirror.get("repo_target") or ".codex-design/repo/IMPLEMENTATION_SCOPE.md").strip()
    if repo_source.is_file() and copy_if_changed(repo_source, repo_target):
        changed.append(str(repo_target.relative_to(REPO_ROOT)))

    review_source = DESIGN_ROOT / str(mirror.get("review_source") or "").strip()
    review_target = REPO_ROOT / str(mirror.get("review_target") or ".codex-design/review/REVIEW_CONTEXT.md").strip()
    if review_source.is_file() and copy_if_changed(review_source, review_target):
        changed.append(str(review_target.relative_to(REPO_ROOT)))

    removed = [
        str(Path(product_target) / rel)
        for rel in prune_product_root(REPO_ROOT / product_target, expected_product_rel_paths)
    ]
    queue_cleared = clear_design_mirror_audit_queue_row()

    print(f"changed={len(changed)} removed={len(removed)} queue_cleared={1 if queue_cleared else 0}")
    for rel in changed:
        print(f"update {rel}")
    for rel in removed:
        print(f"remove {rel}")
    if queue_cleared:
        print("queue_clear .codex-studio/published/QUEUE.generated.yaml audit-task-11707")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
