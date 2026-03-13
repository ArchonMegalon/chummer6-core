# GitHub Codex Review

PR: local://core

Findings:
- [high] Chummer.Run.Contracts/Rulesets/RulesetPresentationContracts.cs : line 1 The WL-090 migration deletes presentation/ruleset seam contracts from `Chummer.Contracts` but the replacement files under `Chummer.Run.Contracts/Presentation/` and `Chummer.Run.Contracts/Rulesets/` are currently untracked (`??` in status). If this state is committed/published without adding those files, consumers lose canonical contract source and builds will fail. Add and include the full moved file set in version control as part of this slice.
