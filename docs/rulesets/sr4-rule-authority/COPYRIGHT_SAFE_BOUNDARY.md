# Copyright-Safe Boundary

## Decision

The uploaded SR4 PDF may be used as a **private source reference** for implementation, but the repository must not contain a substitute for the book.

## Allowed

```yaml
allowed:
  - formulas
  - enum values
  - short implementation facts
  - numerical tables required for computation
  - source page references
  - provider code
  - tests and fixtures
  - public-safe explain receipts
```

## Forbidden

```yaml
forbidden:
  - copying sourcebook prose
  - copying fiction
  - copying examples wholesale
  - copying art
  - committing the PDF
  - committing page images
  - exposing full gear/spell/quality descriptions verbatim
  - using official logos or page art
  - public support answers that quote the book at length
```

## Public rules coach boundary

A public rules coach may say:

```text
Chummer calculated this using the active SR4 profile and these RuleFact IDs.
```

It must not say:

```text
The book says...
```

unless a separate human-approved quotation/citation policy exists.

## Private source references

Use compact refs:

```yaml
source_ref:
  book: sr4a_core_2009
  page: 74
  anchor: edge_spending
```

Do not store long source text under those keys.

## Codex warning

Codex must not infer SR4 rules from model memory. If a rule fact is not present in a source reference, fixture, or existing provider, it must be marked as `missing_rulefact`.
