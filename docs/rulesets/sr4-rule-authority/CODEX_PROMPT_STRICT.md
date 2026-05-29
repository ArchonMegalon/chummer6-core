# STRICT CODEX PROMPT — SR4 Rule Authority Implementation

You are Codex. The user provided a private copy of the Shadowrun Fourth Edition / 20th Anniversary Core Rulebook.

## Critical instruction

Do not copy the rulebook into the repository.

You may implement:
- rule math;
- formulas;
- enums;
- short implementation facts;
- numeric/stat tables needed for computation;
- source page references;
- providers;
- tests;
- fixtures;
- public-safe explain receipts.

You may not copy:
- sourcebook prose;
- fiction;
- examples wholesale;
- art;
- page images;
- long item/spell/quality descriptions;
- official logos.

## Required architecture

Implement:

```text
private source PDF
→ SR4 RuleFact registry
→ deterministic SR4 providers
→ explain receipts
→ golden fixtures
→ human review
→ SR4_RULE_AUTHORITY_READY
```

## Required providers

```text
Sr4DiceProvider
Sr4TestProvider
Sr4EdgeProvider
Sr4ActionEconomyProvider
Sr4CharacterCreationProvider
Sr4MetatypeProvider
Sr4AttributeProvider
Sr4SkillProvider
Sr4QualityProvider
Sr4DerivedStatsProvider
Sr4CombatProvider
Sr4DamageProvider
Sr4VehicleProvider
Sr4MagicProvider
Sr4MatrixProvider
Sr4RiggingProvider
Sr4GearProvider
Sr4AdvancementProvider
Sr4ExplainReceiptProvider
```

## Mandatory edition separation

Do not implement SR4 by reusing SR6 assumptions.

SR4:
- Build Points, not SR6 priority/customization.
- Free/Simple/Complex/Interrupt Actions, not SR6 Minor/Major.
- Initiative Score and Initiative Passes, not SR6 initiative dice model.
- Armor participates in damage resistance and damage type comparison.
- Edge is spent/burned, not the SR6 Edge action economy.
- Matrix uses nodes/programs/commlink attributes Firewall/Response/Signal/System.
- Magic uses Force and Drain values.

## Missing rule behavior

If a rule/table is missing, create:

```yaml
status: missing_rulefact
gold_blocker: true
```

Do not invent it.

## Required final artifacts

```text
_completion/sr4_rule_authority/
  SR4_RULEFACT_REGISTRY.generated.json
  SR4_PROVIDER_COVERAGE.generated.json
  SR4_TABLE_IMPORTS.generated.json
  SR4_GOLDEN_FIXTURES.generated.json
  SR4_EXPLAIN_RECEIPTS.generated.json
  SR4_COPYRIGHT_SAFETY.generated.json
  SR4_ERRATA_PROFILE.generated.json
  SR4_SR6_SEPARATION_TESTS.generated.json
  SR4_HUMAN_RULE_REVIEW.md
  FINAL_SR4_RULE_AUTHORITY_VERDICT.md
```

## Final verdict

Only output:

```text
SR4_RULE_AUTHORITY_READY
```

if all gates pass.

Otherwise output:

```text
NOT_READY
```
