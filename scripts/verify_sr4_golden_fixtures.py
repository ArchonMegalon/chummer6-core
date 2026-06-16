#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path


COMPLETION_ROOT = Path("/docker/chummercomplete/_completion/sr4_rule_authority")
REPORT = COMPLETION_ROOT / "SR4_GOLDEN_FIXTURES.generated.json"


def main() -> int:
    if not REPORT.is_file():
        print(json.dumps({"status": "fail", "reason": "missing SR4 fixture receipt"}, indent=2))
        return 1

    payload = json.loads(REPORT.read_text(encoding="utf-8"))
    ok = (
        payload.get("status") in {"seed_fixtures_passed", "core_seed_fixture_pack_passed"}
        and payload.get("failed") == 0
        and payload.get("passed", 0) > 0
        and len(payload.get("required_fixture_ids", [])) > 0
    )
    print(json.dumps({"status": "pass" if ok else "fail", "report": str(REPORT)}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
