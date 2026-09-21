# SR6 Creation methods — implementation increment

The user requested SR6 build types in addition to ongoing SR5 Life Modules and
Origin book work. SR6 must not reuse SR5 creation profiles or module effects.

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

## Not enabled in the app yet

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
