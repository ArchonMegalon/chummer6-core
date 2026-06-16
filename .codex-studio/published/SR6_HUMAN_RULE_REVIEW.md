# SR6 Human Rule Review

Generated: 2026-06-16T02:47:15Z
Status: pending
Row-level decision: pending
Errata decision: pending
Reviewer: pending
Review timestamp: pending
Ready token approved: false

## Machine Evidence

- Rulefacts indexed: `301`
- Row-level mapping status: `pending_human_review`
- Indexed unit count: `5213`
- Source count: `4`
- Source baseline decision status: `pending_human_review`
- Errata posture status: `pending_reviewed_application`
- Errata source count: `3`
- Public copy policy: `metadata only: hashes, positions, categories, and counts; no sourcebook prose, art, page images, examples, item descriptions, or table cell text`

## Required Human Decisions

- Confirm the indexed source surface is the correct edition authority.
- Select or reject the edition/source baseline when multiple books are indexed.
- Confirm row-level mappings are normalized facts, not copied source prose or tables.
- Apply, reject as not applicable, or explicitly defer every applicable errata source.
- Confirm fixture expectations are valid against reviewed rule authority.
- Approve the ready token only after row-level and errata decisions are complete.

## Review Inputs

- `SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR6_ERRATA_SOURCE_POSTURE.generated.json`
- `SR6_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `/docker/chummercomplete/_completion/sr6_rule_authority/private/SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json`

## Indexed Source Files

- `Shadowrun Sixth World.pdf`
- `Shadowrun_6_Downloadversion_2024.pdf`
- `Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf`
- `Shadowrun - 6e - Krime Katalog.pdf`

## Source Identity Evidence

- `Shadowrun Sixth World.pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun Sixth World.pdf`; exists=`True`; sha256=`74ac2d4be4298c79200d9cfebaab235ae2526f45a11c6db1e11cf307a56f76e2`
- `Shadowrun_6_Downloadversion_2024.pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf`; exists=`True`; sha256=`104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023`
- `Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun_Street_Wyrd_(Core_Magic_Rulebook).pdf`; exists=`True`; sha256=`84b0f67bb9347cb8477f636e331d477bea00302ecd50ff98f6e03cef778282c8`
- `Shadowrun - 6e - Krime Katalog.pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun - 6e - Krime Katalog.pdf`; exists=`True`; sha256=`17f50a8bd69219e654641a9b91cbb05329020f240c62342199d3f29690aa673f`

## Approval Contract

Leave this file pending until review is complete. A ready review must change:

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: applied`, `not_applicable`, or `defer` with written rationale
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
- `Errata defer rationale: <reason>` when the errata decision is `defer`
- `Source baseline decision: <selected baseline>` when multiple source files are indexed
