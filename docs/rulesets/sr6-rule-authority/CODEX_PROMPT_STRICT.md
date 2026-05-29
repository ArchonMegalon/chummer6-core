# STRICT CODEX PROMPT — SR6 Rule Authority Implementation

You are Codex. The user provided a private copy of the Shadowrun Sixth World Core Rulebook.

## Critical instruction

Do not copy the rulebook into the repository.

You may implement rule math, formulas, enums, table data needed for computation, source page references, providers, tests, fixtures, and public-safe explain receipts.

You may not copy sourcebook prose, examples, art, page images, or long descriptive text.

## Required architecture

Implement:

```text
private source PDF
→ RuleFact registry
→ deterministic SR6 providers
→ explain receipts
→ golden fixtures
→ human review
→ SR6_RULE_AUTHORITY_READY
```

## Required providers

```text
Sr6DiceProvider
Sr6TestProvider
Sr6EdgeProvider
Sr6ActionEconomyProvider
Sr6CharacterCreationProvider
Sr6MetatypeProvider
Sr6SkillProvider
Sr6QualityProvider
Sr6DerivedStatsProvider
Sr6CombatProvider
Sr6StatusProvider
Sr6MatrixProvider
Sr6MagicProvider
Sr6RiggingProvider
Sr6GearProvider
Sr6AdvancementProvider
Sr6ExplainReceiptProvider
```

## Seed inputs

Use the YAML seeds in this package:
- core mechanics
- character creation
- status effects
- combat
- matrix
- magic
- rigging

Then complete remaining table imports from the private PDF with human review.

## Missing rule behavior

If data is missing, create:

```yaml
status: missing_rulefact
gold_blocker: true
```

Do not invent it.

## Required final artifacts

```text
_completion/sr6_rule_authority/
  SR6_RULEFACT_REGISTRY.generated.json
  SR6_PROVIDER_COVERAGE.generated.json
  SR6_TABLE_IMPORTS.generated.json
  SR6_GOLDEN_FIXTURES.generated.json
  SR6_EXPLAIN_RECEIPTS.generated.json
  SR6_COPYRIGHT_SAFETY.generated.json
  SR6_ERRATA_PROFILE.generated.json
  SR6_HUMAN_RULE_REVIEW.md
  FINAL_SR6_RULE_AUTHORITY_VERDICT.md
```

## Final verdict

Only output:

```text
SR6_RULE_AUTHORITY_READY
```

if all gates pass.

Otherwise output:

```text
NOT_READY
```
