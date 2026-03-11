# Release Dev Guide Split: core-engine

Source: 2026-03-09 Project Chummer release dev guide. This is the `chummer-core-engine` slice only.

## Your authority

`chummer-core-engine` must be the sole authority for:

- deterministic rules math, legality, and derived values
- RuntimeLock / runtime fingerprint composition
- explain/provenance/evidence DTOs
- Build Lab scoring and projections
- session reducers and replay/rebind behavior
- semantic seeds for downstream media/coach flows

## Immediate corrections

1. Treat Milestone 0 as product work, not cleanup.
2. Publish one authoritative `Chummer.Contracts` package and make consumers stop carrying source copies.
3. Move hosted AI/media/Hub/publication/transcription/approval contract families out of core and into run-services-owned contracts.
4. Unify explain contracts on the localization-safe, provenance-bearing shape described in the guide.
5. Unify the session event envelope so reducer, relay, and client cache all speak the same canonical contract.

## What to remove from core ownership

Core should not own:

- HTTP hosts
- auth/identity
- provider routing
- approval workflows
- publication/review/search service contracts
- media job orchestration
- object-store integration

If those concerns are still living in engine-owned contract trees, treat that as a release blocker.

## What to finish next

- RuntimeLock composer with deterministic runtime fingerprinting
- one canonical Explain envelope with evidence and provenance
- typed capability ABI for the identified capability families
- event-only session reducer registry with deterministic replay
- Build Lab backend as ranked projections, not chat output
- semantic seed generation for portrait/dossier/news/coach downstream flows

## Test and CI guidance

- keep engine determinism, explain, reducer, Build Lab, import/export, and content bundle tests here
- remove run-services-owned AI gateway / Hub publication / provider-router / approval / media queue tests from this repo
- add contract fixture tests for RuntimeLock, ExplainEnvelope, SessionEventEnvelope, and SessionProjection
- add repo-boundary CI checks so hosted-service concerns cannot drift back into core

## Definition of done for this repo

Core is not done until:

- hosted-service concerns are gone from engine-owned contract families
- authoritative DTOs contain no final prose
- RuntimeLock and explain fixtures are stable
- reducer replay is deterministic
- consumers can use shared contracts without repo-local source duplication
