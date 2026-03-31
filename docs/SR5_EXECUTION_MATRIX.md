# SR5 Execution Matrix

Purpose: turn the default-registered `Chummer.Rulesets.Sr5` lane into an explicit, testable execution plan for a real deterministic SR5 engine path.

SR5 is the most advanced ruleset lane structurally:

- it is on the default runtime path
- it has the richest legacy oracle (`chummer5a`)
- its workspace codec already delegates to application-layer character queries instead of placeholder section stubs
- the repo already carries runtime-profile, build-kit, NPC-vault, rule-pack, and API-facing SR5 anchors

The deterministic SR5 capability host is now wired for the default capability path (`derive.stat`, `session.quick-actions`), and the remaining lane is broad subsystem parity expansion plus corpus depth.

## Current local SR5 state

| Surface | Current state | Local evidence | Implication |
| --- | --- | --- | --- |
| Runtime registration | on the default runtime path | `docs/MIGRATION_BACKLOG.md` | SR5 is the primary live ruleset lane operationally. |
| Plugin shell/catalogs | present | `Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs` | workbench seams exist and are already wired. |
| Workspace codec | materially real | `Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs` | SR5 already has a stronger import/export and section-query boundary than SR4/SR6. |
| Rule/script execution | deterministic host wired for baseline capabilities | `Chummer.Rulesets.Sr5/Sr5RulesetPlugin.cs` | default SR5 capability execution no longer terminates in an unavailable host. |
| Oracle/parity anchors | rich | `chummer5a`, `official.sr5.core`, SR5 starter build kits, curated SR5 NPC/encounter packets, rule-pack registry/runtime inspector tests | SR5 should be the first lane to graduate from “structurally ready” to “actually computes rules.” |

## Current mismatch

SR5 is default-registered and now executes through `Sr5DeterministicRulesetCapabilityHost` for baseline capabilities. The mismatch shifted from host availability to depth: parity corpus breadth and subsystem coverage are still incomplete.

## Available SR5 oracle and parity anchors

| Anchor type | Current repo evidence | Extraction use | Modern landing zone |
| --- | --- | --- | --- |
| Legacy oracle | `chummer5a` legacy corpus and migration-certification lane | ground SR5 parity in real legacy behavior instead of greenfield interpretation | `docs/LEGACY_MIGRATION_CERTIFICATION.md`, checked-in fixture corpus |
| Default runtime profile | `official.sr5.core` runtime inspector and active-runtime tests | lock profile ids, capability descriptors, runtime fingerprints, and compatibility projections | runtime/profile fixtures and runtime inspector proofs |
| Workspace/document shape | `sr5/chum5-xml`, `.chum5` output, `Sr5WorkspaceCodec` query/metadata boundary | certify import/export shape and section/query stability | `Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs` |
| Starter build kits | `street-sam-starter` and other SR5 build-kit flows in build-kit/API tests | seed reproducible SR5 build inputs and runtime requirements | Build Lab and build-kit compatibility fixtures |
| Curated NPC/encounter packets | `red-samurai`, `renraku-spider`, `renraku-security`, `renraku-checkpoint` | seed real SR5 content examples and validation targets | NPC/content fixture corpus and compatibility proofs |
| Rule-pack/runtime surfaces | rule-pack registry tests, runtime inspector tests, runtime lock/profile services | wire deterministic providers through existing runtime/profile seams instead of inventing a new host path | `Chummer.Rulesets.Sr5` provider host plus runtime/profile services |

## Execution rules

- Treat `chummer5a` as the SR5 behavior oracle, not as a code donor.
- Preserve the current honest failure posture until real deterministic providers exist.
- Reuse existing SR5 runtime/profile/rule-pack seams instead of introducing a second execution path.
- Every migrated SR5 subsystem must gain a fixture and an explainable receipt before widening behavior claims.
- Default runtime registration is allowed only because SR5 is the primary lane; it still needs provider-backed proof before “complete” claims are credible.

## Matrix

| Work item | Priority | Domain | Inputs | Concrete engine work | Landing zone | Acceptance proof |
| --- | --- | --- | --- | --- | --- | --- |
| `SR5-01` | P0 | Deterministic capability-host wiring | `Sr5RulesetPlugin`, runtime/profile services, rule-pack registry surfaces | replace `Sr5UnavailableRulesetCapabilityHost` with a real provider-backed host wired through the existing runtime/profile/rule-pack seams | `Chummer.Rulesets.Sr5/Sr5RulesetPlugin.cs` plus new SR5 provider host types | `derive.stat` and `session.quick-actions` execute deterministically instead of failing unavailable |
| `SR5-02` | P0 | Runtime/profile and provider baseline certification | `official.sr5.core`, runtime inspector tests, rule-pack registry tests | lock canonical SR5 runtime/profile ids, provider bindings, fingerprints, and capability descriptors into a verifier-backed baseline | runtime/profile services, `Chummer.CoreEngine.Tests/Program.cs`, `Chummer.Tests/*Runtime*` | seeded profile projections and provider bindings stay deterministic |
| `SR5-03` | P0 | Workspace codec and export certification | `Sr5WorkspaceCodec`, `chummer5a` saves, migration-certification lane | prove the existing codec/query boundary against a real SR5 corpus and close any remaining section/export gaps | `Chummer.Rulesets.Sr5/Sr5WorkspaceCodec.cs`, `Chummer.CoreEngine.Tests`, migration-certification docs | `.chum5` import/export, section parsing, validation, and metadata update flows stay parity-backed |
| `SR5-04` | P1 | Character derivation and accounting | `chummer5a` corpus, starter build kits, sample career saves | implement or wire derived stats, creation/career accounting, initiative, monitors, essence, and advancement totals through deterministic providers | SR5 providers and explain hooks | golden characters produce expected outputs and localization-ready diagnostics |
| `SR5-05` | P1 | Improvements, qualities, and effect engine | `chummer5a`, quality/improvement fixtures, runtime profile packs | model deterministic effect application order, stacking, gating, and explain receipts for qualities and improvements | SR5 provider/effect types under `Chummer.Rulesets.Sr5` | stacked-modifier fixtures and explain traces stay stable |
| `SR5-06` | P1 | Gear, augmentations, armor, vehicles, and legality | curated NPC packets, build-kit actions, legacy saves, gear/ware fixtures | implement legality, cost, capacity, accessory/modification, and augmentation-impact validation for core SR5 equipment domains | SR5 providers plus explain receipts | legality and purchase fixtures prove deterministic outputs for core equipment flows |
| `SR5-07` | P1 | Magic, resonance, matrix, and special-mode validation | awakened/technomancer SR5 saves, curated packets, runtime profiles | implement MAG/RES enablement, mode-specific validation, matrix hooks, and subsystem-specific provider rules | SR5 providers plus codec/profile enrichment | parity fixtures cover mundane, awakened, and matrix-heavy builds |
| `SR5-08` | P1 | Session quick actions and explain traces | `session.quick-actions` capability descriptor, runtime inspector, session tests | make SR5 quick actions provider-backed, deterministic, and explainable instead of placeholder-unavailable | SR5 script providers plus explain services | session quick actions return deterministic outputs and explain receipts |
| `SR5-09` | P0 | Golden SR5 parity corpus | `chummer5a` saves, curated SR5 starter/build-kit outputs, NPC/encounter packets, explicit expected outputs | check in a deterministic SR5 corpus that covers creation, career, gear, magic, matrix, and edge cases | `Chummer.CoreEngine.Tests`, migration-certification docs, supporting fixtures | corpus runs clean in repo-standard test paths and explains every known exception |
| `SR5-10` | P0 gate | Runtime completeness gate | outputs from `SR5-01` through `SR5-09` | keep SR5 on the default runtime path only with provider-backed proof, parity corpus coverage, and explicit migration-certification closure | `Chummer.Rulesets.Sr5`, runtime registration sites, migration/compliance docs | no unavailable host remains under the default SR5 lane |

## Recommended execution order

1. `SR5-01` deterministic capability-host wiring
2. `SR5-02` runtime/profile baseline certification
3. `SR5-09` golden corpus bootstrap
4. `SR5-03` workspace codec and export certification
5. `SR5-04` character derivation and accounting
6. `SR5-05` improvements, qualities, and effect engine
7. `SR5-06` gear and legality
8. `SR5-07` magic, resonance, matrix, and special-mode validation
9. `SR5-08` session quick actions and explain traces
10. `SR5-10` runtime completeness gate

## Immediate next slice

The next highest-signal implementation slice is:

- expand `SR5-09` from baseline host-capability corpus into full subsystem parity coverage
- drive `SR5-03` and `SR5-04` from `chummer5a` corpus-backed cases rather than speculative provider expansion
- keep `SR5-10` gated on corpus-backed subsystem coverage, not just host wiring

## Non-goals

- treating the current default runtime posture as proof that SR5 mechanics are already complete
- reintroducing UI-era or legacy-app ownership into the active engine solution
- inventing a second SR5 execution path outside the runtime/profile/rule-pack seams already present
- regressing back to unavailable-host semantics on the default SR5 runtime path
