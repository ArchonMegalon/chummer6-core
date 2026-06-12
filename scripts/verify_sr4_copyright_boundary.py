#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
REPORT_PATH = REPO_ROOT / ".codex-studio" / "published" / "SR4_RULE_AUTHORITY_INTEGRATION.generated.json"


def main() -> int:
    report = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
    boundary = report.get("copyright_boundary", {})
    final_verdict = str(report.get("final_verdict") or "")
    ok = (
        report.get("status") == "pass"
        and boundary.get("implementation_facts_only") is True
        and boundary.get("quote_allowed_false") is True
        and final_verdict in {"NOT_READY", "SR4_RULE_AUTHORITY_READY"}
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT_PATH)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
