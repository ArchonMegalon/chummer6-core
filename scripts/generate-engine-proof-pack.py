#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import html
import json
import math
from pathlib import Path
import re
import subprocess
import sys
from typing import Any
from urllib.parse import unquote, unquote_plus

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

REQUIRED_GOLDEN_FIXTURE_COUNT = 10

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

RELEASE_SCOPE = "promoted_desktop_release"

RELEASE_COMMANDS = (
    "dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1 && dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll",
    "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
)

REQUIRED_IMPORT_CERT_COMMANDS = (
    "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release",
)

REQUIRED_IMPORT_CERT_EVIDENCE = (
    "core-engine-tests: ok",
)

REQUIRED_IMPORT_CERT_IDENTITY = {
    "contract_name": "chummer6-core.import_parity_certification",
    "schema_version": 1,
    "proof_kind": "local_parity_harness",
}

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
        "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
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
        "/docker/chummercomplete/chummer-core-engine commit 3c242c2f pins the current f914ce6a helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit ea449f7b pins the current c2872b40 queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit 18365058 pins the current ea449f7b queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit 5031ee41 pins the current 18365058 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit cbce6a19 pins the current 5031ee41 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
        "/docker/chummercomplete/chummer-core-engine commit 71441924 pins the current cbce6a19 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit df1330b4 pins the latest 71441924 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 6610ff2e tightens the M104 proof pack so the df1330b4 queue proof floor must resolve locally.",
        "/docker/chummercomplete/chummer-core-engine commit 2c8742ad fail-closes duplicate M104 package rows in Fleet and design queue staging so future shards verify the unique completed package.",
        "/docker/chummercomplete/chummer-core-engine commit 40babebd pins the latest 5baebb73 queue proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 22171b35 pins the current 40babebd proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 96eca660 pins the current c6fbd75f non-mutating proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 05e47cff binds the M104 proof guard to successor queues so future shards verify the current closed-package guard.",
        "/docker/chummercomplete/chummer-core-engine commit 93d06011 pins the current 05e47cff queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 31aec38a pins the current 93d06011 queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit ceccc309 pins the current 31aec38a queue-bound proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 5dff1a2e tightens worker-safe closure evidence guards for task-local files and run-control helper transcripts.",
        "/docker/chummercomplete/chummer-core-engine commit 2301a043 pins the current 5dff1a2e worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 5c75316f pins the current 2301a043 worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 28be988f pins the current 5c75316f proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit c6a2ee8e pins the current 28be988f proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 6684fc89 pins the current c6a2ee8e proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit ccbfc6b2 pins the current 6684fc89 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 2a3ebcb9 pins the current ccbfc6b2 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 7501f49a pins the current 2a3ebcb9 proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 36311e16 pins the current ac961fe1 local proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit be5755a6 pins the current db3cc033 queue-cited proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 8ffec2b1 pins the latest be5755a6 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 73638668 pins the current 58656418 worker proof hygiene guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit c58d18e1 pins the current 5f50cb7b engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 39c875fd pins the current d584120b local engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit f1b6c5ca pins the queued 39c875fd proof floor into the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit faf14925 pins the current f1b6c5ca proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 64b8f873 requires the current faf14925 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 06a2e06a pins the current 64b8f873 proof floor requirement in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 6d25fb18 pins the current 06a2e06a proof floor receipt in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit cc6cf25b pins the M104 current proof floor guard.",
        "/docker/chummercomplete/chummer-core-engine commit bb9af238 pins the current cc6cf25b proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 44512fcf pins the current bb9af238 proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 5e808a1b pins the current adc72a7e proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit c323b4ad pins the current 5e808a1b queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 7a432bc3 pins the current c323b4ad queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit c124e4af pins the current 7a432bc3 proof pack guard floor in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit 870be707 pins the current af67ecfd handoff proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit b8000b80 tightens the M104 OODA telemetry proof guard so plain governor-loop evidence cannot close the package.",
        "/docker/chummercomplete/chummer-core-engine commit ecbb466c pins the current b8000b80 OODA proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
        "/docker/chummercomplete/chummer-core-engine commit aeeeaf6e tightens M104 package-commit citation proof so registry, Fleet queue, and design queue package-local commit evidence must resolve locally.",
        "/docker/chummercomplete/chummer-core-engine commit c84b251f pins the M104 package-commit citation guard as an explicit local proof floor.",
        "/docker/chummercomplete/chummer-core-engine commit 29b17c68 tightens the M104 engine proof pack release summaries so suite and budget metadata stay release-bound and explainable.",
        "/docker/chummercomplete/chummer-core-engine commit 262030df tightens the M104 proof-pack closure guards so queue closure instructions and import aggregate coverage fail closed.",
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
    "completion_action: verify_closed_package_only",
    "do_not_reopen_reason: M104 chummer6-core engine proof pack is complete;",
    "/docker/chummercomplete/chummer-core-engine/.codex-studio/published/ENGINE_PROOF_PACK.generated.json",
    "/docker/chummercomplete/chummer-core-engine/scripts/generate-engine-proof-pack.py",
    "/docker/chummercomplete/chummer-core-engine/tests/test_engine_proof_pack_generator.py",
    "/docker/chummercomplete/chummer-core-engine/docs/ENGINE_PROOF_PACK.md",
    "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
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
    "/docker/chummercomplete/chummer-core-engine commit 3c242c2f pins the current f914ce6a helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit ea449f7b pins the current c2872b40 queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit 18365058 pins the current ea449f7b queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit 5031ee41 pins the current 18365058 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit cbce6a19 pins the current 5031ee41 queue proof guard in the generated proof pack, unit tests, and proof-pack documentation.",
    "/docker/chummercomplete/chummer-core-engine commit 71441924 pins the current cbce6a19 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit df1330b4 pins the latest 71441924 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 6610ff2e tightens the M104 proof pack so the df1330b4 queue proof floor must resolve locally.",
    "/docker/chummercomplete/chummer-core-engine commit 2c8742ad fail-closes duplicate M104 package rows in Fleet and design queue staging so future shards verify the unique completed package.",
    "/docker/chummercomplete/chummer-core-engine commit 40babebd pins the latest 5baebb73 queue proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 22171b35 pins the current 40babebd proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 96eca660 pins the current c6fbd75f non-mutating proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 05e47cff binds the M104 proof guard to successor queues so future shards verify the current closed-package guard.",
    "/docker/chummercomplete/chummer-core-engine commit 93d06011 pins the current 05e47cff queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 31aec38a pins the current 93d06011 queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit ceccc309 pins the current 31aec38a queue-bound proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 5dff1a2e tightens worker-safe closure evidence guards for task-local files and run-control helper transcripts.",
    "/docker/chummercomplete/chummer-core-engine commit 2301a043 pins the current 5dff1a2e worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 5c75316f pins the current 2301a043 worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 28be988f pins the current 5c75316f proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit c6a2ee8e pins the current 28be988f proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 6684fc89 pins the current c6a2ee8e proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit ccbfc6b2 pins the current 6684fc89 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 2a3ebcb9 pins the current ccbfc6b2 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 7501f49a pins the current 2a3ebcb9 proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 36311e16 pins the current ac961fe1 local proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit be5755a6 pins the current db3cc033 queue-cited proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 8ffec2b1 pins the latest be5755a6 queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 73638668 pins the current 58656418 worker proof hygiene guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit c58d18e1 pins the current 5f50cb7b engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 39c875fd pins the current d584120b local engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit f1b6c5ca pins the queued 39c875fd proof floor into the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit faf14925 pins the current f1b6c5ca proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 64b8f873 requires the current faf14925 proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 06a2e06a pins the current 64b8f873 proof floor requirement in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 6d25fb18 pins the current 06a2e06a proof floor receipt in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit cc6cf25b pins the M104 current proof floor guard.",
    "/docker/chummercomplete/chummer-core-engine commit bb9af238 pins the current cc6cf25b proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 44512fcf pins the current bb9af238 proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 5e808a1b pins the current adc72a7e proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit c323b4ad pins the current 5e808a1b queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 7a432bc3 pins the current c323b4ad queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit c124e4af pins the current 7a432bc3 proof pack guard floor in the generator, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit 870be707 pins the current af67ecfd handoff proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit b8000b80 tightens the M104 OODA telemetry proof guard so plain governor-loop evidence cannot close the package.",
    "/docker/chummercomplete/chummer-core-engine commit ecbb466c pins the current b8000b80 OODA proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt.",
    "/docker/chummercomplete/chummer-core-engine commit aeeeaf6e tightens M104 package-commit citation proof so registry, Fleet queue, and design queue package-local commit evidence must resolve locally.",
    "/docker/chummercomplete/chummer-core-engine commit c84b251f pins the M104 package-commit citation guard as an explicit local proof floor.",
    "/docker/chummercomplete/chummer-core-engine commit 29b17c68 tightens the M104 engine proof pack release summaries so suite and budget metadata stay release-bound and explainable.",
    "/docker/chummercomplete/chummer-core-engine commit 262030df tightens the M104 proof-pack closure guards so queue closure instructions and import aggregate coverage fail closed.",
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
    "/docker/chummercomplete/chummer-core-engine/docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md",
)

PACKAGE_CLOSEOUT_PATH = Path("docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md")
PACKAGE_CLOSEOUT_REQUIRED_TOKENS = (
    "Package: `next90-m104-core-proof-pack`",
    "Frontier: `3227666051`",
    "Milestone: `104`",
    "Owner: `chummer6-core`",
    ".codex-studio/published/ENGINE_PROOF_PACK.generated.json` reports `status=passed`",
    "`successor_wave_authority` reports `status=passed`",
    "`published_golden_fixture_count=10`",
    "`golden_fixtures`",
    "exactly one `next90-m104-core-proof-pack` row",
    "`frontier_id: 3227666051`",
    "`landed_commit: 00800059`",
    "matching `completion_action` and exact `do_not_reopen_reason` text",
    "`completion_action: verify_closed_package_only`",
    "`do_not_reopen_reason`",
    "`src`, `tests`, `docs`, and `scripts`",
    "`engine_proof_pack` and `import_oracle_discipline`",
    "Queue proof anchors resolve inside `/docker/chummercomplete/chummer-core-engine`",
    "`ecbb466c`",
    "`498dff3d`",
    "`a2c8ad9f`",
    "`2c98f61c`",
    "`2e4e8e81`",
    "`8f4702a5`",
    "`c84b251f`",
    "`29b17c68`",
    "`262030df`",
    "Registry and queue evidence do not cite task-local telemetry",
    "active-run handoff field labels",
    "supervisor helper loops",
    "The same proof-hygiene ban applies after percent-decoding and HTML unescaping, after URL form-decoding, and after separator normalization",
    "Do not reopen this core package for adjacent M104 work.",
    "python3 scripts/generate-engine-proof-pack.py --check",
    "python3 tests/test_engine_proof_pack_generator.py",
    "dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1",
    "dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll",
    "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
)
PACKAGE_CLOSEOUT_DISALLOWED_EVIDENCE_TOKENS = (
    "/var/lib/codex-fleet/",
    "TASK_LOCAL_TELEMETRY.generated.json",
    "ACTIVE_RUN_HANDOFF.generated.md",
    "historical operator status",
    "historical operator status snippet",
    "historical operator status snippets",
)

EXPECTED_QUEUE_ALLOWED_PATHS = tuple(SUCCESSOR_WAVE_PACKAGE["allowed_paths"])
EXPECTED_QUEUE_OWNED_SURFACES = tuple(SUCCESSOR_WAVE_PACKAGE["owned_surfaces"])
DISALLOWED_ACTIVE_RUN_PROOF_TOKENS = (
    "/var/lib/codex-fleet/",
    "/docker/fleet/state/chummer_design_supervisor/",
    "ACTIVE_RUN_HANDOFF",
    "TASK_LOCAL_TELEMETRY",
    "active-run telemetry",
    "task-local telemetry",
    "operator telemetry",
    "operator status snippet",
    "operator status snippets",
    "historical operator status",
    "historical operator status snippet",
    "historical operator status snippets",
    "active run helper",
    "active-run helper",
    "active-run helper command",
    "active-run helper commands",
    "operator/OODA telemetry",
    "operator/OODA loop",
    "OODA loop",
    "OODA helper",
    "successor-wave telemetry",
    "implementation-only retry",
    "implementation-only worker",
    "implementation-only pass",
    "implementation-only prompt",
    "implementation-only package",
    "implementation-only proof",
    "implementation-only closeout",
    "implementation-only",
    "previous attempt",
    "previous attempt burned time",
    "run these exact commands first",
    "do not invent another orientation step",
    "first action rule:",
    "do not run supervisor status",
    "do not run supervisor status or eta helpers",
    "read these files directly first",
    "use the shard runtime handoff",
    "execution discipline:",
    "required order:",
    "execution rules inside this run:",
    "assigned slice authority:",
    "assigned successor queue package:",
    "successor frontier detail:",
    "current steering focus:",
    "writable scope roots:",
    "if you stop, report only:",
    "what shipped:",
    "what remains:",
    "exact blocker:",
    "frontier_briefs",
    "first_commands",
    "focus_owners",
    "focus_profiles",
    "focus_texts",
    "status_query_supported",
    "polling_disabled",
    "slice_summary",
    "remaining milestones",
    "remaining queue items",
    "critical path",
    "supervisor status",
    "supervisor status helper",
    "supervisor status or eta helpers",
    "supervisor helper",
    "supervisor helper loop",
    "supervisor helper loops",
    "helper loop",
    "helper loops",
    "status helper",
    "ETA helper",
    "ETA helpers",
    "supervisor ETA",
    "supervisor ETA helper",
    "run id:",
    "frontier ids:",
    "open milestone ids:",
    "focus profiles:",
    "focus owners:",
    "focus texts:",
    "selected account:",
    "selected model:",
    "first output at:",
    "last output at:",
    "prompt path:",
    "recent stderr tail",
)
DISALLOWED_ACTIVE_RUN_PROOF_TOKEN_MATCHES = tuple(
    (token, token.lower()) for token in DISALLOWED_ACTIVE_RUN_PROOF_TOKENS
)
PACKAGE_COMMIT_CITATION_RE = re.compile(
    r"/docker/chummercomplete/chummer-core-engine\s+commit\s+([0-9a-fA-F]{7,40})\b"
)
CANONICAL_COMMIT_CITATION_RE = re.compile(
    r"(/docker/chummercomplete/[^\s]+)\s+commit\s+([0-9a-fA-F]{7,40})\b"
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
    ("77cb53cf", "current M104 helper hygiene proof floor required by local closeout"),
    ("f914ce6a", "current M104 helper hygiene proof floor pin required by local closeout"),
    ("3c242c2f", "current M104 helper hygiene proof floor queue citation required by local closeout"),
    ("c2872b40", "current M104 queue proof floor guard required by local closeout"),
    ("ea449f7b", "current M104 queue proof floor guard pin required by registry and queue closeout"),
    ("18365058", "current M104 queue proof guard pin required by registry and queue closeout"),
    ("5031ee41", "current M104 queue proof guard floor required by registry and queue closeout"),
    ("cbce6a19", "current M104 queue proof guard floor required by registry and queue closeout"),
    ("71441924", "current M104 queue proof floor required by registry and queue closeout"),
    ("df1330b4", "latest M104 queue proof floor required by registry and queue closeout"),
    ("6610ff2e", "current M104 queue proof floor resolution guard required by registry and queue closeout"),
    ("2c8742ad", "current M104 duplicate queue row guard required by registry and queue closeout"),
    ("5baebb73", "latest M104 queue proof guard required by local closeout"),
    ("40babebd", "latest M104 queue proof guard pin required by registry and queue closeout"),
    ("22171b35", "current M104 proof guard pin required by registry and queue closeout"),
    ("c6fbd75f", "current M104 non-mutating proof pack check required by local closeout"),
    ("96eca660", "current M104 non-mutating proof guard pin required by registry and queue closeout"),
    ("05e47cff", "current M104 queue-bound proof guard required by registry and queue closeout"),
    ("93d06011", "current M104 queue-bound proof guard pin required by registry and queue closeout"),
    ("31aec38a", "current M104 queue-bound proof guard floor required by registry and queue closeout"),
    ("ceccc309", "current M104 queue-bound proof floor required by registry and queue closeout"),
    ("5dff1a2e", "current M104 worker-safe closure evidence guard required by registry and queue closeout"),
    ("2301a043", "current M104 worker-safe proof floor required by registry and queue closeout"),
    ("5c75316f", "current M104 worker-safe proof floor pin required by registry and queue closeout"),
    ("28be988f", "current M104 proof floor pin required by registry and queue closeout"),
    ("c6a2ee8e", "current M104 proof floor guard required by registry and queue closeout"),
    ("6684fc89", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("ccbfc6b2", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("2a3ebcb9", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("7501f49a", "current M104 proof floor pin required by registry and queue closeout"),
    ("ac961fe1", "current M104 proof floor pin required by local closeout"),
    ("36311e16", "current M104 local proof floor pin required by queue closeout"),
    ("db3cc033", "current M104 queue-cited proof floor required by local closeout"),
    ("be5755a6", "current M104 queue-cited proof floor pin required by registry and queue closeout"),
    ("8ffec2b1", "latest M104 queue proof floor pin required by registry and queue closeout"),
    ("ee9d88b1", "current M104 local queue proof floor required by local closeout"),
    ("eacefaf2", "current M104 local proof floor required by local closeout"),
    ("e4e502a1", "current M104 proof floor required by local closeout"),
    ("1f2c5724", "current M104 proof pack floor required by local closeout"),
    ("1bcb9b7e", "current M104 proof pack guard required by local closeout"),
    ("e04d7b88", "current M104 checked-in receipt proof guard required by local closeout"),
    ("58656418", "current M104 worker proof hygiene guard required by local closeout"),
    ("73638668", "current M104 worker proof hygiene guard pin required by registry and queue closeout"),
    ("a404b474", "current M104 latest worker proof guard required by local closeout"),
    ("51bb2d8f", "current M104 proof pack guard required by local closeout"),
    ("507f1f6b", "current M104 proof pack guard floor required by local closeout"),
    ("43638c3e", "current M104 proof pack guard floor pin required by local closeout"),
    ("b0776012", "current M104 proof pack floor pin required by local closeout"),
    ("5f50cb7b", "current M104 engine proof floor required by local closeout"),
    ("c58d18e1", "current M104 engine proof floor pin required by registry and queue closeout"),
    ("67e0f654", "current M104 engine proof floor pin required by local closeout"),
    ("d584120b", "current M104 local engine proof floor pin required by local closeout"),
    ("39c875fd", "current M104 queued local engine proof floor pin required by registry and queue closeout"),
    ("f1b6c5ca", "current M104 queued proof floor pin required by registry and queue closeout"),
    ("faf14925", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("64b8f873", "current M104 proof floor guard requirement required by local closeout"),
    ("06a2e06a", "current M104 proof floor requirement pin required by registry and queue closeout"),
    ("6d25fb18", "current M104 proof floor receipt pin required by registry and queue closeout"),
    ("cc6cf25b", "current M104 proof floor guard required by registry and queue closeout"),
    ("bb9af238", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("44512fcf", "current M104 proof floor guard pin required by registry and queue closeout"),
    ("4db6d429", "latest M104 proof floor guard required by local closeout"),
    ("adc72a7e", "current M104 latest local proof floor required by local closeout"),
    ("5e808a1b", "current M104 queue-cited proof floor guard required by registry and queue closeout"),
    ("c323b4ad", "current M104 queue proof floor guard required by registry and queue closeout"),
    ("7a432bc3", "current M104 queue proof floor guard pin required by registry and queue closeout"),
    ("c124e4af", "current M104 proof pack guard floor required by registry and queue closeout"),
    ("5a649e57", "current M104 handoff anchor required by local closeout"),
    ("c01dfa10", "current M104 handoff proof anchor required by local closeout"),
    ("1a98d904", "current M104 documented handoff proof anchor required by local closeout"),
    ("af67ecfd", "current M104 handoff proof floor required by local closeout"),
    ("870be707", "current M104 handoff proof floor pin required by registry and queue closeout"),
    ("498dff3d", "current M104 queue mirror parity guard required by local closeout"),
    ("b8000b80", "current M104 OODA telemetry proof guard required by registry and queue closeout"),
    ("ecbb466c", "current M104 OODA proof guard pin required by registry and queue closeout"),
    ("a2c8ad9f", "current M104 active-run handoff field proof guard required by local closeout"),
    ("2c98f61c", "current M104 closeout handoff evidence guard required by local closeout"),
    ("2e4e8e81", "current M104 handoff evidence proof floor required by local closeout"),
    ("b5d46938", "current M104 engine proof pack discipline guard required by local closeout"),
    ("c1300863", "current M104 engine proof pack discipline guard pin required by local closeout"),
    ("8f4702a5", "current M104 release-bound proof discipline guard required by local closeout"),
    ("aeeeaf6e", "current M104 package-commit citation proof guard required by registry and queue closeout"),
    ("c84b251f", "current M104 package-commit citation local proof floor required by registry and queue closeout"),
    ("29b17c68", "current M104 release-summary proof floor required by registry and queue closeout"),
    ("262030df", "current M104 proof-pack closure guard floor required by registry and queue closeout"),
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


def _without_generated_at(payload: dict[str, Any]) -> dict[str, Any]:
    comparable = dict(payload)
    comparable.pop("generated_at", None)
    return comparable


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


def _validate_package_closeout(root: Path) -> tuple[dict[str, Any], list[str]]:
    closeout_path = root / PACKAGE_CLOSEOUT_PATH
    closeout_text = _read_text(closeout_path)
    missing_tokens = list(PACKAGE_CLOSEOUT_REQUIRED_TOKENS) if not closeout_text else [
        token for token in PACKAGE_CLOSEOUT_REQUIRED_TOKENS if token not in closeout_text
    ]
    lower_text = closeout_text.lower()
    disallowed_closeout_tokens = [
        token for token in PACKAGE_CLOSEOUT_DISALLOWED_EVIDENCE_TOKENS if token.lower() in lower_text
    ]
    disallowed_evidence_tokens = disallowed_closeout_tokens + _find_disallowed_closeout_evidence_tokens(closeout_text)
    disallowed_evidence_tokens = list(dict.fromkeys(disallowed_evidence_tokens))

    unresolved: list[str] = []
    if not closeout_path.is_file():
        unresolved.append("closeout_document_missing")
    if missing_tokens:
        unresolved.append("closeout_required_tokens")
    if disallowed_evidence_tokens:
        unresolved.append("closeout_disallowed_active_run_evidence")

    return (
        {
            "status": "passed" if not unresolved else "failed",
            "path": _to_rel(closeout_path, root),
            "required_tokens": list(PACKAGE_CLOSEOUT_REQUIRED_TOKENS),
            "missing_tokens": missing_tokens,
            "disallowed_evidence_tokens": disallowed_evidence_tokens,
        },
        unresolved,
    )


def _find_disallowed_active_run_tokens(text: str) -> list[str]:
    lower_text = "\n".join(_proof_text_variants(text))
    return [
        token
        for token, normalized in DISALLOWED_ACTIVE_RUN_PROOF_TOKEN_MATCHES
        if normalized in lower_text or _normalize_proof_separators(normalized) in lower_text
    ]


def _proof_text_variants(text: str) -> list[str]:
    variants: list[str] = []
    candidates = [text, html.unescape(text)]
    for candidate in candidates:
        stack = [candidate]
        for _ in range(4):
            next_stack: list[str] = []
            for current in stack:
                lowered = current.lower()
                if lowered not in variants:
                    variants.append(lowered)
                separator_normalized = _normalize_proof_separators(lowered)
                if separator_normalized not in variants:
                    variants.append(separator_normalized)
                for decoded in (unquote(current), unquote_plus(current)):
                    if decoded != current and decoded not in next_stack:
                        next_stack.append(decoded)
            if not next_stack:
                break
            stack = next_stack
    return variants


def _normalize_proof_separators(text: str) -> str:
    return re.sub(r"[\s._:/\\+-]+", " ", text).strip()


def _find_disallowed_closeout_evidence_tokens(text: str) -> list[str]:
    evidence_lines = [
        line
        for line in text.splitlines()
        if "proof:" in line.lower() or "evidence:" in line.lower()
    ]
    return _find_disallowed_active_run_tokens("\n".join(evidence_lines))


def _extract_list_item_block(text: str, item_header: str) -> str:
    blocks = _extract_list_item_blocks(text, item_header)
    return blocks[0] if blocks else ""


def _extract_list_item_blocks(text: str, item_header: str) -> list[str]:
    lines = text.splitlines()
    blocks: list[str] = []
    for match_index, line in enumerate(lines):
        stripped = line.lstrip()
        if stripped != item_header and stripped != f"- {item_header}":
            continue

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
        for block_line in lines[start_index + 1 :]:
            stripped = block_line.lstrip()
            indent = len(block_line) - len(stripped)
            if indent == item_indent and stripped.startswith("- "):
                break
            block_lines.append(block_line)
        blocks.append("\n".join(block_lines) + "\n")
    return blocks


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


def _extract_yaml_scalar_value(block: str, key: str) -> str:
    for line in block.splitlines():
        stripped = line.lstrip()
        prefix = f"{key}:"
        if not stripped.startswith(prefix):
            continue
        return stripped[len(prefix) :].strip()
    return ""


def _list_drift(actual: list[str], expected: tuple[str, ...]) -> tuple[list[str], list[str]]:
    actual_set = set(actual)
    expected_set = set(expected)
    missing = [value for value in expected if value not in actual_set]
    unexpected = [value for value in actual if value not in expected_set]
    return missing, unexpected


def _duplicate_values(values: list[str]) -> list[str]:
    seen: set[str] = set()
    duplicates: list[str] = []
    for value in values:
        if value in seen and value not in duplicates:
            duplicates.append(value)
        seen.add(value)
    return duplicates


def _duplicate_workload_names(workloads: list[Any]) -> list[str]:
    names = [
        str(row.get("name") or "").strip()
        for row in workloads
        if isinstance(row, dict) and str(row.get("name") or "").strip()
    ]
    return _duplicate_values(names)


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


def _queue_proof_path_items(proof_items: list[str]) -> list[str]:
    path_items: list[str] = []
    canonical_prefix = str(CANONICAL_CHUMMER_ROOT) + "/"
    for item in proof_items:
        path_part = item.partition("::")[0].strip()
        if not path_part.startswith(canonical_prefix):
            continue
        if " commit " in path_part:
            continue
        path_items.append(path_part)
    return path_items


def _path_is_planned_generated(path: Path, generated_output_path: Path | None) -> bool:
    return generated_output_path is not None and path.resolve() == generated_output_path.resolve()


def _validate_queue_authority(queue_path: Path, generated_output_path: Path | None = None) -> dict[str, Any]:
    queue_text = _read_text(queue_path)
    queue_scopes = _extract_list_item_blocks(queue_text, "package_id: next90-m104-core-proof-pack")
    queue_scope = queue_scopes[0] if queue_scopes else ""
    missing_queue_tokens = ["queue_item:next90-m104-core-proof-pack"] if not queue_scope else []
    duplicate_package_rows = max(0, len(queue_scopes) - 1)
    if duplicate_package_rows:
        missing_queue_tokens.append("duplicate_queue_item:next90-m104-core-proof-pack")
    missing_queue_tokens.extend(token for token in SUCCESSOR_QUEUE_TOKENS if token not in queue_scope)
    disallowed_active_run_tokens = _find_disallowed_active_run_tokens(queue_scope)
    queue_proof = _extract_yaml_list_values(queue_scope, "proof") if queue_scope else []
    missing_queue_proof_anchors = []
    for anchor in SUCCESSOR_QUEUE_PROOF_ANCHORS:
        anchor_path = Path(anchor)
        if not anchor_path.exists() and not _path_is_planned_generated(anchor_path, generated_output_path):
            missing_queue_proof_anchors.append(anchor)
    queue_path_proof_anchors = _queue_proof_path_items(queue_proof)
    for anchor in queue_path_proof_anchors:
        anchor_path = Path(anchor)
        if _is_canonical_anchor_outside_package(anchor_path):
            continue
        if not anchor_path.exists() and not _path_is_planned_generated(anchor_path, generated_output_path):
            missing_queue_proof_anchors.append(anchor)
    missing_queue_proof_anchors = list(dict.fromkeys(missing_queue_proof_anchors))
    off_package_queue_proof_anchors = [
        anchor
        for anchor in queue_path_proof_anchors
        if _is_canonical_anchor_outside_package(Path(anchor))
    ]
    queue_allowed_paths = _extract_yaml_list_values(queue_scope, "allowed_paths") if queue_scope else []
    missing_queue_allowed_paths, unexpected_queue_allowed_paths = _list_drift(queue_allowed_paths, EXPECTED_QUEUE_ALLOWED_PATHS)
    queue_owned_surfaces = _extract_yaml_list_values(queue_scope, "owned_surfaces") if queue_scope else []
    missing_queue_owned_surfaces, unexpected_queue_owned_surfaces = _list_drift(queue_owned_surfaces, EXPECTED_QUEUE_OWNED_SURFACES)
    duplicate_queue_proof_items = _duplicate_values(queue_proof)
    for value in missing_queue_allowed_paths:
        missing_queue_tokens.append(f"allowed_paths:{value}")
    for value in unexpected_queue_allowed_paths:
        missing_queue_tokens.append(f"unexpected_allowed_path:{value}")
    for value in missing_queue_owned_surfaces:
        missing_queue_tokens.append(f"owned_surfaces:{value}")
    for value in unexpected_queue_owned_surfaces:
        missing_queue_tokens.append(f"unexpected_owned_surface:{value}")
    for value in duplicate_queue_proof_items:
        missing_queue_tokens.append(f"duplicate_proof_item:{value}")

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
        "package_row_count": len(queue_scopes),
        "duplicate_package_rows": duplicate_package_rows,
        "allowed_paths": queue_allowed_paths,
        "expected_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
        "unexpected_allowed_paths": unexpected_queue_allowed_paths,
        "owned_surfaces": queue_owned_surfaces,
        "expected_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
        "unexpected_owned_surfaces": unexpected_queue_owned_surfaces,
        "proof": queue_proof,
        "path_proof_anchors": queue_path_proof_anchors,
    }


def _extract_package_commit_citations(text: str) -> list[str]:
    commits: list[str] = []
    for match in PACKAGE_COMMIT_CITATION_RE.finditer(text):
        commit = match.group(1).lower()
        if commit not in commits:
            commits.append(commit)
    return commits


def _extract_off_package_commit_citations(text: str) -> list[str]:
    citations: list[str] = []
    for match in CANONICAL_COMMIT_CITATION_RE.finditer(text):
        repo_path = Path(match.group(1))
        commit = match.group(2).lower()
        if not _is_canonical_anchor_outside_package(repo_path):
            continue
        citation = f"{repo_path} commit {commit}"
        if citation not in citations:
            citations.append(citation)
    return citations


def _validate_local_commit_set(root: Path, commits: list[str]) -> tuple[dict[str, Any], list[str]]:
    unresolved: list[str] = []
    git_available = (root / ".git").exists()
    rows: list[dict[str, Any]] = []
    for commit in commits:
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
        rows.append(
            {
                "commit": commit,
                "status": "passed" if resolved else ("failed" if git_available else "skipped"),
            }
        )

    return (
        {
            "status": "passed" if git_available and not unresolved else ("failed" if unresolved else "skipped"),
            "git_available": git_available,
            "commits": rows,
            "missing_commits": unresolved,
        },
        unresolved,
    )


def _validate_successor_wave_authority(root: Path, generated_output_path: Path | None = None) -> tuple[dict[str, Any], list[str]]:
    registry_path = Path(SUCCESSOR_WAVE_PACKAGE["source_registry_path"])
    queue_path = Path(SUCCESSOR_WAVE_PACKAGE["source_queue_path"])
    design_queue_path = Path(SUCCESSOR_WAVE_PACKAGE["source_design_queue_path"])

    registry_text = _read_text(registry_path)
    registry_scope = _extract_list_item_block(registry_text, "id: 104")
    registry_missing_scope_tokens = ["milestone_block:id:104"] if not registry_scope else []
    missing_registry_tokens = registry_missing_scope_tokens + [token for token in SUCCESSOR_REGISTRY_MILESTONE_TOKENS if token not in registry_scope]
    missing_registry_task_tokens: dict[str, list[str]] = {}
    disallowed_registry_active_run_tokens: dict[str, list[str]] = {}
    registry_task_scopes: list[str] = []
    for task_id, required_tokens in SUCCESSOR_REGISTRY_TASK_TOKENS.items():
        task_scope = _extract_list_item_block(registry_scope, f"id: {task_id}") if registry_scope else ""
        if task_scope:
            registry_task_scopes.append(task_scope)
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
    fleet_queue_scope = _extract_list_item_block(
        _read_text(queue_path),
        "package_id: next90-m104-core-proof-pack",
    )
    design_queue_scope = _extract_list_item_block(
        _read_text(design_queue_path),
        "package_id: next90-m104-core-proof-pack",
    )
    package_proof_scopes = registry_task_scopes + [fleet_queue_scope, design_queue_scope]
    package_proof_text = "\n".join(scope for scope in package_proof_scopes if scope)
    package_commit_citations, unresolved_package_commit_citations = _validate_local_commit_set(
        root,
        _extract_package_commit_citations(package_proof_text),
    )
    off_package_package_commit_citations = _extract_off_package_commit_citations(package_proof_text)
    queue_proof = fleet_queue_authority["proof"]
    design_queue_proof = design_queue_authority["proof"]
    queue_completion_action = _extract_yaml_scalar_value(fleet_queue_scope, "completion_action")
    design_queue_completion_action = _extract_yaml_scalar_value(design_queue_scope, "completion_action")
    queue_do_not_reopen_reason = _extract_yaml_scalar_value(fleet_queue_scope, "do_not_reopen_reason")
    design_queue_do_not_reopen_reason = _extract_yaml_scalar_value(design_queue_scope, "do_not_reopen_reason")
    queue_proof_set = set(queue_proof)
    design_queue_proof_set = set(design_queue_proof)
    queue_proof_missing_from_design_queue = [
        proof for proof in queue_proof if proof not in design_queue_proof_set
    ]
    design_queue_proof_missing_from_queue = [
        proof for proof in design_queue_proof if proof not in queue_proof_set
    ]
    queue_closure_field_drift: list[str] = []
    if queue_completion_action != design_queue_completion_action:
        queue_closure_field_drift.append("completion_action")
    if queue_do_not_reopen_reason != design_queue_do_not_reopen_reason:
        queue_closure_field_drift.append("do_not_reopen_reason")
    queue_mirror_parity_status = (
        "passed"
        if not queue_proof_missing_from_design_queue
        and not design_queue_proof_missing_from_queue
        and not queue_closure_field_drift
        else "failed"
    )
    registry_status = "passed" if not missing_registry_tokens else "failed"

    unresolved: list[str] = []
    if registry_status != "passed":
        unresolved.append("source_registry")
    if fleet_queue_authority["status"] != "passed":
        unresolved.append("source_queue")
    if design_queue_authority["status"] != "passed":
        unresolved.append("source_design_queue")
    if queue_mirror_parity_status != "passed":
        unresolved.append("queue_mirror_parity")
    if unresolved_package_commit_citations:
        unresolved.append("package_commit_citations")
    if off_package_package_commit_citations:
        unresolved.append("off_package_package_commit_citations")

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
            "queue_package_row_count": fleet_queue_authority["package_row_count"],
            "duplicate_queue_package_rows": fleet_queue_authority["duplicate_package_rows"],
            "queue_allowed_paths": fleet_queue_authority["allowed_paths"],
            "expected_queue_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
            "unexpected_queue_allowed_paths": fleet_queue_authority["unexpected_allowed_paths"],
            "queue_owned_surfaces": fleet_queue_authority["owned_surfaces"],
            "expected_queue_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
            "unexpected_queue_owned_surfaces": fleet_queue_authority["unexpected_owned_surfaces"],
            "queue_path_proof_anchors": fleet_queue_authority["path_proof_anchors"],
            "design_queue_missing_tokens": design_queue_authority["missing_tokens"],
            "design_queue_missing_proof_anchors": design_queue_authority["missing_proof_anchors"],
            "design_queue_off_package_proof_anchors": design_queue_authority["off_package_proof_anchors"],
            "disallowed_design_queue_active_run_tokens": design_queue_authority["disallowed_active_run_tokens"],
            "design_queue_package_row_count": design_queue_authority["package_row_count"],
            "duplicate_design_queue_package_rows": design_queue_authority["duplicate_package_rows"],
            "design_queue_allowed_paths": design_queue_authority["allowed_paths"],
            "expected_design_queue_allowed_paths": list(EXPECTED_QUEUE_ALLOWED_PATHS),
            "unexpected_design_queue_allowed_paths": design_queue_authority["unexpected_allowed_paths"],
            "design_queue_owned_surfaces": design_queue_authority["owned_surfaces"],
            "expected_design_queue_owned_surfaces": list(EXPECTED_QUEUE_OWNED_SURFACES),
            "unexpected_design_queue_owned_surfaces": design_queue_authority["unexpected_owned_surfaces"],
            "design_queue_path_proof_anchors": design_queue_authority["path_proof_anchors"],
            "queue_proof_count": len(queue_proof),
            "design_queue_proof_count": len(design_queue_proof),
            "queue_completion_action": queue_completion_action,
            "design_queue_completion_action": design_queue_completion_action,
            "queue_do_not_reopen_reason": queue_do_not_reopen_reason,
            "design_queue_do_not_reopen_reason": design_queue_do_not_reopen_reason,
            "queue_proof_missing_from_design_queue": queue_proof_missing_from_design_queue,
            "design_queue_proof_missing_from_queue": design_queue_proof_missing_from_queue,
            "queue_closure_field_drift": queue_closure_field_drift,
            "queue_mirror_parity_status": queue_mirror_parity_status,
            "package_commit_citations": package_commit_citations,
            "off_package_package_commit_citations": off_package_package_commit_citations,
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
    suites_spec: list[dict[str, Any]] = [
        {
            "id": "creation",
            "description": "Creation legality and deterministic builder entry checks.",
            "coverage_focus": "builder_entry",
            "rulesets": ["sr5"],
            "evidence": [
                "Chummer.CoreEngine.Tests/Program.cs::LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService",
                "Chummer.Tests/TestFiles/Fuzzy-chargen.chum5",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/sr5-parity-corpus.golden.json",
            ],
        },
        {
            "id": "advancement",
            "description": "Career advancement deltas and post-chargen progression checks.",
            "coverage_focus": "career_progression",
            "rulesets": ["sr5"],
            "evidence": [
                "Chummer.CoreEngine.Tests/Program.cs::LegacyChummer5FixtureCorpusImportsRoundTripThroughWorkspaceService",
                "Chummer.Tests/TestFiles/Munin_Career.chum5",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/session-ledger.golden.json",
            ],
        },
        {
            "id": "augment",
            "description": "Cyberware/bioware and augmentation parity checks.",
            "coverage_focus": "augmentation_interactions",
            "rulesets": ["sr5"],
            "evidence": [
                "Chummer.CoreEngine.Tests/HeroLabRulesParityAudit.cs",
                "Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr5/Two Banshees.por",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-diff.golden.json",
            ],
        },
        {
            "id": "matrix",
            "description": "Matrix-heavy fixture and import parity checks.",
            "coverage_focus": "matrix_edge_cases",
            "rulesets": ["sr4", "sr6"],
            "evidence": [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-technomancer-hacker.chum4",
                "Chummer.CoreEngine.Tests/Fixtures/HeroLab/Sr6/sr6-starter.hlo.json",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/sr4-parity-corpus.golden.json",
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/sr6-parity-corpus.golden.json",
            ],
        },
        {
            "id": "magic",
            "description": "Magic lane fixture and import parity checks.",
            "coverage_focus": "magic_edge_cases",
            "rulesets": ["sr4", "sr5"],
            "evidence": [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-hermetic-mage.chum4",
                "Chummer.Tests/TestFiles/Spirit_Warden.chum5",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/explain-trace.golden.json",
            ],
        },
        {
            "id": "vehicle",
            "description": "Vehicle and rigger lane fixture checks.",
            "coverage_focus": "vehicle_edge_cases",
            "rulesets": ["sr4", "sr5"],
            "evidence": [
                "Chummer.CoreEngine.Tests/Fixtures/Sr4/sr4-rigger-wheelman.chum4",
                "Chummer.Tests/TestFiles/Apex Predator.chum5",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-summary.golden.json",
            ],
        },
        {
            "id": "source_toggle",
            "description": "Source-toggle and source-selection receipt checks.",
            "coverage_focus": "sourcebook_and_settings_discipline",
            "rulesets": ["sr5"],
            "evidence": [
                "Chummer.Infrastructure/Xml/XmlToolCatalogService.cs::BuildSourceToggleLaneReceipt",
                "Chummer.Tests/ApiIntegrationTests.cs::sourceToggleLaneReceipt",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/buildkit-manifest.normalized.golden.json",
            ],
        },
        {
            "id": "amend_package",
            "description": "Amend package apply/diff receipt checks.",
            "coverage_focus": "custom_data_and_xml_diff_apply",
            "rulesets": ["sr5"],
            "evidence": [
                "Chummer.Application/Content/DefaultRuleProfileApplicationService.cs",
                "Chummer.Application/Content/DefaultRuntimeLockDiffService.cs",
            ],
            "golden_fixtures": [
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-preview.normalized.golden.json",
                "Chummer.CoreEngine.Tests/Fixtures/Contracts/runtime-lock-install-candidate.normalized.golden.json",
            ],
        },
    ]

    suites: list[dict[str, Any]] = []
    unresolved: list[str] = []
    for suite_spec in suites_spec:
        suite_id = str(suite_spec["id"])
        description = str(suite_spec["description"])
        evidence = list(suite_spec["evidence"])
        golden_fixtures = list(suite_spec.get("golden_fixtures", []))
        rulesets = [str(row).strip().lower() for row in suite_spec.get("rulesets", []) if str(row).strip()]
        status, missing = _evidence_status(root, evidence)
        _, missing_golden_fixtures = _evidence_status(root, golden_fixtures)
        if missing or missing_golden_fixtures:
            unresolved.append(suite_id)
        suites.append(
            {
                "id": suite_id,
                "description": description,
                "coverage_focus": str(suite_spec.get("coverage_focus") or "").strip(),
                "rulesets": rulesets,
                "release_scope": RELEASE_SCOPE,
                "fixture_count": len(evidence),
                "golden_fixture_count": len(golden_fixtures),
                "total_fixture_count": len(evidence) + len(golden_fixtures),
                "status": "passed" if status == "passed" and not missing_golden_fixtures else "failed",
                "evidence": evidence,
                "missing_evidence": missing,
                "golden_fixtures": golden_fixtures,
                "missing_golden_fixtures": missing_golden_fixtures,
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
    duplicate_workloads = _duplicate_workload_names(workloads)
    duplicate_workload_set = set(duplicate_workloads)

    budget_specs: list[dict[str, str]] = [
        {
            "id": "import",
            "workload": "workspace.import.bastion",
            "source": "Benchmark budget workload",
            "release_gate": "desktop_release_import",
            "scenario": "Import Bastion legacy workspace into the promoted desktop release candidate.",
        },
        {
            "id": "load",
            "workload": "workspace.section.skills.bastion",
            "source": "Benchmark budget workload",
            "release_gate": "desktop_release_load",
            "scenario": "Load the imported Bastion workspace and hydrate the skills section for flagship desktop review.",
        },
        {
            "id": "diff_apply",
            "workload": "workspace.save.bastion",
            "source": "Benchmark budget workload",
            "release_gate": "desktop_release_diff_apply",
            "scenario": "Save the imported Bastion workspace so release-bound diff/apply posture stays under budget.",
        },
        {
            "id": "explain",
            "workload": "runtime.explain.trace",
            "source": "Benchmark budget workload",
            "release_gate": "desktop_release_explain",
            "scenario": "Compose an explain trace receipt for the promoted desktop release proof pack.",
        },
        {
            "id": "export_prep",
            "workload": "workspace.export.bastion",
            "source": "Benchmark budget workload",
            "release_gate": "desktop_release_export_prep",
            "scenario": "Prepare export payloads from the imported Bastion workspace for release-bound exchange proof.",
        },
    ]

    budgets: list[dict[str, Any]] = []
    unresolved: list[str] = []
    for budget_spec in budget_specs:
        budget_id = budget_spec["id"]
        workload_name = budget_spec["workload"]
        source = budget_spec["source"]
        workload_payload = by_name.get(workload_name, {})
        executable_workload_present = _benchmark_workload_is_executable(benchmark_workload_source, workload_name)
        duplicate_workload = workload_name in duplicate_workload_set
        ms = _try_float(workload_payload.get("maxMeanMilliseconds")) if workload_payload else None
        alloc = _try_json_int(workload_payload.get("maxAllocatedBytes")) if workload_payload else None
        malformed_threshold = workload_payload and (
            ms is None or alloc is None or not math.isfinite(ms)
        )
        status = (
            "passed"
            if workload_payload
            and executable_workload_present
            and not duplicate_workload
            and ms is not None
            and alloc is not None
            and math.isfinite(ms)
            and ms > 0
            and alloc > 0
            else "failed"
        )
        if status != "passed":
            unresolved.append(budget_id)
        budgets.append(
            {
                "id": budget_id,
                "workload": workload_name,
                "release_gate": budget_spec["release_gate"],
                "scenario": budget_spec["scenario"],
                "release_scope": RELEASE_SCOPE,
                "status": status,
                "max_mean_milliseconds": ms or 0,
                "max_allocated_bytes": alloc or 0,
                "source": source,
                "benchmark_budget_source": _to_rel(workload_budgets_path, root),
                "missing_workload": not bool(workload_payload),
                "benchmark_workload_evidence": _to_rel(benchmark_workload_source_path, root),
                "missing_executable_workload": not executable_workload_present,
                "duplicate_workload": duplicate_workload,
                "duplicate_workload_names": duplicate_workloads,
                "malformed_threshold": bool(malformed_threshold),
            }
        )

    return budgets, unresolved


def _build_oracle_suite_summary(suites: list[dict[str, Any]]) -> dict[str, Any]:
    passed_count = sum(1 for row in suites if row.get("status") == "passed")
    required_suite_ids = list(REQUIRED_ORACLE_SUITE_IDS)
    published_suite_ids = [str(row.get("id") or "").strip() for row in suites if str(row.get("id") or "").strip()]
    matching_suite_ids = sorted(published_suite_ids) == sorted(required_suite_ids)
    duplicate_suite_ids = _duplicate_values(published_suite_ids)
    missing_required_suite_ids = [
        suite_id for suite_id in required_suite_ids if suite_id not in published_suite_ids
    ]
    unexpected_published_suite_ids = [
        suite_id for suite_id in published_suite_ids if suite_id not in required_suite_ids
    ]
    rulesets = sorted(
        {
            str(ruleset).strip().lower()
            for row in suites
            for ruleset in row.get("rulesets", [])
            if str(ruleset).strip()
        }
    )
    published_golden_fixture_count = sum(
        int(row.get("golden_fixture_count") or 0) for row in suites if isinstance(row.get("golden_fixture_count"), int)
    )
    return {
        "required_suite_ids": required_suite_ids,
        "required_suite_count": len(REQUIRED_ORACLE_SUITE_IDS),
        "published_suite_count": len(suites),
        "passed_suite_count": passed_count,
        "required_golden_fixture_count": REQUIRED_GOLDEN_FIXTURE_COUNT,
        "published_golden_fixture_count": published_golden_fixture_count,
        "missing_required_suite_ids": missing_required_suite_ids,
        "unexpected_published_suite_ids": unexpected_published_suite_ids,
        "duplicate_published_suite_ids": duplicate_suite_ids,
        "coverage_status": (
            "passed"
            if passed_count == len(REQUIRED_ORACLE_SUITE_IDS) and matching_suite_ids
            else "failed"
        ),
        "covered_rulesets": rulesets,
        "release_scope": RELEASE_SCOPE,
    }


def _build_budget_summary(budgets: list[dict[str, Any]]) -> dict[str, Any]:
    passed_count = sum(1 for row in budgets if row.get("status") == "passed")
    required_budget_ids = list(REQUIRED_BUDGET_IDS)
    published_budget_ids = [str(row.get("id") or "").strip() for row in budgets if str(row.get("id") or "").strip()]
    matching_budget_ids = sorted(published_budget_ids) == sorted(required_budget_ids)
    duplicate_budget_ids = _duplicate_values(published_budget_ids)
    missing_required_budget_ids = [
        budget_id for budget_id in required_budget_ids if budget_id not in published_budget_ids
    ]
    unexpected_published_budget_ids = [
        budget_id for budget_id in published_budget_ids if budget_id not in required_budget_ids
    ]
    return {
        "required_budget_ids": required_budget_ids,
        "required_budget_count": len(REQUIRED_BUDGET_IDS),
        "published_budget_count": len(budgets),
        "passed_budget_count": passed_count,
        "missing_required_budget_ids": missing_required_budget_ids,
        "unexpected_published_budget_ids": unexpected_published_budget_ids,
        "duplicate_published_budget_ids": duplicate_budget_ids,
        "coverage_status": (
            "passed"
            if passed_count == len(REQUIRED_BUDGET_IDS) and matching_budget_ids
            else "failed"
        ),
        "release_commands": list(RELEASE_COMMANDS),
        "release_scope": RELEASE_SCOPE,
    }


def _benchmark_workload_is_executable(source: str, workload_name: str) -> bool:
    escaped_workload = re.escape(workload_name)
    benchmark_attribute = re.search(
        rf'\[Benchmark\s*\(\s*Description\s*=\s*"{escaped_workload}"\s*\)\]',
        source,
    )
    budget_factory_row = re.search(
        rf'\bName\s*:\s*"{escaped_workload}"',
        source,
    )
    return benchmark_attribute is not None and budget_factory_row is not None


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
    malformed_artifact_rows = [
        index
        for index, row in enumerate(artifacts)
        if not isinstance(row, dict) or not str(row.get("artifactId") or row.get("id") or "").strip()
    ]
    artifact_id_values = [
        str(row.get("artifactId") or row.get("id") or "").strip()
        for row in artifacts
        if isinstance(row, dict)
        and str(row.get("artifactId") or row.get("id") or "").strip()
    ]
    artifact_ids = set(artifact_id_values)
    duplicate_artifact_ids = _duplicate_values(artifact_id_values)
    malformed_route_truth_rows = [
        index
        for index, row in enumerate(route_truth)
        if not isinstance(row, dict) or not str(row.get("tupleId") or "").strip()
    ]
    route_tuple_values = [
        str(row.get("tupleId") or "").strip()
        for row in route_truth
        if isinstance(row, dict)
        and str(row.get("tupleId") or "").strip()
    ]
    duplicate_route_tuple_ids = _duplicate_values(route_tuple_values)

    if status != "published":
        unresolved.append("release_channel_status")
    if rollout_state != "promoted_preview":
        unresolved.append("rollout_state")
    if release_proof_status != "passed":
        unresolved.append("release_proof_status")
    if not desktop_complete:
        unresolved.append("desktop_tuple_coverage")
    unresolved.extend(f"malformed_artifact_row:{index}" for index in malformed_artifact_rows)
    unresolved.extend(f"malformed_desktop_tuple_row:{index}" for index in malformed_route_truth_rows)
    unresolved.extend(f"duplicate_artifact_id:{artifact_id}" for artifact_id in duplicate_artifact_ids)
    unresolved.extend(f"duplicate_desktop_tuple:{tuple_id}" for tuple_id in duplicate_route_tuple_ids)

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


def _oracle_coverage_is_complete(row: Any) -> bool:
    if not isinstance(row, dict):
        return False
    if "sources_covered" not in row or "sources_expected" not in row:
        return False
    covered = _try_json_int(row.get("sources_covered"))
    expected = _try_json_int(row.get("sources_expected"))
    if covered is None or expected is None:
        return False
    return expected > 0 and covered == expected


def _coverage_summary_is_complete(row: Any, expected_total: int) -> bool:
    if not isinstance(row, dict):
        return False
    covered = _try_json_int(row.get("sources_covered"))
    expected = _try_json_int(row.get("sources_expected"))
    percent = _try_json_int(row.get("coverage_percent"))
    if covered is None or expected is None or percent is None:
        return False
    return expected == expected_total and covered == expected_total and percent == 100


def _try_json_int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    return None


def _try_int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        if not math.isfinite(value) or not value.is_integer():
            return None
        return int(value)
    return None


def _try_float(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


def _string_list_errors(payload: dict[str, Any], key: str) -> tuple[bool, bool]:
    rows = payload.get(key)
    if not isinstance(rows, list):
        return False, False
    has_value = any(isinstance(row, str) and row.strip() for row in rows)
    has_malformed = any(not isinstance(row, str) or not row.strip() for row in rows)
    return has_value, has_malformed


def _string_list_values(payload: dict[str, Any], key: str) -> list[str]:
    rows = payload.get(key)
    if not isinstance(rows, list):
        return []
    return [row.strip() for row in rows if isinstance(row, str) and row.strip()]


def _duplicate_oracle_names(rows: list[Any]) -> list[str]:
    seen: set[str] = set()
    duplicates: list[str] = []
    for row in rows:
        normalized = _normalize_token(_oracle_name(row))
        if not normalized:
            continue
        if normalized in seen and normalized not in duplicates:
            duplicates.append(normalized)
        seen.add(normalized)
    return duplicates


def _malformed_oracle_rows(rows: list[Any]) -> list[int]:
    malformed: list[int] = []
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            malformed.append(index)
            continue
        if not _normalize_token(_oracle_name(row)):
            malformed.append(index)
    return malformed


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
    coverage_summary = import_cert.get("coverage")
    expected_coverage_total = len(REQUIRED_IMPORT_ORACLE_NAMES) + len(REQUIRED_ADJACENT_ORACLE_NAMES)

    unresolved: list[str] = []
    if not import_cert_path.is_file():
        unresolved.append("source_receipt_missing")
    for key, expected_value in REQUIRED_IMPORT_CERT_IDENTITY.items():
        if import_cert.get(key) != expected_value:
            unresolved.append(f"source_receipt_identity:{key}")
    if import_status not in {"pass", "passed", "ready"}:
        unresolved.append("source_receipt_status")
    has_commands, malformed_commands = _string_list_errors(import_cert, "commands")
    if not has_commands:
        unresolved.append("source_receipt_commands")
    if malformed_commands:
        unresolved.append("source_receipt_commands_malformed")
    has_evidence, malformed_evidence = _string_list_errors(import_cert, "evidence")
    if not has_evidence:
        unresolved.append("source_receipt_evidence")
    if malformed_evidence:
        unresolved.append("source_receipt_evidence_malformed")
    if not _coverage_summary_is_complete(coverage_summary, expected_coverage_total):
        unresolved.append("source_receipt_coverage")

    command_values = _string_list_values(import_cert, "commands")
    evidence_values = _string_list_values(import_cert, "evidence")
    duplicate_commands = _duplicate_values(command_values)
    duplicate_evidence = _duplicate_values(evidence_values)
    missing_required_commands = [
        command for command in REQUIRED_IMPORT_CERT_COMMANDS if command not in command_values
    ]
    missing_required_evidence = [
        evidence for evidence in REQUIRED_IMPORT_CERT_EVIDENCE if evidence not in evidence_values
    ]
    unexpected_commands = [
        command for command in command_values if command not in REQUIRED_IMPORT_CERT_COMMANDS
    ]
    unexpected_evidence = [
        evidence for evidence in evidence_values if evidence not in REQUIRED_IMPORT_CERT_EVIDENCE
    ]
    disallowed_command_tokens = _find_disallowed_active_run_tokens("\n".join(command_values))
    disallowed_evidence_tokens = _find_disallowed_active_run_tokens("\n".join(evidence_values))
    for command in missing_required_commands:
        unresolved.append(f"source_receipt_required_command:{command}")
    for evidence in missing_required_evidence:
        unresolved.append(f"source_receipt_required_evidence:{evidence}")
    for command in unexpected_commands:
        unresolved.append(f"source_receipt_unexpected_command:{command}")
    for evidence in unexpected_evidence:
        unresolved.append(f"source_receipt_unexpected_evidence:{evidence}")
    for command in duplicate_commands:
        unresolved.append(f"source_receipt_duplicate_command:{command}")
    for evidence in duplicate_evidence:
        unresolved.append(f"source_receipt_duplicate_evidence:{evidence}")
    for token in disallowed_command_tokens:
        unresolved.append(f"source_receipt_command_disallowed_active_run_proof:{token}")
    for token in disallowed_evidence_tokens:
        unresolved.append(f"source_receipt_evidence_disallowed_active_run_proof:{token}")

    for duplicate_name in _duplicate_oracle_names(import_oracles):
        unresolved.append(f"duplicate_import_oracle:{duplicate_name}")
    malformed_import_oracle_rows = _malformed_oracle_rows(import_oracles)
    for index in malformed_import_oracle_rows:
        unresolved.append(f"malformed_import_oracle_row:{index}")

    published_import_oracle_names = [_oracle_name(row) for row in import_oracles if _oracle_name(row)]
    unexpected_import_oracle_names = [
        oracle_name
        for oracle_name in published_import_oracle_names
        if _normalize_token(oracle_name)
        not in {_normalize_token(required_name) for required_name in REQUIRED_IMPORT_ORACLE_NAMES}
    ]
    for oracle_name in unexpected_import_oracle_names:
        unresolved.append(f"unexpected_import_oracle:{oracle_name}")

    oracle_by_name = {_normalize_token(_oracle_name(row)): row for row in import_oracles}
    for required_name in REQUIRED_IMPORT_ORACLE_NAMES:
        row = oracle_by_name.get(_normalize_token(required_name))
        if not isinstance(row, dict):
            unresolved.append(f"missing_import_oracle:{required_name}")
            continue
        if not _oracle_coverage_is_complete(row):
            unresolved.append(f"incomplete_import_oracle:{required_name}")

    for duplicate_name in _duplicate_oracle_names(adjacent_oracles):
        unresolved.append(f"duplicate_adjacent_oracle:{duplicate_name}")
    malformed_adjacent_oracle_rows = _malformed_oracle_rows(adjacent_oracles)
    for index in malformed_adjacent_oracle_rows:
        unresolved.append(f"malformed_adjacent_oracle_row:{index}")

    published_adjacent_oracle_names = [_oracle_name(row) for row in adjacent_oracles if _oracle_name(row)]
    unexpected_adjacent_oracle_names = [
        oracle_name
        for oracle_name in published_adjacent_oracle_names
        if _normalize_token(oracle_name)
        not in {_normalize_token(required_name) for required_name in REQUIRED_ADJACENT_ORACLE_NAMES}
    ]
    for oracle_name in unexpected_adjacent_oracle_names:
        unresolved.append(f"unexpected_adjacent_oracle:{oracle_name}")

    adjacent_by_name = {_normalize_token(_oracle_name(row)): row for row in adjacent_oracles}
    for required_name in REQUIRED_ADJACENT_ORACLE_NAMES:
        row = adjacent_by_name.get(_normalize_token(required_name))
        if row is None:
            unresolved.append(f"missing_adjacent_oracle:{required_name}")
            continue
        if not isinstance(row, dict):
            unresolved.append(f"malformed_adjacent_oracle:{required_name}")
            continue
        if not _oracle_coverage_is_complete(row):
            unresolved.append(f"incomplete_adjacent_oracle:{required_name}")

    return (
        {
            "status": "passed" if not unresolved else "failed",
            "source_receipt_path": _to_rel(import_cert_path, root),
            "source_receipt_status": import_status,
            "source_receipt_coverage": coverage_summary if isinstance(coverage_summary, dict) else None,
            "required_import_oracle_names": list(REQUIRED_IMPORT_ORACLE_NAMES),
            "required_adjacent_oracle_names": list(REQUIRED_ADJACENT_ORACLE_NAMES),
            "published_import_oracle_names": published_import_oracle_names,
            "published_adjacent_oracle_names": published_adjacent_oracle_names,
            "malformed_import_oracle_rows": malformed_import_oracle_rows,
            "malformed_adjacent_oracle_rows": malformed_adjacent_oracle_rows,
            "required_source_receipt_coverage_total": expected_coverage_total,
            "required_source_receipt_commands": list(REQUIRED_IMPORT_CERT_COMMANDS),
            "required_source_receipt_evidence": list(REQUIRED_IMPORT_CERT_EVIDENCE),
            "source_receipt_commands": command_values,
            "source_receipt_evidence": evidence_values,
            "missing_required_source_receipt_commands": missing_required_commands,
            "missing_required_source_receipt_evidence": missing_required_evidence,
            "unexpected_source_receipt_commands": unexpected_commands,
            "unexpected_source_receipt_evidence": unexpected_evidence,
            "duplicate_source_receipt_commands": duplicate_commands,
            "duplicate_source_receipt_evidence": duplicate_evidence,
            "disallowed_source_receipt_command_tokens": disallowed_command_tokens,
            "disallowed_source_receipt_evidence_tokens": disallowed_evidence_tokens,
            "unexpected_import_oracle_names": unexpected_import_oracle_names,
            "unexpected_adjacent_oracle_names": unexpected_adjacent_oracle_names,
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
    oracle_suite_summary = _build_oracle_suite_summary(oracle_suites)
    performance_budget_summary = _build_budget_summary(performance_budgets)
    command_receipts, unresolved_command_ids = _validate_release_commands(root, generated_output_path)
    successor_authority, unresolved_successor_authority_ids = _validate_successor_wave_authority(root, generated_output_path)
    closeout_document, unresolved_closeout_document_ids = _validate_package_closeout(root)
    local_commit_proofs, unresolved_local_commit_ids = _validate_local_commit_proofs(root)
    release_channel_binding, unresolved_release_channel_ids = _build_release_channel_binding(RELEASE_CHANNEL_PATH)
    import_discipline, unresolved_import_ids = _build_import_discipline(root, import_cert_path, import_cert)

    pack_status = (
        "passed"
        if not unresolved_suite_ids
        and not unresolved_budget_ids
        and not unresolved_command_ids
        and not unresolved_successor_authority_ids
        and not unresolved_closeout_document_ids
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
        "frontier_id": SUCCESSOR_WAVE_PACKAGE["frontier_id"],
        "queue_completion_action": successor_authority["queue_completion_action"],
        "design_queue_completion_action": successor_authority["design_queue_completion_action"],
        "queue_do_not_reopen_reason": successor_authority["queue_do_not_reopen_reason"],
        "design_queue_do_not_reopen_reason": successor_authority["design_queue_do_not_reopen_reason"],
        "queue_closure_field_drift": successor_authority["queue_closure_field_drift"],
        "successor_wave_package": SUCCESSOR_WAVE_PACKAGE,
        "successor_wave_authority": successor_authority,
        "closeout_document": closeout_document,
        "local_commit_proofs": local_commit_proofs,
        "release_channel_binding": release_channel_binding,
        "release_commands": command_receipts,
        "commands": list(RELEASE_COMMANDS),
        "required_oracle_suite_ids": list(REQUIRED_ORACLE_SUITE_IDS),
        "oracle_suites": oracle_suites,
        "oracle_suite_summary": oracle_suite_summary,
        "required_performance_budget_ids": list(REQUIRED_BUDGET_IDS),
        "performance_budgets": performance_budgets,
        "performance_budget_summary": performance_budget_summary,
        "import_oracle_discipline": import_discipline,
        "unresolved": {
            "oracle_suites": unresolved_suite_ids,
            "performance_budgets": unresolved_budget_ids,
            "release_commands": unresolved_command_ids,
            "successor_wave_authority": unresolved_successor_authority_ids,
            "closeout_document": unresolved_closeout_document_ids,
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
        "--check",
        action="store_true",
        help="verify the checked-in proof pack is current without rewriting it",
    )
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
    if args.check:
        if payload.get("status") != "passed":
            unresolved = payload.get("unresolved") if isinstance(payload.get("unresolved"), dict) else {}
            unresolved_ids = {
                key: value
                for key, value in unresolved.items()
                if isinstance(value, list) and value
            }
            print(
                f"ENGINE_PROOF_PACK check failed closed: {json.dumps(unresolved_ids, sort_keys=True)}",
                file=sys.stderr,
            )
            return 1
        checked_in_payload = _load_json(out)
        if not checked_in_payload:
            print(f"ENGINE_PROOF_PACK check failed: missing or invalid checked-in receipt at {out}", file=sys.stderr)
            return 1
        if _without_generated_at(payload) != _without_generated_at(checked_in_payload):
            print(f"ENGINE_PROOF_PACK check failed: checked-in receipt is stale at {out}", file=sys.stderr)
            return 1
        print(str(out))
        return 0

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
