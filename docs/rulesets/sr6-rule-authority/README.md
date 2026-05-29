# SR6 Rule Authority Extraction Package

Date: 2026-05-29  
Source input: user-provided `Shadowrun Sixth World.pdf`  
Purpose: Give Codex an implementation-safe package for SR6 rules without copying the sourcebook prose.

## What this package is

This is a **rule-authority implementation package** for Chummer/Jammer 6.

It contains:

- implementation-safe rule facts;
- page-reference source map;
- provider/interface design;
- extraction pipeline for remaining tables;
- golden fixture plan;
- copyright-safe boundaries;
- acceptance gates so Codex cannot falsely claim SR6 is complete.

## What this package is not

It is not a copy of the rulebook.

It does not include:
- sourcebook prose;
- art;
- long examples;
- full spell/gear/item text;
- official logos;
- full copyrighted tables beyond minimal implementation seeds.

## Core rule

```text
Rule math may be implemented.
Rulebook prose must not be copied into the product.
```

## Final implementation verdict

Codex may only write:

```text
SR6_RULE_AUTHORITY_READY
```

after all required providers, RuleFacts, fixtures, explain receipts, table imports, and human review pass.

Otherwise:

```text
NOT_READY
```

## Files

- `COPYRIGHT_SAFE_BOUNDARY.md`
- `SR6_RULE_AUTHORITY_DECISION.md`
- `SOURCE_MAP.yaml`
- `SR6_RULEFACT_SCHEMA.yaml`
- `SR6_RULESET_PROFILE.yaml`
- `SR6_CORE_MECHANICS_SEED.yaml`
- `SR6_CHARACTER_CREATION_SEED.yaml`
- `SR6_STATUS_EFFECTS_SEED.yaml`
- `SR6_COMBAT_SEED.yaml`
- `SR6_MATRIX_SEED.yaml`
- `SR6_MAGIC_SEED.yaml`
- `SR6_RIGGING_SEED.yaml`
- `SR6_GEAR_DATA_PLAN.md`
- `SR6_EXTRACTION_PIPELINE.md`
- `SR6_PROVIDER_INTERFACES.md`
- `SR6_GOLDEN_FIXTURE_PLAN.yaml`
- `SR6_IMPLEMENTATION_WORKPACKAGES.yaml`
- `VERIFICATION_MATRIX.yaml`
- `FINAL_ACCEPTANCE_GATES.yaml`
- `CODEX_PROMPT_STRICT.md`
