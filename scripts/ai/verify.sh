#!/usr/bin/env bash
set -euo pipefail

"$(dirname "$0")/build.sh" "$@"
"$(dirname "$0")/test_core_engine.sh" --no-build
