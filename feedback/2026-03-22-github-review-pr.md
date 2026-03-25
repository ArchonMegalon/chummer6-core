# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/9

Findings:
- [high] Chummer.Application/Chummer.Application.csproj [contracts] contracts-external-hintpath-bootstrap-drift
Multiple csproj files now reference `Chummer.Run.Contracts` and `Chummer.Hub.Registry.Contracts` via `..\\..\\chummer.run-services\\...\\bin\\$(Configuration)\\net10.0\\*.dll` / `..\\..\\chummer-hub-registry\\...\\bin\\$(Configuration)\\net10.0\\*.dll` HintPaths.; The local design mirror’s package bootstrap rule states canonical bootstrap should be package-feed / generated compatibility-tree based and not monorepo-relative reference posture (`core.md`, package bootstrap rule section).; `scripts/ai/build.sh` hardcodes sibling-repo builds (`../chummer-hub-registry/...` and `../chummer.run-services/...`), reinforcing non-portable bootstrap coupling.
Expected fix: Replace sibling-bin HintPath contract wiring with a deterministic package/compatibility feed path (or explicit generated local compatibility tree rooted in this repo), and add verifier coverage that fails on new `..\\..\\chummer*` contract HintPaths in active core projects.
