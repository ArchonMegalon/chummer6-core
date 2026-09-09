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

## Still required before roaming integration

- A complete transport codec must preserve this entire graph, including the
  delegated history omitted from `WorkspaceDocumentSnapshot`. The old Hub public
  snapshot carrier is not yet a carrier of the new complete continuation state.
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
