# Legacy Root Surface Inventory

Purpose: close `WL-100` by making the remaining broad repo-body roots explicit compatibility cargo instead of letting them masquerade as active engine ownership.

## Compatibility-only roots

| Root | Current posture | Rule |
|---|---|---|
| `Chummer/` | legacy app/oracle cargo | may support parity, extraction, or verification work only; no net-new engine ownership may re-enter through this root |
| `Plugins/` | legacy plugin/interoperability cargo | compatibility-only; package and engine-contract authority must stay outside this root |
| `Chummer.Infrastructure.Browser/` | browser-facing compatibility cargo | may not become the active headless-core runtime path |
| `Chummer.Tests/` | mixed historical verification cargo | allowed only as parity/compliance coverage while active engine/runtime truth remains package-owned and verifier-backed elsewhere |

## Active boundary rule

The active engine boundary remains:

- `Chummer.Contracts`
- `Chummer.Application`
- `Chummer.Core`
- `Chummer.Infrastructure`
- `Chummer.Rulesets.*`

Everything else must stay either explicit compatibility cargo or a test/oracle surface.

## Exit statement

Core purification is now materially closed for design purposes: the remaining broad roots are explicit, compatibility-only, and verifier-guarded instead of silently competing with the active engine boundary.
