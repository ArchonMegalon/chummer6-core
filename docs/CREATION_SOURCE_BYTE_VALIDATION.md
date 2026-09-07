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

- Initial capture and subsequent outer source-context entries validate content
  hashes even when native metadata is available and equal.
- Metadata, file/link identity and directory membership checks remain; obvious
  changes still fail closed before expensive projection.
- Validation streams at most the captured length plus one byte through a pooled
  buffer. It does not allocate another full source byte array or parse XML again.
- Before/after metadata checks bracket validation. Symlink identity is separate
  from content length: a link's `FileInfo.Length` is not the target's byte count.
- The captured source bytes, parsed trees and digests stay immutable. A changed
  source cannot silently become the authority of the old context; a fresh
  context is required.
- Diagnostics distinguish initial capture reads/parses from validation reads and
  bytes. The old Linux expectation of zero validation reads was invalid and has
  been replaced with an explicit content-validation requirement.

Two focused tests reproduced the missing byte validation before the patch.
The 14 source-context cases cover capture caching, restored metadata, fallback
validation, atomic replacement, new overlays/custom directories, mid-capture
mutation, direct skill lookup, gear, and changed symlink targets. Full combined
Core/native verification remains required for each final source commit.

This is a local source correction, not a new package seal, phone-performance
measurement, device-process proof, or release receipt. A future optimization
must provide genuine immutable/transactional source ownership; it must not
restore metadata-only acceptance or weaken source-change rejection.
