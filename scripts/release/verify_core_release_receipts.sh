#!/usr/bin/env bash
set -euo pipefail
python3 /docker/chummercomplete/chummer-core-engine/scripts/verify-next90-m141-import-route-receipts.py >/dev/null
python3 - <<'PY'
import json
from pathlib import Path
src = Path('/docker/chummercomplete/.codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json')
payload = json.loads(src.read_text())
required = ['generated_at','status']
for key in required:
    if key not in payload:
        raise SystemExit(f"core receipt missing {key}: {src}")
if str(payload.get('status')).lower() not in {'pass','passed','ready'}:
    raise SystemExit(f"core receipt status is not pass: {payload.get('status')}")
print('core release receipts ok')
PY
