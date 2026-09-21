# SR6 Creation methods — implementation increment

The user requested SR6 build types in addition to ongoing SR5 Life Modules and
Origin book work. SR6 must not reuse SR5 creation profiles or module effects.

## Implemented

- Separate SR6 wire identities: `Priority`, `SumtoTen`, `PointBuy`, `LifePath`.
  The latter three alternatives are described in Catalyst's Sixth World
  Companion; this identity catalog is not a rule-budget or readiness claim.
- Shape validation admits an explicitly SR6, uncreated draft with a known method
  before metatype selection. It does not choose Human, substitute SR5 Karma/Life
  Modules, authorize a bootstrap, or permit Career finalization.
- Renaming that pending SR6 draft no longer inserts an invalid empty metatype.
  Explicit empty values remain invalid; the codec does not silently repair them.
- Focused tests cover all four identities through rename, file-store reopen and
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

UI feature commit `2905f4450` now shows SR6's own four method identities, but
`DialogCoordinator.StartAuthoritativeNewCharacterAsync` and Core's
`CharacterCreationBootstrapService` explicitly require SR5. The associated
profile and source-binding contracts are SR5-only. These guards are unchanged.

Next: an SR6-owned creation bootstrap and source context, correct edition-bound
method options, then usable typed editors and one save/reopen route per method.
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
