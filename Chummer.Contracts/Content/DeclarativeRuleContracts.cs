namespace Chummer.Contracts.Content;

public static class DeclarativeRuleOverrideModes
{
    public const string SetConstant = "set-constant";
    public const string OverrideThreshold = "override-threshold";
    public const string EnableOption = "enable-option";
    public const string DisableOption = "disable-option";
    public const string ReplaceCreationProfile = "replace-creation-profile";
    public const string ModifyCap = "modify-cap";
    public const string RenameLabel = "rename-label";
}

public static class DeclarativeRuleTargetKinds
{
    public const string Constant = "constant";
    public const string Threshold = "threshold";
    public const string Option = "option";
    public const string CreationProfile = "creation-profile";
    public const string Cap = "cap";
    public const string Label = "label";
}

public static class DeclarativeRuleValueKinds
{
    public const string Boolean = "boolean";
    public const string Number = "number";
    public const string String = "string";
    public const string Json = "json";
}

public static class DeclarativeRuleConditionOperators
{
    public const string EqualTo = "equals";
    public const string NotEquals = "not-equals";
    public const string GreaterThanOrEqual = "greater-than-or-equal";
    public const string LessThanOrEqual = "less-than-or-equal";
    public const string Contains = "contains";
}

public sealed record DeclarativeRuleTarget(
    string TargetKind,
    string TargetId,
    string? Path = null,
    string? Scope = null);

public sealed record DeclarativeRuleCondition(
    string Field,
    string Operator,
    string Value,
    string? ValueKind = null);

public sealed record DeclarativeRuleValue(
    string ValueKind,
    string Value);

public sealed record DeclarativeRuleOverride(
    string OverrideId,
    string Mode,
    DeclarativeRuleTarget Target,
    DeclarativeRuleValue Value,
    IReadOnlyList<DeclarativeRuleCondition> Conditions,
    IReadOnlyList<string> CapabilityIds);

public sealed record DeclarativeRuleOverrideSet(
    string SetId,
    string RulesetId,
    IReadOnlyList<DeclarativeRuleOverride> Overrides);
