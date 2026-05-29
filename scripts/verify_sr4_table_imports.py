#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr4_rule_authority")
REPORT = COMPLETION_ROOT / "SR4_TABLE_IMPORTS.generated.json"


def main() -> int:
    if not REPORT.is_file():
        print(json.dumps({"status": "fail", "reason": "missing SR4 table import receipt"}, indent=2))
        return 1

    payload = json.loads(REPORT.read_text(encoding="utf-8"))
    ok = (
        payload.get("status") == "structured_legacy_data_indexed_pending_human_review"
        and payload.get("ruleset") == "sr4"
        and payload.get("source_kind") == "legacy_chummer_structured_xml"
        and payload.get("file_count", 0) >= 20
        and payload.get("row_count", 0) > 0
        and "no sourcebook prose" in str(payload.get("public_copy_policy", ""))
        and "human review" in str(payload.get("remaining_gate", ""))
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
