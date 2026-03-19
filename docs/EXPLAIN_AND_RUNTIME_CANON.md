# Explain And Runtime Canon

Last reviewed: 2026-03-19

## Purpose

This repo owns both structured explain truth and runtime-bundle truth for Chummer6.
Other repos may transport, cache, render, or publish those payloads, but they do not get to redefine them.

## Explain canon (`D0`)

Canonical explain contracts live in:

* `Chummer.Contracts/Rulesets/RulesetExplainContracts.cs`

Canonical explain consumption remains downstream-safe because:

* `chummer6-core` verification rejects repo-local explain DTO copies in sibling repos
* `chummer6-ui` renders explain traces through `Chummer.Presentation/Explain/RulesetExplainRenderer.cs`
* the shared explain surface stays based on core-owned trace/provenance contracts instead of UI-local schema inventions

## Runtime bundle canon (`D2`)

Canonical runtime-bundle contracts and receipts live in:

* `Chummer.Contracts/Session/SessionContracts.cs`
* `Chummer.Contracts/Session/SessionLifecycleContracts.cs`
* `Chummer.Contracts/Session/SessionRuntimeBundleIssueContracts.cs`
* `Chummer.Application/Session/OwnerScopedSessionService.cs`

Canonical runtime-bundle consumption remains coherent because:

* `chummer6-hub` issues, stores, versions, and restores runtime-bundle heads through verifier-backed registry flows
* `chummer6-mobile` consumes package-owned runtime-bundle metadata and keeps offline cache, resume, and replay lineage regression-guarded
* core verification rejects sibling source ownership of `SessionRuntimeBundle` and related engine DTO families

## Verification path

The executable proof for this canon spans:

* `bash scripts/ai/verify.sh` in `chummer6-core`
* `bash ../chummer.run-services/scripts/ai/run_services_verification.sh`
* `bash ../chummer.run-services/scripts/ai/run_services_smoke.sh`
* `bash ../chummer-play/scripts/ai/verify.sh`

The closure claim is: explain traces and runtime bundles are now single-owned in core and materially consumed, not redefined, everywhere else.
