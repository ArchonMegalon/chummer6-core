# SR6 Execution Matrix

Purpose: turn the current scaffolded `Chummer.Rulesets.Sr6` lane into an explicit, testable execution plan for a real deterministic SR6 engine path.

Unlike SR4, there is no named `chummer5a`-style legacy oracle in this repo for SR6. That means the SR6 lane has to bootstrap its parity corpus from checked-in `.chum6` fixtures, curated preview packets, build-kit manifests, runtime-profile expectations, and explicit expected outputs rather than leaning on a historical monolith.

## Current local SR6 state

| Surface | Current state | Local evidence | Implication |
| --- | --- | --- | --- |
| Runtime registration | demoted from the default runtime path | `docs/MIGRATION_BACKLOG.md`, `Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` | SR6 no longer ships as a default-registered experimental host; SR6 stays opt-in until parity proof lands. |
| Plugin shell/catalogs | present | `Chummer.Rulesets.Sr6/Sr6ShellCatalogs.cs` | workbench seams exist, so engine work can land without inventing new UI contracts. |
| Workspace codec | partial | `Chummer.Rulesets.Sr6/Sr6WorkspaceCodec.cs` | import/export shape exists, but most section projections still need real parsing. |
| Rule/script execution | not implemented | `Chummer.Rulesets.Sr6/Sr6RulesetPlugin.cs` | deterministic SR6 capability providers do not exist yet. |
| Fixture anchors | partially present | `Chummer.CoreEngine.Tests/Program.cs`, `Chummer.Tests/BuildKitRegistryServiceTests.cs`, `Chummer.Tests/ActiveRuntimeStatusServiceTests.cs` | the repo already has curated SR6 starter/runtime expectations that can seed a parity corpus. |

## Current mismatch

SR6 is no longer default-registered, and its plugin still returns explicit experimental failures:

- `SR6 rules engine is not implemented; this ruleset remains experimental.`
- script execution also fails with an explicit not-implemented diagnostic.

The runtime mismatch is now explicit and bounded. The lane still needs:

1. real deterministic SR6 providers, or
2. continued SR6 opt-in posture until those providers exist.

## Available SR6 parity anchors

| Anchor type | Current repo evidence | Extraction use | Modern landing zone |
| --- | --- | --- | --- |
| Default runtime profile | `official.sr6.core` fallback status in `Chummer.Tests/ActiveRuntimeStatusServiceTests.cs` | lock profile ids, titles, and runtime-fingerprint expectations | SR6 runtime-profile fixtures and runtime inspector proofs |
| Starter build kits | `edge-runner-starter`, `shadow-face-starter`, `arcane-scout-starter` in `Chummer.Tests/BuildKitRegistryServiceTests.cs` | seed reproducible SR6 concept/build inputs | SR6 Build Lab and build-kit compatibility fixtures |
| Curated NPC preview packets | `neon-razor-biker`, `hex-lantern-mage` in `Chummer.CoreEngine.Tests/Program.cs` | seed real SR6 content examples and validation targets | SR6 NPC/content fixture corpus |
| Workspace/document shape | `sr6/chum6-xml`, `.chum6` output, codec section parsing in `Chummer.Rulesets.Sr6/Sr6WorkspaceCodec.cs` | define the native import/export surface and fixture format | `Chummer.Rulesets.Sr6/Sr6WorkspaceCodec.cs` |
| Shell/workbench seams | `Sr6ShellCatalogs.cs` and ruleset seam tests | keep UI seams stable while mechanics arrive | `Chummer.Rulesets.Sr6` plugin and contracts tests |

## Execution rules

- Treat SR6 preview assets as parity anchors, not as proof of implemented mechanics.
- Every SR6 rule added must terminate in deterministic providers and explainable receipts.
- Every migrated subsystem must gain a checked-in fixture before SR6 behavior claims expand.
- Default runtime registration must not be used as a substitute for completion proof.
- UI-era or API-facing expectations may shape fixtures, but canonical mechanics stay in core.

## Matrix

| Work item | Priority | Domain | Inputs | Concrete engine work | Landing zone | Acceptance proof |
| --- | --- | --- | --- | --- | --- | --- |
| `SR6-01` | P0 | Runtime posture reconciliation | current default-registration posture, `Sr6RulesetPlugin`, migration docs | make the mismatch explicit and choose the rule: either wire real providers or demote SR6 from default runtime until parity proof exists | runtime registration sites, `Chummer.Rulesets.Sr6/Sr6RulesetPlugin.cs`, docs | no hidden “default but experimental” posture remains |
| `SR6-02` | P0 | Workspace import/export completion | `.chum6` payload shape, `Sr6WorkspaceCodec`, future sample saves | replace placeholder `attributes`, `skills`, `inventory`, `qualities`, and `contacts` projections with real SR6 XML parsing; document any unmapped fields | `Chummer.Rulesets.Sr6/Sr6WorkspaceCodec.cs`, `Chummer.CoreEngine.Tests/Program.cs` | fixture-driven codec tests parse real SR6 payloads into stable summaries/sections |
| `SR6-03` | P0 | Content/runtime baseline materialization | `official.sr6.core` expectations, build-kit manifests, NPC preview packets, future SR6 content manifests | materialize a deterministic SR6 content/runtime baseline with stable ids, runtime fingerprints, and rule/profile bindings | SR6 content/runtime layer under core plus profile/runtime services | runtime/profile projections are deterministic and test-locked |
| `SR6-04` | P1 | Character derivation and accounting | sample `.chum6` saves, starter build kits, profile fixtures | implement core derived stats, creation/career accounting, edge pools, initiative, monitors, and build-mode transitions | `Chummer.Rulesets.Sr6` providers and explain hooks | golden characters produce expected outputs and localization-ready diagnostics |
| `SR6-05` | P1 | Qualities, metatype, and effect engine | starter kits, sample saves, future SR6 content manifests | implement deterministic effect application order, stacking rules, metatype/quality gating, and explain receipts | new SR6 provider/effect types under `Chummer.Rulesets.Sr6` | fixture cases prove effect ordering and explain traces stay stable |
| `SR6-06` | P1 | Magic, resonance, edge economy, and action-state validation | awakened/technomancer sample saves, starter kits, future SR6 runtime content | implement MAG/RES enablement, mode-specific validation, and SR6-specific edge/action-state rule hooks | SR6 providers plus codec/profile enrichment | parity fixtures cover mundane, awakened, and matrix-heavy builds |
| `SR6-07` | P1 | Gear, augmentations, vehicles, and legality | curated NPC packets, sample saves, build-kit actions, future SR6 manifests | implement legality, cost, capacity, augmentation impact, and inventory validation for core SR6 equipment domains | SR6 rule providers plus explain receipts | legality and purchase fixtures prove deterministic outputs for core equipment flows |
| `SR6-08` | P2 | Build Lab and NPC vault alignment | starter build kits, curated SR6 preview packets, codec Build Lab projection | keep Build Lab, build-kit compatibility, and curated NPC fixtures aligned with real SR6 mechanics instead of preview-only scaffolds | `Chummer.CoreEngine.Tests`, build-kit/NPC services, SR6 providers | starter kits and curated SR6 NPCs validate against the same rule surface |
| `SR6-09` | P2 | Golden SR6 parity corpus | checked-in `.chum6` fixtures, starter-kit outputs, curated NPC packets, explicit expected outputs | create a deterministic SR6 corpus covering creation, career, edge economy, magic, matrix, gear, and advanced cases | `Chummer.CoreEngine.Tests`, compliance/migration docs | corpus runs clean in repo-standard test paths and explains every known exception |
| `SR6-10` | P0 gate | Runtime honesty/completeness gate | outputs from `SR6-01` through `SR6-09` | replace the experimental SR6 host with real providers and keep or restore default runtime registration only once parity proof exists | `Chummer.Rulesets.Sr6/Sr6RulesetPlugin.cs`, runtime registration sites, migration/compliance docs | no experimental host remains under a shipped runtime posture |

## Recommended execution order

1. `SR6-01` runtime posture reconciliation
2. `SR6-02` codec completion
3. `SR6-03` content/runtime baseline materialization
4. `SR6-09` golden corpus bootstrap
5. `SR6-04` character derivation and accounting
6. `SR6-05` qualities, metatype, and effect engine
7. `SR6-06` magic, resonance, edge economy, and action-state validation
8. `SR6-07` gear and legality
9. `SR6-08` Build Lab and NPC vault alignment
10. `SR6-10` runtime honesty/completeness gate

## Immediate next slice

The next highest-signal implementation slice is:

- decide `SR6-01` explicitly so the repo stops carrying an implicit default-runtime mismatch
- finish `SR6-02` by replacing placeholder section projections in `Sr6WorkspaceCodec`
- bootstrap `SR6-09` from checked-in `.chum6` fixtures plus the existing starter-kit and NPC preview anchors

## Non-goals

- using default runtime registration as evidence of SR6 completeness
- treating curated SR6 preview packets as a substitute for rules parity
- inventing canonical SR6 mechanics outside core
- promoting or retaining SR6 as “ready” while the plugin still returns experimental-host diagnostics
