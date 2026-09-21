# SR5 Life Modules — continued draft sequence

This is Core source work on the Life Modules/Origin feature branch. It is not a
package seal, Android runtime qualification, character finalization, or Play release.

## Implemented

`CharacterCreationFoundationService` now also implements
`ICharacterCreationLifeModuleJourneyService`: load the next stage, preview one
module with its explicit follow-up answers, and confirm that exact preview.

The existing Foundation draft retains metatype and nationality. Its optional
`AdditionalModules` contains the ordered, source-projected continuation:
Formative Years, Teen Years, Further Education, then repeatable Real Life.
Absent continuation data is omitted from JSON, preserving the serialized shape
and digest of existing nationality-only drafts.

Each confirmation atomically appends to the workspace's existing draft and
advances the saved revision. It does not write attributes, skills, qualities or
other effects into character XML. Every later load reprojects the accepted
entries from the enabled source catalog; recomputing a tampered draft hash does
not legitimize changed costs, effects, follow-ups, stage order or story templates.

Karma includes metatype and nationality once, plus every accepted continuation
module, including repeats. A stale workspace/draft/source binding, missing or
unknown follow-up, altered preview digest, absent explicit confirmation, or
over-budget selection cannot append. Starting continuation locks legacy
metatype/nationality replacement so later decisions are not silently invalidated.

## Focused verification

The `CharacterCreationFoundation*` and `LifeModuleOriginDossierServiceTests`
subset runs locally in the existing keyless Docker toolchain with .NET 10.0.103.
Final result: **59 passed, 0 failed** (`core-journey-tests-final.log`).
The real catalog/FileWorkspaceStore regression follows an Elf through Arcology
Living, Corporate Education, Skipped Further Education and repeated Bounty
Hunter modules. It reopens the disk store after every stage, checks all costs and
saved revisions, preserves character XML and initial effects, rejects stale
confirmation, and stops further repeats at the 750-karma limit. Negative tests
cover missing answers, unknown answers, phase skipping, forged bindings and a
rehashed stored cost change. The route runs both from an imported pending runner
and from the real native Creation bootstrap with no preselected metatype.
An unavailable persistence authority cannot authorize continuation.

## Next product work

This continuation service is not yet connected to the Android Origin decisions.
The current Origin adapter still emits `nationality-accepted` as its terminal
turn. Its acceptance append and next-turn projection must advance atomically
with each module, preserving the existing chapter/checkpoint/recovery chain.
Android also needs the later-stage and follow-up controls.

Finalization remains blocked: the existing effect compiler only handles a
nationality subgraph. A complete draft sequence must not be presented as applied
mechanics or as a runner ready for Career until cumulative effects and the final
transaction are implemented. Provider prose remains a separate reviewed layer;
no First Book output is used as rules or draft authority here.
