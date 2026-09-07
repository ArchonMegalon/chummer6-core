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

## What this does not complete

These records are source evidence, not saved quality instances or executable
improvements. The whole-character finalizer still rejects awakened and free
Talent-grant effects until their real output graph is implemented. Do not append
source XML directly to a runner or remove the guards merely because these source
definitions are present.

Remaining projection work includes Heritage quality instances, attribute/tab
enablement, Aspected skill-unlock choice, exclusions, prerequisites, selected
tradition/stream/powers/spells/forms, and paid skill/group bases separated from
free Heritage improvements. The actual Technomancer definition additionally
grants Living Persona gear and a conditional Computer bonus; these cannot be
silently discarded. Mystic Adept purchased/split power points remain separate
unfinished work.

The oracle is Chummer5a `SelectMetatypePriority.cs` (Heritage Quality creation and
`AddFreeSkills`) and `AddImprovementCollection.cs` (source bonus handlers).
Managed source tests are not an Android Activity/process-death journey, package
seal, phone performance measurement, or authorization to sign or upload to Play.
