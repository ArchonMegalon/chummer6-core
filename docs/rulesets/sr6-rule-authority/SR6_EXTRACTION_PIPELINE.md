# SR6 Extraction Pipeline

## Goal

Extract implementation facts, not book prose.

## Inputs

```text
private_source/Shadowrun Sixth World.pdf
```

The source PDF must remain private and must not be committed.

## Pipeline

### 1. Page map

Create:

```text
SR6_SOURCE_PAGE_MAP.generated.json
```

Each page:
```yaml
page:
chapter:
section_heading:
contains_tables:
contains_examples:
contains_art:
extraction_status:
```

### 2. RuleFact extraction

For each relevant section, produce:

```text
SR6_RULEFACT_REGISTRY.generated.json
```

Each fact must use `SR6_RULEFACT_SCHEMA.yaml`.

### 3. Table extraction

Extract only computation tables:
- priority table;
- metatype attribute ranges;
- skill list;
- advancement costs;
- action list;
- status effects;
- gear stats;
- Matrix actions;
- spells;
- spirits;
- vehicles/drones.

Do not extract descriptive prose.

### 4. Human review

Create:

```text
SR6_HUMAN_RULE_REVIEW.md
```

Review:
- math correctness;
- ambiguous rules;
- errata needed;
- copyright safety;
- provider mapping.

### 5. Provider implementation

Provider code must only consume RuleFacts and table data.

### 6. Explain receipts

Every computed result must produce a public-safe explain receipt:
- no sourcebook prose;
- list RuleFact IDs;
- show calculation steps.

## Error handling

If extraction confidence is low:

```yaml
status: needs_human_review
gold_blocker: true
```

## No hallucination rule

If the extracted data is missing, Codex must not fill it from memory. It must create a `missing_rulefact` entry.
