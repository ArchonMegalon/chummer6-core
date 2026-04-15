#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path
import subprocess
import sys
from typing import Any

UTC = dt.timezone.utc

REQUIRED_ORACLE_SUITE_IDS = (
    "creation",
    "advancement",
    "augment",
    "matrix",
    "magic",
    "vehicle",
    "source_toggle",
    "amend_package",
)

REQUIRED_BUDGET_IDS = (
    "load",
    "explain",
    "diff_apply",
    "import",
    "export_prep",
)

REQUIRED_PROMOTED_DESKTOP_TUPLES = (
    ("avalonia:linux:linux-x64", "avalonia", "linux", "linux-x64"),
    ("avalonia:windows:win-x64", "avalonia", "windows", "win-x64"),
    ("avalonia:macos:osx-arm64", "avalonia", "macos", "osx-arm64"),
)

REQUIRED_IMPORT_ORACLE_NAMES = (
    "Chummer4",
    "Chummer5a",
    "Hero Lab Classic",
)

REQUIRED_ADJACENT_ORACLE_NAMES = (
    "Genesis",
    "CommLink6",
)

RELEASE_COMMANDS = (
    "dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1 && dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll",
    "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
)

RELEASE_CHANNEL_PATH = Path("/docker/chummercomplete/chummer-hub-registry/.codex-studio/published/RELEASE_CHANNEL.generated.json")
CANONICAL_CHUMMER_ROOT = Path("/docker/chummercomplete")
CANONICAL_PACKAGE_ROOT = CANONICAL_CHUMMER_ROOT / "chummer-core-engine"

SUCCESSOR_WAVE_PACKAGE = {
    "program_wave": "next_90_day_product_advance",
    "frontier_id": 3227666051,
    "wave": "W7",
    "milestone_id": 104,
    "package_id": "next90-m104-core-proof-pack",
    "repo": "chummer6-core",
    "title": "Build golden oracle suites and release-bound engine proof packs",
    "owned_surfaces": [
        "engine_proof_pack",
        "import_oracle_discipline",
    ],
    "allowed_paths": [
        "src",
        "tests",
        "docs",
        "scripts",
    ],
    "source_registry_path": "/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml",
    "source_queue_path": "/docker/fleet/.codex-studio/published/NEXT_90_DAY_QUEUE_STAGING.generated.yaml",
    "source_design_queue_path": "/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml",
}

SUCCESSOR_REGISTRY_MILESTONE_TOKENS = (
    "id: 104",
    "title: Engine proof pack, explain budgets, and import-oracle discipline",
    "104.1",
    "104.2",
)

SUCCESSOR_REGISTRY_TASK_TOKENS = {
    "104.1": (
        "id: 104.1",
        "owner: chummer6-core",
        "status: complete",
        "required oracle suites creation, advancement, augment, matrix, magic, vehicle, source_toggle, and amend_package",
        "python3 tests/test_engine_proof_pack_generator.py exits 0",
    ),
    "104.2": (
        "id: 104.2",
        "owner: chummer6-core",
        "status: complete",
        "successor_wave_authority=passed",
        "/docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
        "/docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
        "/docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so the completed M104 proof pack cannot pass if the latest guard disappears.",
        "/docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
        "/docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
        "/docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
        "/docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
        "/docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
        "/docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
        "/docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
        "/docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
        "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json exits 0",
    ),
}

SUCCESSOR_QUEUE_TOKENS = (
    "package_id: next90-m104-core-proof-pack",
    "frontier_id: 3227666051",
    "milestone_id: 104",
    "repo: chummer6-core",
    "status: complete",
    "landed_commit: 00800059",
    "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/ENGINE_PROOF_PACK.generated.json",
    "/docker/chummercomplete/chummer-core-engine/scripts/generate-engine-proof-pack.py",
    "/docker/chummercomplete/chummer-core-engine/tests/test_engine_proof_pack_generator.py",
    "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md",
    "/docker/chummercomplete/chummer-core-engine commit 8dd516ef makes failed engine proof pack generation exit nonzero while still writing diagnostic receipts.",
    "/docker/chummercomplete/chummer-core-engine commit c88178fa tightens design-owned queue scope proof so canonical allowed-path or owned-surface drift cannot keep M104 passed.",
    "/docker/chummercomplete/chummer-core-engine commit 769e7259 pins local commit proof through guard 56048971 so future shards verify the closed package instead of repeating it.",
    "/docker/chummercomplete/chummer-core-engine commit d4b3b0ba requires the current 769e7259 guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit a2173476 requires the current d4b3b0ba guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit 4b124997 binds M104 proof pack generation, tests, documentation, and checked-in receipt to active-run hygiene guard 4a56911d.",
    "/docker/chummercomplete/chummer-core-engine commit b488d109 pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it.",
    "/docker/chummercomplete/chummer-core-engine commit b6fddf74 tightens the current M104 proof pack authority guard so future shards verify the latest closed package.",
    "/docker/chummercomplete/chummer-core-engine commit f6608678 tightens the latest M104 proof pack local guard so future shards verify the closed package.",
    "/docker/chummercomplete/chummer-core-engine commit a3cbb548 refreshes the M104 engine proof receipt after latest local guard tightening.",
    "/docker/chummercomplete/chummer-core-engine commit df0527b2 tightens the M104 proof pack receipt guard so future shards verify the latest closed package.",
    "/docker/chummercomplete/chummer-core-engine commit 8574f63f pins the M104 proof pack receipt guard.",
    "/docker/chummercomplete/chummer-core-engine commit 6b3a662c requires the current 8574f63f guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit 3b63478f pins the current 6b3a662c guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit cd30503f pins the current d2ee91a9 engine proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit e10f2739 pins the current cd30503f queue proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
    "allowed_paths:",
    "- src",
    "- tests",
    "- docs",
    "- scripts",
    "engine_proof_pack",
    "import_oracle_discipline",
)

SUCCESSOR_QUEUE_PROOF_ANCHORS = (
    "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/ENGINE_PROOF_PACK.generated.json",
    "/docker/chummercomplete/chummer-core-engine/scripts/generate-engine-proof-pack.py",
    "/docker/chummercomplete/chummer-core-engine/tests/test_engine_proof_pack_generator.py",
    "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md",
)

EXPECTED_QUEUE_ALLOWED_PATHS = tuple(SUCCESSOR_WAVE_PACKAGE["allowed_paths"])
EXPECTED_QUEUE_OWNED_SURFACES = tuple(SUCCESSOR_WAVE_PACKAGE["owned_surfaces"])
DISALLOWED_ACTIVE_RUN_PROOF_TOKENS = (
    "ACTIVE_RUN_HANDOFF",
    "active-run telemetry",
    "operator telemetry",
    "active run helper",
    "active-run helper",
)
DISALLOWED_ACTIVE_RUN_PROOF_TOKEN_MATCHES = tuple(
    (token, token.lower()) for token in DISALLOWED_ACTIVE_RUN_PROOF_TOKENS
)

REQUIRED_LOCAL_COMMIT_PROOFS = (
    ("00800059", "initial fail-closed successor authority and oracle/budget generator tests"),
    ("fd15fe87", "row-scoped successor queue allowed-path authority"),
    ("44fdda0f", "non-resolving successor queue proof-anchor guard"),
    ("cfc465a5", "checked-in receipt reproducibility guard"),
    ("86040e30", "unassigned queue allowed-path and owned-surface guard"),
    ("35cd27b4", "successor frontier repeat-prevention guard"),
    ("8dd516ef", "failed generator runs exit nonzero while writing diagnostics"),
    ("c88178fa", "design-owned queue scope drift guard"),
    ("53d5678c", "design queue scope guard binding"),
    ("26f2921f", "engine proof commit guard"),
    ("7b42b69f", "package-local proof anchor scope guard"),
    ("220dd257", "current proof pack guard binding"),
    ("b5571717", "proof pack guard binding"),
    ("56048971", "latest proof pack guard binding"),
    ("769e7259", "completed queue proof bound to latest local guard"),
    ("d4b3b0ba", "current proof pack guard required by registry and queue closeout"),
    ("a2173476", "current proof pack guard required by registry and queue closeout"),
    ("dafc1205", "latest proof pack guard required by local closeout"),
    ("65df3894", "active-run proof hygiene guard required by local closeout"),
    ("4a56911d", "active-run proof hygiene guard binding required by local closeout"),
    ("4b124997", "current proof pack guard binding required by registry and queue closeout"),
    ("2187db33", "latest proof pack guard authority required by local closeout"),
    ("b488d109", "current M104 proof pack authority required by registry and queue closeout"),
    ("b6fddf74", "latest M104 proof pack authority guard required by registry and queue closeout"),
    ("3b9a29c2", "latest M104 proof pack guard required by local closeout"),
    ("f6608678", "latest M104 proof pack local guard required by registry and queue closeout"),
    ("a3cbb548", "latest M104 checked-in proof receipt refresh required by registry and queue closeout"),
    ("df0527b2", "latest M104 proof pack receipt guard required by registry and queue closeout"),
    ("8574f63f", "current M104 proof pack receipt guard required by registry and queue closeout"),
    ("6b3a662c", "current M104 proof pack guard required by registry and queue closeout"),
    ("3b63478f", "current M104 proof pack guard floor required by registry and queue closeout"),
    ("31c75c02", "current M104 closed-package guard floor required by local closeout"),
    ("ef46554c", "current M104 proof pack guard floor required by local closeout"),
    ("0771b7ea", "current M104 proof pack guard floor required by local closeout"),
    ("fdb6a273", "current M104 engine proof guard floor required by local closeout"),
    ("d2ee91a9", "current M104 engine proof floor required by local closeout"),
    ("cd30503f", "current M104 engine proof floor queue citation required by local closeout"),
    ("e10f2739", "current M104 queue proof floor required by local closeout"),
    ("e7d4270e", "current M104 queue proof floor guard required by local closeout"),
    ("bbc877d7", "current M104 proof floor guard required by local closeout"),
    ("56ff7283", "current M104 proof floor guard required by local closeout"),
    ("7ae79416", "current M104 proof floor guard required by local closeout"),
    ("a613bdb2", "current M104 engine proof pack guard required by local closeout"),
    ("353921e7", "current M104 engine proof pack guard floor required by local closeout"),
    ("9de2455b", "current M104 proof pack guard floor required by local closeout"),
    ("d8e826a3", "current M104 proof pack guard floor required by local closeout"),
    ("7a1f0e7c", "current M104 proof pack guard floor required by local closeout"),
    ("d464cfab", "current M104 proof pack guard floor required by local closeout"),
    ("a1a2d956", "current M104 proof pack local floor required by local closeout"),
    ("abf63719", "current M104 proof pack local floor required by local closeout"),
    ("bbc7fba8", "current M104 engine proof pack floor required by local closeout"),
    ("a1a1d505", "current M104 engine proof pack floor required by local closeout"),
    ("18d03556", "current M104 active-run helper hygiene floor required by local closeout"),
)


def _iso_now() -> str:
    return dt.datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _load_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return {}
    return payload if isinstance(payload, dict) else {}


def _extract_status(payload: dict[str, Any]) -> str:
    return str(payload.get("status") or "").strip().lower()


def _to_rel(path: Path, root: Path) -> str:
    try:
        return str(path.resolve().relative_to(root.resolve())).replace("\\", "/")
    except Exception:
        return str(path)


def _evidence_status(root: Path, evidence_items: list[str]) -> tuple[str, list[str]]:
    missing: list[str] = []
    for item in evidence_items:
        path_part, _, symbol_part = item.partition("::")
        path_part = path_part.strip()
        symbol_part = symbol_part.strip()
        if not path_part:
            missing.append(item)
            continue
        evidence_path = root / path_part
        if not evidence_path.exists():
            missing.append(item)
            continue
        if symbol_part:
            try:
                evidence_text = evidence_path.read_text(encoding="utf-8", errors="replace")
            except Exception:
                missing.append(item)
                continue
            if symbol_part not in evidence_text:
                missing.append(item)
    return ("passed" if not missing else "failed", missing)


def _validate_release_commands(root: Path, generated_output_path: Path | None = None) -> tuple[list[dict[str, Any]], list[str]]:
    command_specs: list[tuple[str, str, list[str]]] = [
        (
            "core_engine_tests",
            RELEASE_COMMANDS[0],
            [
                "Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj",
                ".codex-studio/published/ENGINE_PROOF_PACK.generated.json",
            ],
        ),
        (
            "benchmark_budget_check",
            RELEASE_COMMANDS[1],
            [
                "Chummer.Benchmarks/Chummer.Benchmarks.csproj",
                "Chummer.Benchmarks/workspace-benchmark-budgets.json",
            ],
        ),
    ]

    commands: list[dict[str, Any]] = []
    unresolved: list[str] = []
    for command_id, command, required_inputs in command_specs:
        missing_inputs = []
        for item in required_inputs:
            input_path = root / item
            planned_generated_input = generated_output_path is not None and input_path.resolve() == generated_output_path.resolve()
            if not input_path.exists() and not planned_generated_input:
                missing_inputs.append(item)
        if missing_inputs:
            unresolved.append(command_id)
        commands.append(
            {
                "id": command_id,
                "command": command,
                "status": "passed" if not missing_inputs else "failed",
                "required_inputs": required_inputs,
                "missing_inputs": missing_inputs,
            }
        )

    return commands, unresolved


def _read_text(path: Path) -> str:
    if not path.is_file():
        return ""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return ""


def _validate_text_tokens(path: Path, required_tokens: tuple[str, ...]) -> tuple[str, list[str]]:
    text = _read_text(path)
    if not text:
        return "failed", list(required_tokens)
    missing = [token for token in required_tokens if token not in text]
    return ("passed" if not missing else "failed", missing)


def _find_disallowed_active_run_tokens(text: str) -> list[str]:
    lower_text = text.lower()
    return [
        token
        for token, normalized in DISALLOWED_ACTIVE_RUN_PROOF_TOKEN_MATCHES
        if normalized in lower_text
    ]


def _extract_list_item_block(text: str, item_header: str) -> str:
    lines = text.splitlines()
    match_index = -1
    for index, line in enumerate(lines):
        stripped = line.lstrip()
        if stripped == item_header or stripped == f"- {item_header}":
            match_index = index
            break

    if match_index < 0:
        return ""

    match_indent = len(lines[match_index]) - len(lines[match_index].lstrip())
    start_index = match_index
    item_indent = match_indent
    if not lines[match_index].lstrip().startswith("- "):
        for parent_index in range(match_index - 1, -1, -1):
            parent = lines[parent_index]
            parent_stripped = parent.lstrip()
            parent_indent = len(parent) - len(parent_stripped)
            if parent_indent < match_indent and parent_stripped.startswith("- "):
                start_index = parent_index
                item_indent = parent_indent
                break

    block_lines = [lines[start_index]]
    for line in lines[start_index + 1 :]:
        stripped = line.lstrip()
        indent = len(line) - len(stripped)
        if indent == item_indent and stripped.startswith("- "):
            break
        block_lines.append(line)
    return "\n".join(block_lines) + "\n"


def _extract_yaml_list_values(block: str, key: str) -> list[str]:
    lines = block.splitlines()
    for index, line in enumerate(lines):
        stripped = line.lstrip()
        if stripped != f"{key}:":
            continue

        key_indent = len(line) - len(stripped)
        values: list[str] = []
        for item_line in lines[index + 1 :]:
            item_stripped = item_line.lstrip()
            item_indent = len(item_line) - len(item_stripped)
            if not item_stripped:
                continue
            if item_indent <= key_indent:
                break
            if item_stripped.startswith("- "):
                values.append(item_stripped[2:].strip())
        return values
    return []


def _list_drift(actual: list[str], expected: tuple[str, ...]) -> tuple[list[str], list[str]]:
    actual_set = set(actual)
    expected_set = set(expected)
    missing = [value for value in expected if value not in actual_set]
    unexpected = [value for value in actual if value not in expected_set]
    return missing, unexpected


def _is_canonical_anchor_outside_package(anchor_path: Path) -> bool:
    try:
        resolved = anchor_path.resolve()
        resolved.relative_to(CANONICAL_CHUMMER_ROOT)
    except ValueError:
        return False
    try:
        resolved.relative_to(CANONICAL_PACKAGE_ROOT)
    except ValueError:
        return True
    return False


def _validate_queue_authority(queue_path: Path, generated_output_path: Path | None = None) -> dict[str, Any]:
    queue_text = _read_text(queue_path)
    queue_scope = _extract_list_item_block(queue_text, "package_id: next90-m104-core-proof-pack")
    missing_queue_tokens = ["queue_item:next90-m104-core-proof-pack"] if not queue_scope else []
    missing_queue_tokens.extend(token for token in SUCCESSOR_QUEUE_TOKENS if token not in queue_scope)
    disallowed_active_run_tokens = _find_disallowed_active_run_tokens(queue_scope)
    missing_queue_proof_anchors = []
    for anchor in SUCCESSOR_QUEUE_PROOF_ANCHORS:
        anchor_path = Path(anchor)
        planned_generated_anchor = generated_output_path is not None and anchor_path.resolve() == generated_output_path.resolve()
        if not anchor_path.exists() and not planned_generated_anchor:
            missing_queue_proof_anchors.append(anchor)
    off_package_queue_proof_anchors = [
        anchor
        for anchor in SUCCESSOR_QUEUE_PROOF_ANCHORS
        if anchor in queue_scope and _is_canonical_anchor_outside_package(Path(anchor))
    ]
    queue_allowed_paths = _extract_yaml_list_values(queue_scope, "allowed_paths") if queue_scope else []
    missing_queue_allowed_paths, unexpected_queue_allowed_paths = _list_drift(queue_allowed_paths, EXPECTED_QUEUE_ALLOWED_PATHS)
    queue_owned_surfaces = _extract_yaml_list_values(queue_scope, "owned_surfaces") if queue_scope else []
    missing_queue_owned_surfaces, unexpected_queue_owned_surfaces = _list_drift(queue_owned_surfaces, EXPECTED_QUEUE_OWNED_SURFACES)
    for value in missing_queue_allowed_paths:
        missing_queue_tokens.append(f"allowed_paths:{value}")
    for value in unexpected_queue_allowed_paths:
        missing_queue_tokens.append(f"unexpected_allowed_path:{value}")
    for value in missing_queue_owned_surfaces:
        missing_queue_tokens.append(f"owned_surfaces:{value}")
    for value in unexpected_queue_owned_surfaces:
        missing_queue_tokens.append(f"unexpected_owned_surface:{value}")

    return {
        "path": str(queue_path),
        "status": "passed"
        if not missing_queue_tokens
        and not missing_queue_proof_anchors
        and not off_package_queue_proof_anchors
        and not disallowed_active_run_tokens
        else "failed",
        "missing_tokens": missing_queue_tokens,
        "missing_proof_anchors": missing_queue_proof_anchors,
        "off_package_proof_anchors": off_package_queue_proof_anchors,
        "disallowed_active_run_tokens": disallowed_active_run_tokens,
        "allowed_paths": queue_allowed_paths,
        "expected_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
        "unexpected_allowed_paths": unexpected_queue_allowed_paths,
        "owned_surfaces": queue_owned_surfaces,
        "expected_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
        "unexpected_owned_surfaces": unexpected_queue_owned_surfaces,
    }


def _validate_successor_wave_authority(generated_output_path: Path | None = None) -> tuple[dict[str, Any], list[str]]:
    registry_path = Path(SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
    queue_path = Path(SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
    design_queue_path = Path(SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])

    registry_text = _read_text(registry_path)
    registry_scope = _extract_list_item_block(registry_text, "id: 104")
    registry_missing_scope_tokens = ["milestone_block:id:104"] if not registry_scope else []
    missing_registry_tokens = registry_missing_scope_tokens + [token for token in SUCCESSOR_REGISTRY_MILESTONE_TOKENS if token not in registry_scope]
    missing_registry_task_tokens: dict[str, list[str]] = {}
    disallowed_registry_active_run_tokens: dict[str, list[str]] = {}
    for task_id, required_tokens in SUCCESSOR_REGISTRY_TASK_TOKENS.items():
        task_scope = _extract_list_item_block(registry_scope, f"id: {task_id}") if registry_scope else ""
        missing = [f"task_block:id:{task_id}"] if not task_scope else []
        missing.extend(token for token in required_tokens if token not in task_scope)
        if missing:
            missing_registry_task_tokens[task_id] = missing
            missing_registry_tokens.extend(f"{task_id}:{token}" for token in missing)
        disallowed = _find_disallowed_active_run_tokens(task_scope)
        if disallowed:
            disallowed_registry_active_run_tokens[task_id] = disallowed
            missing_registry_tokens.extend(f"{task_id}:disallowed_active_run_proof:{token}" for token in disallowed)

    fleet_queue_authority = _validate_queue_authority(queue_path, generated_output_path)
    design_queue_authority = _validate_queue_authority(design_queue_path, generated_output_path)
    registry_status = "passed" if not missing_registry_tokens else "failed"

    unresolved: list[str] = []
    if registry_status != "passed":
        unresolved.append("source_registry")
    if fleet_queue_authority["status"] != "passed":
        unresolved.append("source_queue")
    if design_queue_authority["status"] != "passed":
        unresolved.append("source_design_queue")

    return (
        {
            "status": "passed" if not unresolved else "failed",
            "registry_path": str(registry_path),
            "queue_path": str(queue_path),
            "design_queue_path": str(design_queue_path),
            "required_registry_tokens": list(SUCCESSOR_REGISTRY_MILESTONE_TOKENS),
            "required_registry_task_tokens": {task_id: list(tokens) for task_id, tokens in SUCCESSOR_REGISTRY_TASK_TOKENS.items()},
            "required_queue_tokens": list(SUCCESSOR_QUEUE_TOKENS),
            "validation_scope": {
                "registry": "milestones item id: 104",
                "queue": "items package_id: next90-m104-core-proof-pack",
                "design_queue": "items package_id: next90-m104-core-proof-pack",
            },
            "missing_registry_tokens": missing_registry_tokens,
            "missing_registry_task_tokens": missing_registry_task_tokens,
            "disallowed_registry_active_run_tokens": disallowed_registry_active_run_tokens,
            "missing_queue_tokens": fleet_queue_authority["missing_tokens"],
            "missing_queue_proof_anchors": fleet_queue_authority["missing_proof_anchors"],
            "off_package_queue_proof_anchors": fleet_queue_authority["off_package_proof_anchors"],
            "disallowed_queue_active_run_tokens": fleet_queue_authority["disallowed_active_run_tokens"],
            "queue_allowed_paths": fleet_queue_authority["allowed_paths"],
            "expected_queue_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
            "unexpected_queue_allowed_paths": fleet_queue_authority["unexpected_allowed_paths"],
            "queue_owned_surfaces": fleet_queue_authority["owned_surfaces"],
            "expected_queue_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
            "unexpected_queue_owned_surfaces": fleet_queue_authority["unexpected_owned_surfaces"],
            "design_queue_missing_tokens": design_queue_authority["missing_tokens"],
            "design_queue_missing_proof_anchors": design_queue_authority["missing_proof_anchors"],
            "design_queue_off_package_proof_anchors": design_queue_authority["off_package_proof_anchors"],
            "disallowed_design_queue_active_run_tokens": design_queue_authority["disallowed_active_run_tokens"],
            "design_queue_allowed_paths": design_queue_authority["allowed_paths"],
            "expected_design_queue_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
            "unexpected_design_queue_allowed_paths": design_queue_authority["unexpected_allowed_paths"],
            "design_queue_owned_surfaces": design_queue_authority["owned_surfaces"],
            "expected_design_queue_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
            "unexpected_design_queue_owned_surfaces": design_queue_authority["unexpected_owned_surfaces"],
            "closure_requirements": {
                "status": "complete",
                "frontier_id": SUCCESSOR_WAVE_PACKAGE["frontier_id"],
                "landed_commit": "00800059",
                "proof_anchors": list(SUCCESSOR_QUEUE_PROOF_ANCHORS),
            },
        },
        unresolved,
    )


def _validate_local_commit_proofs(root: Path) -> tuple[dict[str, Any], list[str]]:
    unresolved: list[str] = []
    commits: list[dict[str, Any]] = []
    git_dir = root / ".git"
    git_available = git_dir.exists()

    for commit, purpose in REQUIRED_LOCAL_COMMIT_PROOFS:
        resolved = False
        if git_available:
            result = subprocess.run(
                ["git", "-C", str(root), "cat-file", "-e", f"{commit}^{{commit}}"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
            resolved = result.returncode == 0
        if git_available and not resolved:
            unresolved.append(commit)
        commits.append(
            {
                "commit": commit,
                "purpose": purpose,
                "status": "passed" if resolved else ("failed" if git_available else "skipped"),
            }
        )

    status = "passed" if git_available and not unresolved else ("failed" if unresolved else "skipped")
    return (
        {
            "status": status,
            "repository": str(root),
            "git_available": git_available,
            "required_commits": commits,
            "missing_commits": unresolved,
        },
        unresolved,
    )


def _build_oracle_suites(root: Path) -> tuple[list[dict[str, Any]], list[str]]:
    suites_spec: list[tuple[str, str, list[str]]] = [
        (
            "creation",
            "Creation legality and deterministic builder entry checks.",
            [
                "Chummer.CoreEngine.Tests/Program.cs::LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService",
                "Chummer.Tests/TestFiles/Fuzzy-chargen.chum5",
            ],
        ),
        (
            "advancement",
            "Career advancement deltas and post-chargen progression checks.",
            [
                "Chummer.CoreEngine.Tests/Program.cs::LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService",
                "Chummer.Tests/TestFiles/Munin_Career.chum5",
            ],
        ),
        (
            "augment",
            "Cyberware/bioware and augmentation parity checks.",
            [
                "Chummer.CoreEngine.Tests/HeroLabRulesParityAudit.cs",
                "Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr5/Two Banshees.por",
            ],
        ),
        (
            "matrix",
            "Matrix-heavy fixture and import parity checks.",
            [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-technomancer-hacker.chum4",
                "Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr6/sr6-starter.hlo.json",
            ],
        ),
        (
            "magic",
            "Magic lane fixture and import parity checks.",
            [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-hermetic-mage.chum4",
                "Chummer.Tests/TestFiles/Spirit_Warden.chum5",
            ],
        ),
        (
            "vehicle",
            "Vehicle and rigger lane fixture checks.",
            [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-rigger-wheelman.chum4",
                "Chummer.Tests/TestFiles/Apex Predator.chum5",
            ],
        ),
        (
            "source_toggle",
            "Source-toggle and source-selection receipt checks.",
            [
                "Chummer.Infrastructure/Xml/XmlToolCatalogService.cs::BuildSourceToggleLaneReceipt",
                "Chummer.Tests/ApiIntegrationTests.cs::sourceToggleLaneReceipt",
            ],
        ),
        (
            "amend_package",
            "Amend package apply/diff receipt checks.",
            [
                "Chummer.Application/Content/DefaultRuleProfileApplicationService.cs",
                "Chummer.Application/Content/DefaultRuntimeLockDiffService.cs",
            ],
        ),
    ]

    suites: list[dict[str, Any]] = []
    unresolved: list[str] = []
    for suite_id, description, evidence in suites_spec:
        status, missing = _evidence_status(root, evidence)
        if missing:
            unresolved.append(suite_id)
        suites.append(
            {
                "id": suite_id,
                "description": description,
                "status": status,
                "evidence": evidence,
                "missing_evidence": missing,
            }
        )

    return suites, unresolved


def _build_budget_map(root: Path) -> tuple[list[dict[str, Any]], list[str]]:
    workload_budgets_path = root / "Chummer.Benchmarks" / "workspace-benchmark-budgets.json"
    benchmark_workload_source_path = root / "Chummer.Benchmarks" / "MigrationWorkspaceBenchmarks.cs"
    workload_budgets = _load_json(workload_budgets_path)
    workloads = workload_budgets.get("workloads") if isinstance(workload_budgets.get("workloads"), list) else []
    benchmark_workload_source = benchmark_workload_source_path.read_text(encoding="utf-8", errors="replace") if benchmark_workload_source_path.is_file() else ""

    by_name: dict[str, dict[str, Any]] = {}
    for row in workloads:
        if not isinstance(row, dict):
            continue
        name = str(row.get("name") or "").strip()
        if not name:
            continue
        by_name[name] = row

    budget_specs: list[tuple[str, str, str]] = [
        ("import", "workspace.import.bastion", "Benchmark budget workload"),
        ("load", "workspace.section.skills.bastion", "Benchmark budget workload"),
        ("diff_apply", "workspace.save.bastion", "Benchmark budget workload"),
        ("explain", "runtime.explain.trace", "Benchmark budget workload"),
        ("export_prep", "workspace.export.bastion", "Benchmark budget workload"),
    ]

    budgets: list[dict[str, Any]] = []
    unresolved: list[str] = []
    for budget_id, workload_name, source in budget_specs:
        workload_payload = by_name.get(workload_name, {})
        executable_workload_present = workload_name in benchmark_workload_source
        ms = float(workload_payload.get("maxMeanMilliseconds") or 0)
        alloc = int(workload_payload.get("maxAllocatedBytes") or 0)
        status = "passed" if workload_payload and executable_workload_present and ms > 0 and alloc > 0 else "failed"
        if status != "passed":
            unresolved.append(budget_id)
        budgets.append(
            {
                "id": budget_id,
                "workload": workload_name,
                "status": status,
                "max_mean_milliseconds": ms,
                "max_allocated_bytes": alloc,
                "source": source,
                "benchmark_budget_source": _to_rel(workload_budgets_path, root),
                "missing_workload": not bool(workload_payload),
                "benchmark_workload_evidence": _to_rel(benchmark_workload_source_path, root),
                "missing_executable_workload": not executable_workload_present,
            }
        )

    return budgets, unresolved


def _build_release_channel_binding(release_channel_path: Path) -> tuple[dict[str, Any], list[str]]:
    payload = _load_json(release_channel_path)
    unresolved: list[str] = []
    if not release_channel_path.is_file():
        unresolved.append("release_channel_missing")

    status = str(payload.get("status") or "").strip().lower()
    rollout_state = str(payload.get("rolloutState") or payload.get("rollout_state") or "").strip().lower()
    channel_id = str(payload.get("channelId") or payload.get("channel_id") or "").strip()
    version = str(payload.get("version") or payload.get("releaseVersion") or "").strip()
    release_proof = payload.get("releaseProof") if isinstance(payload.get("releaseProof"), dict) else {}
    release_proof_status = str(release_proof.get("status") or "").strip().lower()
    desktop_coverage = payload.get("desktopTupleCoverage") if isinstance(payload.get("desktopTupleCoverage"), dict) else {}
    desktop_complete = bool(desktop_coverage.get("complete"))
    route_truth = desktop_coverage.get("desktopRouteTruth") if isinstance(desktop_coverage.get("desktopRouteTruth"), list) else []
    artifacts = payload.get("artifacts") if isinstance(payload.get("artifacts"), list) else []
    artifact_ids = {
        str(row.get("artifactId") or row.get("id") or "").strip()
        for row in artifacts
        if isinstance(row, dict)
    }

    if status != "published":
        unresolved.append("release_channel_status")
    if rollout_state != "promoted_preview":
        unresolved.append("rollout_state")
    if release_proof_status != "passed":
        unresolved.append("release_proof_status")
    if not desktop_complete:
        unresolved.append("desktop_tuple_coverage")

    route_by_tuple = {
        str(row.get("tupleId") or "").strip(): row
        for row in route_truth
        if isinstance(row, dict)
    }

    promoted_primary_tuples: list[dict[str, Any]] = []
    missing_required_tuples: list[str] = []
    for tuple_id, head, platform, rid in REQUIRED_PROMOTED_DESKTOP_TUPLES:
        row = route_by_tuple.get(tuple_id)
        if not isinstance(row, dict):
            missing_required_tuples.append(tuple_id)
            continue

        artifact_id = str(row.get("artifactId") or "").strip()
        row_status = "passed"
        row_unresolved: list[str] = []
        required_values = {
            "head": head,
            "platform": platform,
            "rid": rid,
            "routeRole": "primary",
            "promotionState": "promoted",
            "parityPosture": "flagship_primary",
            "updateEligibility": "eligible",
            "revokeState": "not_revoked",
            "installPosture": "installer_first",
        }
        for key, expected in required_values.items():
            actual = str(row.get(key) or "").strip()
            if actual != expected:
                row_unresolved.append(f"{key}:{actual or '<missing>'}")
        if not artifact_id:
            row_unresolved.append("artifactId:<missing>")
        elif artifact_id not in artifact_ids:
            row_unresolved.append(f"artifact_not_on_shelf:{artifact_id}")
        if row_unresolved:
            row_status = "failed"
            missing_required_tuples.append(tuple_id)
        promoted_primary_tuples.append(
            {
                "tuple_id": tuple_id,
                "head": head,
                "platform": platform,
                "rid": rid,
                "artifact_id": artifact_id,
                "status": row_status,
                "unresolved": row_unresolved,
            }
        )

    if missing_required_tuples:
        unresolved.extend(f"required_promoted_tuple:{tuple_id}" for tuple_id in missing_required_tuples)

    return (
        {
            "status": "passed" if not unresolved else "failed",
            "source_receipt_path": str(release_channel_path),
            "channel_id": channel_id,
            "version": version,
            "release_channel_status": status,
            "rollout_state": rollout_state,
            "release_proof_status": release_proof_status,
            "desktop_tuple_coverage_complete": desktop_complete,
            "required_promoted_desktop_tuples": [
                tuple_id for tuple_id, _, _, _ in REQUIRED_PROMOTED_DESKTOP_TUPLES
            ],
            "promoted_primary_tuples": promoted_primary_tuples,
            "unresolved": unresolved,
        },
        unresolved,
    )


def _oracle_name(row: Any) -> str:
    if isinstance(row, dict):
        return str(row.get("name") or row.get("id") or "").strip()
    return str(row or "").strip()


def _normalize_token(value: str) -> str:
    return value.strip().lower().replace(" ", "")


def _build_import_discipline(
    root: Path,
    import_cert_path: Path,
    import_cert: dict[str, Any],
) -> tuple[dict[str, Any], list[str]]:
    import_status = _extract_status(import_cert)
    import_oracles = import_cert.get("import_oracles") if isinstance(import_cert.get("import_oracles"), list) else []
    adjacent_oracles = import_cert.get("adjacent_oracles") if isinstance(import_cert.get("adjacent_oracles"), list) else []

    unresolved: list[str] = []
    if not import_cert_path.is_file():
        unresolved.append("source_receipt_missing")
    if import_status not in {"pass", "passed", "ready"}:
        unresolved.append("source_receipt_status")

    oracle_by_name = {_normalize_token(_oracle_name(row)): row for row in import_oracles}
    for required_name in REQUIRED_IMPORT_ORACLE_NAMES:
        row = oracle_by_name.get(_normalize_token(required_name))
        if not isinstance(row, dict):
            unresolved.append(f"missing_import_oracle:{required_name}")
            continue
        covered = int(row.get("sources_covered") or 0)
        expected = int(row.get("sources_expected") or 0)
        if expected <= 0 or covered < expected:
            unresolved.append(f"incomplete_import_oracle:{required_name}")

    adjacent_tokens = {_normalize_token(_oracle_name(row)) for row in adjacent_oracles}
    for required_name in REQUIRED_ADJACENT_ORACLE_NAMES:
        if _normalize_token(required_name) not in adjacent_tokens:
            unresolved.append(f"missing_adjacent_oracle:{required_name}")

    return (
        {
            "status": "passed" if not unresolved else "failed",
            "source_receipt_path": _to_rel(import_cert_path, root),
            "source_receipt_status": import_status,
            "required_import_oracle_names": list(REQUIRED_IMPORT_ORACLE_NAMES),
            "required_adjacent_oracle_names": list(REQUIRED_ADJACENT_ORACLE_NAMES),
            "import_oracles": import_oracles,
            "adjacent_oracles": adjacent_oracles,
            "unresolved": unresolved,
        },
        unresolved,
    )


def build_payload(root: Path, generated_output_path: Path | None = None) -> dict[str, Any]:
    import_cert_path = root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
    import_cert = _load_json(import_cert_path)

    oracle_suites, unresolved_suite_ids = _build_oracle_suites(root)
    performance_budgets, unresolved_budget_ids = _build_budget_map(root)
    command_receipts, unresolved_command_ids = _validate_release_commands(root, generated_output_path)
    successor_authority, unresolved_successor_authority_ids = _validate_successor_wave_authority(generated_output_path)
    local_commit_proofs, unresolved_local_commit_ids = _validate_local_commit_proofs(root)
    release_channel_binding, unresolved_release_channel_ids = _build_release_channel_binding(RELEASE_CHANNEL_PATH)
    import_discipline, unresolved_import_ids = _build_import_discipline(root, import_cert_path, import_cert)

    pack_status = (
        "passed"
        if not unresolved_suite_ids
        and not unresolved_budget_ids
        and not unresolved_command_ids
        and not unresolved_successor_authority_ids
        and not unresolved_local_commit_ids
        and not unresolved_release_channel_ids
        and not unresolved_import_ids
        else "failed"
    )

    return {
        "contract_name": "chummer6-core.engine_proof_pack",
        "schema_version": 1,
        "generated_at": _iso_now(),
        "status": pack_status,
        "proof_kind": "release_bound_engine_proof",
        "milestone_id": 104,
        "package_id": "next90-m104-core-proof-pack",
        "successor_wave_package": SUCCESSOR_WAVE_PACKAGE,
        "successor_wave_authority": successor_authority,
        "local_commit_proofs": local_commit_proofs,
        "release_channel_binding": release_channel_binding,
        "release_commands": command_receipts,
        "commands": list(RELEASE_COMMANDS),
        "required_oracle_suite_ids": list(REQUIRED_ORACLE_SUITE_IDS),
        "oracle_suites": oracle_suites,
        "required_performance_budget_ids": list(REQUIRED_BUDGET_IDS),
        "performance_budgets": performance_budgets,
        "import_oracle_discipline": import_discipline,
        "unresolved": {
            "oracle_suites": unresolved_suite_ids,
            "performance_budgets": unresolved_budget_ids,
            "release_commands": unresolved_command_ids,
            "successor_wave_authority": unresolved_successor_authority_ids,
            "local_commit_proofs": unresolved_local_commit_ids,
            "release_channel_binding": unresolved_release_channel_ids,
            "import_oracle_discipline": unresolved_import_ids,
        },
        "notes": (
            "Release-bound engine proof pack for milestone 104. "
            "This receipt keeps golden oracle suites, import-oracle discipline, and budget posture machine-readable "
            "for desktop release waves."
        ),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate ENGINE_PROOF_PACK.generated.json")
    parser.add_argument(
        "--repo-root",
        default=str(Path(__file__).resolve().parents[1]),
        help="path to the chummer-core-engine repo root",
    )
    parser.add_argument(
        "--out",
        default=".codex-studio/published/ENGINE_PROOF_PACK.generated.json",
        help="output path relative to repo root unless absolute",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.repo_root).resolve()
    out = Path(args.out)
    if not out.is_absolute():
        out = root / out

    payload = build_payload(root, out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(str(out))
    if payload.get("status") != "passed":
        unresolved = payload.get("unresolved") if isinstance(payload.get("unresolved"), dict) else {}
        unresolved_ids = {
            key: value
            for key, value in unresolved.items()
            if isinstance(value, list) and value
        }
        print(
            f"ENGINE_PROOF_PACK generation failed closed: {json.dumps(unresolved_ids, sort_keys=True)}",
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
