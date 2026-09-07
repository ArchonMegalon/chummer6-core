# Source-bound Talent quality definitions

The SR5 Priority Magic/Resonance authority resolves `qualities.xml` through the
same effective source context as priorities, metatypes, traditions, streams,
powers, spells and complex forms. Its input digest includes the quality input
digest. The effective rows (including custom-data changes) are not replaced by
hardcoded Magician/Adept/Technomancer effects.

Each Talent carries its ordered `GrantedQualitySources`: the exact reference
from priorities, optional forced selection, normalized source GUID, canonical
quality XML, source book/page, source anchors, effective input digest and node
and XML digests. Resolution by name or GUID must be unique. Missing, duplicate,
malformed or inactive-book definitions disable the affected Talent. Aliases may
not grant the same source/selection twice. XML namespaces, DTDs, unknown reference
attributes and malformed reference containers are rejected.

Confirmation copies those definitions into the digest-bound finalization
contribution. Revalidation derives the expected contribution from the independent
current source authority; self-rehashing a modified definition is insufficient.
The optional/null JSON property preserves historical payload serialization but
does not authorize completing an unresolved historical draft. Such drafts need
explicit re-review against the current source environment; receipts are not
silently rewritten. The existing file-store cold-reopen and command-recovery
checks retain the confirmed source definitions without changing runner XML.

## Catalog corrections exposed by actual-source tests

- `Toxic` has a valid uppercase GUID in the canonical tradition data. Typed
  identities normalize GUID case without changing the original source row or
  weakening duplicate/source-byte checks.
- Effective catalog XML can retain indentation. Canonical payload normalization
  now agrees with the finalization validator. Original effective file bytes
  remain separately digest-bound.
- `Berserker Temper` is a hidden zero-cost source row. Its source representation
  can be validated while the option stays disabled with its unsupported-semantics
  blocker. It cannot be re-enabled as a free purchasable power.

The existing canonical Magic source test is now included in the isolated
CreationHistory suite. It exposed these whole-catalog blockers that the prior
synthetic service fixtures did not exercise.

## Heritage budget and whole-character projection

The Quality service joins these independently resolved definitions into its
budget authority. Source karma cost and metagenic metadata are retained; Heritage
origin excludes the grant from the purchase/quality/metagenic budgets. A granted
single-instance quality cannot also be purchased. This budget join alone never
authorizes saving a bonus graph.

The finalizer now compiles supported source effects into saved-instance shapes:
Heritage quality IDs, attribute/tab enablement, selected skill-group unlocks,
spell restrictions, tradition spirit/drain data, spells, and undiscounted
prompt-free/effect-free adept powers. Paid skill/group bases exclude their free
Heritage grants; matching `SkillBase`/`SkillGroupBase` improvements retain the free
ratings. Source XML is not appended as a saved quality or power instance.

Actual-source tests exercise Priority Magician, Aspected Magician with a selected
free group, Mystic Adept's spell-only path, and Adept. They review and explicitly
confirm the composite write, reconstruct the file store, and verify the saved
graph and idempotent replay. Invalid/uncompiled effects reject the complete
projection, not just the offending effect. The independent source revalidation
and atomic workspace write remain mandatory.

## Adept power source limits

The source `limit` restricts instances; it is not a level cap. `maxlevel` or
`maxlevels` bounds levels when present; otherwise a levelled power is capped by
the current confirmed MAG through the Core helper. Non-levelled powers remain
single-level. Presentation must use this effective cap and the confirmed Core
budget while preserving the original Talent's starting MAG and source budget.
Way eligibility metadata is retained, but no Way discount is applied. Powers
requiring other bonus/choice or variable-cost semantics remain disabled.

## What this does not complete

Technomancer's Living Persona gear and conditional Computer bonus are still
uncompiled and therefore block finalization. Mystic Adept purchased/split power
points remain unfinished. An Aspected path without a selected free group needs
an explicit user aspect choice; the finalizer never silently chooses Sorcery.
Additional source bonus forms, prerequisites, discounts and enhancements need
their own typed projections and tests. These are unfinished capabilities, not
evidence for claiming all magical creation paths complete.

The oracle is Chummer5a `SelectMetatypePriority.cs` (Heritage Quality creation and
`AddFreeSkills`) and `AddImprovementCollection.cs` (source bonus handlers).
Managed source tests are not an Android Activity/process-death journey, package
seal, phone performance measurement, or authorization to sign or upload to Play.
