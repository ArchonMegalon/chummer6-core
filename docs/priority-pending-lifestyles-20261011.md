# Pending Priority lifestyles

Priority and Sum-to-Ten now use their confirmed Resources/Gear balance for
Creation lifestyle purchases. Previously the service read the unfinished raw
character XML, which did not yet contain that balance. Android's Contacts →
Lifestyles route consequently opened an unavailable budget.

## Behavior

- Add, edit and delete use the existing typed lifestyle commands and source
  cost rules. A new optional auxiliary draft preserves choices without writing
  unfinished build data into character XML.
- Each confirmation appends one receipt and checkpoints atomically. Parent
  draft hashes, source policy, revision, budgets and sibling preservation remain
  mandatory. Replaying a completed request does not buy another month.
- Finalization deducts purchased lifestyles before applying the carryover cap,
  preserves their instance identities, months and location, and then adds the
  explicitly entered starting-cash roll. Only owned lifestyle tiers are offered;
  the greatest expected cash is the initial selection. An empty purchase list
  retains the historical free Street fallback.
- Continuation evaluation freezes the Contact/carryover policy and the exact
  draft-derived lifestyle source context. Other XML inputs cannot reuse it;
  domain evaluation has no live-source fallback. Independent recapture must
  still match, and evaluation grants neither restore nor historical provenance
  authority.
- New optional fields are omitted when null. Historical XML-based purchases,
  receipts and Street-only finalization retain their existing representation.

## Focused checks

The local CreationHistory test project exercises actual file stores and typed
services, not copied rules: Priority/Sum-to-Ten budgets, raw XML preservation,
cold reopening and receipt replay, paid-month finalization, two lifestyles in
reverse identity order, edit/delete, overspend and foreign starting-cash choices.
Continuation checks use exported real drafts and independently recaptured
sources; existing legacy source-capture/domain cases are retained.

Actual restore now passes the complete snapshot, including auxiliary drafts,
to the independent source fence. Tests exercise dirty restore, finalization,
Career mutation and a second cold restore, with and without purchased Low
lifestyle months. Expiry, cancellation and caller-buffer mutation still retain
their existing rejection/atomic-commit behavior.

Local verification on 11 October 2026: 30/30 focused purchase, actual restore
and restore-lifetime tests passed (`pending-restore-green05.log`). The earlier
33/33 continuation capture/domain run passed before the final restore-service
conversion fix; it is separate evidence, not a second run of the final tree.
Existing test-source analyzer warnings remain; no warning-free claim is made.

An exploratory attempt to extend the pending purchase test to Dwarf/Troll
failed before reaching lifestyles: the existing Priority racial authority only
admits Human/Elf/Ork. Those invalid fixture rows were not counted as purchase
passes and are not part of this change's supported-metatype claim. Full source-
bound Dwarf/Troll racial bonuses, attribute admission and backward-compatible
catalog handling remain a separate Creation completion gap; do not enable them
by bypassing the existing unsupported-heritage guard. The failed local run is
retained as `pending-restore-green04.log` (11 passed, 2 failed).

These are Core checks. Android intake, affected native save/reopen/restart,
signed release artifacts and Play observation must be recorded separately.
This source change alone does not qualify a new Android release or establish
complete Creation, Origin, desktop or startup-performance acceptance.
