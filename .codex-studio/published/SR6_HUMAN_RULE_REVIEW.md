# SR6 Human Rule Review

Generated: 2026-06-16T09:20:35Z
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

## Fastest Defensible Pass Path

- spot-check the listed 2024-core line-hash candidates first; approve row-level mapping if no contradiction is found
- prefer an Errata decision of applied when the selected 2024 baseline is accepted as the consolidated core source
- use defer only if a specific official errata source cannot be reconciled to the 2024 baseline
- approve the human review file and rerun the ready checks

## Suggested Default Decisions

- Row-level decision: `approved` if the bounded spot checks below do not reveal contradictions
- Errata decision: `applied` unless a specific official errata source remains unreconciled to the selected 2024 core baseline

## Review Inputs

- `SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR6_ERRATA_SOURCE_POSTURE.generated.json`
- `SR6_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `/docker/chummercomplete/_completion/sr6_rule_authority/private/SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json`

## Indexed Source Files

- `Shadowrun_6_Downloadversion_2024.pdf`

## Source Identity Evidence

- `Shadowrun_6_Downloadversion_2024.pdf` at `/mnt/pcloud/personal/Roleplay/sr/Shadowrun_6_Downloadversion_2024.pdf`; exists=`True`; sha256=`104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023`

## Bounded Spot-Check Plan

- `matrix` page=`3` line=`23` line_sha256=`a970924cd249a508d1b34907264d6e9f9fcc1121682e917d115923a80a440b39` numeric_tokens=`2` dice=`False` money=`False`
- `cyberware_bioware` page=`6` line=`51` line_sha256=`11d548b2152ec5d667b357f2fae2655df8a4de002f05de3c0358b5083ef20681` numeric_tokens=`2` dice=`False` money=`False`
- `magic_spells` page=`133` line=`40` line_sha256=`63668f34c0e005afe3d4fbccf8b5cc26603f20907a38f24903c1b47eb3b73c71` numeric_tokens=`1` dice=`False` money=`True`
- `armor` page=`263` line=`6` line_sha256=`cf4f894d08a3ac37de5f603a740df8c6acde2a7776f9b30cd57b33aca6ae1ded` numeric_tokens=`4` dice=`False` money=`False`
- `rigging_vehicles_drones` page=`5` line=`25` line_sha256=`aab6546fc3fe9d13471ef9089cf39bf757b97096196596b5af8b31da78270cd5` numeric_tokens=`2` dice=`False` money=`False`
- `priority_metatype` page=`3` line=`25` line_sha256=`0fe46d014b2cf63f253c4efd3e75b3cbf75fceca3f5c0613866bf65b4fa2c29c` numeric_tokens=`3` dice=`False` money=`False`

## Approval Contract

Leave this file pending until review is complete. A ready review must change:

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: applied`, `not_applicable`, or `defer` with written rationale
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
- `Errata defer rationale: <reason>` when the errata decision is `defer`
