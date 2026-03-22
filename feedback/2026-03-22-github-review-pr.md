# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/4

Findings:
- [high] WORKLIST.md [review] queue-publication-drift-wl105
`WORKLIST.md` states `Repo-local live queue: none` and says stale residual-scope prompts were removed on 2026-03-22.; `.codex-studio/published/QUEUE.generated.yaml` currently contains 11 prepended items, including `Publish or append runnable backlog for `Chummer.Application`, legacy browser infrastructure, and helper tooling still remain in the repo surface..` plus other previously closed prompts.
Expected fix: Regenerate/refresh `.codex-studio/published/QUEUE.generated.yaml` so it matches the closed state documented in `WORKLIST.md` (empty or only truly open runnable items), and keep queue/worklist publication outputs in sync.
