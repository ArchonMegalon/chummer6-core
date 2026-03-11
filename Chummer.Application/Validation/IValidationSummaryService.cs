using Chummer.Contracts;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;

namespace Chummer.Application.Validation;

public interface IValidationSummaryService
{
    ValidationSummary BuildSummary(
        string scopeKind,
        string scopeId,
        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics,
        string? runtimeFingerprint = null,
        IReadOnlyDictionary<string, ExplainHookReference>? explainHooksByCode = null);
}
