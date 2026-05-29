# SR4 Rule Authority Extraction Package

Date: 2026-05-29  
Source input: user-provided `(SR4) Shadowrun 4e Core Rules.pdf`  
Edition posture: Shadowrun Fourth Edition / 20th Anniversary Core Rulebook  
Purpose: Give Codex an implementation-safe package for SR4/SR4A rules without copying the sourcebook prose.

## What this package is

This is a **rule-authority implementation package** for Chummer/Jammer 6.

It contains:

- implementation-safe SR4 rule facts;
- private source page map;
- RuleFact schema;
- deterministic provider design;
- seed facts for the SR4 core system;
- extraction pipeline for remaining tables;
- golden fixture plan;
- copyright-safe boundaries;
- acceptance gates so Codex cannot falsely claim SR4 is complete.

## What this package is not

It is not a copy of the SR4 rulebook.

It does not include:
- sourcebook prose;
- fiction;
- art;
- page images;
- long examples;
- full gear, spell, quality, program, or critter text;
- official logos;
- any public substitute for the book.

## Core rule

```text
Rule math may be implemented.
Rulebook prose must not be copied into the product.
```

## Final implementation verdict

Codex may only write:

```text
SR4_RULE_AUTHORITY_READY
```

after all required providers, RuleFacts, fixtures, explain receipts, table imports, and human review pass.

Otherwise:

```text
NOT_READY
```

## Files

- `COPYRIGHT_SAFE_BOUNDARY.md`
- `SR4_RULE_AUTHORITY_DECISION.md`
- `SOURCE_MAP.yaml`
- `SR4_RULEFACT_SCHEMA.yaml`
- `SR4_RULESET_PROFILE.yaml`
- `SR4_CORE_MECHANICS_SEED.yaml`
- `SR4_CHARACTER_CREATION_SEED.yaml`
- `SR4_SKILLS_SEED.yaml`
- `SR4_COMBAT_SEED.yaml`
- `SR4_MAGIC_SEED.yaml`
- `SR4_MATRIX_SEED.yaml`
- `SR4_RIGGING_SEED.yaml`
- `SR4_GEAR_DATA_PLAN.md`
- `SR4_EXTRACTION_PIPELINE.md`
- `SR4_PROVIDER_INTERFACES.md`
- `SR4_GOLDEN_FIXTURE_PLAN.yaml`
- `SR4_IMPLEMENTATION_WORKPACKAGES.yaml`
- `VERIFICATION_MATRIX.yaml`
- `FINAL_ACCEPTANCE_GATES.yaml`
- `CODEX_PROMPT_STRICT.md`
