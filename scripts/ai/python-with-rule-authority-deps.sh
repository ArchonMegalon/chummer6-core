#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
venv_root="${CHUMMER_CORE_RULE_AUTHORITY_VENV:-$repo_root/.tmp/rule-authority-python}"
python_bin="$venv_root/bin/python"
requirements="$repo_root/scripts/ai/rule-authority-requirements.txt"

if [[ ! -x "$python_bin" ]]; then
  python3 -m venv "$venv_root"
fi

if ! "$python_bin" -c 'import pypdf; raise SystemExit(0 if pypdf.__version__ == "6.14.2" else 1)' >/dev/null 2>&1; then
  "$python_bin" -m pip install \
    --disable-pip-version-check \
    --no-deps \
    --only-binary=:all: \
    --require-hashes \
    --requirement "$requirements"
fi

exec "$python_bin" "$@"
