# Creation owner-admission focused tests

Actual local verification on 9 September 2026, SDK 10.0.103:

- 17/17 owner-bound service cases passed; build zero warnings/errors.
- Three scoped FileWorkspaceStore capability groups passed separately.
- All 66 unchanged Bootstrap/Contacts MSTest rows passed through
  `../CreationLegacyRegression/CreationLegacyRegression.FocusedTests.csproj`;
  zero skipped, build zero warnings/errors. That harness uses the already
  authorized feature-test friend identity; no production visibility was widened.

Evidence is retained under the integration workspace's `_completion/chummer-next-wave/`:
`core-creation-owner-companions-20260909.OcFFCB` and
`core-scoped-creation-store-20260909.HNV8Xn`. Scripts, exact source hashes,
binaries and actual logs are retained there. These are local Core/store proofs,
not package seals, Android compilation, API36 or release authority.

Earlier failures remain separate evidence: the legacy Bootstrap and corrected
Contacts probes performed real trusted-local writes rather than linked-owner
writes. The first Contacts fixture incorrectly used a scoped API for reserved
Local, then was corrected to use the explicit local seam. The first companion
run passed five cases before failing the missing scoped Contacts capability
outcome; the linked path now rejects it as unavailable before preview replay.
The first legacy regression harness failed compilation because it lacked the
existing friend identity. None of those failed attempts is relabelled GREEN.

The default program requires the new Application companion APIs. Before those
interfaces exist, a compiler failure is a **missing-interface feature RED**, not
an executed owner-isolation failure. `LegacyProbe.cs` compiles separately against
the existing trusted-local services and intentionally fails its linked-owner
expectation after proving a real local FileWorkspaceStore write. That probe is
diagnostic, not a regression suite: legacy trusted-local behavior is preserved.

Logical commands (actual governed runs use the retained exact SDK/feed scripts):

```text
dotnet run --project FocusedTests/CreationOwnerAdmission/CreationOwnerAdmission.FocusedTests.csproj -p:RunLegacyProbe=true -- --core-root <exact-core-root>
dotnet run --project FocusedTests/CreationOwnerAdmission/CreationOwnerAdmission.FocusedTests.csproj -p:RunLegacyProbe=true -- --core-root <exact-core-root> --contacts
dotnet run --project FocusedTests/CreationOwnerAdmission/CreationOwnerAdmission.FocusedTests.csproj -- --core-root <exact-core-root>
```

The real file-store tests seed identical workspace IDs/content in local, A and B
partitions. The shared canonical owner lease is admitted from the exact injected
accessor; a scope-only or foreign authority cannot gain a fallback local lease.
Exclusion is observed with another thread's immediate `Monitor.TryEnter`, not a
sleep-based scheduling assumption. Every fixture uses its own temporary store.

Implemented Application/store seams (native composition and package review remain):

- `IOwnerBoundCharacterCreationBootstrapService`: synchronous
  `Create(expectedOwner, request)`, `CreateActivation(expectedOwner, request)`,
  `TryValidateCurrent(expectedOwner, bundle, out blockers)`.
- `IOwnerBoundCharacterCreationContactsService`: synchronous
  `Load`, `Preview`, `Confirm`, `LookupReceipt`, each receiving the caller-retained
  original `OwnerContextStamp` followed by the existing typed request.
- Existing result envelopes and domain request/receipt schemas remain unchanged.
  New Application companion implementations consume the existing concrete domain
  services through internal owner-scoped entrypoints, without copying algorithms.
- `IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability`:
  `SupportsOwnerScopedCharacterCreationBootstrapAtomicCreate` and
  `CreateCharacterCreationBootstrapWorkspaceDocument(OwnerScope, id, document)`.
- `IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability`:
  `SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit` and
  `ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(OwnerScope, id,
  expectedContentRevision, expectedAuxiliaryStateDigest, document)`.

There is no generic Create/Import fallback. After full-stamp lease admission,
the actual trusted Local identity deliberately uses existing explicit local
store seams; linked owners always require the scoped capabilities. A public
owner value resembling Local must never gain that trusted-local branch. The
store's existing rejection of reserved Local values in scoped APIs is unchanged.

An old pending activation bundle is
transient and must not survive an owner ABA or process restart. Its already
created workspace and historical Contacts receipt remain readable under fresh
same-stable-owner authorization; refusing an old bundle must not create a second
workspace or persist epoch/issuer fields in historical storage.

Native Android dialog/preview provenance, native registration changes, device
proof and package seals are separate work. These tests do not claim to close them.
