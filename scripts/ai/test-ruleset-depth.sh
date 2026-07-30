#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_env.sh"
cd "$repo_root"

receipt_path="${CHUMMER_RULESET_DEPTH_RECEIPT:-$repo_root/.codex-studio/published/RULESET_DEPTH_LINUX_GATE.generated.json}"
mkdir -p "$(dirname "$receipt_path")"

rules_filter='FullyQualifiedName~Sr4|FullyQualifiedName~Sr5|FullyQualifiedName~Sr6|FullyQualifiedName~LifeModules|FullyQualifiedName~DeterministicRulesetCapabilityHostTests|FullyQualifiedName~RulesetShellCatalogResolverTests'
rules_command=(dotnet test Chummer.Tests/Chummer.Tests.csproj -c Release -f net10.0 -m:1 -p:UseSharedCompilation=false --no-restore --filter "$rules_filter" -v minimal)
core_command=(dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --no-restore)

has_windows_desktop_runtime() {
  dotnet --list-runtimes | grep -Eq '^Microsoft\.WindowsDesktop\.App 10\.0\.'
}

started_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
windows_execution_status="skipped_host_missing_windowsdesktop"
if has_windows_desktop_runtime; then
  windows_execution_status="available"
fi

echo "[ruleset-depth] restore linux target: Chummer.Tests/Chummer.Tests.csproj (net10.0)"
bash "$SCRIPT_DIR/build-local-contract-dependencies.sh" Release
bash "$SCRIPT_DIR/restore.sh" Chummer.Tests/Chummer.Tests.csproj -p:TargetFramework=net10.0

echo "[ruleset-depth] restore core executable audit"
bash "$SCRIPT_DIR/restore.sh" Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj

echo "[ruleset-depth] linux vstest: ${rules_command[*]}"
"${rules_command[@]}"

echo "[ruleset-depth] core executable audit: ${core_command[*]}"
core_output="$("${core_command[@]}")"
printf '%s\n' "$core_output"
if [[ "$core_output" != *"core-engine-tests: ok"* ]]; then
  echo "core executable audit did not emit core-engine-tests: ok" >&2
  exit 1
fi

python3 - "$receipt_path" "$started_at_utc" "$windows_execution_status" "${rules_command[*]}" "${core_command[*]}" <<'PY'
import json
import sys
from datetime import datetime, timezone

path, started_at_utc, windows_execution_status, rules_command, core_command = sys.argv[1:6]
payload = {
    "generated_at_utc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    "started_at_utc": started_at_utc,
    "status": "pass",
    "contract_name": "chummer-core.ruleset-depth-linux-gate",
    "linux_vstest": {
        "status": "pass",
        "framework": "net10.0",
        "configuration": "Release",
        "command": rules_command,
        "filter": "SR4/SR5/SR6/LifeModules/deterministic-host/ruleset-shell depth slice"
    },
    "core_executable_audit": {
        "status": "pass",
        "configuration": "Release",
        "command": core_command,
        "required_evidence": "core-engine-tests: ok"
    },
    "windows_desktop_execution": {
        "status": windows_execution_status,
        "requirement": "Run scripts/ai/test-native-host-matrix.sh on a WindowsDesktop-capable host for native desktop execution certification.",
        "linux_host_blocker": False
    },
    "bare_multitarget_linux_dotnet_test_policy": {
        "status": "blocked_by_command_guidance",
        "reason": "Chummer.Tests targets net10.0 and net10.0-windows; Linux ruleset gates must pin -f net10.0 or use scripts/ai/test-matrix.sh."
    }
}
with open(path, "w", encoding="utf-8") as f:
    json.dump(payload, f, indent=2)
    f.write("\n")
PY

echo "[ruleset-depth] receipt: $receipt_path"
