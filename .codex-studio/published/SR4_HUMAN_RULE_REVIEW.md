# SR4 Human Rule Review

Generated: 2026-06-16T04:41:21Z
Status: pending
Row-level decision: pending
Errata decision: pending
Reviewer: pending
Review timestamp: pending
Ready token approved: false

## Machine Evidence

- Rulefacts indexed: `449`
- Row-level mapping status: `pending_human_review`
- Indexed unit count: `6989`
- Source count: `27`
- Selected core baseline: `legacy Chummer4 XML as implemented for core readiness`
- Source baseline decision status: `operator_policy_selected_core_baseline`
- Errata posture status: `pending_reviewed_application`
- Errata source count: `0`
- Errata policy: `official errata or official web notices only`
- Public copy policy: `metadata only: file names, hashes, XML container names, and counts; no sourcebook prose, art, page images, item descriptions, or stat rows`

## Required Human Decisions

- Confirm the indexed source surface is the correct edition authority.
- Confirm row-level mappings are normalized facts, not copied source prose or tables.
- Apply, reject as not applicable, or explicitly defer every applicable errata source.
- Confirm fixture expectations are valid against reviewed rule authority.
- Approve the ready token only after row-level and errata decisions are complete.

## Review Inputs

- `SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR4_ERRATA_SOURCE_POSTURE.generated.json`
- `SR4_RULE_AUTHORITY_REVIEW_HANDOFF.md`
- Private registry: `none`

## Indexed Source Files

- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/armor.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/bioware.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/books.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/critterpowers.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/critters.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/cyberware.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/echoes.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/gear.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/improvements.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/lifestyles.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/martialarts.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/mentors.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/metamagic.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/metatypes.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/packs.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/paragons.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/powers.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/programs.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/qualities.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/ranges.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/skills.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/spells.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/streams.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/traditions.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/vehicles.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/vessels.xml`
- `/docker/fleet/repos/chummer4/Chummer/bin/Release/data/weapons.xml`

## Source Identity Evidence

- `(SR4) Shadowrun 4e Core Rules.pdf` at `/mnt/pcloud/personal/Roleplay/sr/(SR4) Shadowrun 4e Core Rules.pdf`; exists=`True`; sha256=`28da9d6dfd8eba79a2ae46dc41e2ec825d16067d288e6f20e23c65767616d41d`

## Approval Contract

Leave this file pending until review is complete. A ready review must change:

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: applied`, `not_applicable`, or `defer` with written rationale
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
- `Errata defer rationale: <reason>` when the errata decision is `defer`
