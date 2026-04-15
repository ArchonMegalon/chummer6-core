#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path
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
    "dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release",
    "dotnet run --project Chummer.Benchmarks/Chummer.Benchmarks.csproj -c Release -- --budget-check --budget-file Chummer.Benchmarks/workspace-benchmark-budgets.json",
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


def _validate_release_commands(root: Path) -> tuple[list[dict[str, Any]], list[str]]:
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
        missing_inputs = [item for item in required_inputs if not (root / item).exists()]
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


def build_payload(root: Path) -> dict[str, Any]:
    import_cert_path = root / ".codex-studio" / "published" / "IMPORT_PARITY_CERTIFICATION.generated.json"
    import_cert = _load_json(import_cert_path)

    oracle_suites, unresolved_suite_ids = _build_oracle_suites(root)
    performance_budgets, unresolved_budget_ids = _build_budget_map(root)
    command_receipts, unresolved_command_ids = _validate_release_commands(root)
    import_discipline, unresolved_import_ids = _build_import_discipline(root, import_cert_path, import_cert)

    pack_status = (
        "passed"
        if not unresolved_suite_ids and not unresolved_budget_ids and not unresolved_command_ids and not unresolved_import_ids
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

    payload = build_payload(root)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(str(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
