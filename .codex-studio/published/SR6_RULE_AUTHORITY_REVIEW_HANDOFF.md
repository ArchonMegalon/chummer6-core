# SR6 Rule Authority Review Handoff

Generated: 2026-06-16T09:46:04Z

## Current Verdict

- Ready token: withheld
- Row-level mapping: `pending_human_review`
- Errata posture: `pending_reviewed_application`
- Ready for gold: `False`

## Machine-Completed Evidence

- Indexed units: `5213`
- Source count: `1`
- Selected core baseline: `Shadowrun_6_Downloadversion_2024.pdf`
- Registry rulefacts: `447`
- Public-safe row receipt: `True`
- Errata source metadata count: `3`
- Errata policy: `official errata or official web notices only`
- Fixture alignment: `pass`
- Explain alignment: `pass`

## Human Decisions Required

- Confirm source identity, license posture, and edition fit.
- Map indexed rows or line hashes into normalized public-safe rule records.
- Apply, reject as not applicable, or explicitly defer the official errata scope.
- Confirm no sourcebook prose, art, page images, examples, or table text are promoted.
- Sign off before any ready token is emitted.

## Recommended Signoff Path

- spot-check the listed 2024-core line-hash candidates first; approve row-level mapping if no contradiction is found
- prefer an Errata decision of applied when the selected 2024 baseline is accepted as the consolidated core source
- use defer only if a specific official errata source cannot be reconciled to the 2024 baseline
- approve the human review file and rerun the ready checks

## Suggested Default Decisions

- Row-level decision: `approved` if the bounded spot checks do not reveal contradictions
- Errata decision: `applied`

## Decision Fields

- Row-level decision: `pending | approved | rejected | defer`
- Errata decision: `pending | applied | not_applicable | defer`
- Final reviewer: `pending`
- Final review timestamp: `pending`

## Blocking Receipts

- `SR6_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR6_ERRATA_SOURCE_POSTURE.generated.json`
- `SR6_HUMAN_RULE_REVIEW.md`

## Bounded Spot-Check Plan

- `matrix` page=`3` line=`23` line_sha256=`a970924cd249a508d1b34907264d6e9f9fcc1121682e917d115923a80a440b39` numeric_tokens=`2` dice=`False` money=`False`
- `cyberware_bioware` page=`6` line=`51` line_sha256=`11d548b2152ec5d667b357f2fae2655df8a4de002f05de3c0358b5083ef20681` numeric_tokens=`2` dice=`False` money=`False`
- `magic_spells` page=`133` line=`40` line_sha256=`63668f34c0e005afe3d4fbccf8b5cc26603f20907a38f24903c1b47eb3b73c71` numeric_tokens=`1` dice=`False` money=`True`
- `armor` page=`263` line=`6` line_sha256=`cf4f894d08a3ac37de5f603a740df8c6acde2a7776f9b30cd57b33aca6ae1ded` numeric_tokens=`4` dice=`False` money=`False`
- `rigging_vehicles_drones` page=`5` line=`25` line_sha256=`aab6546fc3fe9d13471ef9089cf39bf757b97096196596b5af8b31da78270cd5` numeric_tokens=`2` dice=`False` money=`False`
- `priority_metatype` page=`3` line=`25` line_sha256=`0fe46d014b2cf63f253c4efd3e75b3cbf75fceca3f5c0613866bf65b4fa2c29c` numeric_tokens=`3` dice=`False` money=`False`

## Private Review Registry

- `/docker/chummercomplete/_completion/sr6_rule_authority/private/SR6_TABLE_ROW_HASH_REGISTRY.private.generated.json`
