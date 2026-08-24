# Public Core runtime package handoff

The Core package-plane workflow publishes a secret-free GitHub Release only
after the exact `main` push passes the existing no-siblings package authority.
This makes the runtime bundle readable by the public Hub workflow without a
cross-repository token.

## Contract

For Core commit `<sha>`, the producer uses only these commit-derived names:

- release tag: `core-runtime-package-plane-<sha>`
- bundle: `chummer-core-runtime-package-plane-<sha>.zip`
- receipt: `chummer-core-runtime-package-plane-<sha>.public-handoff.json`

The publisher downloads the Actions artifact by the numeric ID emitted by the
current build job. Using `actions: read`, it obtains the authenticated artifact
and run-attempt metadata and the raw outer archive. It requires the API
repository, run ID, attempt, workflow identity, head SHA, and head tree to match
the exact `main` push, then hashes the raw archive before parsing any member.

The public bundle is a deterministic, stored-only ZIP of the exact eight
packages, runtime inventory, runtime lock, and no-siblings v3 receipt produced
by that successful workflow. The v2 public receipt binds every member SHA-256
and size, the public bundle SHA-256 and size, and the raw Actions artifact ID,
SHA-256, size, authenticated metadata projection, run attempt, workflow
identity, head SHA, and head tree. It contains no credentials.

Publication runs in a separate job. The build job keeps `contents: read`; only
the main-push publication job receives `contents: write`. Checkout credentials
are not persisted. Before publication, the job requires the release to be
absent, then atomically creates the lightweight Git tag directly at the exact
commit. Any pre-existing or concurrently created tag conflicts instead of being
reused. Release creation uses `--verify-tag` and never uses an overwrite
command. A partial or repeated publication therefore fails closed and requires
explicit forensic cleanup.

After creating the release, the workflow downloads the release API response
and Git tag response plus both assets without an authorization header. It
requires both the release target and lightweight Git tag to name the exact
commit, non-draft/non-prerelease posture, exactly two uploaded assets, GitHub's
SHA-256 metadata, and byte-for-byte agreement with the local receipt.

## Consumer authority

The GitHub Release is transport, not a trust root. A Hub change must review and
pin all of the following before consuming a new Core handoff:

1. the full Core commit and commit-derived release tag;
2. the exact workflow run, attempt, workflow identity, head tree, and Actions
   artifact ID represented in the receipt;
3. the receipt asset name, SHA-256, and size;
4. the bundle asset name, SHA-256, and size from that receipt;
5. the receipt's exact 11-member inventory and package provenance.

The Hub consumer must download anonymously, reject redirects outside HTTPS,
enforce tight byte/member bounds, compare the pinned receipt before extraction,
and validate only an immutable byte snapshot. Moving a tag, editing a release,
or replacing an asset then fails the Hub's independent digest authority.

Repository administrators can still delete releases or force-move tags. That
can cause denial of service, but it cannot substitute package bytes accepted by
a correctly pinned Hub authority. The producer intentionally does not publish
on pull requests, forks, dispatches, or non-main branches.
