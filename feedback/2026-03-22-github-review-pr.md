# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/6

Findings:
- [high] WORKLIST.md [review] wl108-increment-not-executed
`WORKLIST.md` still shows `WL-111` as `queued` for executing `WL-108.1` and `WL-108.3`.; `.codex-studio/published/QUEUE.generated.yaml` still publishes the task: execute legacy cargo purification increment (`WL-108.1`, `WL-108.3`).; No files under `Plugins/` or `Chummer/Plugins/` are changed in `git diff main...HEAD`.; Legacy targets remain present on disk (`Plugins/ChummerHub.Client/*`, `Chummer/Plugins/PluginControl.cs`).
Expected fix: Implement the WL-108.1/WL-108.3 increment (migrate/freeze `Plugins/ChummerHub.Client` and retire/quarantine `Chummer/Plugins` per backlog exit criteria) and update queue state from queued to done with concrete evidence.
- [high] scripts/ai/verify.sh [tests] wl108-guardrail-gap
Current verifier checks only that documentation references `Plugins/` (`rg` against `docs/LEGACY_ROOT_SURFACE_INVENTORY.md`) rather than enforcing WL-108.1/WL-108.3 execution outcomes.; No new compliance assertions were added to prove `Chummer/Plugins` loader retirement/quarantine or to validate `Plugins/ChummerHub.Client` migration/freeze criteria in this increment.
Expected fix: Add regression/compliance guardrails that assert the implemented WL-108.1/WL-108.3 state (not just documented intent), including active-solution/runtime decoupling and loader retirement/quarantine invariants.
