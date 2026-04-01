# Core implementation scope

## Mission

`chummer6-core` owns deterministic mechanics, reducer-safe session mutation, runtime bundles, explain traces, and the canonical engine contract plane.

## Owns

* rules math
* runtime fingerprints
* runtime bundles
* runtime-bundle fingerprints
* runtime-bundle build-axis truth
* deterministic pharmacology profiles (SR5 baseline with SR4/SR6 profile modules)
* deterministic rule-environment compilation
* amend graph normalization and package fingerprints
* legacy amend/custom-data import and loss classification
* deterministic reducers
* explain provenance
* engine contract canon
* ruleset/plugin/script ABI

## Must not own

* UI rendering or shell chrome
* hosted-service workflows
* relay or campaign orchestration
* media rendering
* registry persistence
* installer packaging or updater feed policy
* provider routing
* play/mobile client implementation

## Current purification focus

* remove `Chummer.Presentation.Contracts` and `Chummer.RunServices.Contracts` source leaks
* quarantine legacy tooling out of the active engine solution
* keep `Chummer.Engine.Contracts` as the only canonical engine/shared DTO source
* keep README and scope docs aligned so engine ownership stays explicit as the repo continues to shrink

## Current reality

Contract canon is materially closed:

* `Chummer.Engine.Contracts` is the sole engine/shared package boundary
* hosted contract mirrors are gone from this repo
* session semantic ownership is verifier-backed

The remaining work is engine purification, not continued contract ambiguity or missing hardening/certification proof.

## Package bootstrap rule

`Chummer.Engine.Contracts` must be boring to restore.

Canonical bootstrap paths:

* canonical local/CI package feed
* explicit generated compatibility tree for legacy consumers

Ambient monorepo-relative project references are not the assumed default bootstrap posture.

## Milestone spine

* E0 purification
* E1 runtime DTO canon
* E2 explain canon
* E3 session reducer canon
* E4 ruleset ABI stabilization
* E5 explain backend completion
* E6 Build Lab backend
* E7 legacy migration certification
* E8 hardening
* E9 finished engine

## Worker rule

If a feature can be answered by deterministic mechanics or explain provenance, it belongs here.
If it depends on HTTP, browser UX, player shell behavior, registry workflow, or render execution, it does not.

## Flagship-grade bar

`chummer6-core` is not flagship grade until:

* SR4, SR5, and SR6 rules modules can explain edition-specific outcomes without pushing edition ambiguity up into the UI
* source packs, presets, and amend packages compile into one canonical rule-environment graph with stable fingerprints and activation receipts
* Chummer5a-style amend packs either compile into the canonical operation set or emit explicit lossy/blocking receipts instead of silent fallback behavior
* legacy Chummer4, Chummer5a, and approved Hero Lab imports act as oracle fixtures, with divergences turned into triaged hardening work rather than ignored compatibility noise
* custom drugs, stacked active effects, timing windows, and modifier expiry resolve through deterministic engine state rather than UI-side folklore
* explain receipts surface enough canonical provenance that the product can answer "why?" without reverse-engineering engine internals

## Drug and pharmacology rule focus

* SR5 is the active profile baseline for deterministic drug semantics.
* SR4/SR6 support must be profile modules, not branching UI logic.
* drug import artifacts must resolve to canonical formulas before any explain receipt is generated.
* custom-drug construction and application must resolve to inspectable effect stacks, durations, and crash/comedown outcomes before the UI presents them as valid.


## External integration note

`chummer6-core` remains external-tool-agnostic.

It may emit deterministic payloads or consume approved deterministic inputs for other repos to use, but it must not:

* depend on provider SDKs
* depend on third-party orchestration APIs
* embed vendor-specific receipts as canonical engine truth
