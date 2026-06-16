# SR4 Rule Authority Decision

## Decision

Build SR4/SR4A support through a private RuleFact registry and deterministic provider layer, using the legacy Chummer4 XML implementation as the core-readiness baseline and the private PDF only for source identity and reviewer anchoring.

```text
Private SR4 PDF
→ private extraction
→ RuleFact registry
→ deterministic SR4 providers
→ explain receipts
→ golden fixtures
→ human review
→ SR4_RULE_AUTHORITY_READY
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

6_llm_or_answerly:
  role: optional explanation humanizer
  status: non_authoritative
```

## Required providers

```yaml
providers:
  - Sr4DiceProvider
  - Sr4TestProvider
  - Sr4EdgeProvider
  - Sr4ActionEconomyProvider
  - Sr4CharacterCreationProvider
  - Sr4MetatypeProvider
  - Sr4AttributeProvider
  - Sr4SkillProvider
  - Sr4QualityProvider
  - Sr4DerivedStatsProvider
  - Sr4CombatProvider
  - Sr4DamageProvider
  - Sr4VehicleProvider
  - Sr4MagicProvider
  - Sr4MatrixProvider
  - Sr4RiggingProvider
  - Sr4GearProvider
  - Sr4AdvancementProvider
  - Sr4ExplainReceiptProvider
```

## Key edition differences to preserve

Do not confuse SR4 and SR6.

```yaml
sr4:
  character_creation: build_points
  initiative: initiative_score_plus_initiative_passes
  actions: free_simple_complex_interrupt
  armor: used in damage resistance and physical_to_stun comparison
  edge: spend/burn edge model
  matrix: commlinks/nodes/programs/firewall-response-signal-system
  magic: spell force and drain
```

## Completion rule

SR4 is not ready until:
- core rules compile;
- all required providers exist;
- all seed facts are implemented;
- tables are imported or explicitly deferred;
- explain receipts exist;
- golden fixtures pass;
- copyright safety passes;
- human review passes.
- supplements remain out of scope unless explicitly promoted later.
