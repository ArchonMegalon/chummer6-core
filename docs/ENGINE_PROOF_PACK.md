# Engine Proof Pack

## Purpose

`ENGINE_PROOF_PACK.generated.json` is the release-bound core proof contract for milestone `104`.
It keeps engine trust evidence machine-readable so desktop release polish cannot outrun mechanical confidence.

## Artifact

- path: `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`
- contract: `chummer6-core.engine_proof_pack`
- successor package: `next90-m104-core-proof-pack`
- successor frontier: `3227666051`

## Required coverage

The proof pack must fail closed unless it includes:

- successor-wave authority for milestone `104`, owned surfaces `engine_proof_pack` and `import_oracle_discipline`, and the canonical successor registry plus queue staging paths
- queue closeout proof that the successor queue row is marked `status: complete`, pins frontier `3227666051`, cites landed commit `00800059`, and lists the proof anchors for this generated pack, generator, tests, and documentation
- package-local closeout proof in `docs/NEXT90_M104_CORE_PROOF_PACK_CLOSEOUT.md`, so future shards have an explicit do-not-reopen handoff for the completed core slice
- closeout-document hygiene that fails closed if the package-local handoff loses its package identity, closed-scope tokens, do-not-reopen instruction, verification commands, or cites task-local run artifacts as proof
- closeout-document verification commands for the generator check, generator unit suite, core engine harness build/run, and benchmark budget command, so future package verification cannot silently drop the release-bound performance-budget leg
- queue and registry closeout proof for commit `8dd516ef`, which makes failed generator runs exit nonzero while still writing diagnostic receipts
- queue and registry closeout proof for commit `c88178fa`, which proves design-owned queue scope drift fails closed
- queue and registry closeout proof for commit `769e7259`, which binds this completed package to the latest local guard chain
- queue and registry closeout proof for commit `d4b3b0ba`, which requires the current `769e7259` guard in the generated proof pack, unit tests, and documentation
- queue and registry closeout proof for commit `a2173476`, which requires the current `d4b3b0ba` guard in the generated proof pack, unit tests, and documentation
- queue and registry closeout proof for commit `4b124997`, which binds the proof-pack generator, tests, documentation, and checked-in receipt to active-run hygiene guard `4a56911d`
- queue and registry closeout proof for commit `b488d109`, which pins the latest M104 proof pack authority so future shards verify the closed package instead of repeating it
- queue and registry closeout proof for commit `b6fddf74`, which tightens the current M104 proof pack authority guard so future shards verify the latest closed package
- queue and registry closeout proof for commit `f6608678`, which tightens the latest M104 proof pack local guard so future shards verify the closed package
- queue and registry closeout proof for commit `a3cbb548`, which refreshes the checked-in M104 proof receipt after latest local guard tightening
- queue and registry closeout proof for commit `df0527b2`, which tightens the M104 proof pack receipt guard so future shards verify the latest closed package
- queue and registry closeout proof for commit `8574f63f`, which pins the M104 proof pack receipt guard
- queue and registry closeout proof for commit `6b3a662c`, which requires the current `8574f63f` guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `3b63478f`, which pins the current `6b3a662c` guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `cd30503f`, which pins the current `d2ee91a9` engine proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `e10f2739`, which pins the current `cd30503f` queue proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `3c242c2f`, which pins the current `f914ce6a` helper hygiene proof floor in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `ea449f7b`, which pins the current `c2872b40` queue proof floor guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `18365058`, which pins the current `ea449f7b` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `5031ee41`, which pins the current `18365058` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `cbce6a19`, which pins the current `5031ee41` queue proof guard in the generated proof pack, unit tests, and proof-pack documentation
- queue and registry closeout proof for commit `71441924`, which pins the current `cbce6a19` queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `df1330b4`, which pins the latest `71441924` queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `6610ff2e`, which requires the `df1330b4` queue proof floor to resolve locally
- queue and registry closeout proof for commit `2c8742ad`, which fail-closes duplicate M104 package rows in Fleet and design queue staging
- queue and registry closeout proof for commit `40babebd`, which pins the latest `5baebb73` queue proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `22171b35`, which pins the current `40babebd` proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `96eca660`, which pins the current `c6fbd75f` non-mutating proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `05e47cff`, which binds the M104 proof guard to successor queues so future shards verify the current closed-package guard
- queue and registry closeout proof for commit `93d06011`, which pins the current `05e47cff` queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `31aec38a`, which pins the current `93d06011` queue-bound proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `ceccc309`, which pins the current `31aec38a` queue-bound proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `5dff1a2e`, which tightens worker-safe closure evidence guards for task-local files and run-control helper transcripts
- queue and registry closeout proof for commit `2301a043`, which pins the current `5dff1a2e` worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `5c75316f`, which pins the current `2301a043` worker-safe proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `28be988f`, which pins the current `5c75316f` proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `c6a2ee8e`, which pins the current `28be988f` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `6684fc89`, which pins the current `c6a2ee8e` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `ccbfc6b2`, which pins the current `6684fc89` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `2a3ebcb9`, which pins the current `ccbfc6b2` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `7501f49a`, which pins the current `2a3ebcb9` proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `36311e16`, which pins the current `ac961fe1` local proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `be5755a6`, which pins the current `db3cc033` queue-cited proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `8ffec2b1`, which pins the latest `be5755a6` queue proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `73638668`, which pins the current `58656418` worker proof hygiene guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `c58d18e1`, which pins the current `5f50cb7b` engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `67e0f654`, which pins the current `c58d18e1` engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `d584120b`, which pins the current `67e0f654` engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `39c875fd`, which pins the current `d584120b` local engine proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `f1b6c5ca`, which pins the queued `39c875fd` proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `faf14925`, which pins the current `f1b6c5ca` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `64b8f873`, which requires the current `faf14925` proof floor guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `06a2e06a`, which pins the current `64b8f873` proof floor requirement in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `6d25fb18`, which pins the current `06a2e06a` proof floor receipt in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `cc6cf25b`, which pins the M104 current proof floor guard
- local proof for commit `bb9af238`, which pins the current `cc6cf25b` proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `44512fcf`, which pins the current `bb9af238` proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `4db6d429`, which pins the latest M104 proof floor guard
- local proof for commit `adc72a7e`, which pins the current M104 latest local proof floor
- local proof for commit `5e808a1b`, which pins the current `adc72a7e` proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `c323b4ad`, which pins the current `5e808a1b` queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `7a432bc3`, which pins the current `c323b4ad` queue proof floor guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `c124e4af`, which pins the current `7a432bc3` proof pack guard floor in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `5a649e57`, which closes the M104 proof pack handoff anchor so future shards verify the closed package instead of repeating it
- local proof for commit `c01dfa10`, which pins the M104 handoff proof anchor in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `1a98d904`, which documents the M104 handoff anchor proof in the closeout note
- local proof for commit `af67ecfd`, which pins the current M104 handoff proof floor in the generator, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `870be707`, which pins the current `af67ecfd` handoff proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `498dff3d`, which pins the current M104 queue-mirror parity guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- queue and registry closeout proof for commit `b8000b80`, which tightens the M104 OODA telemetry proof guard so plain governor-loop evidence cannot close the package
- queue and registry closeout proof for commit `ecbb466c`, which pins the current `b8000b80` OODA proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `a2c8ad9f`, which pins the current M104 active-run handoff field proof guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `2c98f61c`, which pins the current M104 closeout handoff evidence guard in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `2e4e8e81`, which pins the current M104 handoff evidence proof floor in the generated proof pack, unit tests, proof-pack documentation, and checked-in receipt
- local proof for commit `b5d46938`, which tightens release-bound import-oracle, queue, encoded-helper, release-channel, and performance-budget proof discipline
- local proof for commit `c1300863`, which pins the current engine proof pack discipline guard in the generator, unit tests, proof-pack documentation, and checked-in receipt
- local git commit proof that the cited M104 guard commits, including guards `56048971`, `769e7259`, `d4b3b0ba`, `a2173476`, `dafc1205`, `65df3894`, `4a56911d`, `4b124997`, `2187db33`, `b488d109`, `b6fddf74`, `3b9a29c2`, `f6608678`, `a3cbb548`, `df0527b2`, `8574f63f`, `6b3a662c`, `3b63478f`, `31c75c02`, `ef46554c`, `0771b7ea`, `fdb6a273`, `d2ee91a9`, `cd30503f`, `e10f2739`, `e7d4270e`, `bbc877d7`, `56ff7283`, `7ae79416`, `a613bdb2`, `353921e7`, `9de2455b`, `d8e826a3`, `7a1f0e7c`, `d464cfab`, `a1a2d956`, `abf63719`, `bbc7fba8`, `a1a1d505`, `18d03556`, `77cb53cf`, `f914ce6a`, `3c242c2f`, `c2872b40`, `ea449f7b`, `18365058`, `5031ee41`, `cbce6a19`, `71441924`, `df1330b4`, `6610ff2e`, `2c8742ad`, `5baebb73`, `40babebd`, `22171b35`, `c6fbd75f`, `96eca660`, `05e47cff`, `93d06011`, `31aec38a`, `ceccc309`, `5dff1a2e`, `2301a043`, `5c75316f`, `28be988f`, `c6a2ee8e`, `6684fc89`, `ccbfc6b2`, `2a3ebcb9`, `7501f49a`, `ac961fe1`, `36311e16`, `db3cc033`, `be5755a6`, `8ffec2b1`, `ee9d88b1`, `eacefaf2`, `e4e502a1`, `1f2c5724`, `1bcb9b7e`, `e04d7b88`, `58656418`, `73638668`, `a404b474`, `51bb2d8f`, `507f1f6b`, `43638c3e`, `b0776012`, `5f50cb7b`, `c58d18e1`, `67e0f654`, `d584120b`, `39c875fd`, `f1b6c5ca`, `faf14925`, `64b8f873`, `06a2e06a`, `6d25fb18`, `cc6cf25b`, `bb9af238`, `44512fcf`, `4db6d429`, `adc72a7e`, `5e808a1b`, `c323b4ad`, `7a432bc3`, `c124e4af`, `5a649e57`, `af67ecfd`, `870be707`, `498dff3d`, `b8000b80`, `ecbb466c`, `a2c8ad9f`, `2c98f61c`, `2e4e8e81`, `b5d46938`, and `c1300863`, resolve in this repository before the release-bound proof pack can pass
- case-insensitive active-run proof hygiene, so registry or queue evidence for this closed package cannot cite active-run handoff files, concrete `/var/lib/codex-fleet/` or `/docker/fleet/state/chummer_design_supervisor/` run paths, handoff fields or labels such as `Open milestone ids`, `Focus profiles`, `Prompt path`, or `Recent stderr tail`, operator telemetry, or active-run helper output as release proof
- worker-safe closure hygiene, so registry, Fleet queue, or design queue evidence for this closed package cannot cite task-local telemetry files, telemetry field names such as `frontier_briefs`, `first_commands`, `status_query_supported`, `polling_disabled`, or `slice_summary`, successor-wave telemetry summaries, operator/OODA loop helper output, operator status snippets, supervisor status or ETA helper output, supervisor helper loops, or active-run helper-command transcripts as package proof
- implementation-only retry hygiene, so copied worker prompt fragments such as previous-attempt, previous attempt, or implementation-only retry language cannot be used as registry, queue, closeout, import-certification command, or import-certification evidence proof for this closed package
- retry-orientation hygiene, so copied worker instructions such as exact-command orientation, current steering focus, direct-read handoff context, writable scope roots, or stop-report templates cannot be used as registry or queue proof for this closed package
- encoded proof hygiene, so percent-encoded, URL form-encoded, or HTML-escaped variants of active-run paths, task-local telemetry, supervisor helpers, OODA helper output, or handoff labels fail the same way as plain copied evidence
- stale historical-status hygiene, so exact historical operator status snippets are rejected as closure evidence even when copied in as plain closeout notes rather than `proof:` or `evidence:` lines
- design-owned queue closeout proof from `/docker/chummercomplete/chummer-design/products/chummer/NEXT_90_DAY_QUEUE_STAGING.generated.yaml`, in addition to the Fleet staging mirror, so Fleet-local staging cannot be the only authority that keeps the package closed
- Fleet/design queue proof-row parity for this package, so the completed package cannot remain green when one queue cites a different proof list than the other
- design-owned queue scope proof that fails closed if the canonical queue row adds unassigned allowed paths or owned surfaces even when the Fleet staging mirror stays clean
- Fleet and design queue uniqueness proof, so duplicate `next90-m104-core-proof-pack` rows cannot make future shards repeat or ambiguously re-close the completed package
- Fleet and design queue proof-list uniqueness, so duplicate evidence citations inside the completed package row cannot hide stale, padded, or copy-looped proof
- filesystem resolution for every absolute package proof path in the queue proof lists, so stale, moved, or padded closeout evidence cannot keep the package marked passed
- package-local canonical proof paths, so successor queue closeout cannot stay green by citing sibling package proof under `/docker/chummercomplete/...` instead of `chummer-core-engine`, even when the extra sibling proof item appears in both queue mirrors
- row-scoped queue authority for the assigned allowed paths: `src`, `tests`, `docs`, and `scripts`
- exact row-scoped queue authority for allowed paths and owned surfaces, so later queue edits cannot widen the package beyond `src`, `tests`, `docs`, `scripts`, `engine_proof_pack`, and `import_oracle_discipline` while keeping the proof pack green
- row-scoped registry and queue validation so tokens from another successor milestone or package cannot satisfy `next90-m104-core-proof-pack`
- task-scoped registry validation so completion and evidence for core tasks `104.1` and `104.2` cannot be satisfied by later milestone-104 tasks owned by another package
- oracle suites: `creation`, `advancement`, `augment`, `matrix`, `magic`, `vehicle`, `source_toggle`, `amend_package`
- performance budget lanes: `load`, `explain`, `diff_apply`, `import`, `export_prep`
- import-oracle discipline status sourced from `IMPORT_PARITY_CERTIFICATION.generated.json`
- import-oracle source receipt identity for `contract_name=chummer6-core.import_parity_certification`, `schema_version=1`, and `proof_kind=local_parity_harness`, so a helper summary or non-canonical receipt cannot satisfy oracle discipline by carrying the same status and counts
- import-oracle aggregate coverage sourced from the canonical receipt `coverage` block, so row-level oracle detail cannot hide a missing, malformed, boolean, fractional, string-encoded, under-100-percent, zero, undercovered, or adjacent-oracle-omitting source summary
- release-channel binding sourced from `/docker/chummercomplete/chummer-hub-registry/.codex-studio/published/RELEASE_CHANNEL.generated.json`
- release commands with existing repo-local project and budget inputs
- evidence anchors that resolve to checked-in files; anchors with `::` must also resolve to a symbol or stable token in that file

All required performance lanes must resolve to named workloads in `Chummer.Benchmarks/workspace-benchmark-budgets.json`.
They must also resolve to executable workload evidence in `Chummer.Benchmarks/MigrationWorkspaceBenchmarks.cs`.
The proof generator fails closed when a required budget lane is missing from either the budget file or the executable benchmark workload source, or when the budget file duplicates a required workload name that could mask stale or conflicting release-budget thresholds.
Malformed, string-encoded, non-finite, fractional byte, integer-valued float byte, zero, negative, or boolean budget thresholds fail closed as unresolved performance-budget lanes rather than crashing proof generation, coercing JSON strings, truncating precision, or passing as implicit zeros.

The import-oracle discipline lane requires named coverage for Chummer4, Chummer5a, Hero Lab Classic, Genesis, and CommLink6.
Required import and adjacent oracle rows must be structured receipt objects, not bare names.
The source import-certification receipt must keep the canonical contract identity, schema version, and proof kind before any status, command, evidence, or coverage row can close the lane.
The source import-certification receipt must also carry command and evidence lists made only of non-empty strings, so a name-and-count-only receipt or mixed malformed row cannot close release-bound oracle discipline.
The source import-certification receipt must carry a complete aggregate `coverage` object with JSON integer `sources_covered`, `sources_expected`, and `coverage_percent` values before row-level oracle details can close the lane.
That aggregate coverage must cover exactly the five release-bound oracle sources: Chummer4, Chummer5a, Hero Lab Classic, Genesis, and CommLink6.
The command list must contain only the release-bound core import harness command `dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release`, and the evidence list must contain only the stable `core-engine-tests: ok` receipt marker.
Those command and evidence rows must be unique and must not include extra non-release-bound rows or cite active-run, task-local, supervisor, ETA, OODA, handoff telemetry, previous-attempt or previous attempt prompt text, or implementation-only retry prompt text as proof, including percent-encoded, URL form-encoded, HTML-escaped, or separator-obfuscated copies of those tokens.
Their coverage counts and percent must be complete JSON integer coverage rather than string-encoded numbers, fractional values, integer-valued floats, booleans, under-100-percent summaries, adjacent-oracle-omitting totals, or a later duplicate row masking an earlier incomplete receipt.
String, boolean, float, or fractional `sources_covered` or `sources_expected` values are rejected as malformed coverage instead of being coerced or accepted as Python integer aliases.
Adjacent oracle rows such as Genesis or CommLink6 must include explicit `sources_covered` and `sources_expected` counts, and the proof pack fails closed unless those counts show complete coverage.
Name-only or malformed adjacent oracle rows fail closed instead of satisfying import-oracle discipline.
Adjacent oracle rows must also be unique by normalized oracle name, so a later complete row cannot mask an earlier undercovered Genesis or CommLink6 receipt.

The release-channel binding requires the current release shelf to be `published`, `promoted_preview`, release-proof `passed`, and desktop tuple coverage `complete`.
Release-channel artifact ids and desktop tuple ids must be unique, so a duplicate shelf item or route-truth row cannot mask a stale or conflicting promoted desktop receipt.
It also fail-closes unless the promoted primary Avalonia installer tuples resolve for Linux, Windows, and macOS:

- `avalonia:linux:linux-x64`
- `avalonia:windows:win-x64`
- `avalonia:macos:osx-arm64`

Each required tuple must remain `routeRole=primary`, `promotionState=promoted`, `parityPosture=flagship_primary`, `updateEligibility=eligible`, `revokeState=not_revoked`, and `installPosture=installer_first`, with its artifact id present on the release shelf.

## Generation

From repo root:

```bash
python3 scripts/generate-engine-proof-pack.py
```

The generator treats the generated proof pack path as a planned output so a clean first run cannot fail only because `ENGINE_PROOF_PACK.generated.json` does not already exist. Other successor queue proof anchors must resolve on disk.
Use `python3 scripts/generate-engine-proof-pack.py --check` when verification must prove the checked-in receipt is current without rewriting the artifact.
Both the Fleet staging queue and the design-owned staging queue must retain the completed `next90-m104-core-proof-pack` row with the same frontier, allowed paths, owned surfaces, landed commit, and proof anchors.
Each queue must contain exactly one `next90-m104-core-proof-pack` row.
The package proof lists in those two rows must also match exactly by proof item; a Fleet-only or design-only proof citation fails `queue_mirror_parity`.
Each proof list must be duplicate-free before queue authority can pass.
Every absolute `/docker/chummercomplete/...` proof path in either queue row must resolve inside `/docker/chummercomplete/chummer-core-engine`; commit citations remain textual proof items, while added sibling-repo or missing package-local paths fail queue authority.
The package-local closeout note must remain listed as a resolved queue proof anchor beside the generated receipt, generator, tests, and this proof-pack contract.
The generator also validates the closeout note content directly, including package/frontier/owner identity, closed-scope authority, allowed paths, owned surfaces, do-not-reopen posture, verification commands, absence of concrete active-run artifact paths, and absence of active-run telemetry or handoff field labels from closeout proof/evidence lines, including separator-obfuscated variants.
The checked-in receipt is also treated as a reproducible artifact: `tests/test_engine_proof_pack_generator.py` rebuilds the payload from repo-local evidence and compares it to `.codex-studio/published/ENGINE_PROOF_PACK.generated.json`, ignoring only `generated_at`.
The generator still writes the diagnostic receipt when evidence is missing, but exits nonzero whenever the generated pack status is not `passed`.

## Verification

The generator unit tests prove fail-closed behavior for missing evidence symbols, missing executable benchmark workloads, string-encoded, fractional-byte, or integer-valued-float byte budgets, missing, duplicated, undercovered, string-encoded, boolean, fractional, or otherwise malformed required and adjacent import oracles, missing, incomplete, under-100-percent, adjacent-oracle-omitting, string-encoded, boolean, fractional, or otherwise malformed import-certification aggregate coverage, import-certification receipt identity drift, missing, malformed, duplicated, non-release-bound, or active-run-contaminated source receipt command/evidence rows, release-channel promoted tuple drift, release-channel duplicate tuple or artifact ids, release-channel artifact shelf drift, successor registry or queue tokens that only appear on another milestone/package row, duplicate Fleet or design queue rows for the same completed package, duplicate proof items inside either completed queue row, Fleet/design queue proof-row drift, missing successor frontier id, unassigned successor queue allowed paths or owned surfaces, non-resolving successor queue proof anchors, sibling package proof anchors under `/docker/chummercomplete/...`, closeout document drift, concrete active-run artifact paths in the closeout document, case-insensitive, percent-encoded, URL form-encoded, or separator-obfuscated active-run telemetry, task-local telemetry, task-local telemetry fields such as `frontier_briefs` and `status_query_supported`, successor-wave telemetry, concrete `/var/lib/codex-fleet/` and `/docker/fleet/state/chummer_design_supervisor/` run paths, copied handoff labels such as `Open milestone ids`, `Frontier ids`, and `Focus profiles`, copied handoff fields such as `Prompt path` and `Recent stderr tail`, handoff, operator/OODA loop helper, supervisor-status or ETA helper citations in registry, Fleet queue, and design queue evidence, helper-command proof citations, implementation-only retry prompt fragments, or retry-orientation prompt fragments in registry and queue evidence, missing local closeout commits including the current `d2ee91a9` engine proof floor, the queue-cited `cd30503f` floor pin, the `e10f2739` queue proof floor, the `e7d4270e` queue proof floor guard, the `bbc877d7` proof floor guard, the `56ff7283` proof floor guard, the `7ae79416` proof floor guard, the `a613bdb2` engine proof pack guard, the `353921e7` engine proof pack guard floor, the `9de2455b` proof pack guard floor, the `d8e826a3` proof pack guard floor, the `7a1f0e7c` proof pack guard floor, the `d464cfab` proof pack guard floor, the `a1a2d956` proof pack local floor, the `abf63719` proof pack local floor, the `bbc7fba8` engine proof pack floor, the `a1a1d505` engine proof pack floor, the `18d03556` active-run helper hygiene floor, the `77cb53cf` helper hygiene proof floor, the `f914ce6a` helper hygiene proof floor pin, the `3c242c2f` helper hygiene queue citation, the `c2872b40` queue proof floor guard, the `ea449f7b` queue proof guard pin, the `18365058` queue proof guard floor, the `5031ee41` queue proof guard floor, the `cbce6a19` queue proof guard floor, the `71441924` queue proof floor, the `df1330b4` queue proof floor pin, the `6610ff2e` queue floor resolution guard, the `2c8742ad` duplicate queue row guard, the `5baebb73` queue proof guard, the `40babebd` queue proof guard pin, the `22171b35` proof guard pin, the `c6fbd75f` non-mutating proof-pack check, the current `96eca660` non-mutating proof guard pin, the current `05e47cff` queue-bound proof guard, the current `93d06011` queue-bound proof guard pin, the current `31aec38a` queue-bound proof guard floor, the current `ceccc309` queue-bound proof floor, the current `5dff1a2e` worker-safe closure evidence guard, the current `2301a043` worker-safe proof floor, the current `5c75316f` worker-safe proof floor pin, the current `28be988f` proof floor pin, the current `c6a2ee8e` proof floor guard, the current `6684fc89` proof floor guard pin, the current `ccbfc6b2` proof floor guard pin, the current `2a3ebcb9` proof floor guard pin, the current `7501f49a` proof floor pin, the current `ac961fe1` local proof floor pin, the current `36311e16` queue-cited local proof floor pin, the current `db3cc033` queue-cited proof floor, the current `be5755a6` queue-cited proof floor pin, the current `8ffec2b1` queue proof floor pin, the current `ee9d88b1` local queue proof floor pin, the current `eacefaf2` local proof floor, the current `e4e502a1` proof floor, the current `1f2c5724` proof-pack floor, the current `1bcb9b7e` proof-pack guard, the current `e04d7b88` checked-in receipt guard, the current `58656418` worker proof hygiene guard, the current `73638668` worker proof hygiene guard pin, the current `a404b474` latest worker proof guard, the current `51bb2d8f` proof pack guard, the current `507f1f6b` proof pack guard floor, the current `43638c3e` proof pack guard floor pin, the current `b0776012` proof pack floor pin, the current `5f50cb7b` engine proof floor, the current `c58d18e1` engine proof floor pin, the current `67e0f654` engine proof floor pin, the latest local `39c875fd` engine proof floor pin, the queued `f1b6c5ca` proof floor pin, the current `faf14925` proof floor guard, the current `64b8f873` proof floor requirement, the current `06a2e06a` proof floor requirement pin, the current `6d25fb18` proof floor receipt pin, the current `cc6cf25b` proof floor guard, the current `bb9af238` proof floor guard pin, the current `44512fcf` proof floor guard pin, the latest `4db6d429` local proof floor guard, the current `adc72a7e` latest local proof floor, the current `5e808a1b` queue-cited proof floor guard, the current `c323b4ad` queue proof floor guard, the current `7a432bc3` queue proof floor guard pin, and the current `c124e4af` proof pack guard floor, the current `5a649e57` handoff anchor, the current `af67ecfd` handoff proof floor, the current `870be707` handoff proof floor pin, the current `a2c8ad9f` active-run handoff field proof guard, the current `2c98f61c` closeout handoff evidence guard, the current `2e4e8e81` handoff evidence proof floor, the current `b5d46938` engine proof pack discipline guard, the current `c1300863` engine proof pack discipline guard pin, design-owned queue authority and scope drift, core-task completion evidence that only appears on a later milestone-104 task, and checked-in receipt drift from generator output:

```bash
python3 tests/test_engine_proof_pack_generator.py
```

The core engine test harness enforces the generated proof pack shape and required coverage after regenerating the receipt:

```bash
dotnet build Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release --nologo -m:1
dotnet Chummer.CoreEngine.Tests/bin/Release/net10.0/Chummer.CoreEngine.Tests.dll
```

The benchmark budget command listed in the proof pack remains the release command for measured workload budget enforcement.
