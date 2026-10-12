# Skip custom-directory discovery only when the profile selects none

Restoring the retained synthetic Preview 198 Adept selected an all-sources
profile with no enabled custom-data keys. The shared Creation source context
nevertheless captured 55 installed `manifest.xml` files. Every outer authority
query then revalidated those unrelated bytes and directory membership.

`TryResolveProfileDirectories` now parses the profile's existing key/enabled
rules first. An empty enabled-key list resolves to an empty directory selection
without discovering installed custom directories. Even one enabled key retains
full discovery, including competing GUID/version candidates and ambiguity checks.

This is dependency selection, not a time-based or metadata-only cache:

- Settings bytes remain captured and compared on subsequent admissions.
- Saved directory names must still exactly match the resolved profile selection.
- Activating a custom key invalidates the old context. Restoring the original
  settings afterward cannot revive an already-poisoned operation.
- Enabled custom manifests, membership changes, amendments, overlays, live source
  bytes, identity changes and symlink retargets retain their existing checks.
- No workspace mutation, rule arithmetic, canonical digest, public contract,
  signing rule or Android runtime configuration changed.

## Local verification

The three new no-selection cases (absent list, disabled key, empty key) failed
against the prior production resolver because it captured unused manifests.
The profile activation/poisoning negative case already passed. After the change,
all four pass, together with 45 existing source/context/overlay/identity cases.

The existing standard `Chummer.Tests` project also passed all 60
`OwnerBoundCharacterCreationFinalizationServiceTests` cases, including exact
owner admission, source failure, all four methods, commit recovery, cold reopen
and receipt replay. The first attempt with the standalone CreationFinalization
project stopped at compilation because its assembly cannot access the existing
internal `FileWorkspaceStore.CachedReadValidationCount`; it is not recorded as
a runtime pass. No production visibility or test assertions were weakened.

Retained local log SHA-256 values:

- 49 source/context tests:
  `6fa75770e6d84d2c7131cf16e636458136f15c5a7fdf41470689a2c6265e2d1b`.
- 60 owner/finalization tests:
  `96514d7239cff56f0cc4af6f74544a0b027543a8dd33f4dfcbd68b86a8915c50`.

An isolated Linux managed comparison used the same saved nine-runner fixture,
selected workspace, Android/Presentation test binary, content and dependencies.
Only the resolver assembly differed; both resolver variants were built locally
with the same Debug configuration. This was explicitly a private source-build
diagnostic, not a sealed package graph or distributable Android artifact.

| Last shared source context | Before | After |
| --- | ---: | ---: |
| Initial reads / XML parses | 68 / 68 | 13 / 13 |
| Validation reads | 4,293 | 718 |
| Validation bytes | 189,323,915 | 183,642,655 |
| Directory validations | 64 | 0 |
| Prerequisite projections | 1 | 1 |

The complete serialized shared Creation overview has the same SHA-256 in both:
`8d28f9dc66dab4987460ec61f95e42e647c6d81c7a189d0fbb2498a777f4303a`.
All nine workspace records, including the retained input copies, remained
byte-identical. Owner, selected runner and initial-route readiness checks passed.

The single runs took 2,950 ms and 2,978 ms overall. Thus the deterministic I/O
reduction is established, but an end-to-end startup improvement is **not**.
Do not describe this as resolving the previously slow API-36 emulator or as
physical-phone evidence. The next native comparison must consume the actual new
package bytes and preserve save/reopen behavior before a release claim widens.
