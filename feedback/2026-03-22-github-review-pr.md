# GitHub Codex Review

PR: https://github.com/ArchonMegalon/chummer6-core/pull/7

Findings:
- [high] Chummer.Application/Chummer.Application.csproj [contracts] nonhermetic-contract-dll-hintpath-bootstrap
Multiple projects reference sibling-repo binaries via HintPath, e.g. `..\..\chummer.run-services\...\Chummer.Run.Contracts.dll` and `..\..\chummer-hub-registry\...\Chummer.Hub.Registry.Contracts.dll`.; `scripts/ai/build.sh` prebuilds `../chummer-hub-registry/...csproj` and `../chummer.run-services/...csproj`, making core build/verification depend on external checkout topology and precompiled artifacts.
Expected fix: Replace sibling-bin HintPath coupling with the approved deterministic bootstrap path (package feed or explicit compatibility-tree policy) and remove sibling-repo prebuild dependency from core build flow.
- [high] Chummer.Tests/Compliance/MigrationComplianceTests.cs [tests] migration-compliance-assertion-stale-vs-csproj
`MigrationComplianceTests.cs` asserts csproj text contains `..\Chummer.Run.Contracts\Chummer.Run.Contracts.csproj` for SR4/SR5/SR6/Hosting project files.; Those csproj files now use `<Reference Include="Chummer.Run.Contracts">` with `HintPath`, so the asserted project-reference string no longer matches current wiring.
Expected fix: After correcting bootstrap wiring, update compliance assertions to validate the intended boundary policy so tests enforce real contract rules instead of stale string expectations.
