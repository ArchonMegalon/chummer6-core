# Source-bound awakened Creation attributes

Local implementation guidance; not package, Android device or Play authority.

## Corrected behavior

The Attributes step formerly admitted only Human/Mundane prerequisite drafts.
That prevented the existing Magician → Attributes → Magic/Resonance confirmation
test from reaching its intended workflow. The step now admits the supported
Human talent projections for Magician, Adept, Mystic Adept, Aspected Magician and
Technomancer, without inventing starting values in the client.

The independent prerequisite/source validation remains mandatory. For these
talents, exactly the matching awakening quality and one positive source grant
are required. Unknown talents, mixed grants, Depth, additional unmodeled quality
effects, unsupported metatypes/variants and unsupported attribute house rules
remain blocked. This does not make those remaining product requirements complete.

- Magic or Resonance starts at the source talent grant, not racial minimum plus
  grant and not a purchased allocation.
- The special maximum is `max(metatype maximum, grant)`; its augmented cap is the
  same maximum. Priority rows with `maxmagic`/`maxresonance` overrides are currently
  rejected by the prerequisite source projector, not silently treated as default.
- A special priority point costs one point from the same pool used by Edge.
- Karma levels follow priority levels and cost the sum of their new ratings
  multiplied by the active profile's `KarmaAttribute` value.
- Normal attribute points and the normal maximum-count rule remain separate.
- Essence is display-only; inactive Magic/Resonance and Depth cannot be purchased.
- Each enabled awakened projection includes both metatype and talent source
  anchors. Load/preview do not change XML; confirmation keeps the existing atomic
  auxiliary draft, source binding, explicit review and revision checks.

The reference is the read-only Chummer5a `SelectMetatypePriority.cs` block that
calls `MAG.AssignLimitsAsync` / `RES.AssignLimitsAsync` after selecting a talent
(around lines 1923–1948 in the inspected oracle checkout), plus the active Core
`priorities.xml` / `metatypes.xml` projections. No rulebook page is claimed.

## Tests and remaining work

Tests cover all five families, source grants, both special pools, Karma ordering,
caps, overflow, disabled attributes, malformed/unknown grants, extra qualities,
unchanged XML and persisted draft re-evaluation. A separate test uses the real
canonical file resolver, Human E / Magician C, explicit confirmation, then a new
store and resolver to verify a saved Magic 4 projection from a Magic 3 grant.
The existing full Magician selection/confirmation/replay test stays enabled.

This is an attribute-stage fix, not completion of awakened finalization.
`CharacterCreationFinalizationProjector` still rejects awakened effects and
talent skill-grant application. `CharacterCreationMagicResonanceService` and its
contribution contract still derive Adept power-point budget from the source
talent, rather than an increased confirmed Magic projection. That budget binding
and complete source-backed effect projection must be implemented and tested
before claiming that an ordinary awakened runner can finish Creation correctly.

Creation contacts/lifestyles, non-Human attribute authority, the other build
methods, package reseals, downstream source inventories/pins, real Android
lifecycle qualification and release approval remain separate open work.
