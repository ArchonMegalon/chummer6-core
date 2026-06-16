# SR4 Rule Authority Review Handoff

Generated: 2026-06-16T04:56:27Z

## Current Verdict

- Ready token: withheld
- Row-level mapping: `pending_human_review`
- Errata posture: `not_applicable_by_policy`
- Ready for gold: `False`

## Machine-Completed Evidence

- Indexed units: `6989`
- Source count: `27`
- Selected core baseline: `legacy Chummer4 XML as implemented for core readiness`
- Registry rulefacts: `449`
- Public-safe row receipt: `True`
- Errata source metadata count: `0`
- Errata policy: `official errata or official web notices only`

## Human Decisions Required

- Confirm source identity, license posture, and edition fit.
- Map indexed rows or line hashes into normalized public-safe rule records.
- Apply, reject as not applicable, or explicitly defer the official errata scope.
- Confirm no sourcebook prose, art, page images, examples, or table text are promoted.
- Sign off before any ready token is emitted.

## Decision Fields

- Row-level decision: `pending | approved | rejected | defer`
- Errata decision: `pending | applied | not_applicable | defer`
- Final reviewer: `pending`
- Final review timestamp: `pending`

## Blocking Receipts

- `SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR4_ERRATA_SOURCE_POSTURE.generated.json`
- `SR4_HUMAN_RULE_REVIEW.md`
