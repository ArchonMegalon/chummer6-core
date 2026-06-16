# SR6 Human Rule Review

Generated: 2026-06-16T04:56:27Z
Status: pending
Row-level decision: pending
Errata decision: pending
Reviewer: pending
Review timestamp: pending
Ready token approved: false

## Machine Evidence

- Rulefacts indexed: `447`
- Row-level mapping status: `pending_human_review`
- Indexed unit count: `5213`
- Source count: `1`
- Selected core baseline: `Shadowrun_6_Downloadversion_2024.pdf`
- Source baseline decision status: `operator_policy_selected_core_baseline`
- Errata posture status: `pending_reviewed_application`
- Errata source count: `3`
- Errata policy: `official errata or official web notices only`
- Public copy policy: `metadata only: hashes, positions, categories, and counts; no sourcebook prose, art, page images, examples, item descriptions, or table cell text`

## Required Human Decisions

- Confirm the indexed source surface is the correct edition authority.
- Confirm row-level mappings are normalized facts, not copied source prose or tables.
- Apply, reject as not applicable, or explicitly defer every applicable errata source.
- Approve the ready token only after row-level and errata decisions are complete.

## Review Inputs

- `SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR6_ERRATA_SOURCE_POSTURE.generated.json`
- `SR6_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `/docker/chummercomplete/_completion/sr6_rule_authority/private/SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json`

## Indexed Source Files

- `Shadowrun_6_Downloadversion_2024.pdf`

## Source Identity Evidence

- `Shadowrun_6_Downloadversion_2024.pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf`; exists=`True`; sha256=`104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023`

## Approval Contract

Leave this file pending until review is complete. A ready review must change:

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: applied`, `not_applicable`, or `defer` with written rationale
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
- `Errata defer rationale: <reason>` when the errata decision is `defer`
