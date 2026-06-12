#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import shlex
import subprocess
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Callable

import yaml


REPO_ROOT = Path(__file__).resolve().parents[1]
PUBLISHED_ROOT = REPO_ROOT / ".codex-studio" / "published"
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion")
EXPECTED_READY_BLOCKERS = {
    "sr4": {"SR4-G013"},
    "sr6": {"SR6-G012"},
}


def now_iso() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def matrix_path(ruleset: str) -> Path:
    return REPO_ROOT / "docs" / "rulesets" / f"{ruleset}-rule-authority" / "VERIFICATION_MATRIX.yaml"


def load_matrix(ruleset: str) -> dict[str, Any]:
    return yaml.safe_load(matrix_path(ruleset).read_text(encoding="utf-8")) or {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def default_runner(command: str, timeout_seconds: int) -> dict[str, Any]:
    completed = subprocess.run(
        command,
        cwd=REPO_ROOT,
        shell=True,
        capture_output=True,
        text=True,
        timeout=timeout_seconds,
    )
    returncode = completed.returncode
    no_test_matches = "No test matches the given testcase filter" in completed.stdout
    if no_test_matches and returncode == 0:
        returncode = 3
    return {
        "returncode": returncode,
        "no_test_matches": no_test_matches,
        "stdout_tail": completed.stdout[-4000:],
        "stderr_tail": completed.stderr[-4000:],
    }


def build_payload(
    ruleset: str,
    *,
    timeout_seconds: int,
    runner: Callable[[str, int], dict[str, Any]] = default_runner,
) -> dict[str, Any]:
    payload = load_matrix(ruleset)
    expected_blockers = EXPECTED_READY_BLOCKERS[ruleset]
    gate_results: list[dict[str, Any]] = []
    failures: list[str] = []
    unexpected_failures: list[str] = []

    for gate in payload.get("gates", []):
        gate_id = str(gate.get("id"))
        command = str(gate.get("command"))
        try:
            result = runner(command, timeout_seconds)
        except subprocess.TimeoutExpired as exc:
            result = {
                "returncode": 124,
                "stdout_tail": (exc.stdout or "")[-4000:] if isinstance(exc.stdout, str) else "",
                "stderr_tail": (exc.stderr or "")[-4000:] if isinstance(exc.stderr, str) else "",
                "timeout_seconds": timeout_seconds,
            }
        passed = result.get("returncode") == 0
        expected_blocker = gate_id in expected_blockers
        if not passed:
            failures.append(gate_id)
            if not expected_blocker:
                unexpected_failures.append(gate_id)
        gate_results.append(
            {
                "id": gate_id,
                "title": gate.get("title"),
                "command": command,
                "argv_preview": shlex.split(command),
                "returncode": result.get("returncode"),
                "no_test_matches": result.get("no_test_matches", False),
                "pass": passed,
                "expected_ready_blocker": expected_blocker,
                "stdout_tail": result.get("stdout_tail", ""),
                "stderr_tail": result.get("stderr_tail", ""),
            }
        )

    status = "pass" if not failures else "blocked" if not unexpected_failures else "fail"
    return {
        "contract_name": f"chummer.{ruleset}.rule_authority_verification_matrix",
        "generated_at_utc": now_iso(),
        "ruleset": ruleset,
        "matrix_path": str(matrix_path(ruleset)),
        "status": status,
        "ready_matrix_passed": not failures,
        "expected_ready_blockers": sorted(expected_blockers),
        "failed_gates": failures,
        "unexpected_failed_gates": unexpected_failures,
        "gates": gate_results,
    }


def materialize(ruleset: str, timeout_seconds: int) -> dict[str, Any]:
    result = build_payload(ruleset, timeout_seconds=timeout_seconds)
    upper = ruleset.upper()
    write_json(COMPLETION_ROOT / f"{ruleset}_rule_authority" / f"{upper}_VERIFICATION_MATRIX_RUN.generated.json", result)
    write_json(PUBLISHED_ROOT / f"{upper}_VERIFICATION_MATRIX_RUN.generated.json", result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Run SR4/SR6 rule-authority verification matrices and write fail-closed receipts.")
    parser.add_argument("rulesets", nargs="*", choices=["sr4", "sr6"], default=["sr4", "sr6"])
    parser.add_argument("--timeout-seconds", type=int, default=120)
    args = parser.parse_args()

    results = [materialize(ruleset, args.timeout_seconds) for ruleset in args.rulesets]
    summary = {
        "status": "pass" if all(result["status"] == "pass" for result in results) else "blocked" if all(result["status"] in {"pass", "blocked"} for result in results) else "fail",
        "results": results,
    }
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0 if summary["status"] == "pass" else 2 if summary["status"] == "blocked" else 1


if __name__ == "__main__":
    raise SystemExit(main())
