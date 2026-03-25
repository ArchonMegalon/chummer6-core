# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/9

Findings:
- [high] Chummer.Application/Chummer.Application.csproj [contracts] contracts-sibling-hintpath-bootstrap-drift
These csproj files include `Reference` HintPaths to `..\\..\\chummer.run-services\\...\\bin\\$(Configuration)\\net10.0\\Chummer.Run.Contracts.dll` and `..\\..\\chummer-hub-registry\\...\\bin\\$(Configuration)\\net10.0\\Chummer.Hub.Registry.Contracts.dll`.; A source scan in `Chummer.Application` and `Chummer.Rulesets.*` found no direct usage of `Chummer.Run.Contracts` or `Chummer.Hub.Registry.Contracts` types, indicating coupling is not justified by local compile-time use.; Compared to `main`, these projects previously used in-repo project references and did not require sibling-repo bin outputs; this introduces new non-portable bootstrap coupling.; `scripts/ai/build.sh` hardcodes sibling-repo builds (`../chummer-hub-registry/...` and `../chummer.run-services/...`), reinforcing the drift.
Expected fix: Replace sibling-bin HintPath coupling with deterministic package/compatibility-feed consumption (or an explicit local generated compatibility tree rooted in this repo) and remove unnecessary hosted-contract references from active core ruleset/application projects.
- [medium] scripts/ai/verify.sh [tests] tests-missing-guardrail-for-sibling-contract-hintpaths
Current verify guardrails enforce `Chummer.Engine.Contracts` migration but do not fail on `..\\..\\chummer.run-services\\...` or `..\\..\\chummer-hub-registry\\...` contract HintPaths in active core projects.; Given the new bootstrap coupling risk, lack of verifier coverage allows regression to persist unnoticed.
Expected fix: Add verifier checks that block new sibling-repo hosted-contract HintPaths in active-boundary projects unless explicitly declared as compatibility exceptions.
