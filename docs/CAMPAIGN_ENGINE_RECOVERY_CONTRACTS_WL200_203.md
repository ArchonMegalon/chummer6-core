# Campaign Engine Recovery Contracts

## Scope

This closes the `WL-200` through `WL-203` recovery-contract slice for core.

The runtime contract now gives downstream UI, Hub, support, and mobile surfaces deterministic recovery semantics instead of generic failure copy.

## Contract Surface

`Chummer.Contracts/Session/SessionOperationRecoveryContracts.cs` defines:

- `SessionOperationFailureClasses` for rule execution failures, fallback transitions, throughput budget breaches, timeouts, cancellations, interruptions, provider unavailability, and validation blocks.
- `SessionOperationRetryClasses` for immediate retry, retry-after-backoff, fallback, continue-current-state, resume, and do-not-retry.
- `SessionOperationSafeActionIds` for user-actionable next steps.
- `SessionOperationRecoveryContract` for user-message keys, safe actions, retry posture, fallback posture, and observability correlation.
- `SessionOperationThroughputGuardrail` for max batch size, target p95 latency, hard timeout, allocation ceiling, and metric seam.
- `SessionLongRunningOperationState` for recoverable, retryable, cancellable, resumable, and rollback-safe async operation state.

## Release Gate Wiring

`.codex-design/product/METRICS_AND_SLOS.yaml` now includes release-critical metrics for:

- campaign failure recovery contract coverage
- engine retry-class coverage
- campaign-engine batch budget regressions
- ambiguous long-running operation states

`.codex-design/product/GOLDEN_JOURNEY_RELEASE_GATES.yaml` now includes failure-mode scripts for:

- build/explain rule execution failure with fallback and retry copy
- campaign batch budget exceeded with retry-after-backoff and continue-current-state copy
- recoverable campaign snapshot cancellation with resume and cancel copy

## Verification

`Chummer.CoreEngine.Tests/Program.cs` verifies:

- rule-execution failures expose `rule_execution_failure`
- safest rule failure action is `use_fallback`
- recovery contracts preserve observability correlation
- campaign batch guardrails expose `campaign_engine_batch` and the duration metric seam
- recoverable cancellation exposes stable cancelled state, resume retry class, rollback availability, and resume token posture

## Downstream Rule

UI and Hub surfaces should render from these contracts instead of branching on raw exception text.

If a promoted operation cannot classify its failure into one of these contracts, that is a release-blocking recovery gap rather than a copywriting issue.
