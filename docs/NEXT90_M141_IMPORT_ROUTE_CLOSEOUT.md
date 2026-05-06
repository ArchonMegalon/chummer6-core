# Next90 M141 Core Import Route Closeout

Package: `next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic`  
Frontier: `4304178368`  
Flagship frontier: `2350979521`  
Milestone: `141`  
Owner: `chummer6-core`

## Closed scope

This core slice is closed when these repo-local facts remain true:

- `Chummer.Contracts/Api/ToolCatalogModels.cs` keeps the four deterministic `/api/tools/master-index` receipt contracts on the canonical response surface:
  `customDataXmlBridgeDeterministicReceipt`, `translatorDeterministicReceipt`, `importOracleDeterministicReceipt`, and `amendPackageDeterministicReceipt`.
- `Chummer.Infrastructure/Xml/XmlToolCatalogService.cs` continues to build those receipts directly from the governed custom-data/XML bridge posture, translator corpus posture, import parity certification, and `ENGINE_PROOF_PACK.generated.json` amend-package suite rows.
- `Chummer.Tests/ToolCatalogServiceTests.cs` and `Chummer.Tests/ApiIntegrationTests.cs` keep the service and serialized API shape pinned to `source:translator_route`, `family:custom_data_xml_and_translator_bridge`, `family:legacy_and_adjacent_import_oracles`, and the engine-proof-pack-backed amend-package receipt.
- `.codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json` reports `status=passed`, keeps `package_id=next90-m141-core-bind-import-oracle-custom-data-and-amend-package-flows-to-deterministic`, `frontier_id=4304178368`, `flagship_frontier_id=2350979521`, and `milestone_id=141`, and cites the stable proof anchors for this package.
- The generated M141 receipt keeps `receipt_family_ids=["family:custom_data_xml_and_translator_bridge","family:legacy_and_adjacent_import_oracles"]`, `parity_route_id=source:translator_route`, and `engine_proof_pack_required_suite_ids=["source_toggle","amend_package"]` so parity and workflow gates can cite one canonical deterministic core proof bundle.
- The verifier still fails closed when the canonical `141.2` successor registry work-task row or either queue mirror row drifts, duplicates, or loses the package identity.
- The verifier also fails closed when the supporting `ENGINE_PROOF_PACK.generated.json` or `IMPORT_PARITY_CERTIFICATION.generated.json` receipts keep their filenames but lose the expected passed suite statuses, canonical oracle names, per-oracle `1 of 1` source coverage counts, or 100% aggregate parity coverage that this package cites.
- The verifier also fails closed in `--check` mode when the current payload no longer passes proof, even if a refreshed checked-in receipt would otherwise match byte-for-byte.

## Verification

Future shards assigned this package should verify:

```bash
python3 tests/test_next90_m141_import_route_receipts.py
python3 scripts/verify-next90-m141-import-route-receipts.py --repo-root . --out .codex-studio/published/NEXT90_M141_IMPORT_ROUTE_RECEIPTS.generated.json --check
bash scripts/ai/verify.sh
```

If those checks pass, the correct action is to advance a different open owner slice instead of reopening the core receipt lane.

## Do Not Reopen

Do not reopen this core package for desktop screenshot capture, Hub parity-claim posture, Fleet gate materialization, or EA compare-packet work.
Those are sibling owner packages under milestone `141`; this core lane is limited to deterministic receipt truth for import-oracle, custom-data/XML bridge, translator, and amend-package proof.
