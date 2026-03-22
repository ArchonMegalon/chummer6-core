# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/8

Findings:
- [high] Chummer.Application/Chummer.Application.csproj [contracts] nonhermetic-contract-bootstrap-hintpath
Diff replaces `<ProjectReference Include="..\Chummer.Run.Contracts\Chummer.Run.Contracts.csproj" />` with `<Reference ...><HintPath>..\..\chummer.run-services\...\Chummer.Run.Contracts.dll</HintPath></Reference>` and similar `chummer-hub-registry` DLL hint path.; This hard-couples core build to external checkout layout/artifacts instead of deterministic package feed or explicit in-repo compatibility tree bootstrap.
Expected fix: Remove sibling-repo DLL HintPath references and restore deterministic bootstrap through approved package/compatibility-tree inputs.
- [high] scripts/ai/build.sh [correctness] build-script-cross-repo-prebuild-coupling
Diff adds mandatory prebuilds of `../chummer-hub-registry/...csproj` and `../chummer.run-services/...csproj` before building core.; Core build/verify now depends on external repositories being present and buildable, violating repo-hermetic build expectations.
Expected fix: Remove mandatory external-repo prebuild steps; build core from its own deterministic dependency inputs.
- [high] Chummer.Tests/Compliance/MigrationComplianceTests.cs [tests] compliance-test-masks-boundary-drift
New helpers `TryResolveCanonicalOwnerPath`/`TryResolveCanonicalOwnerDirectory` search upward for sibling repos (`../chummer.run-services`, `../chummer-hub-registry`) and are wired into `FindPath`, `FindDirectory`, and `PathExistsInCandidateRoots`.; These changes allow tests to pass via cross-repo fallback even when local repo boundaries/bootstrap are broken, reducing regression detection for contract ownership and hermeticity.
Expected fix: Keep compliance tests repo-local/hermetic and assert the intended local bootstrap/contract boundary directly without sibling-repo fallback resolution.
