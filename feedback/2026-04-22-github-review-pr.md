# GitHub Codex Review

PR: local://core

Findings:
- [high] scripts/ai/verify_design_mirror.py [review] mirror-audit-row-not-bounded-to-stale-set
`scripts/ai/verify_design_mirror.py:108-122` only checks that `source_items` is non-empty, rooted under `/.codex-design/`, and free of `..`; it never checks that the queue row actually covers the stale mirror files it is supposed to bound.; Running `python3 scripts/ai/verify_design_mirror.py` on this branch returns `stale_paths=7 queue_errors=0`, with stale files including `.codex-design/product/README.md`, `VISION.md`, `USER_JOURNEYS.md`, and `RELEASE_PIPELINE.md`.; The published queue row at `.codex-studio/published/QUEUE.generated.yaml:9-12` lists only `NEXT_90_DAY_PRODUCT_ADVANCE_REGISTRY.yaml`, `NEXT_90_DAY_QUEUE_STAGING.generated.yaml`, and `horizons/karma-forge.md`, so the verifier currently accepts a row that does not describe the full stale bundle.
Expected fix: Tighten the verifier so the single `audit-task-11707` row is validated against the actual stale mirror bundle it is standing in for, or regenerate the row so its bounded scope matches the stale files the verifier detects.
- [high] Chummer.CoreEngine.Tests/Program.cs [tests] missing-stale-row-regression-tests
`Chummer.CoreEngine.Tests/Program.cs:5980-6045` only covers the clean-mirror/no-row case and the unexpected-leftover-file failure case.; There is no test that proves the intended new contract for this slice: stale mirror bytes plus exactly one bounded `audit-task-11707` queue row should be the only acceptable in-progress state.; There is also no test for the repeat-churn failure modes this change is meant to prevent, such as duplicate `audit-task-11707` rows or a malformed bounded row that the verifier should reject.
Expected fix: Add regression tests that exercise `verify_design_mirror.py` for the stale-with-single-row success path and for duplicate/misaligned queue-row failures.
