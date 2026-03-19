#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"

test -f docs/CONTRACT_BOUNDARY_MAP.md

if [ -d Chummer.Run.Contracts ]; then
  echo "repo-local Chummer.Run.Contracts mirror must stay deleted after owner-repo cutover" >&2
  exit 1
fi

if rg -n 'namespace Chummer\.Contracts\.Session;' . --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!Chummer.Contracts/Session/**' >/dev/null 2>&1; then
  echo "semantic session namespaces must remain owned only by Chummer.Contracts/Session" >&2
  exit 1
fi

if rg -n 'public (sealed )?record (EffectAppliedEvent|TrackerIncrementedEvent)\b|public interface ISessionEvent\b' . --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!Chummer.Contracts/Session/**' >/dev/null 2>&1; then
  echo "semantic session event contracts must remain owned only by Chummer.Contracts/Session" >&2
  exit 1
fi

for sibling_repo in \
  ../chummer.run-services \
  ../chummer-play \
  ../chummer-presentation \
  ../chummer-ui-kit \
  ../chummer-hub-registry; do
  if [ ! -d "$sibling_repo" ]; then
    continue
  fi

  if rg -n 'namespace Chummer\.Contracts\.Session;|public (sealed )?record (EffectAppliedEvent|TrackerIncrementedEvent)\b|public interface ISessionEvent\b' \
    "$sibling_repo" --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' >/dev/null 2>&1; then
    echo "sibling repo ${sibling_repo##*/} must not source-own engine session semantics" >&2
    exit 1
  fi

  if rg -n 'public (sealed )?record (RulesetExplainTrace|RulesetTraceStep|RulesetExecutionOptions|SessionLedger|SessionRuntimeBundle|SessionSyncBatch|SessionReplayReceipt)\b' \
    "$sibling_repo" --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/tests/**' --glob '!**/Chummer.Tests/**' >/dev/null 2>&1; then
    echo "sibling repo ${sibling_repo##*/} must not source-own engine explain/runtime/session DTO families" >&2
    exit 1
  fi
done

"$(dirname "$0")/build.sh" "$@"
"$(dirname "$0")/test_core_engine.sh" --no-build
