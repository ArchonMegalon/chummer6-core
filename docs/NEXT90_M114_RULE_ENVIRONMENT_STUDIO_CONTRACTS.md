# M114 Rule-Environment Studio Contracts

This repo owns the core contract slice for milestone `114` package `next90-m114-core-rule-environment-studio`.

## Shipped seam

`IRuleEnvironmentStudioService` now publishes one engine-owned projection that downstream UI and support surfaces can consume without rebuilding rule-environment heuristics locally.

The projection carries:

- lifecycle and promotion posture from the runtime inspector contract
- target-aware before/after diff posture from the runtime-lock registry plus diff service
- deterministic first-pin, clear, and requires-review diff states so downstream surfaces do not have to infer the environment lifecycle locally
- preview/apply warnings and confirmation requirements from the rule-profile preview contract
- explain-receipt requirements that pin engine truth, source anchors, warning coverage, and before/after delta coverage when the environment changed
- explain-receipt lifecycle and diff-state fields so support and UI flows can cite promotion posture without rejoining the surrounding projection locally
- matching no-warning runtimes collapse explain requirements to the mechanical-result plus source-anchor floor instead of forcing downstream support surfaces to invent extra receipt cargo

## Files

- `Chummer.Contracts/Content/RuleEnvironmentStudioContracts.cs`
- `Chummer.Application/Content/IRuleEnvironmentStudioService.cs`
- `Chummer.Application/Content/DefaultRuleEnvironmentStudioService.cs`
- `Chummer.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `Chummer.CoreEngine.Tests/Program.cs`
- `scripts/ai/verify.sh`

`scripts/ai/verify.sh` now runs the focused rule-environment contract lane, so the M114 receipt fails closed when the checked-in proof, engine filter, or package-local verifier drifts.

## Verification

```bash
CHUMMER_CORE_ENGINE_TEST_FILTER=rule-environment-studio dotnet run --project Chummer.CoreEngine.Tests/Chummer.CoreEngine.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
python3 tests/test_next90_m114_rule_environment_studio.py
python3 scripts/verify-next90-m114-rule-environment-studio.py --repo-root . --out .codex-studio/published/NEXT90_M114_RULE_ENVIRONMENT_STUDIO.generated.json
```
