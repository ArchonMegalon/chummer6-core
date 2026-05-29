#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr6_rule_authority")
REPORT = COMPLETION_ROOT / "SR6_TABLE_IMPORTS.generated.json"


def main() -> int:
    if not REPORT.is_file():
        print(json.dumps({"status": "fail", "reason": "missing SR6 table import receipt"}, indent=2))
        return 1

    payload = json.loads(REPORT.read_text(encoding="utf-8"))
    ok = (
        payload.get("status") == "private_pdf_line_hash_import_indexed_pending_review"
        and payload.get("ruleset") == "sr6"
        and payload.get("source_kind") == "private_local_sourcebook_pdf_line_hashes"
        and payload.get("sourcebook_count", 0) >= 1
        and payload.get("nonempty_line_count", 0) > 0
        and payload.get("candidate_table_line_count", 0) > 0
        and "no sourcebook prose" in str(payload.get("public_copy_policy", ""))
        and "human review" in str(payload.get("remaining_gate", ""))
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
