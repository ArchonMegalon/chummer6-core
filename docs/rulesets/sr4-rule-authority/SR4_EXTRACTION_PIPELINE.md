# SR4 Extraction Pipeline

## Goal

Extract implementation facts, not book prose.

## Input

```text
private_source/(SR4) Shadowrun 4e Core Rules.pdf
```

The source PDF must remain private and must not be committed.

## Pipeline

### 1. Page map

Create:

```text
SR4_SOURCE_PAGE_MAP.generated.json
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
SR4_RULEFACT_REGISTRY.generated.json
```

Each fact must use `SR4_RULEFACT_SCHEMA.yaml`.

### 3. Table extraction

Extract computation tables only:
- metatype attribute table;
- quality table;
- skill list/group list;
- action lists;
- combat modifiers;
- armor/damage/weapon tables;
- spell list;
- spirit stats;
- Matrix programs/actions;
- sprite/complex form data;
- gear stats;
- vehicle/drone data;
- lifestyle and advancement tables.

Do not extract fiction or descriptive prose.

### 4. Human review

Create:

```text
SR4_HUMAN_RULE_REVIEW.md
```

Review:
- math correctness;
- SR4 vs SR6 separation;
- table extraction quality;
- errata;
- copyright safety.

### 5. Provider implementation

Provider code must consume RuleFacts and table data only.

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
