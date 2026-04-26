# Opposition And Scene Packet Contracts

This slice defines the engine-local contract seam for governed opposition and scene prep packets in `chummer6-core`.

- package id: `next90-m113-core-opposition-packet-contracts`
- frontier id: `2325506990`
- milestone id: `113`

The semantic owner for reusable opposition packets still lives outside this repo, but core now provides:

- canonical `OppositionPacketContract` and `ScenePacketContract` DTOs
- deterministic, rules-backed spotlight stats via `GmPrepPacketRuleStat` plus direct `GmPrepPacketRulesAnchor` pointers
- packet-level peak stats so roster and scene packets advertise their strongest governed pressure lanes without consumers re-aggregating member data
- explicit `GmPrepPacketBoundedLossReceipt` posture, packet identity, ruleset identity, and grounded/runtime-bound stat counts instead of silent packet bluffing
- receipt context stays first-class so downstream prep consumers can display packet identity, ruleset identity, and stat coverage without rebuilding the receipt contract themselves
- a seeded `IOppositionPacketContractService` over the existing NPC vault so downstream repos can bind against executable examples

## Contract posture

- The packet seam is deliberately bounded.
- Threat posture, reusable role lanes, and explain-backed stat anchors are governed.
- Packet-level peak stats stay rules-backed and point back to the governing packet plus source entry set.
- Consumers do not need to parse `ExplainTrace` to discover the governing rule pointer, capability descriptor, source entry, or runtime fingerprint for a stat.
- Exact gear, bespoke tactics, spell order, and scene-specific scripting stay authored and appear in bounded-loss receipts instead of being hidden as fake certainty.

## Seeded coverage

- `red-samurai` demonstrates a single-entry opposition packet with runtime-pinned combat stats.
- `renraku-security` demonstrates a reusable opposition roster packet.
- `renraku-checkpoint` demonstrates a scene packet with explicit role lanes and spotlight stats.
- `broken-pack` and `broken-scene` prove `review-required` bounded-loss posture when governed entries are missing.
- `runtime-unbound-guard` proves the packet remains explicit about missing runtime fingerprints instead of bluffing full grounding.

## Verification

Run:

```bash
dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj --no-restore
python3 tests/test_opposition_packet_contract_receipt.py
python3 scripts/verify-opposition-packet-contracts.py --repo-root . --out .codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json
```

The managed `dotnet run` path is the behavior proof for the seeded opposition and scene packets.
The Python receipt test and verifier are the repo-local package proof anchors that keep this slice in standard verification.
The checked-in receipt lives at `/docker/chummercomplete/chummer6-core/.codex-studio/published/OPPOSITION_PACKET_CONTRACTS.generated.json` and is validated for drift by the focused Python test and verifier check mode.
