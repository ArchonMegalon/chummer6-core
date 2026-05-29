# Copyright-Safe Boundary

## Decision

The uploaded PDF may be used as a **private source reference** for implementation, but the repository must not contain a substitute for the book.

## Allowed

```yaml
allowed:
  - short rule facts
  - formulas
  - enum values
  - source page references
  - provider code
  - tests and fixtures
  - explain receipts that describe Chummer's calculation
  - compact implementation tables required for computation
```

## Forbidden

```yaml
forbidden:
  - copying sourcebook prose
  - copying examples wholesale
  - copying art
  - publishing the PDF
  - committing full extracted text
  - exposing long spell/gear descriptions verbatim
  - using official logos or page images
  - public support answers that quote the book at length
```

## Public rules coach boundary

A public or support-facing rules coach may say:

```text
Chummer calculated this from the active SR6 profile, enabled options, and the following rule-fact IDs.
```

It must not say:

```text
The book says...
```

unless a human-approved citation/export policy exists.

## Implementation model

Use private source references:

```yaml
source_ref:
  book: sr6_core_2019
  page: 45
  anchor: edge_gain
```

Do not store long source text under that key.

## Human review

Every implemented rule fact must have:
- source page reference;
- deterministic provider reference;
- test reference;
- human review status.

## Codex warning

Codex must not infer rules from model memory. If a rule fact is not in a source reference, fixture, or existing provider, it is `missing`, not guessed.
