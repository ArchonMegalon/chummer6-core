# SR6 Creation methods — implementation increment

The user requested SR6 build types in addition to ongoing SR5 Life Modules and
Origin book work. SR6 must not reuse SR5 creation profiles or module effects.

## Current increment: source-backed equipment profiles — 22 September 2026

The read-only draft summary and character projection now retain Core-owned
equipment statistics for the existing nine armor, six commlink, six cyberdeck
and ten RCC rows (the last two RCCs remain unavailable at creation). Values and
conditional/included traits were checked against the owned German 2024 Core
pp265–269; its SHA-256 and page anchors travel with each profile. No PDF or
source prose is committed. This platform-neutral contract also serves future
Windows consumers; it is not a Windows UI or build qualification.

Armor Defense Rating is not SR5 soak. Capacity and bonuses are per item, never
multiplied by quantity. Nothing is worn, activated, wirelessly enabled or added
to the unarmored baseline automatically. Commlink D/F and cyberdeck A/S pairs
remain distinct; absent attributes are not synthesized as zero. RCC sharing,
slave limits and noise reduction are preserved separately from program slots.
Included components/conditional traits do not grant extra independently owned
items or active effects. No optional full-body helmet is invented.

Historical purchase options, previews and decisions keep their original shape
and bytes. Profiles are computed from a freshly revalidated saved ledger and
included in the final reviewed document/digest. A supported mundane basket can
now complete atomically. Any unimplemented item (including weapons/medical
gear/credsticks in this increment) still blocks completion explicitly.

`core-sr6-equipment-profiles-1.log`: **296 focused SR6 foundation tests PASS**,
including table values, zero-versus-absent attributes, quantity and unequipped
behavior, unchanged purchase receipts, all three methods completing with gear,
cold receipt reopen and rejection of unavailable/unimplemented gear. No new
package seal or distribution is implied.

## Historical increment: atomic SR6 completion — 22 September 2026

Priority, Sum-to-Ten and Point Buy now have an edition-owned finalization review
and explicit atomic commit. The review revalidates every saved decision and
materializes the exact character; missing mandatory selections, unsupported
equipment/formula statistics, magical tradition and conflicting effects block
completion. This is not blanket SR6 creation or Career support.

Unused allocation points and cash/Karma above carry-over limits require a
separate explicit loss acknowledgement. Core applies the existing SR6 carry-over
calculation, marks the supported character created and sets the no-implant
Essence baseline to six (owned German core p40; final steps pp69–70). It retains
the original XML, decision ledger, exact review and completion receipt in an
immutable archive. One owner/workspace lease and one atomic file replacement
advance both content and saved revision. Recovery reads the receipt and never
repeats an uncertain write. Imported history cannot replay an old operation.

Generic writes cannot inject/remove the archive or reset a finalized runner to
Creation. Later permitted metadata changes retain the archive. The archive's
current closed shape intentionally does not admit unrelated SR5 receipt lanes;
future SR6 Career transactions need their own explicit contract extension.

`core-sr6-finalization-3.log`: **310 focused tests PASS** (Creation/codec/related
auxiliary-state coverage). `core-sr6-finalization-store-regression-1.log`:
**152 store/checkpoint/finalizer regression tests PASS**, zero failures/skips.
New cases cover three methods, loss consent, cold reopen, exact receipt replay,
pre/post-replace faults, competing commits, owner changes and archive forgery.
No package seal or Play delivery is implied.

## Historical increment: character-content projection — 22 September 2026

The SR6 service now materializes the fully revalidated saved choices into a
deterministic character document through an owner- and revision-bound read-only
entry point. Priority, Sum-to-Ten and Point Buy retain their own saved budgets.
Natural attributes, pool/Karma skills, specialty permissions, combined languages,
qualities, contacts, equipment purchases, lifestyle, powers and formula identities
are represented without consulting SR5 sources. Saved IDs remain stable; generated
IDs are workspace/domain-bound and deterministic across cold loads and locales.

Natural and permanent effects remain separate. Known monitor sizes come from
the existing Core calculation, not a new projector formula. Overflow capacity
is not written as current damage. Equipment remains unequipped and explicitly
lacks runtime stats; formula identities do not invent drain, range or effects.
Conflicting passive values stay unresolved, not zero. Karma/Nuyen carry-over
caps are shown separately without discarding the full remaining balances.

The returned document is **an uncreated materialized draft**, not a write plan,
exported completed runner or Career-entry receipt. The bootstrap attestation is
removed from this copy because it binds the original pending XML. Store bytes,
history and revisions remain unchanged. Missing selections, runtime-stat gaps,
magical tradition and the finalization transaction remain explicit. An atomic
terminal transaction must still archive the exact draft history, review lost
balances and commit the reviewed character before native SR6 completion exists.

`core-sr6-character-projection-3.log`: **292 focused Core Creation/codec tests
PASS**, zero failed/skipped. New tests cover all three methods, shared section
reading, decimal costs, IDs, natural/passive separation, cold/culture-independent
reads, owner/stale-binding rejection and rehashed historical price forgery.
The first focused run caught an incorrect test expectation for Mystic Armor
(the catalog uses 0.25 PP, not 0.5); no rule cost was changed to make it pass.
No package seal, Android release artifact or Play change is implied.

## Historical increment: passive and derived values — 22 September 2026

The revalidated saved summary now also projects permanent adept attribute and
skill effects, keeping natural values intact. Noncombat-only Improved Ability
remains separate from the rating usable in attacks. Activated boosts, spells,
gear and situational Edge are not assumed active. Purchased armor is not worn.

Core derives physical/stun monitors, overflow capacity, physical initiative
base/dice, unarmored Defense Rating, defense dice pool and unarmed Attack Rating.
Each result includes its equation and page anchor. Innate ork/troll Built Tough
and purchased upgrades use the total once; trolls receive dermal protection.
Glass Jaw and Will to Live affect the correct monitors. Improved Reflexes and
permanent Improved Reaction cannot be silently stacked: dependent values are
null with an explicit conflict until resolved. Combat Paralysis warns about the
rolled total and first-round restrictions; it does not halve the initiative base.

The owned German 2024 core pp40–41/66/74/76/80/108/124/159–160 was inspected for
these formulas. No source prose or PDF is committed. This is a read-only saved
draft projection, not finalized-character persistence or a full runtime effects
engine. Historical previews, decisions and pending XML stay byte-compatible.

`core-sr6-passive-values-1.log`: **285 focused Core Creation/codec tests PASS**,
zero failed/skipped. Added coverage spans three methods, five metatypes,
upgrades, permanent versus activated powers, mixed-use skills, conflicting
effects, removal, cold reopen, replay and unchanged durable bytes.

## Historical increment: combined natural values — 22 September 2026

The saved overview now projects natural attribute/skill ratings from the
revalidated pool and Karma decisions. Attribute bases, normal/adjustment spend,
Karma increases and final natural ratings remain separate. Specialties are
conditional dice bonuses, never rating increases; exotic weapon permissions
have no specialty bonus, including the first subject purchased with Karma.

Knowledge combines pool and Karma topics by their saved identities. Language
upgrades replace the same-ID pool language rather than adding a duplicate;
new languages remain distinct and the native language is unchanged. Missing
domains stay null rather than inventing allocations. Replacing/removing Karma
purchases is reflected immediately on the next validated load.

These are unaugmented saved values, not equipment/power effects, final dice
pools or character finalization. The summary's current binding covers the
projection; no pending XML, ledger, historic preview or decision bytes change.
`core-sr6-natural-values-2.log`: **274 focused Core Creation/codec tests PASS**.
The first compile rejected the new Attribute type name; it was renamed rather
than suppressing the contract analyzer. No package seal or release claim.

## Historical increment: saved draft overview — 22 September 2026

Priority, Sum-to-Ten and Point Buy expose a read-only, current-binding summary
of the revalidated saved ledger. It distinguishes missing choices, prerequisite
dependencies, saved selections and remaining pools. Optional absent qualities
are explicitly "none selected": historical empty-quality decisions normalize
to null, so no review is inferred and no new mandatory choice is invented.

Talent-specific steps are projected by Core. A saved Conjuring-only aspect has
no spell step. Unused free spell/form entitlements differ from optional Point
Buy purchase ceilings. Fractional power points retain their exact precision.
Balances combine resource/Karma cash, equipment and prepaid lifestyle exactly
once and show projected cash/Karma carry-over and excess without discarding it.

The summary belongs to the current saved revision, not the historical preview
binding. It does not write workspace bytes, change old decision digests or
authorize finalization: the SR6 effect/materialization graph and Career entry
remain unfinished. Life Path and optional Karma still need distinct editors.
`core-sr6-draft-summary-4.log`: **269 focused Core Creation/codec tests PASS**.
Earlier failed checks are retained, not counted as passes. No package seal or
release authority is implied by the source change.

## Historical increment: basic lifestyle and cash projection — 22 September 2026

The owned German 2024 core p59 supplies six basic monthly costs: Street 0,
Squatter 500, Low 2,000, Middle 5,000, High 10,000 and Luxury 100,000 Nuyen.
Whole-month prepayment is not restricted to six months. One basic lifestyle
can be explicitly saved/replaced for Priority, Sum-to-Ten and Point Buy.
This is neither a permanent/custom lifestyle nor SIN-linked taxation.

Lifestyle is evaluated after the gear basket. Its preview contains the combined
resource/Karma-cash budget, gear cost, prepaid cost and remaining cash. Per p70,
projected starting cash is the unspent balance capped at 5,000 Nuyen, not an SR5
dice roll. Excess is shown, never discarded by this draft operation. Changes
to gear, resources, Karma or prepaid months re-evaluate the combined budget.
The original gear preview remains explicitly gear-only; consumers with a
lifestyle must use the lifestyle preview's combined balance.

Nullable fields preserve old decisions and gear authority bytes. Confirm and
cold reopen re-evaluate exact source/prices; forged prices remain rejected even
after rehashing. The full mixed-domain history now admits fifteen source anchors.
`core-sr6-lifestyle-1.log`: **261 focused Core Creation/codec tests PASS**.
Finalization, recurring Career payments, custom lifestyles and remaining build
methods are not supplied by this increment. No package seal or Play release.

## Historical increment: equipment basket — 22 September 2026

Priority, Sum-to-Ten and Point Buy now evaluate a separate Nuyen purchase
basket. The owned German 2024 core pp69–70/244/247 supplies availability ≤7,
the 5,000¥ cash carry-over ceiling and SR6 size pricing: trolls +10% on all
equipment, dwarfs +10% on fitted armor but not weapons or electronics. Prices
use decimal Nuyen; no rounding to whole Nuyen and no SR5 doubling. Resource
cash includes admitted customization-Karma conversion without spending CP or
charging Karma again. Reducing resources below saved purchases blocks.

A deliberately partial catalogue has 148 item/rating options from pp249–259,
266, 268–269, 273 and 283: basic weapons, armor, commlinks, decks, RCCs, empty
credsticks and medical gear. Two RCCs above availability seven remain visible
but unavailable. Licensed/illegal flags require GM review, not an invented
creation ban or an automatic license grant. Ammunition, accessories, bodytech,
SIN/license assignments, vehicles and lifestyles remain outside this basket.
No rulebook prose or PDF is distributed, and saved purchases do not yet apply
runtime equipment effects or finalize the runner.

Stable row IDs and positive bounded integer quantities support explicit
add/edit/remove. Costs, sources, budget/carry-over and legality warnings are
Core-owned. Every confirmation and cold load re-evaluates the exact catalogue,
metatype pricing and cash. Nullable absent fields preserve old decision bytes.
The complete mixed-domain draft now uses fourteen source anchors.

`core-sr6-gear-1.log`: **254 focused Core Creation/codec tests PASS**. Coverage
includes three methods, five metatypes, decimal prices, independent pools,
availability/budget negatives, hostile quantities/IDs, exact cold replay,
fourteen-anchor history and rehashed price forgery rejection. This is draft
equipment, not complete SR6 creation, a package seal or a Play release.

## Historical increment: contacts — 22 September 2026

Priority, Sum-to-Ten and Point Buy have a separate free contact pool. The owned
German 2024 core pp52–53/70 supplies Charisma × 6, Connection + Loyalty cost,
and the creation cap of final natural Charisma on each rating. General ratings
run 1–12, not SR5's Loyalty cap of six. Final Charisma includes admitted pool
and customization-Karma increases. Contacts do not spend Karma or CP.

Names/optional roles are bounded player text, with stable entry IDs; identical
names do not imply identical NPCs. The preview records GM-review need, not GM
approval. Changes to attributes or Karma recheck saved contacts and block
rating/budget excess rather than deleting entries. Old nullable fields remain
omitted, preserving earlier decision bytes. All thirteen combined-domain
source anchors fit the shape bound and retain exact replay validation.

`core-sr6-contacts-1.log`: **242 focused Core Creation/codec tests PASS**.
Coverage includes all three methods, final-Charisma costs/caps, Loyalty above
six, stable-ID editing/removal/cold reopen, hostile text/rating/ID shapes,
unchanged other pools, thirteen-anchor history and rehashed cost forgery.
These remain pending draft contacts; equipment and finalization are still open.
No package seal, in-play social automation or Play publication is claimed.

## Historical increment: rated qualities — 22 September 2026

Five rated families extend the existing catalogue: Focused Concentration
(1–3, 12 Karma/level), Built Tough (1–4, 4/level), Will to Live (1–3, 8/level),
Glass Jaw (4 bonus Karma/level) and Dependents (1–3, 4 bonus/level). These costs
and limits come from the owned German 2024 core pp74/76/80–81. Source names
remain German; no rulebook prose is distributed. The catalogue now has 89
human, 88 ork or 87 troll variants, including the earlier fixed/targeted choices.

Built Tough charges only above the ork's one or troll's two innate levels.
An upgrade occupies one of the six selections, as clarified by the
[official racial-quality FAQ](https://shadowrunsixthworld.com/shadowrun-sixth-world-faq/).
The saved preview binds total/innate/purchased levels, per-level cost and the
additional FAQ anchor. One family cannot be selected twice. Glass Jaw uses
the final Core-evaluated Willpower after pool and Karma purchases; a selection
leaving fewer than two stun boxes is rejected. Ten is the largest selectable
reduction under the currently supported natural creation caps, not a claimed
rulebook maximum. Removing a prerequisite or bonus cannot discard purchases.

Nullable rating metadata is omitted for older nonrated choices, preserving
their historical decision bytes. The exact-source bound is now twelve for
the complete combined-domain draft, including the racial-upgrade FAQ; exact
rule/digest re-evaluation is unchanged. Forged free levels and purchase deltas
are rejected even when the enclosing ledger has been rehashed.

`core-sr6-rated-qualities-1.log`: **234 focused Core Creation/codec tests PASS**.
Coverage includes three methods, all metatypes' paid/free Built Tough levels,
six-choice replacement history, dependent bonus removal, final-Willpower
monitor limits, rehashed forgery and twelve-anchor save/cold reopen. Further
parameterized choices, equipment and finalization remain open. These remain
pending draft selections, not automatic in-play effects or a package/release.

## Historical increment: selected qualities — 22 September 2026

Priority, Sum-to-Ten and Point Buy now evaluate an explicit quality selection
before attribute/skill pools and customization Karma. The owned German 2024
core pp66/68/69/74–81 provides costs, limits and prerequisites. A partial
catalogue supplies 39 fixed-price advantages/disadvantages, eight physical/
mental Exceptional Attribute targets and 19 Aptitude targets. Costs and source
anchors are Core-owned; no rulebook prose is shipped. This lane admits one
Aptitude family and one Exceptional purchase per attribute. Rated qualities,
other parameterized families, Indebted conversion and Bilingual remain absent.

At most six selected qualities and a net bonus of at most 20 Karma are admitted.
The separate 50-Karma base budget is adjusted by quality costs/bonuses, never
by spending CP. Exceptional Attribute/Aptitude raise caps, not current values.
The resulting pool/Karma limits are re-evaluated; removing a prerequisite or
bonus cannot drop existing purchases silently. Metatype/talent restrictions
and Blandness/Distinctive Style conflict are checked. Innate metatype traits
stay free and outside this selection; dwarf Toxin Resistance cannot be rebought.

Null/empty selections preserve historical no-quality decision bytes. Every
preview, confirmation and cold load re-evaluates costs and caps, including
rehashed forged projections. The bounded source list now accommodates all
eleven supported domain anchors without relaxing exact authority validation.
These are pending draft choices, not automated situational effects or a
finalized runner.

Local keyless Docker/.NET10.0.103: **216 focused Core tests PASS** in
`core-sr6-qualities-2.log` (SR6 Creation plus workspace codec); **three mixed-domain
save/reopen cases PASS** in `core-sr6-qualities-combined.log`, including the new
eleven-anchor case. Android integration is recorded in its foundation handoff.
Equipment, further qualities, finalization/Career entry, and complete Life Path/
optional Karma method flows remain open. No package seal or Play claim follows.

## Historical increment: Karma knowledge and languages — 22 September 2026

The same customization step now buys additional unrated knowledge (3 Karma)
and language levels (3 Karma per additional level), using the owned German
2024 core pp70/72/100. Free Logic-pool choices and native language remain
unchanged. A purchased target level for an existing language binds its exact
entry ID/name and charges only the difference; new languages cost all levels.
Only Basic/Specialist/Expert are supported, with comprehension bonuses 0/2/3.
Knowledge topics remain bounded player text subject to GM review.

Core rejects native/duplicate purchases, ID substitution, non-increases and
malformed names. Changes to the free pool re-evaluate the entire selection;
Karma and free-pool overspending block rather than dropping choices. Purchases
share the existing 50-Karma budget, while CP/base pools stay separate. New
nullable fields preserve historical decisions when no knowledge is bought.
Knowledge anchors reuse the existing authority and are deduplicated, keeping
the complete mixed-domain draft within its ten-anchor bound.

`core-sr6-karma-knowledge-1.log`: **194 Core tests PASS**. Coverage includes
three methods, costs, stable IDs, free-pool separation, budget/identity/name
negatives, canonical null/empty compatibility, cold reopen/replay and rehashed
cost/comprehension forgery rejection. Qualities, equipment and finalization,
plus full Life Path/optional Karma method flows, remain unfinished.
`core-sr6-karma-knowledge-combined.log`: both mixed-domain mystic-adept cases
also pass after adding Karma knowledge to the ten-source-anchor regression.

## Historical increment: Karma specializations — 22 September 2026

Priority, Sum-to-Ten and Point Buy now permit the allowed five-Karma specialty
purchase in the same atomic customization step. The owned German 2024 core
pp66/72/94/97 was inspected directly. Final skill rating must be positive;
the rank and specialty can be bought together. Normal skills may have only
one specialty across pool and Karma purchases. Exotic Weapons permits multiple
different weapon types, without the ordinary +2 dice bonus. Its initial type
is still free when the skill is acquired, never charged again. Names remain
bounded player text requiring GM review, not an inferred equipment catalog.

Source correction: expertise is prohibited during creation (p66), and the
advancement table prohibits Karma-bought spells, rituals and complex forms
during creation (p72). Earlier lists calling those missing creation features
were inaccurate. They remain potential Career work, not unfinished Creation
purchases. Karma knowledge/languages, qualities, equipment and finalization
remain open. Life Path and optional Karma still need full method flows.

Nullable specialty selection/preview fields preserve historical no-specialty
decision bytes and authority. New purchases bind their own rule options,
source anchor, exact cost and bonus into the existing preview. Cold load
rejects even redigested forged expertise-level bonuses. Partial drafts remain
partial: no runtime bonuses, finalized runner or release claim follows.

Local keyless Docker/.NET10.0.103: **185 Core tests PASS**, zero failed/skipped,
`core-sr6-specializations-1.log`, filter `FullyQualifiedName~Sr6Creation`.
Coverage adds all three methods, separate pool accounting, atomic rank-plus-
specialty acquisition, combined creation limits, case-insensitive duplicates,
shared budget, malformed/bounded inputs, detached canonical arrays, null-field
compatibility, save/cold reopen/idempotency and forged-bonus rejection.

The actual Android mystic-adept smoke exposed an old eight-anchor ledger
shape limit: the new specialty needs nine anchors, or ten with knowledge.
The same request was reproduced as a failing Core regression before the fix.
The bounded limit now accommodates the ten supported domain anchors; exact
SR6 re-evaluation, preview/digest and historical-chain checks are unchanged.
`core-sr6-specializations-anchor-fix-green.log`: **187 Core tests PASS**,
including existing-draft specialty save/cold reopen and oversized rehashed
anchor rejection. Failed emulator save left the old workspace unchanged.

## Historical increment: customization Karma — 22 September 2026

Priority, Sum-to-Ten and Point Buy now share a separate customization-Karma
step after saved attribute/skill pools. The owned German 2024 core pp69/71–72/
158 and Companion pp30–31 were inspected directly. Core calculates each
attribute/active-skill increase separately at five times the new rating,
including acquisition from skill rank zero. The first exotic-weapon subject
is explicit GM-reviewed text, not a hidden default. Creation caps, talent and
aspect access remain enforced after all increases, including unchanged ranks.

The base 50 Karma is not CP and does not enlarge the priority-point pools.
Cash conversion adds 2,000 Nuyen per Karma. Drafts retain all unspent Karma;
the five-Karma carry-over limit and excess are exposed, not silently discarded.
Qualities and their modified budgets/conversion rates remain unsupported.
Knowledge/language purchases, additional specialties/expertise and Karma
formula purchases are not included in this increment.

Base allocation previews remain intact. Separately evaluated Karma ratings
drive Logic's free knowledge budget, talent ratings and natural-rating power
ceilings. Adept/mystic Magic increases grant free power points without charging
their CP price again. CP purchases precede customization (Companion p31), so
Karma does not retroactively increase original free formula grants or the CP
purchase caps. Reduced inputs are fully recalculated; incompatible dependent
choices block the new draft instead of being dropped.

The nullable selection/preview extension preserves prior canonical decisions.
Local keyless Docker / .NET10.0.103: **179 Core tests PASS** in
`core-sr6-karma-1.log`, filter `FullyQualifiedName~Sr6Creation`. New coverage
includes cumulative costs, all three methods, separate CP/pool accounting,
creation caps and access restrictions, additional power points, dependent
knowledge/power limits, malformed/overspent selections, detached canonical
arrays, cold reopen/replay and rehashed forged-cost rejection. No package seal,
main merge, active character effects or completed SR6 method is implied.

## Historical increment: adept-power selections — 22 September 2026

Core now offers all 22 core-book power families as 51 explicit purchase options,
including physical-attribute, sense and improved-skill variants. The owned German
2024 core pp95/158–160 was inspected directly. Costs use integer quarter-points;
level limits use Magic and any lower power-specific or natural-rating ceiling.
Unspent power points are permitted. Point Buy pays for its whole power-point
budget once; allocating powers does not charge CP again. Mystic budgets remain
separate from spell slots, and reducing a budget rejects an incompatible saved
selection rather than silently dropping powers.

The [official FAQ](https://shadowrunsixthworld.com/shadowrun-sixth-world-faq/)
was checked on 22 September 2026 for Improved Ability combat pricing. Mixed-use
skills expose separate full-use and noncombat-only purchases, never both for
one skill. The frozen catalog, scope and costs are bound into Core's power
authority digest with the FAQ source anchor. The German profile's prohibition
on Magic-linked improved skills remains in force; Tasking is not available to
adepts. Astral uses Intuition and requires the paid Astral Perception power.

Astral Perception unlocks the Astral skill within the same atomic draft. Removing
the power while retaining Astral ranks is rejected. Improved abilities use
natural saved skill/attribute ratings, not their own enhanced projection.
Improved Reflexes carries an explicit non-stacking warning. These are known
power selections only: no runtime buffs, qi foci, quality discounts, gear
interaction or finalization is claimed.

Local keyless Docker: **167 Core tests PASS**, `core-sr6-powers-1.log`, filter
`FullyQualifiedName~Sr6Creation`. Tests cover every option, both adept types and
all three methods, fractional costs, natural-rating caps, combat scopes, Astral
admission/removal, budget reductions, invalid input, detached canonical choices,
cold reopen/idempotency, unchanged XML and rehashed forged-cost rejection.
No package reseal, main merge or Play publication is implied.

## Historical increment: spell and ritual selections — 22 September 2026

Core functional commit `4ac8a21b1` adds 73 spells and eight rituals from the
owned German 2024 core, pp134–148, to the existing atomic SR6 draft service.
The creation rules on pp67–68, alchemy on p152 and Companion p30 were inspected
directly. Core supplies stable formula identities, kinds, categories and page
anchors. Attribute/element/trigger decisions made when casting do not become
invented extra purchases. In SR6, alchemy uses known spells; no SR5-style
duplicate alchemical catalog or purchase is introduced.

Priority/Sum-to-Ten share original-Magic spell/ritual grants. Point Buy charges
2 CP per formula, shares the final-Magic limit, and subtracts mystic-adept power
points before determining the limit. Full/mystic magicians have Sorcery and
Enchanting access; aspected Sorcery accepts spells/rituals, aspected Enchanting
accepts only spells, and Conjuring grants neither. The aspect does not create
a second free pool. Empty/absent choices remain distinguishable, historical
null-field digests remain stable, and costs join the existing total CP review.

Local keyless Docker / .NET10.0.103: **156 Core tests PASS**,
`core-sr6-spells-1.log`, filter `FullyQualifiedName~Sr6Creation`. New coverage
checks every catalog entry, three methods, aspects, mystic split, CP exhaustion,
malformed/duplicate/unknown IDs, canonical detached arrays, save/cold reopen,
idempotent replay and rehashed forged-cost rejection. No warnings/errors were
reported by this focused build/test run.

These are known-formula draft selections only. They do not cast a spell,
prepare an alchemical object, satisfy runtime ritual requirements or finalize
a character. Adept-power selection, qualities/Karma, equipment, finalization
and complete Life Path/optional Karma flows remain open. No package reseal,
main merge or Play publication is implied.

## Historical increment: concrete complex-form selections — 22 September 2026

Core functional commit `18b4e317f` adds Technomancer complex-form choices to the
existing atomic draft service. The 15 core form families expand into 71 selectable
identities: ordinary forms, four matrix-attribute variants for each increase/
decrease form, 20 fixed programs, 25 roll-bearing Overclock action variants,
and six autosoft kinds. Source names follow the owned German 2024 core;
pp180–185, 189–191 and 201 were inspected directly. No SR5 catalog or book prose
is copied. Drone/weapon subjects for the applicable autosofts remain bounded,
normalized player text requiring GM review, not catalog-verified equipment.

Priority/Sum-to-Ten consume the original-Resonance free-form slots; Point Buy
charges 2 CP per form under its final-Resonance ceiling. The total CP review
includes these purchases. Lowered budgets, duplicate variants, unknown IDs,
missing/extraneous subjects and malformed collections are rejected without
dropping saved selections. Arrays are frozen and canonically ordered. Existing
null-field hashes remain unchanged. Cold load recomputes both source identity
and cost, rejecting a redigested forged projection; repeated confirmation
replays the original receipt instead of adding another decision.

Local keyless Docker / .NET 10.0.103: **145 tests PASS**, zero failed/skipped,
`core-sr6-forms-1.log`, filter `FullyQualifiedName~Sr6Creation`. This is a
different selected subset from the older 155-test run below. New coverage
includes all three methods, every offered catalog variant, canonical ordering,
invalid inputs/duplicates, CP exhaustion/overspend, reduced-Resonance rejection,
deep copies, atomic save/cold reopen/replay and forged-cost rejection.

These remain draft choices, not activated Matrix effects or a finalized runner.
Spell/ritual selection, adept-power spending, customization Karma, remaining
creation domains and Life Path/optional Karma still need implementation. No
package seal, main merge, signed AAB or Play claim is implied.

## Historical increment: talent budgets and whole power-point purchases

Priority/Sum-to-Ten and Point Buy now use a separate Core-owned talent budget
after attribute allocation. The owned German 2024 core pp67–68/158–160 and
Companion p30 were checked directly. Priority free spells/forms use the original
talent-priority Magic/Resonance, not adjustment increases. Adept power points
use final Magic. A mystic adept splits original priority Magic between whole
power points and twice the remaining amount in free spells/rituals. Aspected
Sorcery, Enchanting and Conjuring have distinct entitlements.

Point Buy has no free spells/forms/power points. This increment purchases whole
power points at 4 CP for adepts or 8 CP for mystic adepts, capped by final Magic.
Spell/form purchase ceilings use final Magic/Resonance; mystic adept power
points reduce the spell ceiling first. These are ceilings only: no individual
spell, ritual, alchemical spell, complex form or adept power is learned here.
The SR6 catalogs and ability selection, Karma/quality adjustments and final
character effects remain open. No SR5 catalog is substituted.

The total Point Buy review includes power-point costs and rejects overspend.
Its existing authority digest still identifies the pool calculation; the new
talent authority and enclosing preview digest bind the additional purchase.
Attribute changes preserve talent choices and block an invalid reduced-Magic
budget instead of silently deleting purchases. Atomic confirmation/replay and
cold projection recomputation use the existing foundation service. Omitted
nullable talent fields preserve older decision hashes.

Local keyless Docker / .NET 10.0.103: **155 tests PASS**, zero failed/skipped,
`core-sr6-talents-2.log`. Coverage includes both priority methods, adjusted versus
original ratings, three aspects, Point Buy purchases/caps, cold save/replay,
invalid inputs, exact CP exhaustion and rehashed forged entitlements. Run 1
passed 151 cases before the additional Sum-to-Ten rows. No package seal, main
merge, finalization or Play publication is implied.

## Historical increment: Point Buy pool purchase and allocation

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
