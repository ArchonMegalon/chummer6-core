# Life Modules completion: avoid redundant catalog work

This change reduces repeated work during the existing owner-bound completion
transaction. It does not remove rule replay, source-byte validation, owner
admission, atomic persistence, or post-flush checks.

`CanonicallyEquals` freshly serializes both inputs and compares validated JSON
using the existing pooled SHA-256 writer. Matching serialized values need no
property sorting. Different serialization order falls back to canonical hashing
of those same captured documents. Persisted canonical digest bytes are unchanged;
there is no reference-identity shortcut or mutable-input cache.

An admitted source snapshot can copy one selected effective XML row instead of
cloning its whole catalog. This applies only to warmed lookups without selected
custom directories or contributor-identity inspection. The result is detached;
the snapshot is never returned. Cold lookup, custom amendment and ambiguity
checks retain their existing paths.

## Local verification

- 226 canonical-digest/source-resolver tests passed, including source drift/ABA,
  malformed inputs, custom overlays and returned-object isolation.
- 16 existing finalization/owner/persistence tests passed on the combined change,
  including cold reopen, before/after replacement failure, changed owner/source,
  rehashed forged previews and source loss after flush.
- New selected-row allocation regression fails on the prior implementation:
  724,296 bytes for one lookup versus 405,440 for copying the base catalog.
- Large identical catalog comparison allocates 2,080 bytes versus 837,824 for
  the former two-canonical-digest comparison in the focused warmed fixture.

Separate private diagnostic builds measured the original real-content
owner-bound confirmation at 2,582.62 ms / 989,743,632 allocated bytes before these
changes and 2,215.06 ms / 853,916,376 afterward. Both perform all 267 source-byte
validations. These are individual Linux-host measurements, not an Android
responsiveness result. The diagnostic instrumentation is not part of this
change or of package inputs.

Logs and TRX files are retained in the private local packets
`life-canonical-equality-20260928.pM6Symni` and
`life-finalization-hotspots-20260928.W0XUTDMd`. Native completion/Career/restart
verification of an APK consuming this change remains outstanding. No release
AAB, signing or Play publication is claimed here.
