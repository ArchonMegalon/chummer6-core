# Next90 M141 Import Route Receipts

This repo owns the core slice for milestone `141` package `next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic`.

- frontier id: `4304178368`
- milestone id: `141`
- wave: `W22P`
- repo: `chummer6-core`

## Scope

This slice makes the import-route lane machine-readable for parity and workflow gates instead of leaving it as loose posture strings only.

The `/api/tools/master-index` response now carries three deterministic receipt objects:

- `customDataXmlBridgeDeterministicReceipt`
- `translatorDeterministicReceipt`
- `importOracleDeterministicReceipt`

Those receipts bind the exact parity targets the wider W22P gate plane needs:

- `source:translator_route`
- `family:custom_data_xml_and_translator_bridge`
- `family:legacy_and_adjacent_import_oracles`

## Contract intent

- `customDataXmlBridgeDeterministicReceipt` keeps custom-data authoring posture and XML bridge posture on one canonical object.
- `translatorDeterministicReceipt` keeps translator route posture, bridge posture, and language-overlay counts on one canonical object.
- `importOracleDeterministicReceipt` keeps legacy import-oracle posture, adjacent SR6 oracle posture, fixture counts, and missing-source truth on one canonical object.
- `amend_package` remains grounded through `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`; this slice binds the import-route and custom-data receipts so parity gates can cite them beside the existing engine proof pack instead of inventing a second amend-package oracle.

## Verification

Run:

```bash
dotnet test Chummer.Tests/Chummer.Tests.csproj --filter "FullyQualifiedName~ToolCatalogServiceTests"
dotnet test Chummer.Tests/Chummer.Tests.csproj --filter "FullyQualifiedName~ApiIntegrationTests.Master_index_endpoint_returns_data"
bash scripts/ai/verify.sh
```

The focused `ToolCatalogServiceTests` assertions prove the deterministic receipt objects track both missing and governed posture.
The API integration test proves those receipt objects are serialized on the live `/api/tools/master-index` route.
The standard verify script keeps the M141 receipt contract, docs, and source builders wired into repo-local verification.
