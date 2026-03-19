# AI Provider Transport Boundary

Last reviewed: 2026-03-19

## Purpose

This document keeps the remaining `WL-D020` compatibility lane explicit.

`chummer6-core` still contains a repo-local remote AI provider transport path even though orchestration-side third-party adapter authority belongs in `chummer6-hub`.

## Current leakage surface

The remaining compatibility-only transport surface is:

- `Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `Chummer.Infrastructure/AI/HttpAiProviderTransportClient.cs`
- `Chummer.Infrastructure/AI/EnvironmentAiProviderCredentialCatalog.cs`
- `Chummer.Infrastructure/AI/EnvironmentAiProviderTransportOptionsCatalog.cs`
- `Chummer.Application/AI/RemoteHttpAiProvider.cs`

These files still keep:

- direct remote provider transport wiring
- provider credential catalog wiring
- provider transport option wiring
- `AiMagicx` and `OneMinAi` remote execution path construction

inside the core repo.

## Active boundary reduction

The active headless-core boundary is no longer supposed to wire these classes by default.

`Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` now treats:

- `EmptyAiProviderCredentialCatalog`
- `EmptyAiProviderTransportOptionsCatalog`
- `NotImplementedAiProviderTransportClient`

as the default neutral registration set for `AddChummerHeadlessCore(...)`.

The legacy direct-provider path is only supposed to re-enter through the explicit compatibility hook:

- `AddLegacyEnvironmentAiTransportCompatibility(...)`

## Why this is still open

This does not reintroduce hub-owned adapter classes such as `BrowserActGatewayAdapter`, `MarkupGoGatewayAdapter`, or `PeekShotGatewayAdapter`.
The remaining issue is narrower: compatibility-only direct-provider code still exists in core, but it is no longer allowed to masquerade as the active default execution boundary.

## Required end state

This lane closes under option 2 from the earlier design rule:

1. remote provider transport ownership still exists in compatibility code, but
2. that code is quarantined behind an explicit compatibility hook, and
3. the active headless-core boundary no longer wires it by default

The remaining follow-through is purification, not ambiguity: delete the compatibility lane later if it stops serving real migration needs.
