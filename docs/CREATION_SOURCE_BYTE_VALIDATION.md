# Creation source validation: timestamps are not write generations

The local post-Talent integration regression passed 242/243 Creation tests but
failed the same-size, restored-mtime Skills source-change check. Its isolated
repeat passed. The failed run did not capture native timestamps, so it does not
prove which filesystem timestamp collision occurred.

The resolver nevertheless had an unsafe shortcut: it treated equal `statx`
inode/device/mount/ctime metadata as sufficient proof of unchanged bytes. Linux
ctime is not a guaranteed collision-free change counter. See the primary
[Linux multigrain timestamp documentation](https://docs.kernel.org/filesystems/multigrain-ts.html).

## Changed behavior

- Initial capture and subsequent outer source-context entries compare the live
  content byte-for-byte with the privately retained capture, even when native
  metadata is available and equal. Equality of hashes is no longer needed for
  this internal comparison; public authority digests still use unchanged SHA-256.
- Metadata, file/link identity and directory membership checks remain; obvious
  changes still fail closed before expensive projection.
- Validation streams at most the captured length plus one byte through a pooled
  buffer. It does not allocate another full source byte array or parse XML again.
  A differing byte, premature end or extra byte rejects reuse. An observed
  mismatch poisons the context even if the original file is subsequently restored.
- Before/after metadata checks bracket validation. Symlink identity is separate
  from content length: a link's `FileInfo.Length` is not the target's byte count.
- The captured source bytes, parsed trees and digests stay immutable. A changed
  source cannot silently become the authority of the old context; a fresh
  context is required.
- Diagnostics distinguish initial capture reads/parses from validation reads and
  bytes. The old Linux expectation of zero validation reads was invalid and has
  been replaced with an explicit content-validation requirement.

Two focused tests reproduced the missing byte validation before the patch.
The source-context cases cover capture caching, restored metadata, fallback
validation, atomic replacement, new overlays/custom directories, mid-capture
mutation, direct skill lookup, gear, changed symlink targets, and equal-length
changes on both sides of the 64 KiB streaming-buffer boundary. Finalization
tests separately protect the post-flush source check, atomic replacement and
cold receipt recovery. These host tests do not establish Android performance.
Before Android delivery, verify the affected native finalization/save/reopen
route against the candidate that actually consumes this resolver.

This is a local source correction, not a new package seal, phone-performance
measurement, device-process proof, or release receipt. A future optimization
must provide genuine immutable/transactional source ownership; it must not
restore metadata-only acceptance or weaken source-change rejection.

## Reusing parsed Magic/Resonance payloads

Magic finalization now weakly retains the canonical XML parse and its digest for
the same immutable source string (up to 64 Ki characters). This is not a cached
admission result: each call still compares the supplied digest and expected
root, and rechecks the option's typed values, costs, restrictions and blockers.
Source-file validation, authority hashing, owner leases and budget checks are
unchanged. Larger payloads keep the ordinary uncached path and the existing
one-Mi-character input limit. Private parsed trees are only read, not exposed.
Retired source strings are not kept alive by the weak cache.

The isolated managed investigation measured repeated Magic authority validation
allocations falling from about 21.2 MB to 3.6 MB. Those probes also contained a
separate serializer experiment; that experiment and all timing probes were
removed from the final patch. The focused allocation regression independently
compares the same string with fresh equal-content strings and requires less
than one third of the allocation. It also verifies collection of retired text.

All 98 selected Magic service/source, owner-bound Quality and canonical-digest
tests passed, including mutable-list tampering, malformed/large XML, parallel
reads, source drift, confirm/reopen and replay checks. Existing unrelated test
compiler warnings remain. The clean production patch also restored the same
nine saved synthetic runners without byte changes or overview digest changes:
`8d28f9dc66dab4987460ec61f95e42e647c6d81c7a189d0fbb2498a777f4303a`.
Its source context still performed 718 validation reads / 183,642,655 bytes.
The single managed run took 2,707 ms; this is not a controlled end-to-end or
native startup speedup, sealed package, physical-device proof or publication.

Retained focused-test log SHA-256:
`76a11b3825b0fa90965f4e4e41c8bae942630ebbfa91e3ed504ffcab9fda08d9`.
Clean managed restoration log SHA-256:
`573bb62f6aadc3e87009c3ce2965a0c2dc20ba340c89c9ba8339373515c359d6`.
