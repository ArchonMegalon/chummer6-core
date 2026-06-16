# SR6 Rule Authority Decision

## Decision

Build SR6 support through a private RuleFact registry and deterministic provider layer, using `Shadowrun_6_Downloadversion_2024.pdf` as the selected core baseline for current signoff scope.

```text
Source PDF
→ private extraction
→ RuleFact registry
→ deterministic providers
→ explain receipts
→ golden fixtures
→ human review
→ SR6_RULE_AUTHORITY_READY
```

## Authority hierarchy

```yaml
1_private_uploaded_core_rulebook:
  role: base source
  status: user_provided_private_source

2_errata_profile:
  role: corrections and versioning
  status: pending
  scope: official errata or official web notices only

3_rulefact_registry:
  role: normalized implementation facts
  status: required

4_deterministic_providers:
  role: computations
  status: required

5_golden_fixtures:
  role: validation
  status: required

6_answerly_or_llm:
  role: optional explanation humanizer
  status: non_authoritative
```

## Required Chummer components

```yaml
providers:
  - Sr6DiceProvider
  - Sr6TestProvider
  - Sr6EdgeProvider
  - Sr6ActionEconomyProvider
  - Sr6CharacterCreationProvider
  - Sr6MetatypeProvider
  - Sr6SkillProvider
  - Sr6QualityProvider
  - Sr6DerivedStatsProvider
  - Sr6CombatProvider
  - Sr6StatusProvider
  - Sr6MagicProvider
  - Sr6MatrixProvider
  - Sr6RiggingProvider
  - Sr6GearProvider
  - Sr6AdvancementProvider
  - Sr6ExplainReceiptProvider
```

## Completion rule

SR6 is not ready until:
- core rules compile;
- all required providers exist;
- all seed facts are implemented;
- tables are imported or explicitly deferred;
- explain receipts exist;
- fixtures pass;
- human review passes.
- supplements remain out of scope unless explicitly promoted later.
