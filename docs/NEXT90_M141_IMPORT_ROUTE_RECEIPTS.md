# Next90 M141 Import Route Receipts

This repo owns the core slice for milestone `141` package `next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic`.

- package frontier id: `4304178368`
- flagship closeout frontier id: `2350979521`
- milestone id: `141`
- wave: `W22P`
- repo: `chummer6-core`

## Scope

This slice makes the import-route lane machine-readable for parity and workflow gates instead of leaving it as loose posture strings only.

The `/api/tools/master-index` response now carries four deterministic receipt objects:

- `customDataXmlBridgeDeterministicReceipt`
- `translatorDeterministicReceipt`
- `importOracleDeterministicReceipt`
- `amendPackageDeterministicReceipt`

Those receipts bind the exact parity targets the wider W22P gate plane needs:

- `source:translator_route`
- `family:custom_data_xml_and_translator_bridge`
- `family:legacy_and_adjacent_import_oracles`

## Contract intent

- `customDataXmlBridgeDeterministicReceipt` keeps custom-data authoring posture and XML bridge posture on one canonical object.
- `translatorDeterministicReceipt` keeps translator route posture, bridge posture, and language-overlay counts on one canonical object.
- `importOracleDeterministicReceipt` keeps legacy import-oracle posture, adjacent SR6 oracle posture, fixture counts, and missing-source truth on one canonical object.
- `amendPackageDeterministicReceipt` binds the `source_toggle` and `amend_package` oracle-suite rows from `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` onto the same master-index response, so parity and workflow gates can cite amend-package proof directly instead of carrying a side lookup.

## Published receipt

The checked-in proof artifact for this package is:

- `.codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json`

It binds the stable core anchors for this slice:

- `Chummer.Contracts/Api/ToolCatalogModels.cs`
- `Chummer.Infrastructure/Xml/XmlToolCatalogService.cs`
- `Chummer.Tests/ToolCatalogServiceTests.cs`
- `Chummer.Tests/ApiIntegrationTests.cs`
- `.codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json`
- `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`

The verifier fails closed when the canonical `141.2` successor registry work-task row or either queue staging row drifts, duplicates, loses the package identity for this core package, diverges between the Fleet queue mirror and the design-owned queue mirror, or stops carrying the closed-package `status: complete` plus `completion_action: verify_closed_package_only` contract for this lane.
It also fails closed when `.codex-studio/published/ENGINE_PROOF_PACK.generated.json` stops reporting the expected proof-pack contract/package/frontier identity, stops reporting the full required oracle-suite set, stops reporting passed `source_toggle` and `amend_package` suites, or drifts on the exact `sr5` ruleset / release-scope / golden-fixture metadata that this package cites for those two suite rows.
It also fails closed when those cited `source_toggle` and `amend_package` suite rows drift on the exact evidence paths or golden fixture paths that this package binds into its deterministic receipt.
It also fails closed when `.codex-studio/published/IMPORT_PARITY_CERTIFICATION.generated.json` loses the exact Chummer4/Chummer5a/Hero Lab Classic plus Genesis/CommLink6 parity coverage set or drifts away from the expected per-source `1 of 1` coverage counts.
It also fails closed when the local M141 docs or the cited registry/queue package rows try to use worker handoff artifacts or run-local telemetry artifacts as package proof.
It also fails closed when `--check` sees a current failed payload, even if the checked-in receipt was refreshed to that same failed state.
The checked-in receipt also cites the live flagship closeout frontier so desktop workflow and parity gates can point at the current closeout lane instead of only the successor-wave package row.

## Verification

Run:

```bash
python3 tests/test_next90_m141_import_route_receipts.py
python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check
bash scripts/ai/verify.sh
```

The checked-in C# source assertions in `Chummer.Tests/ToolCatalogServiceTests.cs` and `Chummer.Tests/ApiIntegrationTests.cs` remain proof anchors for both service posture and serialized `/api/tools/master-index` shape.
The executable worker-safe lane for this package is the Python verifier plus the standard repo verify script, which fail closed when those C# anchors, the deterministic receipt fields, or the queue and registry bindings drift.
