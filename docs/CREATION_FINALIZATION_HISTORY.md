# Completed Creation history

This is local implementation guidance, not package or Android release evidence.

## Ownership and persistence

Finalization previously replaced all auxiliary state with its own receipt. A real
Bootstrap → Priority decisions → finalize → cold-reopen test demonstrated loss of
Skills, Qualities, Resources and Gear confirmation history.

`CharacterCreationFinalizationArchive` now retains the complete previous auxiliary
graph. Its canonical digest must equal the finalization receipt's
`PreviousAuxiliaryStateDigest`. The archive and finalization receipt are written
with the final character XML and checkpoint in the existing atomic transaction.
Active Creation drafts are still consumed; they do not become Career inputs.

The archive contains no enclosing archive or earlier finalization receipt. The
store validates the archived graph using its original workspace revision and the
existing draft/receipt validators. Later mutations must preserve it. It remains
outside character XML/downloads and is not provider or rulebook-upload authority.

The new optional field is omitted when null, preserving old auxiliary-state JSON
and digests. Old finalized files remain readable, but previously discarded
history cannot be reconstructed or claimed to exist.

## Recovery

Creation receipt lookups and exact-command replay may read the validated archive.
This applies to Skills, Qualities, Magic/Resonance, Resources, Gear, Contacts and
Lifestyles. It does not permit a new Creation mutation in Career. Active
`Load`/`Preview` paths never use archived drafts as current selections.

The full-service regression retains the original four ordinary Priority commands,
finalizes, cold-reopens, recovers their original receipts, rejects changed-command
replays and refuses new Creation commands without advancing the revision. Other
tests preserve the archive through later reputation and After Run reward commits,
and reject dropped, edited, nested or reactivated historical state.

## Remaining Contacts/Lifestyles integration

This archive is not a completed Contacts/Lifestyles wizard. Current contact edits
require existing contact rows and raw `contactpoints`. New Bootstrap documents
contain neither a derived contact budget nor contact choices. The source-context
capability near `ICharacterSourceDataResolver`'s Contacts/Lifestyles documentation
resolves lifestyles; it is not a contact-budget resolver.

The next implementation must derive budget from the saved source profile's
`contactpointsexpression` and validated typed attribute allocations. Canonical
settings contain both `{CHAUnaug} * 3` and `{CHAUnaug} * 6`; a UI multiplier/default
would be wrong. The legacy oracle is `Character.ContactPoints` in
`Chummer/Backend/Characters/Character.cs`, including expression/rounding behavior.

Normal new-runner contact choices need an auxiliary draft/contribution integrated
with global budgets and finalization. Reusing legacy contact/lifestyle XML writes
would change the raw XML to which existing Priority drafts are bound. Do not
relax those bindings or seed a guessed `contactpoints` field to hide the gap.
Real Contact/Lifestyle confirmation → finalization → Career recovery still needs
its own reachable end-to-end test once that typed path exists.

## Verification boundary

`Chummer.Tests/Chummer.CreationHistory.Tests.csproj` runs the affected existing
Creation suites plus lifecycle/history regressions. Its isolated output paths
avoid shared-obj restore collisions; its friend assembly name retains existing
store fault-injection tests without making those hooks public.

Core package reseal, downstream dependency pins, Android inventory regeneration,
APK/lifecycle proof and Play publication are separate, still-required work.

The broadened suite exposed an existing Magician integration gap: the Attributes
step admitted only Mundane talent. The follow-up in
`CREATION_AWAKENED_ATTRIBUTES.md` implements source-bound special grants and costs;
the positive Magician preview/confirm test remains enabled, not waived or replaced
with a blocked-success claim. Increased Magic is now separately bound to the
Adept power budget; awakened whole-character finalization still needs complete
effect and talent-grant projection.
The historical Qualities unit fixture used a prefixed auxiliary digest; it now
uses the existing raw 64-character workspace-digest contract.
