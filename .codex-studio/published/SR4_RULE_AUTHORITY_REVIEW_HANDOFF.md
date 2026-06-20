# SR4 Rule Authority Review Handoff

Generated: 2026-06-20T22:36:46Z

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
- Fixture alignment: `pass`
- Explain alignment: `pass`

## Human Decisions Required

- Confirm source identity, license posture, and edition fit.
- Map indexed rows or line hashes into normalized public-safe rule records.
- Apply, reject as not applicable, or explicitly defer the official errata scope.
- Confirm no sourcebook prose, art, page images, examples, or table text are promoted.
- Sign off before any ready token is emitted.

## Recommended Signoff Path

- spot-check the listed high-volume XML files first; approve row-level mapping if no contradiction is found
- keep Errata decision at not_applicable
- approve the human review file and rerun the ready checks

## Suggested Default Decisions

- Row-level decision: `approved` if the bounded spot checks do not reveal contradictions
- Errata decision: `not_applicable`

## Decision Fields

- Row-level decision: `pending | approved | rejected | defer`
- Errata decision: `pending | applied | not_applicable | defer`
- Final reviewer: `pending`
- Final review timestamp: `pending`

## Blocking Receipts

- `SR4_ROW_LEVEL_AUTHORITY_MAPPING.generated.json`
- `SR4_ERRATA_SOURCE_POSTURE.generated.json`
- `SR4_HUMAN_RULE_REVIEW.md`

## Bounded Spot-Check Plan

- `gear.xml` rows=`1704` sha256=`0ccdf61d8e50619e10b47341f6f4d767d78f566930d21329cc2e11c472bdd799` containers=`gears=1591, categories=113, version=0`
- `weapons.xml` rows=`955` sha256=`a0d2998f485dc499757ab9cc2f49cea29440b3ee133c6b68d7dd5fb86b7c1700` containers=`weapons=743, mods=99, accessories=83`
- `vehicles.xml` rows=`839` sha256=`7396cc6d342853dad3faa1c004d2fa7196cec3e87d69311ba274a472a22d28ee` containers=`vehicles=483, mods=321, categories=19`
- `qualities.xml` rows=`485` sha256=`d5fa2a1aeb6ff47fa984f6b8b9da1a706c1e987f88308ccbfa1ea48cc316e06f` containers=`qualities=483, categories=2, version=0`
- `cyberware.xml` rows=`344` sha256=`5179e29b4c9758d936f6da2269def14f2b85b610502888581e5b1bb0dde179ee` containers=`cyberwares=275, suites=44, categories=13`
- `spells.xml` rows=`259` sha256=`e7e5f9f611bd0106f9f01d50029cd588003c229781fc0d5b73e8f51153c65664` containers=`spells=253, categories=6, version=0`
