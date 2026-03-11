#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/_env.sh"
dotnet_with_default_target build "$@" --nologo -m:1
