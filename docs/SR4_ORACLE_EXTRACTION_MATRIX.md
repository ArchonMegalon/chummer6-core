# SR4 Oracle Extraction Matrix

Purpose: turn the historical `chummer5a/chummer` SR4 archive into an explicit extraction plan for `Chummer.Rulesets.Sr4` without importing legacy WinForms ownership into the active engine boundary.

## Current local SR4 state

| Surface | Current state | Local evidence | Implication |
| --- | --- | --- | --- |
| Runtime registration | scaffolded only; not part of default runtime path | `docs/MIGRATION_BACKLOG.md` | SR4 cannot ship by accident; promotion must be gated by parity proof. |
| Plugin shell/catalogs | present | `Chummer.Rulesets.Sr4/Sr4ShellCatalogs.cs` | workbench seams exist, so engine work can land without inventing new UI contracts. |
| Workspace codec | partial | `Chummer.Rulesets.Sr4/Sr4WorkspaceCodec.cs` | import/export shape exists, but most section projections still need real parsing. |
| Rule/script execution | not implemented | `Chummer.Rulesets.Sr4/Sr4RulesetPlugin.cs` | deterministic SR4 capability providers do not exist yet. |
| Test posture | partial scaffold coverage only | `Chummer.CoreEngine.Tests/Program.cs` and `Chummer.Tests/RulesetSeamContractsTests.cs` | SR4 still lacks a parity corpus and executable rules-coverage gate. |

## Legacy oracle inputs

The historical oracle is useful in four ways:

1. Data catalogs: canonical content ids, source-book switches, optional content, and dependency shape.
2. Save/schema behavior: what legacy SR4 files look like and which fields are actually populated.
3. Rule knowledge: how costs, caps, improvements, and edge cases were interpreted in the old app.
4. Parity fixtures: sample characters and workflows that can be replayed against the new deterministic engine.

It is not useful as a target architecture. The old repo is a monolithic WinForms app with UI flow and rule logic fused together.

## Oracle inventory

| Oracle bucket | Legacy source | Extraction use | Modern landing zone |
| --- | --- | --- | --- |
| Content catalogs | `Chummer/bin/Release/data/*.xml` | item ids, categories, books, base stats, rules metadata | new SR4 content loader/materializer under `Chummer.Rulesets.Sr4` |
| Save/schema shape | `Chummer/bin/Debug/data/character.xsd`, saved `.chum4` fixtures, `clsXmlManager.cs` | import field map, nullability, compatibility quirks | `Chummer.Rulesets.Sr4/Sr4WorkspaceCodec.cs` |
| Character math | `Chummer/clsCharacter.cs` | BP/Karma accounting, derived stats, attribute/essence handling, creation/career transitions | `Chummer.Rulesets.Sr4` capability providers |
| Equipment logic | `Chummer/clsEquipment.cs` | availability, cost, capacity, accessory and modification rules | `Chummer.Rulesets.Sr4` capability providers |
| Improvement graph | `Chummer/clsImprovement.cs`, `data/improvements.xml`, `data/qualities.xml` | stacked effects, quality/improvement application order, dependency semantics | `Chummer.Rulesets.Sr4` provider and explain path |
| XML/data loading quirks | `Chummer/clsXmlManager.cs` | source resolution, overrides, custom content behavior | SR4 content import and migration fixtures |
| Flow discovery only | `Chummer/frmCreate.cs`, `Chummer/frmCareer.cs`, `frmSelect*.cs` | identify user-visible edge cases and decision ordering | parity fixtures and acceptance expectations only |
| Print/export parity | `Chummer/bin/Release/sheets/Shadowrun 4*.xsl` | output field expectations and naming sanity checks | migration certification fixtures, not engine truth |

## Extraction rules

- Treat `chummer5a/chummer` as an oracle, not a code donor.
- Prefer extracting ids, schemas, examples, and expected outputs over copying imperative code.
- Every extracted SR4 rule must end in a deterministic provider or a deterministic codec projection.
- Every migrated domain must gain a parity fixture before SR4 is promoted into the default runtime path.
- UI-era control flow may inform fixture design, but it must not become engine ownership.

## Matrix

| Work item | Priority | Domain | Legacy oracle inputs | Concrete engine work | Landing zone | Acceptance proof |
| --- | --- | --- | --- | --- | --- | --- |
| `SR4-01` | P0 | Workspace import/export completion | `character.xsd`, save fixtures, `clsXmlManager.cs`, `clsCharacter.cs` | replace placeholder `attributes`, `skills`, `inventory`, and `contacts` projections with real SR4 XML parsing; document any unmapped fields | `Chummer.Rulesets.Sr4/Sr4WorkspaceCodec.cs`, `Chummer.CoreEngine.Tests/Program.cs` | fixture-driven codec tests parse real SR4 payloads into stable summaries/sections |
| `SR4-02` | P0 | Content baseline materialization | `data/armor.xml`, `bioware.xml`, `books.xml`, `cyberware.xml`, `gear.xml`, `metatypes.xml`, `qualities.xml`, `skills.xml`, `spells.xml`, `vehicles.xml`, `weapons.xml`, plus the remaining SR4 data XML set | build a deterministic SR4 content ingest/materialization layer with stable ids, book gates, and override posture | new SR4 content/materialization types under `Chummer.Rulesets.Sr4` | content counts, ids, and source-book fingerprints are test-locked |
| `SR4-03` | P1 | Character derivation and chargen accounting | `clsCharacter.cs`, `data/metatypes.xml`, `data/skills.xml`, `data/qualities.xml` | implement BP/Karma totals, derived attributes, initiative, condition monitors, essence, and creation/career mode transitions | `Chummer.Rulesets.Sr4/Sr4RulesetPlugin.cs` plus new SR4 providers | golden characters produce expected derived outputs and localized diagnostics |
| `SR4-04` | P1 | Improvement and quality engine | `clsImprovement.cs`, `data/improvements.xml`, `data/qualities.xml`, `data/metamagic.xml`, `data/powers.xml` | model deterministic effect application order, stacking rules, and explain receipts for improvements and qualities | new SR4 provider/effect types under `Chummer.Rulesets.Sr4` | fixture cases prove stacked modifications and explain traces stay stable |
| `SR4-05` | P1 | Magic, resonance, and special-mode enablement | `clsCharacter.cs`, `data/traditions.xml`, `data/mentors.xml`, `data/metamagic.xml`, `data/echoes.xml`, `data/streams.xml`, `data/paragons.xml`, `data/programs.xml`, `data/powers.xml` | implement adept/magician/technomancer mode switching, MAG/RES enablement, initiation/submersion hooks, and mode-specific validation | new SR4 rule providers plus codec/profile enrichment | parity fixtures cover awakened, mundane, and technomancer characters |
| `SR4-06` | P1 | Gear, cyberware, armor, and vehicle legality | `clsEquipment.cs`, `data/armor.xml`, `data/bioware.xml`, `data/cyberware.xml`, `data/gear.xml`, `data/vehicles.xml`, `data/vessels.xml`, `data/weapons.xml`, `data/ranges.xml` | implement cost, availability, slot/capacity, accessory/modification, and essence-impact validation | new SR4 rule providers plus explain receipts | legality and purchase fixtures prove deterministic outputs for core equipment flows |
| `SR4-07` | P2 | Martial arts, critters, summons, and edge catalogs | `data/critterpowers.xml`, `data/critters.xml`, `data/martialarts.xml`, `data/spells.xml`, `data/traditions.xml` | cover non-core but rules-significant catalogs and the provider hooks they require | SR4 content layer plus targeted providers | curated corpus covers at least one fixture per advanced subsystem |
| `SR4-08` | P2 | Packs, starter builds, and Build Lab mapping | `data/packs.xml`, legacy create/career flows, sample saves | map SR4 starter/package content into deterministic Build Lab inputs and intake projections | `Sr4WorkspaceCodec`, Build Lab projection hooks, `Chummer.CoreEngine.Tests` | Build Lab projections can seed reproducible SR4 concept variants |
| `SR4-09` | P2 | Golden SR4 parity corpus | representative `.chum4` saves, print sheets, manual parity expectations from the oracle repo | check in a deterministic SR4 fixture corpus that covers creation, career, magic, matrix, gear, and edge cases | `Chummer.CoreEngine.Tests`, `docs/LEGACY_MIGRATION_CERTIFICATION.md` | corpus runs clean in repo-standard test paths and can explain every known exception |
| `SR4-10` | P0 gate | Runtime promotion gate | outputs from `SR4-01` through `SR4-09` | replace the experimental SR4 unavailable host with real providers and only then add SR4 to the default runtime path | `Chummer.Rulesets.Sr4/Sr4RulesetPlugin.cs`, runtime registration sites, migration/compliance docs | no placeholder codec sections, no experimental host, corpus-backed pass gate, and explicit migration-certification update |

## Recommended execution order

1. `SR4-01` codec completion
2. `SR4-02` content baseline materialization
3. `SR4-09` golden corpus bootstrap
4. `SR4-03` character derivation and chargen accounting
5. `SR4-04` improvement and quality engine
6. `SR4-05` magic/resonance enablement
7. `SR4-06` equipment legality
8. `SR4-07` advanced subsystem coverage
9. `SR4-08` Build Lab mapping
10. `SR4-10` runtime promotion gate

## Immediate next slice

The next highest-signal implementation slice is:

- finish `SR4-01` by replacing placeholder section projections in `Sr4WorkspaceCodec`
- bootstrap `SR4-09` with a checked-in sample corpus from the legacy oracle
- use those fixtures to drive `SR4-03` instead of writing providers blind

## Non-goals

- porting WinForms-era forms or controller code into the active engine solution
- reviving legacy UI ownership inside `chummer6-core`
- promoting SR4 into the default runtime path before parity proof exists
- treating legacy print-sheet behavior as canonical engine semantics
