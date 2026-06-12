#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr6_rule_authority")
REPORT = COMPLETION_ROOT / "SR6_EXPLAIN_RECEIPTS.generated.json"


def main() -> int:
    if not REPORT.is_file():
        print(json.dumps({"status": "fail", "reason": "missing SR6 explain receipt marker"}, indent=2))
        return 1

    payload = json.loads(REPORT.read_text(encoding="utf-8"))
    ok = (
        payload.get("status") == "seeded"
        and payload.get("ruleset") == "sr6"
        and payload.get("public_safe") is True
        and payload.get("provider") == "Sr6ExplainReceiptProvider"
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
