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

### Karma completion (local implementation, 20 September 2026)

Karma now has a separate Core whole-build review and atomic finalization path:
`CharacterCreationKarmaMetatypeService.ReviewFinalization` and
`ConfirmFinalization`, also exposed through the existing owner-bound service.
It consumes the saved Karma decision graph, never a fabricated Priority draft.
Review requires a fresh foundation binding and quote digest. Confirmation binds
the exact review, plan, source authority, dice total and idempotency key.

`LoadFinalizationStartingCash` reads the source-owned dice/multiplier before the
player enters a total. It returns only terms, never a guessed roll or save intent.
The optional `startingCashAuthorityDigest` on review binds the displayed terms;
native callers supply it so source changes cannot silently alter the roll's value.
Two focused read-only/source-drift regressions pass for this addition.

The local transaction projects metatype, attributes, skills/groups, racial grants,
supported purchased qualities and gear, career baseline, the source-owned default
Street lifestyle and initial career money. Carryover caps come from the active
profile. Legacy nonnegative resource-Karma rounding is ceiling, not midpoint
rounding; its adjustment and discarded/carryover amounts are explicit deltas.
Lifestyle starting money is added after carryover and cannot fund creation gear.
Free Grid subscriptions follow the actual profile/book policy.

The file store holds the workspace lease, admits sources again immediately before
atomic replacement, and writes character XML, checkpoint, receipt and complete
archive together. Recovery observes the durable receipt, never automatically
reissues a mutation. The generic auxiliary writer cannot authorize this path.
Karma archives additionally retain the pre-finalization XML and exact projection
inputs, permitting historical plan/receipt reconstruction without treating those
sources as authority for new operations. Archive-less Karma receipts are invalid;
legacy Priority serialization remains unchanged.

This is **not complete Karma creation or phone delivery**. Awakened finalization
still fails closed pending its typed magic/resonance contribution. Purchased
lifestyles, Contacts and remaining creation domains are not silently synthesized.
The local Android completion page now uses this path; Core-backed native page and
owner/cancellation/lost-return checks pass. A real device save/reopen/restart check
must be recorded separately; the earlier debug APK proves only pending drafts.
No release package, signed AAB or Play upload is implied by the managed checks.

Focused local tests cover actual Human/Elf composition, custom/zero caps, disabled
or drifting sources, fractional funding, explicit confirmation, concurrent duplicate
confirmation, foreign/expired owner contexts, pre/post-rename failures, archive
forgery and cold replay. Existing Priority archive/idempotency checks remain active.

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
