# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/5

Findings:
- [high] Chummer.CoreEngine.Tests/Program.cs [correctness] wl107-directory-build-item-indirection-bypass
Guardrail scanning builds item contexts only from `ReadMsBuildDocuments(...)` (explicit `<Import Project=...>` traversal) and does not add `Directory.Build.props`/`Directory.Build.targets` documents to `documentContexts` for item evaluation.; `ReadMsBuildPropertyValues(...)` merges Directory.Build.props via `MergeMsBuildPropertyFile(...)`, but only into property values; item groups from those files are never added to `ReadMsBuildItemValues(...)` input.; As a result, item indirection like `<Compile Include="@(InjectedAppSources)" />` can bypass detection when `InjectedAppSources` is defined in auto-imported Directory.Build.props/targets rather than an explicit project import.; New regression tests cover explicit imported props (`GuardrailPathMatchingResolvesItemAndImportedIndirection`) and `Reference/HintPath`, but not auto-imported Directory.Build.* item-group sources.
Expected fix: Include auto-imported Directory.Build.props/targets in item-document evaluation (not just property expansion), and add a regression test proving Chummer.Application/browser coupling is caught when item lists are defined there.
