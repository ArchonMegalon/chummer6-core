#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
REPORT_PATH = REPO_ROOT / ".codex-studio" / "published" / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json"


def main() -> int:
    subprocess.run(
        [sys.executable, str(REPO_ROOT / "scripts" / "verify_sr4_rule_authority_seed.py")],
        cwd=REPO_ROOT,
        check=True,
    )
    report = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
    boundary = report.get("copyright_boundary", {})
    ok = (
        report.get("status") == "pass"
        and boundary.get("implementation_facts_only") is True
        and boundary.get("quote_allowed_false") is True
        and report.get("final_verdict") == "NOT_READY"
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT_PATH)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
