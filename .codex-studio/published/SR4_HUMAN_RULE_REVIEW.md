# SR4 Human Rule Review

Generated: 2026-06-16T09:46:03Z
Status: pending
Row-level decision: pending
Errata decision: not_applicable
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
- Errata posture status: `not_applicable_by_policy`
- Errata source count: `0`
- Errata policy: `official errata or official web notices only`
- Public copy policy: `metadata only: file names, hashes, XML container names, and counts; no sourcebook prose, art, page images, item descriptions, or stat rows`

## Required Human Decisions

- Confirm the indexed source surface is the correct edition authority.
- Confirm row-level mappings are normalized facts, not copied source prose or tables.
- Approve the ready token only after row-level and errata decisions are complete.

## Fastest Defensible Pass Path

- spot-check the listed high-volume XML files first; approve row-level mapping if no contradiction is found
- keep Errata decision at not_applicable
- approve the human review file and rerun the ready checks

## Suggested Default Decisions

- Row-level decision: `approved` if the bounded spot checks below do not reveal contradictions
- Errata decision: `not_applicable`

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

## Bounded Spot-Check Plan

- `gear.xml` rows=`1704` sha256=`0ccdf61d8e50619e10b47341f6f4d767d78f566930d21329cc2e11c472bdd799` containers=`gears=1591, categories=113, version=0`
- `weapons.xml` rows=`955` sha256=`a0d2998f485dc499757ab9cc2f49cea29440b3ee133c6b68d7dd5fb86b7c1700` containers=`weapons=743, mods=99, accessories=83`
- `vehicles.xml` rows=`839` sha256=`7396cc6d342853dad3faa1c004d2fa7196cec3e87d69311ba274a472a22d28ee` containers=`vehicles=483, mods=321, categories=19`
- `qualities.xml` rows=`485` sha256=`d5fa2a1aeb6ff47fa984f6b8b9da1a706c1e987f88308ccbfa1ea48cc316e06f` containers=`qualities=483, categories=2, version=0`
- `cyberware.xml` rows=`344` sha256=`5179e29b4c9758d936f6da2269def14f2b85b610502888581e5b1bb0dde179ee` containers=`cyberwares=275, suites=44, categories=13`
- `spells.xml` rows=`259` sha256=`e7e5f9f611bd0106f9f01d50029cd588003c229781fc0d5b73e8f51153c65664` containers=`spells=253, categories=6, version=0`

## Approval Contract

Leave this file pending until review is complete. A ready review must change:

- `Status: approved`
- `Row-level decision: approved`
- `Errata decision: not_applicable`
- `Reviewer: <human reviewer>`
- `Review timestamp: <UTC ISO-8601 timestamp>`
- `Ready token approved: true`
