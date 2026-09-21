# SR6 Creation methods — implementation increment

The user requested SR6 build types in addition to ongoing SR5 Life Modules and
Origin book work. SR6 must not reuse SR5 creation profiles or module effects.

## Current increment: Point Buy pool purchase and allocation

The Point Buy bootstrap now enters its own pool-purchase calculation through
the same owner/revision-bound atomic foundation service. It accepts no priority
ranks and cannot be mixed into a Priority/Sum-to-Ten decision. The existing
budget projection uses a null magic/resonance rank for this non-priority method;
historical rank-bearing decisions and their digests are unchanged.

The owned German Companion, printed pp29–31, was checked directly. Core owns
the 100 CP budget, free 4 attribute/12 skill/1 adjustment pools, maximum extra
20/20/12 points at 2/2/4 CP, and up to thirty 15,000-nuyen units at 1 CP each.
The five standard metatypes cost no CP. Mundane costs zero; each supported
awakened/technomancer choice costs 10 CP. Base Magic/Resonance is 1 where active,
except aspected Magic 2. The separate 50 customization Karma is displayed but
not spent by this pool editor. No free spells, forms or power points are granted.

Point Buy adjustment points follow its own rule: Edge, active Magic/Resonance,
and metatype attributes with maxima above six. Unlike the German Priority
rule, a reduced metatype maximum does not permit adjustment spending. The
existing attribute, skill and knowledge allocators consume the purchased pools;
Core recomputes all costs and source anchors on preview and cold reopen.

Local keyless Docker / .NET 10.0.103: **138 tests PASS**, zero failed/skipped,
`core-sr6-point-buy-2.log`. New coverage includes all five metatypes, six talent
costs/base ratings, the 100-CP technomancer pool example, allocation/save/replay,
caps/negative/overflow inputs, cross-method rejection, reduced-maximum rules
and rehashed forged projections. Earlier Priority/Sum-to-Ten tests remain green.
Run 1 passed before final source-anchor aggregation and method-specific error
copy; run 2 covers those final changes too.

AllCharacterPointsSpent means only that the CP pool is exhausted, not that a
runner can finalize. Partial purchases remain drafts. Spell/power/form purchases,
metavariants, quality/Karma adjustments, equipment, finalization and Career are
still open; Life Path and optional Karma remain bootstrap-only. No main merge,
package reseal or Play publication is implied.

## Historical increment: free knowledge and language choices

Priority/Sum-to-Ten drafts now carry a separate knowledge/language allocation.
Core derives its free pool from the validated Logic allocation. One native
language is additional and free. Knowledge topics are unrated and cost one pick;
language levels Basic/Specialist/Expert cost 1/2/3 picks cumulatively and provide
comprehension bonuses 0/2/3. Additional native languages cannot be purchased with
these picks. These rules were checked in the owned German 2024 core printing,
printed pp70 and 100. No source prose or PDF is committed.

Names are bounded, Unicode-normalized player input with stable entry IDs. Topics
explicitly require GM scope review, not an invented GM approval. Duplicate IDs,
duplicate names, unsupported levels, malformed collections and overspend are
rejected. Core requires an attribute allocation, recomputes Logic and carries
its exact attribute authority. Lowering Logic below already chosen knowledge
costs blocks the changed preview instead of silently dropping entries.

The same owner/revision-bound atomic foundation writer handles confirmation.
Attributes and skills remain intact. Null knowledge fields preserve older
decision digests. Cold load recomputes the projection and rejects a rehashed
forged Logic/result. Karma, Bilingual and other quality adjustments, final
character grants, other build methods and Career remain later work.

Local keyless Docker / .NET 10.0.103: **124 tests PASS**, zero failed/skipped,
`core-sr6-knowledge-1.log`. Includes both method persistence/replay, language
levels, free native language, derived-budget rebinding, malformed input, deep
copies and forged-state rejection, alongside prior foundation/attribute/skill
and bootstrap/priority regressions. Not a package seal, main merge or Play claim.

## Historical increment: SR6 skill points and specializations

Priority/Sum-to-Ten foundation decisions now also carry typed skill choices.
The Core-owned list contains the 19 SR6 skills, with separate availability for
magical talents and technomancers. Aspected magicians select one magical aspect;
adept Astral access remains unavailable until the required power is implemented.
The ordinary creation maximum is 6, at most one skill at that maximum. Ratings
cost one point each, an ordinary specialization one additional point. Exotic
weapons share one rating and receive their first weapon specialization free;
additional weapon specialties cost a point each. No expertise is admitted.

Rules were checked in the owned German 2024 core printing, pp66–67 and 94–99.
Names of specializations are bounded player input and visibly require GM review;
saving them does not invent a GM approval. Knowledge/language picks, Aptitude,
Karma expenditure and final character grants still need their later stages.

These choices reuse the existing owner/revision-bound atomic decision, with
exact preview, explicit confirmation, deep-frozen nested collections and
recomputed rule/source authority on cold load. Null skill fields preserve old
foundation/attribute decision bytes. A skill save carries existing attributes.

Local keyless Docker / .NET 10.0.103: **115 tests PASS**, zero failed/skipped,
`core-sr6-skills-2.log`. This includes the existing attribute/bootstrap/priority
tests and new persistence, specialization, talent/aspect, budget, malformed
input, edited confirmation and redigested-forgery coverage. It is not a package
seal, main merge, full SR6 creation claim or Play publication.

## Historical increment: attributed SR6 draft allocation

The existing atomic foundation decision now accepts an optional typed attribute
allocation. It keeps ordinary attribute points separate from metatype adjustment
points, derives maxima from the existing SR6 metatype provider, allows adjustment
on both increased and reduced metatype ranges, and enforces the single physical/
mental attribute at its maximum. Edge and active Magic/Resonance use adjustment
points only. Mundane characters cannot purchase an awakened attribute. Raising
Magic/Resonance does not rewrite the priority base used by later talent grants.

The owned core printing's pp65–67 were inspected directly for these rules. No
rulebook text or PDF was added to Git. The preview includes per-attribute base,
both expenditures, resulting rating, maximum, remaining pools and source binding.
Partial allocations may be saved as drafts; they do not imply completion.
Qualities, customization Karma, augmentation and final character effects are
not part of this allocation yet.

Allocation is in the same owner/revision-bound atomic decision, not a separate
uncoordinated writer. The new nullable fields are omitted when absent, preserving
earlier foundation serialization/digests. Both old and new decisions re-evaluate
current rules on load. Edited allocations require another exact preview and
explicit confirmation; stale foundations and rehashed forged results fail closed.

Local keyless Docker / .NET 10.0.103: **98 tests PASS**, zero failed/skipped,
`core-sr6-attributes-1.log`. Includes new allocation, negative, cold-reopen,
idempotency and old-digest tests plus existing SR6 foundation/bootstrap/priority
regressions. This branch integrates Life Modules `83ea93b5b` and SR6 `e8efffedd`;
it is not a new package seal or main release authority.

The preceding native increment, Android `d12ab8a7`, already completed Priority
and Sum-to-Ten foundation save/restart routes on API36. It did not yet have this
attribute allocation. See Android's `SR6_FOUNDATION_PHONE_20260921.md` for its
bounded debug evidence. Full SR6 creation/finalization and the other methods
remain open. No Play publication is implied by either increment.

## Historical increment: persisted Priority/Sum-to-Ten foundation

The SR6-owned service now loads, previews and explicitly confirms the five
priorities plus metatype and talent. It derives Companion availability from the
persisted SR6 Sum-to-Ten profile, not a caller flag. Metatype/rank restrictions,
mundane versus awakened talent availability, priority base Magic/Resonance and
attribute/skill/resource/adjustment budgets are evaluated together.

Confirmation appends a workspace-owned pending decision through the existing
file-store lock and atomic checkpoint. It does **not** grant character effects,
spells, power points, allocated attributes, or finalization permission. Reopen
revalidates the bootstrap, current SR6 calculation and decision history. Generic
workspace writers cannot add or remove that history. The full owner stamp is
leased synchronously; stale revisions, changed quotes, foreign/expired owners
and reused operation IDs with different commands fail closed. Recovery observes
the durable result without repeating an uncertain write. Imported history is
not accepted as a successful local replay.

The artifact-intake skill located and privately cached the user's German SR6
core PDF `Shadowrun_6_Downloadversion_2024.pdf` (354 PDF pages), SHA-256
`104dd5cc0f167232c3bc0f6453b389d9114dd7df483345e5b1211fda667bf023`.
Printed pp65–67 were checked for the priority, metatype and talent rules used
here. The foundation quote explicitly identifies this source; it does not reuse
an English printing's page numbers. The PDF and prose remain outside Git.

Local keyless Docker / .NET 10.0.103: affected build and **85 focused tests PASS**,
zero failed/skipped, in `core-sr6-foundation-5.log`. Coverage includes durable
cold reopen, replay/conflict/concurrency, before/after-replace I/O recovery,
forged-budget revalidation, generic-writer rejection, owner isolation, imported
history and selected existing SR5 bootstrap/Karma persistence regressions.
Run 4 passed 73 before the two additional boundaries and SR5 regression selection.
Runs 1/2 found compile errors; run 3 exposed the missing required alias in the
new test fixture. They were corrected and are not counted as passing evidence.

The existing SR6 method-selection Presentation build was checked against these
changed Core inputs: **31 tests PASS**, zero errors, seven existing unrelated
MSTEST0032 warnings, in `sr6-ui-foundation-1.log`.

This remains a typed Core service, not an Android page. Native foundation and
allocation adapters, source-bound grants, completion for the alternative
methods, DE/EN/ES copy and real phone save/restart testing are still outstanding.
No package reseal, main merge, APK/AAB, signing or Play publication is claimed.

## Implemented

- All five methods now have separate SR6 bootstrap profile identities and can
  create a real, atomic pending workspace through the existing owner-bound
  service/store. SR5-only profile APIs retain their exact old meaning.
- The SR6 provider binds its own built-in profile, metatype ranges, Priority
  rows when relevant, and edition-specific source anchors. It never opens SR5
  settings/catalog files, chooses a metatype, or grants spendable creation pools.
  Companion profiles are pending-draft identities, not complete Companion packs.
- Local and linked-owner draft creation, save checkpoint and cold file-store
  reopen are tested for all five methods. Missing/duplicate providers, wrong
  method/profile/edition, expired owners, changed source hashes and generic
  marker import fail closed. `IsCurrent` rebinds current SR6 source inputs; future
  SR6 editors must call it before admitting the pending foundation.
- Shared Presentation now dispatches the exact SR6 request and opens a valid
  receipt. There is no fabricated SR5 activation bundle. The UI explicitly labels
  the result a draft with the remaining SR6 wizard steps unavailable.
- Separate SR6 method catalog: `Priority`, `SumtoTen`, `PointBuy`, `LifePath`,
  and the optional `Karma` system. The latter is not SR5's Karma rules even though
  the wire name is shared. This catalog is not a rule-budget or readiness claim.
- Shape validation admits an explicitly SR6, uncreated draft with a known method
  before metatype selection. It does not choose Human, substitute SR5 Karma/Life
  Modules, authorize a bootstrap, or permit Career finalization.
- Renaming that pending SR6 draft no longer inserts an invalid empty metatype.
  Explicit empty values remain invalid; the codec does not silently repair them.
- Focused tests cover all five identities through rename, file-store reopen and
  `.chum6` download/reimport, plus cross-edition/ambiguous-field rejection and
  the unchanged SR5 creation-method/profile boundary.

Source for method names:
[Catalyst's Sixth World Companion preview](https://d1vzi28wh99zvq.cloudfront.net/pdf_previews/396661-sample.pdf).

## Local verification

Local keyless Docker / .NET 10.0.103: **55 focused tests passed**, zero failed or
skipped, including the file/codec regressions and exact SR5 bootstrap rejection
and method/profile tests. Log: `core-sr6-methods-2.log` in the existing local
`life-module-book-tests-20260921.L0D7pON5` packet. The first run selected the much
larger unrelated Karma suite by class name and was stopped; it is not counted
as a pass. No APK, native SR6 smoke, package reseal or release was produced.

## Historical method-bootstrap status

The previous SR5-only bootstrap restriction has been replaced by explicit SR6
dispatch with its own profiles/provider. Neither repo has been package-resealed
or integrated into a native APK. This is working **pending-draft creation**, not
complete native SR6 character creation.

Next: usable SR6 typed foundation/allocation editors, method-specific grants and
finalization, then one native save/reopen route per completed method.
The current SR6 provider has basic Priority rows, not the full Companion Point
Buy or Life Path mechanics. Do not reuse SR5 profiles or label method identity
recognition as complete SR6 character creation. No Android release is claimed.

## Next increment: SR6 priority allocation

`Sr6CharacterCreationProvider.EvaluatePriorities` now projects all five categories
together using the existing SR6 rows. Priority requires each rank exactly once;
Sum-to-Ten permits repeated ranks, requires a total of ten, and requires an explicit
Companion-enabled input from the future Core source context. Lowercase/unknown
ranks, duplicate/missing/unknown categories, other editions and other methods
return no partial budget. No user-supplied point amounts are accepted.

The result separates SR6 metatype adjustment points from SR5 special-attribute
points. It retains the Magic/Resonance rank without inventing a talent, spell or
power-point grant. This is a pure calculation, not an active-book decision,
workspace bootstrap, persistence command, or finalization permission. In
particular, the optional Companion flag must not become a client authorization
switch when the source context is connected.

The affected local Docker build and 25 focused tests passed in
`core-sr6-priorities-1.log`; the final bounded-input regression is recorded in
`core-sr6-priorities-2.log`. No full Android method has been enabled by this change.
Next concrete dependency remains the SR6 source-bound bootstrap and typed editor,
followed by save/reopen. Point Buy and Life Path still need their own Companion
mechanics rather than either edition's priority projection.

## Owned Companion source located

The artifact-intake lookup located the user's `Shadowrun_Schattenkompendium.pdf`
in the established pCloud Shadowrun library. A private local cache was made
outside Git. ISBN: 978-3-96928-057-7; publisher: Pegasus, 2022; PDF: 225 pages;
SHA-256: `fe4e5b69c6ea721bc59c26ceec084f40d3f29f84671e2ea3ad17482bf9e236fa`.
Printed page numbers below are not interchangeable with the English edition.
No PDF, illustrations, examples or source prose belong in this repository.

Implementation inputs checked against this source:

- p28: Sum-to-Ten uses five categories, ranks A–E costing 4–0 and an exact
  total of ten. This confirms the new allocation calculation.
- p29–31: Point Buy uses 100 **character points**, not Karma. Initial pools are
  4 attribute points, 12 skill points and 1 adjustment point. Additional maxima
  are 20/20/12, priced at 2/2/4 CP respectively. Resources cost 1 CP per
  15,000 nuyen, capped at 450,000. Talent and magic purchases need their own
  rules; Point Buy does not receive free spells or adept power points.
- p32–34: Life Path has three initial stages followed by exactly eight adult
  modules. Only one adult module may be selected twice. It is not SR5's
  Karma-costed sequence. Contacts and knowledge/languages come from its modules,
  not automatic Charisma/Logic pools.
- p156–157: Optional Karma creation is a **fifth** distinct method. Its ordinary
  budget is 1,000 Karma (800/1,200 for explicitly selected alternative levels),
  not the SR5 budget and not Point Buy's 100 CP. Its talent/magic costs also differ.

These observations are not a complete imported Companion rules pack. Native
allocation editors, typed grants, full method validation and finalization
still have to consume edition- and book-bound rules before any method is enabled.

After adding the optional Karma identity, the local affected build and **82
focused Core tests passed** with zero failed/skipped in
`core-sr6-five-methods-1.log`. This includes all five pending-method file/codec
roundtrips, the unchanged SR5 bootstrap boundary, and the allocation tests.

## Bootstrap verification (current increment)

Local keyless Docker / .NET 10.0.103, affected build and **74 focused tests PASS**,
zero failed/skipped, in `core-sr6-bootstrap-3.log`. Includes the new SR6 draft,
owner-isolation, save/cold-reopen and negative tests; existing SR5 bootstrap,
activation fallback and profile tests; SR6 codec and Priority allocation tests.
The first build exposed an internal-only digest helper dependency; the SR6
provider now hashes its own fixed-shape source snapshots. Run 2 passed 34 tests
before the final save-checkpoint/import regression was added.

Presentation's matching local build passed **31 method tests** in
`sr6-ui-bootstrap-1.log`, with seven existing unrelated MSTEST0032 warnings.
Cold store instances are not an Android process-restart smoke. No APK/AAB,
package seal, main merge, signing, Play publication or full-method claim exists.
