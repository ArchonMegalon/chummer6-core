#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"

test -f docs/CONTRACT_BOUNDARY_MAP.md
test -f docs/EXPLAIN_AND_RUNTIME_CANON.md
test -f docs/AI_PROVIDER_TRANSPORT_BOUNDARY.md
test -f docs/CORE_RUNTIME_RESTORE_RUNBOOK.md
test -f docs/LEGACY_MIGRATION_CERTIFICATION.md
test -f docs/LEGACY_ROOT_SURFACE_INVENTORY.md
rg -n 'HttpAiProviderTransportClient|EnvironmentAiProviderCredentialCatalog|EnvironmentAiProviderTransportOptionsCatalog|RemoteHttpAiProvider|AddLegacyEnvironmentAiTransportCompatibility|EmptyAiProviderCredentialCatalog|EmptyAiProviderTransportOptionsCatalog|WL-D020' docs/AI_PROVIDER_TRANSPORT_BOUNDARY.md >/dev/null
rg -n 'EXPLAIN_AND_RUNTIME_CANON\.md|scripts/runbook\.sh|scripts/migration-loop\.sh|DualHeadAcceptanceTests|MigrationComplianceTests|RUNBOOK_MODE=local-tests' docs/CORE_RUNTIME_RESTORE_RUNBOOK.md >/dev/null
rg -n 'chummer5a|PARITY_ORACLE\.json|scripts/migration-loop\.sh 1|scripts/audit-compliance\.sh|MigrationComplianceTests|DualHeadAcceptanceTests|ArchitectureGuardrailTests' docs/LEGACY_MIGRATION_CERTIFICATION.md >/dev/null
rg -n 'Chummer/|Plugins/|Chummer\.Infrastructure\.Browser/|compatibility-only|Chummer\.Contracts|Chummer\.Application|Chummer\.Core|Chummer\.Infrastructure|Chummer\.Rulesets' docs/LEGACY_ROOT_SURFACE_INVENTORY.md >/dev/null
rg -n 'AddSingleton<IAiProviderCredentialCatalog, EmptyAiProviderCredentialCatalog>\(\)|AddSingleton<IAiProviderTransportOptionsCatalog, EmptyAiProviderTransportOptionsCatalog>\(\)|AddSingleton<IAiProviderTransportClient>\(_ => new NotImplementedAiProviderTransportClient\(\)\)|AddLegacyEnvironmentAiTransportCompatibility|AddSingleton<IAiProviderCredentialCatalog, EnvironmentAiProviderCredentialCatalog>\(\)|AddSingleton<IAiProviderTransportOptionsCatalog, EnvironmentAiProviderTransportOptionsCatalog>\(\)|new HttpAiProviderTransportClient\(provider.GetRequiredService<IAiProviderCredentialCatalog>\(\)\)' Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs >/dev/null

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

if [ -d ../chummer-presentation ]; then
  test -f ../chummer-presentation/Chummer.Presentation/Explain/RulesetExplainRenderer.cs
  test -f ../chummer-presentation/Chummer.Blazor/Components/Shared/ExplainTracePanel.razor
fi

if [ -d ../chummer.run-services ]; then
  test -f ../chummer.run-services/tests/RunServicesVerification/RuntimeBundleVerification.cs
  test -f ../chummer.run-services/tests/RunServicesVerification/StateStoreBackupVerification.cs
  rg -n 'IssueRuntimeBundle' ../chummer.run-services/tests/RunServicesVerification/RuntimeBundleVerification.cs >/dev/null
fi

if [ -d ../chummer-play ]; then
  test -f ../chummer-play/src/Chummer.Play.Web/BrowserSessionOfflineCacheService.cs
  test -f ../chummer-play/src/Chummer.Play.Web/BrowserState/RuntimeBundleCacheEntry.cs
  rg -n 'VerifyResumePreservesLedgerWhenRuntimeBundleDriftsAsync|VerifyOfflineCacheRuntimeBundleQuotaEvictionAsync' \
    ../chummer-play/src/Chummer.Play.RegressionChecks/Program.cs >/dev/null
fi

"$(dirname "$0")/build.sh" "$@"
"$(dirname "$0")/test_core_engine.sh" --no-build
