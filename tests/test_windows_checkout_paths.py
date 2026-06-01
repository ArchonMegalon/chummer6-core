from __future__ import annotations

import subprocess
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "scripts" / "verify-windows-checkout-paths.py"


def test_windows_checkout_path_budget_accepts_current_repo() -> None:
    completed = subprocess.run(
        ["python3", str(SCRIPT), "--repo-root", str(REPO_ROOT)],
        text=True,
        capture_output=True,
        check=False,
    )

    assert completed.returncode == 0, completed.stderr
    assert "windows checkout path proof passed" in completed.stdout


def test_windows_checkout_path_budget_rejects_overlong_tracked_paths() -> None:
    completed = subprocess.run(
        ["python3", str(SCRIPT), "--repo-root", str(REPO_ROOT), "--max-path-length", "10"],
        text=True,
        capture_output=True,
        check=False,
    )

    assert completed.returncode == 1
    assert "tracked paths exceed Windows checkout path budget" in completed.stderr
