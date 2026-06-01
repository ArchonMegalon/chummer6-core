# Next90 M115 Core Exchange Contracts

This repo now defines one governed portability receipt vocabulary for successor-wave exchange outputs.

Canonical contract file:

- `Chummer.Contracts/Workspaces/WorkspacePortabilityContracts.cs`

The receipt now carries explicit machine-readable fields for:

- `outputKind`: portable dossier, native workspace XML, campaign bundle, replay timeline, session recap, and external exchange lanes.
- `formatId`: each governed sibling lane now has its own portable contract id instead of pretending every downstream output is still just `chummer.portable-dossier.v1`.
- `lineage`: source-to-output artifact progression so downstream consumers can keep one handoff story.
- `compatibility`: source ruleset, target ruleset, warnings, and blocking posture.
- `loss`: none, bounded loss, or blocking loss plus affected sections.
- `provenance`: receipt id, generation timestamp, source artifact id, source format id, and payload hash.
- `portabilityEnvelope`: the exchange posture and supported handoff modes for receiving surfaces.
- `revocation`: a governed replace family, artifact id, and superseded-artifact list so stale exchange artifacts can be retired without local shadow rules.
- `relatedOutputs`: dossier, campaign federation, replay timeline, session recap, and external exchange siblings now each carry their own lineage, compatibility, loss, provenance, portability envelope, and revocation receipts instead of sharing only prose.

Current repo-local producer coverage:

- `Chummer.Application/Workspaces/WorkspaceService.cs` emits the expanded receipt for governed imports and portable dossier exports.
- native XML imports stay on the `native-workspace-xml` rail, while JSON dossier ingress fails closed to an inspect-first `portable-dossier` posture with explicit `format-review-required` and `native-workspace-review` receipts.
- sibling campaign/replay/recap/external receipts now point their provenance back to the portable dossier package while lineage still anchors the governed native workspace as the canonical source artifact.
- dossier and sibling outputs now publish revocation families so governed replace flows can retire stale dossier, campaign, replay, recap, and external exchange artifacts without mutating canonical workspace truth.
- `Chummer.Tests/WorkspaceServiceTests.cs` proves the portable dossier export emits five explicit governed output receipts and that bounded-loss warnings propagate through every sibling lane.

Expected reuse for successor milestone `115`:

- dossier exports stay the canonical first producer
- campaign, replay, recap, and external exchange outputs should reuse the same receipt families instead of inventing parallel portability schemas

Package-local proof:

- `scripts/verify-next90-m115-core-exchange-contracts.py` verifies the canonical registry and both queue mirrors still point this package at milestone `115`, keep `status: in_progress`, and retain the scoped `allowed_paths` plus `owned_surfaces`.
- `.codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json` is the checked-in machine-readable proof receipt for the current repo-local slice.

Proof:

- `Chummer.Tests/WorkspaceServiceTests.cs`

Verification:

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=core-exchange-contracts dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m115_core_exchange_contracts.py
python3 scripts/verify-next90-m115-core-exchange-contracts.py --repo-root . --out .codex-studio/published/NEXT90_M115_CORE_EXCHANGE_CONTRACTS.generated.json
```
