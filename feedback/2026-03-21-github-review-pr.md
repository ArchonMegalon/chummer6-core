# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/2

Findings:
- [high] .codex-studio/published/QUEUE.generated.yaml [review] wl098-queue-reopen-drift
WORKLIST marks WL-098, WL-099, WL-089, WL-090, WL-091 (and related closure map lanes) as `done`, and states `Repo-local live queue: none`.; QUEUE.generated still prepends closed/reconciled slices (`A0.5 / WL-094`, `A0.5/A1/D1 / WL-099`, `A6-A9 / WL-093`) plus already-closed contract-move prompts (`Relocate AI/media/...`, `Move presentation-specific contract families...`).; This contradicts the slice requirement to refresh queue/worklist evidence without reopening closed implementation slices and risks reintroducing exhausted work as active queue input.
Expected fix: Regenerate `.codex-studio/published/QUEUE.generated.yaml` so it contains only genuinely open runnable items (or is empty if none), aligned with `WORKLIST.md` closure state.
