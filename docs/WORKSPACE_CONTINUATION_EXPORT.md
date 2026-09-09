# Owner-bound workspace continuation export

This is a Core read boundary, not restore, roaming publication, generic character
import, or Android device qualification. Existing release package authorities do
not include this source change until a later explicit package seal.

`WorkspaceContinuationExportService.Export(expectedOwner, workspaceId)` acquires
the host's actual owner-context lease. A store must explicitly implement
`IWorkspaceContinuationReadCapability`; an ordinary public snapshot or a matching
owner string is not a fallback. Unknown/stale/ABA owner stamps fail closed.

`FileWorkspaceStore` captures the canonical document, all creation/career
auxiliary state, content and saved revisions, checkpoint time, and delegated-GM
ledger in one store operation. It retains the pre-finalization archive and
receipt lists without reducing them to the downloadable character payload.

The private GM ledger has two hash fields and an existing public audit receipt.
The store validator proves that both private fields equal their receipt fields.
Export therefore reuses that existing receipt rather than introducing another
parallel audit contract. These are historical records, not permission to replay
the operations or to grant a new GM capability.

The canonical continuation digest binds the versioned contract, normalized owner,
workspace identity, full document state, both revisions, checkpoint time and all
exported receipts. Object keys are sorted; array order is preserved. It is content
identity, not a signature or proof that uploaded historical claims are genuine.

The continuation read rejects unknown and duplicate JSON fields rather than
silently exporting a subset of a future or ambiguous record. Export cannot
perform legacy record or directory migration. Normal owner-authorized store
operations may perform those existing migrations explicitly before another
export attempt. Concurrency lock files and private file modes remain necessary;
export must not rewrite, relocate or delete runner data or recovery temp files.

## Complete bounded transport

`WorkspaceContinuationCodec.Encode(export, maximumBytes)` preserves the entire
graph, including finalization archives and delegated history. It validates the
digest against bounded captured JSON rather than rereading mutable caller data.
Both individual strings and the complete serialized output are bounded; invalid
UTF-16 is rejected instead of replaced. The limit belongs to the caller, not a
hardcoded Core transport policy.

`TryDecodeCandidate(bytes, maximumBytes, out candidate)` checks the raw byte bound
before parsing, rejects duplicate, unknown, missing and normalization-dependent
fields, and requires an exact canonical roundtrip. Object order, whitespace and
equivalent Unicode escaping may vary. Both input and canonical output must fit
the limit. A caller receiving a stream must cap it before buffering these bytes.

Successful decode is **untrusted content**, not restore admission. A positive
future payload schema can be retained losslessly without claiming that the
current engine supports it. The outer continuation contract version and document
format remain strict. Neither hashes nor historical receipts authorize writes.

## Current-source evaluation building blocks

`WorkspaceContinuationSourceCapture` captures the current Creation authorities,
reputation settings and complete Life Modules catalog for exactly one character
XML input. Availability is retained per domain. Both source and returned nested
collections are isolated by copies; queries outside the captured XML, stage,
books or capability set cannot fall back to live sources. Its digest binds the
captured values, not merely source-provided digest labels. Capturing successfully
does not mean every optional authority is available or that a draft is valid.

Foundation's internal continuation check recomputes the selected catalog effects
and complete logical draft. Resources' check recomputes source grants and the
historical budget while allowing a later valid Gear purchase to change the
current budget. Neither helper rereads the workspace or permits a write.
Foundation drafts do not retain an optional original source filter; if that
binding cannot be reproduced, the check remains unresolved/fail-closed rather
than ignoring the mismatch or claiming the historical choice was corrupt.

`WorkspaceAuxiliaryStateIntegrity.IsValidShape` shares the existing persisted
shape and receipt-consistency checks with the file store, including the single
finalization archive. This is not rule validation or historical provenance.
Malformed nested objects can throw; the untrusted candidate boundary rejects
them explicitly rather than treating missing members as empty history.

## Read-only continuation review

The internal `WorkspaceContinuationCandidateEvaluator` decodes bounded bytes
under the actual owner lease, checks complete historical consistency, validates
the native SR5 document, then runs the existing typed services for every present
Creation draft against one private candidate and captured source view. An absent
future wizard choice is not treated as a failed completed-character requirement.
Bootstrap XML markers require validation even if their auxiliary binding is
missing. A Career character cannot carry active Creation drafts or markers.

`WorkspaceContinuationReadView` has no underlying live store and advertises no
write capability. Exact owner and workspace identity are required; legacy reads
can see only the one privately captured candidate. Every mutation is unavailable.
Returned nested state is independently copied. No candidate may borrow data from
another owner or become valid by falling back to an existing local workspace.

Skills and Magic have internal continuation readers which retain independently
recomputed Attributes when the only blocker is missing persistence capability.
Their ordinary public readers are unchanged. Other source, prerequisite, budget
and draft errors remain blockers. Checkpoint/edit-readiness limitations are
reported separately, not globally stripped from failed results.

History checks include intrinsic draft hashes, revision/dependency identities,
the complete pre-finalization archive, and recorded output/checkpoint bindings.
Different atomic lanes cannot both claim one committed workspace revision. GM
receipts at the current revision must match the exact committed profile values;
GM does not imply a saved checkpoint. Later owner edits may change current XML
without invalidating earlier receipts. An older active dependency can remain
historical and unverified while current-draft evaluation refuses continuation.

The evaluator independently captures sources again after domain evaluation;
drift invalidates the observation. This is not a lock spanning a future write.
`CurrentDraftChecksPassed` is only the result of checks on the present drafts,
not full imported-character legality, historical source execution, or permission
to restore. Contacts and Lifestyles still use recorded XML budget totals, so
their checks do not authenticate the provenance of those totals. Unsupported
runtime/schema combinations remain unavailable rather than being normalized.

`RestoreAuthorized` and `HistoricalProvenanceVerified` are always false. Neither
the read-only review nor internally consistent caller-rehashed receipts create
new write or historical replay authority.

Metadata writes now preserve carriage returns using XML entitization. This fixes
a reproduced mismatch between an exact GM command-value hash and the text
previously normalized by the XML writer. Existing records whose original text
was already lost cannot be repaired by guessing it or rewriting their receipts;
an exact latest-value comparison remains unresolved for those records.

## Still required before roaming integration

### Store-local provenance groundwork

File-store record schema 3 requires `WorkspaceLocalHistory`: a local incarnation
ID, an imported-through revision and, for an imported prefix, its snapshot digest.
New ordinary workspaces start with no imported prefix. Every existing replacement,
checkpoint, typed auxiliary commit and delegated edit preserves this metadata.
Deleting and recreating the same workspace ID establishes a new incarnation.
Older binaries reject schema 3 instead of silently dropping the new boundary.

An ordinary read migrates existing schema-2 records without losing auxiliary
state, receipts, revisions or checkpoint time. Continuation export never performs
that migration. Missing/malformed schema-3 provenance and provenance smuggled
into a legacy record fail closed. This is local persistence integrity, not
cryptographic protection against someone who can rewrite the private store.

The portable continuation snapshot deliberately excludes local incarnation and
execution provenance: copying those claims cannot prove local execution on a
different device. The complete character, auxiliary and delegated receipt graphs
remain in the export. A future restore must establish fresh local provenance from
its actual admitted transaction, not deserialize it from the uploaded snapshot.

GM lookup and atomic apply both reserve imported matching keys as conflicts, never
`NotFound` or a successful local replay. The same-revision classification also
guards successful receipt reuse in Skills, Magic, Qualities, Resources, Gear,
Contacts, Lifestyles, Finalization, Life Module acceptance, After Run rewards and
settlements, and Career reputation. It uses the same workspace observation as the
receipt lookup, including recovery after an uncertain commit. Ordinary history
display and complete export remain available; imported receipts are not removed.
Genuine later local receipts above the imported prefix remain replayable.

This groundwork does **not** enable restore. Imported GM authority and timestamp
continuity must not override current grants for future edits; the old whole-ledger
continuity check still needs explicit imported/local segmentation. Atomic restore
admission, source fencing and destination CAS remain unimplemented. The local
incarnation is available for future restore CAS but is not yet a field in every
existing typed edit command. No caller-supplied marker creates restore permission.

- The old Hub public snapshot carrier does not carry this complete continuation
  graph yet. Hub and Android must explicitly adopt the full codec and their
  bounded transport policy; no history may be omitted to fit a limit.
- Restore must independently admit the target owner, exact workspace identity,
  active source/rule state, local conflict baseline and atomic write capability.
  A matching SHA, a Hub access token or a client assertion is not that admission.
- Historical archive/GM provenance must remain explicit; preserving history must
  neither drop it silently nor convert it into new authority.
- Android must retain the full owner stamp across asynchronous upload/review work,
  re-enter the current owner authority before effects, use server CAS, and expose
  review/conflict/recovery instead of overwriting divergent local or remote data.
- Complete Core and consumer package resealing and real Android save/reopen/
  process-restart proof remain separate requirements before any release claim.

The tests distinguish genuine local creation/finalization/career state from
owner-scoped reads and delegated edits. Current career reputation mutation
admission remains local-only; this export does not make that command owner-scoped.
