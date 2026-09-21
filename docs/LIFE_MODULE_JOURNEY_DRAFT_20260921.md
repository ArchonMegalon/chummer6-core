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
The draft-only increment passed **59 tests** (`core-journey-tests-final.log`).
The real catalog/FileWorkspaceStore regression follows an Elf through Arcology
Living, Corporate Education, Skipped Further Education and repeated Bounty
Hunter modules. It reopens the disk store after every stage, checks all costs and
saved revisions, preserves character XML and initial effects, rejects stale
confirmation, and stops further repeats at the 750-karma limit. Negative tests
cover missing answers, unknown answers, phase skipping, forged bindings and a
rehashed stored cost change. The route runs both from an imported pending runner
and from the real native Creation bootstrap with no preselected metatype.
An unavailable persistence authority cannot authorize continuation.

## Atomic Origin continuation

The production Origin adapter now projects the next admitted module choices
after nationality and subsequent module decisions. Confirmation appends the
draft entry and its Origin acceptance in the same workspace transaction; the
next book turn is derived before that transaction can commit. The accepted
history must remain an exact prefix, with one new receipt, continuous revisions,
owner/runner identity, canonical facts and previous-turn binding. Replaying a
confirmed command does not append another module or chapter.

The expanded local subset includes `WorkspaceAuxiliaryStateStoreTests`:
**68 passed, 0 failed** (`core-origin-continuation-3.log`). A real catalog,
Foundation service, FileWorkspaceStore and Origin interaction regression accepts
four successive decisions, restores the exact chapter checkpoint after every
disk reopen, and verifies byte-identical confirmation replay. Changed historical
owner and previous-turn bindings are rejected. This is Core integration evidence,
not a completed native Android or provider-generation test.

## Next product work

The Origin projection now offers source-owned text and single-select follow-ups
for nationality and later modules. It never invents required answers. A resolved
preview binds the exact answers to the choice, workspace revision, decision and
Foundation preview. Confirmation re-resolves them and records the input digest
and accepted answer facts in the same atomic module/Origin transaction. Missing,
unknown, oversized or changed answers cannot authorize a write. Existing
no-input contract JSON retains its optional-field shape.

The local focused subset now passes **70 tests**, zero failures
(`core-origin-inputs-4.log`). The new real-catalog cases exercise both nationality
and Arcology Living answers, read-only preview, pending-preview serialization,
disk reopen, tamper rejection, accepted facts and byte-identical command replay.
The Android input controls have their own focused managed test; native device
verification is recorded separately in Android, not inferred from these tests.

A stage with no admitted choices stops with explicit
unfinished-draft wording, not a claim that character creation is complete.
The user still needs an explicit finish decision for the repeatable Real Life
stage. Android subsequently verified the native three-decision path through
Teen Years, chapter reading, process restart and reopening the unchanged
stage-4 checkpoint using Core `6513b144f` (Android `a5ee82a1`). This does not
establish every module or the final mechanical transaction; see Android's
`docs/LIFE_MODULE_BOOK_CONTINUITY_20260921.md` for the exact scope and APK.

Finalization remains blocked: the existing effect compiler only handles a
nationality subgraph. A complete draft sequence must not be presented as applied
mechanics or as a runner ready for Career until cumulative effects and the final
transaction are implemented. Provider prose remains a separate reviewed layer;
no First Book output is used as rules or draft authority here.
