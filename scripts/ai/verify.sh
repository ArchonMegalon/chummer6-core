#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"

test -f docs/CONTRACT_BOUNDARY_MAP.md
test -f docs/EXPLAIN_AND_RUNTIME_CANON.md
test -f docs/AI_PROVIDER_TRANSPORT_BOUNDARY.md
test -f docs/CORE_RUNTIME_RESTORE_RUNBOOK.md
test -f docs/WORKSPACE_RETENTION_POLICY.md
test -f docs/LEGACY_MIGRATION_CERTIFICATION.md
test -f docs/ENGINE_PROOF_PACK.md
test -f docs/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.md
test -f scripts/verify-next90-m141-import-route-receipts.py
test -f tests/test_next90_m141_import_route_receipts.py
test -f docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md
test -f scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py
test -f tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py
test -f docs/NEXT90_M114_RULE_ENVIRONMENT_STUDIO_CONTRACTS.md
test -f docs/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.md
test -f tests/test_engine_proof_pack_generator.py
test -f docs/LEGACY_ROOT_SURFACE_INVENTORY.md
test -f docs/LEGACY_PLUGIN_PURIFICATION_INCREMENT_WL111.md
test -f docs/LEGACY_PLUGIN_AND_HELPER_OPERATIONAL_EVIDENCE_WL112.md
test -f docs/CHUMMER_CONTRACTS_MIGRATION_AND_VERIFY_WL113_3.md
test -f docs/CAMPAIGN_ENGINE_RECOVERY_CONTRACTS_WL200_203.md
test -f scripts/ai/verify_design_mirror.py
rg -n 'HttpAiProviderTransportClient|EnvironmentAiProviderCredentialCatalog|EnvironmentAiProviderTransportOptionsCatalog|RemoteHttpAiProvider|AddLegacyEnvironmentAiTransportCompatibility|EmptyAiProviderCredentialCatalog|EmptyAiProviderTransportOptionsCatalog|WL-D020' docs/AI_PROVIDER_TRANSPORT_BOUNDARY.md >/dev/null
rg -n 'EXPLAIN_AND_RUNTIME_CANON\.md|scripts/runbook\.sh|scripts/migration-loop\.sh|DualHeadAcceptanceTests|MigrationComplianceTests|RUNBOOK_MODE=local-tests' docs/CORE_RUNTIME_RESTORE_RUNBOOK.md >/dev/null
rg -n 'retention-cleanup|retention-cleanup-smoke|MIG-093|retention|cleanup|runbook' docs/WORKSPACE_RETENTION_POLICY.md >/dev/null
rg -n 'milestone `104`|ENGINE_PROOF_PACK\.generated\.json|oracle_suite_summary|performance_budget_summary|coverage_focus|fixture_count|release_gate|scenario|IMPORT_PARITY_CERTIFICATION\.generated\.json|next90-m104-core-proof-pack|3227666051|completion_action|do_not_reopen_reason|queue-mirror closeout parity|test_engine_proof_pack_generator\.py|unexpected oracle rows fail closed|malformed extra rows fail closed' docs/ENGINE_PROOF_PACK.md >/dev/null
rg -n 'next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic|4304178368|2350979521|source:translator_route|family:custom_data_xml_and_translator_bridge|family:legacy_and_adjacent_import_oracles|customDataXmlBridgeDeterministicReceipt|translatorDeterministicReceipt|importOracleDeterministicReceipt|amendPackageDeterministicReceipt|ENGINE_PROOF_PACK\.generated\.json|amend_package|worker handoff artifacts|run-local telemetry artifacts' docs/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.md >/dev/null
rg -n 'next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi|2778308338|family:sheet_export_print_viewer_and_exchange|family:sr6_supplements_designers_and_house_rules|WorkspaceExchangeDeterministicReceipt|Sr6SuccessorLaneDeterministicReceipt|sr6SuccessorDeterministicReceipt|exchangeDeterministicReceipt|parity-m143' docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md >/dev/null
rg -n 'next90-m114-core-rule-environment-studio|deterministic first-pin, clear, and requires-review diff states|CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio|verify-next90-m114-rule-environment-studio\.py|IRuleEnvironmentStudioService' docs/NEXT90_M114_RULE_ENVIRONMENT_STUDIO_CONTRACTS.md >/dev/null
rg -n 'next90-m115-core-exchange-contracts|`relatedOutputs`|campaign federation|replay timeline|session recap|external exchange|verify-next90-m115-core-exchange-contracts\.py|NEXT90_M115_CORE_EXCHANGE_CONTRACTS\.generated\.json' docs/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.md >/dev/null
rg -n 'next90-m143-core-keep-export-print-supplement-and-rule-environment-receipts-deterministi|2778308338|family:sheet_export_print_viewer_and_exchange|family:sr6_supplements_designers_and_house_rules|WorkspaceExchangeDeterministicReceipt|Sr6SuccessorLaneDeterministicReceipt|sr6SuccessorDeterministicReceipt|exchangeDeterministicReceipt|parity-m143' docs/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.md >/dev/null
rg -n 'next90-m114-core-rule-environment-studio|deterministic first-pin, clear, and requires-review diff states|CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio|verify-next90-m114-rule-environment-studio\.py|IRuleEnvironmentStudioService' docs/NEXT90_M114_RULE_ENVIRONMENT_STUDIO_CONTRACTS.md >/dev/null
rg -n 'next90-m115-core-exchange-contracts|`relatedOutputs`|campaign federation|replay timeline|session recap|external exchange|verify-next90-m115-core-exchange-contracts\.py|NEXT90_M115_CORE_EXCHANGE_CONTRACTS\.generated\.json' docs/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.md >/dev/null
rg -n 'oracle_suite_summary\.coverage_status=passed|oracle_suite_summary\.covered_rulesets=\[sr4, sr5, sr6\]|oracle_suite_summary\.release_scope=promoted_desktop_release|coverage_focus|fixture_count|performance_budget_summary\.coverage_status=passed|performance_budget_summary\.release_scope=promoted_desktop_release|release commands|release_gate|scenario|completion_action: verify_closed_package_only|do_not_reopen_reason|matching `completion_action` and exact `do_not_reopen_reason` text|published_import_oracle_names|published_adjacent_oracle_names|unexpected-name lists remain empty|malformed_import_oracle_rows=\[\]|malformed_adjacent_oracle_rows=\[\]' docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md >/dev/null
rg -n 'RUNBOOK_MODE" == "retention-cleanup"|RUNBOOK_MODE" == "retention-cleanup-smoke"|emit_retention_cleanup|RETENTION_WORKSPACE_DAYS|RETENTION_LOG_DAYS' scripts/runbook.sh >/dev/null
rg -n 'chummer5a|PARITY_ORACLE\.json|scripts/migration-loop\.sh 1|scripts/audit-compliance\.sh|MigrationComplianceTests|DualHeadAcceptanceTests|ArchitectureGuardrailTests' docs/LEGACY_MIGRATION_CERTIFICATION.md >/dev/null
rg -n 'Chummer/|Plugins/|Chummer\.Infrastructure\.Browser/|compatibility-only|Chummer\.Contracts|Chummer\.Application|Chummer\.Core|Chummer\.Infrastructure|Chummer\.Rulesets' docs/LEGACY_ROOT_SURFACE_INVENTORY.md >/dev/null
rg -n 'WL-111|WL-108\.1|WL-108\.3|Plugins/ChummerHub\.Client|Chummer/Plugins|compatibility-only|retirement gate' docs/LEGACY_PLUGIN_PURIFICATION_INCREMENT_WL111.md >/dev/null
rg -n 'WL-112|WL-108\.2|WL-108\.4|WL-108\.5|Plugins/SamplePlugin|repo_tool\.sh|repo_inspect\.sh|read_file\.sh|find_text\.sh|replace_text_literal\.sh|git_commit_repo_work\.sh|git_status\.sh|upsert_env_var\.sh|operational-only|hosted contract ownership' docs/LEGACY_PLUGIN_AND_HELPER_OPERATIONAL_EVIDENCE_WL112.md >/dev/null
rg -n 'WL-113\.3|Chummer\.Engine\.Contracts|bootstrap-contracts-feed\.sh|compatibility-only|Chummer\.Infrastructure\.Browser|Chummer\.Tests|Chummer/Chummer\.csproj|scripts/ai/verify\.sh' docs/CHUMMER_CONTRACTS_MIGRATION_AND_VERIFY_WL113_3.md >/dev/null
rg -n 'WL-200|WL-201|WL-202|WL-203|SessionOperationRecoveryContract|SessionOperationThroughputGuardrail|SessionLongRunningOperationState|retry-after-backoff|resume' docs/CAMPAIGN_ENGINE_RECOVERY_CONTRACTS_WL200_203.md >/dev/null
rg -n 'SessionOperationFailureClasses|SessionOperationRetryClasses|SessionOperationSafeActionIds|SessionOperationThroughputMetrics|RecoverableCancellation' Chummer.Contracts/Session/SessionOperationRecoveryContracts.cs >/dev/null
rg -n 'install_update_recovery_flow_regressions|continuity_and_conflict_recovery_gate|golden_journey_gate|These metrics are release gates, not dashboard decoration\.' .codex-design/product/METRICS_AND_SLOS.yaml >/dev/null
rg -n 'require_support_recovery_path_contract|continuity_and_conflict_recovery_gate|A journey gate is blocked when required published artifacts are missing or stale' .codex-design/product/GOLDEN_JOURNEY_RELEASE_GATES.yaml >/dev/null
rg -n 'WL-108\.1|WL-108\.2|WL-108\.3|WL-108\.4|WL-108\.5|WL-111|WL-112' WORKLIST.md docs/HELPER_TOOLING_RESIDUAL_BACKLOG.md >/dev/null
rg -n 'AddSingleton<IAiProviderCredentialCatalog, EmptyAiProviderCredentialCatalog>\(\)|AddSingleton<IAiProviderTransportOptionsCatalog, EmptyAiProviderTransportOptionsCatalog>\(\)|AddSingleton<IAiProviderTransportClient>\(_ => new NotImplementedAiProviderTransportClient\(\)\)|AddLegacyEnvironmentAiTransportCompatibility|AddSingleton<IAiProviderCredentialCatalog, EnvironmentAiProviderCredentialCatalog>\(\)|AddSingleton<IAiProviderTransportOptionsCatalog, EnvironmentAiProviderTransportOptionsCatalog>\(\)|new HttpAiProviderTransportClient\(provider.GetRequiredService<IAiProviderCredentialCatalog>\(\)\)' Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs >/dev/null

if rg -n 'Plugins\\(ChummerHub\.Client|SamplePlugin)\\.*\.csproj' Chummer.CoreEngine.sln Chummer.sln >/dev/null 2>&1; then
  echo "active solutions must not include legacy plugin projects" >&2
  exit 1
fi

active_contract_consumers=(
  Chummer.Application/Chummer.Application.csproj
  Chummer.Core/Chummer.Core.csproj
  Chummer.Infrastructure/Chummer.Infrastructure.csproj
  Chummer.Rulesets.Hosting/Chummer.Rulesets.Hosting.csproj
  Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj
  Chummer.Rulesets.Sr5/Chummer.Rulesets.Sr5.csproj
  Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj
  Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj
)

compatibility_contract_exceptions=(
  Chummer/Chummer.csproj
  Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj
  Chummer.Tests/Chummer.Tests.csproj
)

if rg -n 'Chummer\\.Contracts\\Chummer\\.Contracts\\.csproj' Chummer.CoreEngine.sln Chummer.sln >/dev/null 2>&1; then
  echo "active solutions must not include Chummer.Contracts source-project references" >&2
  exit 1
fi

for project_file in "${active_contract_consumers[@]}"; do
  if rg -n 'ProjectReference Include="..\\Chummer\.Contracts\\Chummer\.Contracts\.csproj"' "$project_file" >/dev/null 2>&1; then
    echo "active contract consumer ${project_file} must not reference Chummer.Contracts.csproj directly" >&2
    exit 1
  fi

  if ! rg -n 'PackageReference Include="Chummer\.Engine\.Contracts" Version="\$\(ChummerEngineContractsPackageVersion\)"' "$project_file" >/dev/null 2>&1; then
    echo "active contract consumer ${project_file} must package-reference Chummer.Engine.Contracts with ChummerEngineContractsPackageVersion" >&2
    exit 1
  fi
done

for project_file in "${compatibility_contract_exceptions[@]}"; do
  if ! rg -n 'ProjectReference Include="..\\Chummer\.Contracts\\Chummer\.Contracts\.csproj"' "$project_file" >/dev/null 2>&1; then
    echo "compatibility exception ${project_file} must stay explicit until legacy retirement closes" >&2
    exit 1
  fi
done

rg -n '<ChummerEngineContractsPackageVersion>0\.0\.0-local</ChummerEngineContractsPackageVersion>|<ChummerEngineContractsLocalFeed>|RestoreAdditionalProjectSources' Directory.Build.props >/dev/null
rg -n 'bootstrap-contracts-feed\.sh|ChummerEngineContractsPackageVersion|CHUMMER_ENGINE_CONTRACTS_PACKAGE_VERSION' scripts/ai/build.sh scripts/ai/restore.sh scripts/ai/test_core_engine.sh >/dev/null
rg -n 'CustomDataXmlBridgeDeterministicReceipt|TranslatorLaneDeterministicReceipt|ImportOracleLaneDeterministicReceipt' Chummer.Contracts/Api/ToolCatalogModels.cs >/dev/null
rg -n 'CustomDataXmlBridgeDeterministicReceipt|TranslatorLaneDeterministicReceipt|ImportOracleLaneDeterministicReceipt|Sr6SuccessorLaneDeterministicReceipt' Chummer.Contracts/Api/ToolCatalogModels.cs >/dev/null
rg -n 'BuildCustomDataXmlBridgeDeterministicReceipt|BuildTranslatorDeterministicReceipt|BuildImportOracleDeterministicReceipt|BuildSr6SuccessorDeterministicReceipt' Chummer.Infrastructure/Xml/XmlToolCatalogService.cs >/dev/null
python3 tests/test_next90_m141_import_route_receipts.py
python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m143 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m143_export_print_supplement_rule_environment_receipts.py
python3 scripts/verify-next90-m143-export-print-supplement-rule-environment-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M143_EXPORT_PRINT_SUPPLEMENT_RULE_ENVIRONMENT_RECEIPTS.generated.json --check
rg -n 'SessionActionBudgetDeterministicReceipt' Chummer.Contracts/Session/SessionActionBudgetContracts.cs >/dev/null
rg -n 'CoveragePercent|CoveredWorkflowRouteIds|MissingWorkflowRouteIds' Chummer.Contracts/Session/SessionActionBudgetContracts.cs Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs >/dev/null
rg -n 'WorkspaceWorkflowDeterministicReceipt' Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs Chummer.Contracts/Workspaces/WorkspaceApiModels.cs >/dev/null
rg -n 'WorkspaceExchangeDeterministicReceipt' Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs Chummer.Contracts/Workspaces/WorkspaceApiModels.cs >/dev/null
rg -n 'BuildDeterministicReceipt|BuildWorkflowDeterministicReceipt|BuildExchangeDeterministicReceipt|BuildRuleEnvironmentReceipt' Chummer.Application/Session/DefaultSessionActionBudgetService.cs Chummer.Application/Workspaces/WorkspaceService.cs >/dev/null
test -f docs/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.md
test -f scripts/verify-next90-m142-dense-workbench-receipts.py
test -f tests/test_next90_m142_dense_workbench_receipts.py
CHUMMER_CORE_ENGINE_TEST_FILTER=parity-m142 dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m142_dense_workbench_receipts.py
python3 scripts/verify-next90-m142-dense-workbench-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M142_DENSE_WORKBENCH_RECEIPTS.generated.json --check
test -f scripts/generate-engine-proof-pack.py
python3 tests/test_engine_proof_pack_generator.py
test -f scripts/verify-opposition-packet-contracts.py
test -f tests/test_opposition_packet_contract_receipt.py
python3 tests/test_opposition_packet_contract_receipt.py
python3 scripts/verify-opposition-packet-contracts.py --repo-root . --out .codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json --check
test -f scripts/verify-explain-value-packets.py
test -f tests/test_explain_value_packet_receipt.py
python3 tests/test_explain_value_packet_receipt.py
python3 scripts/verify-explain-value-packets.py --repo-root . --out .codex-studio/published/EXPLAIN_VALUE_PACKETS.generated.json --check
test -f scripts/verify-next90-m114-rule-environment-studio.py
test -f tests/test_next90_m114_rule_environment_studio.py
CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m114_rule_environment_studio.py
python3 scripts/verify-next90-m114-rule-environment-studio.py --repo-root . --out .codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json --check
test -f docs/NEXT90_M121_ACTION_ECONOMY_CONTRACTS.md
test -f scripts/verify-next90-m121-action-economy.py
test -f tests/test_next90_m121_action_economy.py
CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m121-action-economy dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m121_action_economy.py
python3 scripts/verify-next90-m121-action-economy.py --repo-root . --out .codex-studio/published/NEXT90_M121_ACTION_ECONOMY.generated.json --check
test -f docs/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.md
test -f scripts/verify-next90-m122-campaign-advance-receipts.py
test -f tests/test_next90_m122_campaign_advance_receipts.py
CHUMMER_CORE_ENGINE_TEST_FILTER=next90-m122-campaign-advance-receipts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m122_campaign_advance_receipts.py
python3 scripts/verify-next90-m122-campaign-advance-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M122_CAMPAIGN_ADVANCE_RECEIPTS.generated.json --check
test -f scripts/verify-next90-m115-core-exchange-contracts.py
test -f tests/test_next90_m115_core_exchange_contracts.py
CHUMMER_CORE_ENGINE_TEST_FILTER=core-exchange-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m115_core_exchange_contracts.py
python3 scripts/verify-next90-m115-core-exchange-contracts.py --repo-root . --out .codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json --check
python3 scripts/generate-engine-proof-pack.py --check
python3 scripts/ai/verify_design_mirror.py
rg -n '"queue_completion_action": "verify_closed_package_only"|"design_queue_completion_action": "verify_closed_package_only"|"queue_do_not_reopen_reason": "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package\."|"design_queue_do_not_reopen_reason": "M104 chummer6-core engine proof pack is complete; future shards must verify this receipt, queue row, design queue row, and closeout note instead of reopening the proof-pack package\."|"queue_closure_field_drift": \[\]|"published_import_oracle_names": \[|"published_adjacent_oracle_names": \[|"malformed_import_oracle_rows": \[\]|"malformed_adjacent_oracle_rows": \[\]|"unexpected_import_oracle_names": \[\]|"unexpected_adjacent_oracle_names": \[\]' .codex-studio/published/ENGINE_PROOF_PACK.generated.json >/dev/null

if rg -n 'repo_tool\.sh|repo_inspect\.sh|read_file\.sh|find_text\.sh|replace_text_literal\.sh|git_commit_repo_work\.sh|git_status\.sh|upsert_env_var\.sh' \
  Chummer.Api Chummer.Application Chummer.Core Chummer.CoreEngine.Tests Chummer.Infrastructure Chummer.Rulesets --glob '*.cs' --glob '*.csproj' --glob '*.props' --glob '*.targets' >/dev/null 2>&1; then
  echo "repo helper and git/env utility scripts must stay operational-only and outside active runtime semantics" >&2
  exit 1
fi

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
  if rg -n 'HintPath>.*chummer-core-engine.*Chummer\.Contracts.*bin.*Chummer\.Engine\.Contracts\.dll' \
    ../chummer.run-services/Chummer.Campaign.Contracts/Chummer.Campaign.Contracts.csproj \
    ../chummer.run-services/Chummer.Run.Contracts/Chummer.Run.Contracts.csproj \
    ../chummer.run-services/Chummer.Run.Api/Chummer.Run.Api.csproj >/dev/null 2>&1; then
    echo "run-services bridge projects must project-reference Chummer.Contracts instead of hint-pathing the engine contracts dll" >&2
    exit 1
  fi
fi

if [ -d ../chummer-play ]; then
  test -f ../chummer-play/src/Chummer.Play.Web/BrowserSessionOfflineCacheService.cs
  test -f ../chummer-play/src/Chummer.Play.Web/BrowserState/RuntimeBundleCacheEntry.cs
  rg -n 'VerifyResumePreservesLedgerWhenRuntimeBundleDriftsAsync|VerifyOfflineCacheRuntimeBundleQuotaEvictionAsync' \
    ../chummer-play/src/Chummer.Play.RegressionChecks/Program.cs >/dev/null
fi

"$(dirname "$0")/build.sh" "$@"
"$(dirname "$0")/test_core_engine.sh" --no-build
