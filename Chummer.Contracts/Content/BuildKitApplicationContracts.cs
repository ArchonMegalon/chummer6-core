using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class BuildKitValidationIssueKinds
{
    public const string RulesetMismatch = "ruleset-mismatch";
    public const string RuntimeFingerprintMismatch = "runtime-fingerprint-mismatch";
    public const string MissingRulePack = "missing-rulepack";
    public const string PromptRequired = "prompt-required";
    public const string ActionRejected = "action-rejected";
}

public static class BuildKitAppliedActionOutcomes
{
    public const string Applied = "applied";
    public const string Skipped = "skipped";
    public const string Blocked = "blocked";
}

public static class BuildKitApplicationStatuses
{
    public const string Validated = "validated";
    public const string Applied = "applied";
    public const string PartiallyApplied = "partially-applied";
    public const string Blocked = "blocked";
}

public sealed record BuildKitPromptResolution(
    string PromptId,
    string? OptionId = null,
    decimal? Quantity = null,
    bool? Toggle = null);

public sealed record BuildKitValidationIssue(
    string Kind,
    string Message,
    string? PromptId = null,
    string? ActionId = null,
    string? MessageKey = null,
    IReadOnlyList<RulesetExplainParameter>? MessageParameters = null);

public sealed record BuildKitAppliedAction(
    string ActionId,
    string Kind,
    string TargetId,
    string Outcome);

public sealed record BuildKitValidationReceipt(
    string BuildKitId,
    bool IsValid,
    IReadOnlyList<BuildKitPromptResolution> ResolvedPrompts,
    IReadOnlyList<BuildKitValidationIssue> Issues);

public sealed record BuildKitApplicationReceipt(
    string Status,
    string BuildKitId,
    string WorkspaceId,
    IReadOnlyList<BuildKitPromptResolution> ResolvedPrompts,
    IReadOnlyList<BuildKitAppliedAction> AppliedActions,
    IReadOnlyList<BuildKitValidationIssue> Issues,
    CharacterVersionReference? ResultingCharacterVersion = null);

public static class BuildKitContractLocalization
{
    public static string ResolveIssueMessageKey(BuildKitValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return string.IsNullOrWhiteSpace(issue.MessageKey)
            ? issue.Message
            : issue.MessageKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveIssueMessageParameters(BuildKitValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.MessageParameters ?? [];
    }
}
