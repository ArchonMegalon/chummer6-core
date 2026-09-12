using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.BuildGhost;

public static class BuildGhostRuleAuthorityContractVersions
{
    public const string RequestV1 = "chummer.avatar-rule-authority/v1";
}

public static class BuildGhostRuleAuthorityStatuses
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";
    public const string Stale = "stale";
    public const string Unavailable = "unavailable";
}

public static class BuildGhostRuleIntentIds
{
    public const string DeriveInitiative = "rules.derive.initiative";
}

public sealed record BuildGhostRuleAuthorityBinding(
    string RulesetId,
    string ProfileId,
    string RuntimeFingerprint,
    string SourceDigest,
    string SourcebookFingerprint,
    string CustomDataFingerprint,
    string GmPolicyFingerprint,
    long WorkspaceRevision);

public sealed record BuildGhostRuleAuthorityRequest(
    string ContractVersion,
    string OwnerId,
    string WorkspaceId,
    string SubjectId,
    string IntentId,
    int IntentVersion,
    string CapabilityId,
    string InvocationKind,
    IReadOnlyList<RulesetCapabilityArgument> Arguments,
    BuildGhostRuleAuthorityBinding ExpectedBinding);

public sealed record BuildGhostActiveRuleAuthority(
    BuildGhostRuleAuthorityBinding Binding,
    IReadOnlyList<string> ActiveSourcebookIds);

public sealed record BuildGhostRuleIntentArgumentDescriptor(
    string Name,
    string ValueKind,
    long? MinimumIntegerValue = null,
    long? MaximumIntegerValue = null);

public sealed record BuildGhostRuleSourceReferenceDescriptor(
    string AnchorId,
    string SourcePackRef,
    string Locale,
    int ExpectedPage,
    string ExpectedSectionHint,
    string ReferenceId);

public sealed record BuildGhostRuleIntentDescriptor(
    string IntentId,
    int IntentVersion,
    string RulesetId,
    string ProfileId,
    string CapabilityId,
    string InvocationKind,
    string RuleId,
    IReadOnlyList<BuildGhostRuleIntentArgumentDescriptor> Arguments,
    IReadOnlyList<BuildGhostRuleSourceReferenceDescriptor> SourceReferences);

public sealed record BuildGhostRuleAuthorityResolution(
    string Status,
    string? IntentId,
    string? RuleId,
    BuildGhostRuleAuthorityBinding? ActiveBinding,
    RulesetCapabilityValue? Output,
    RulesetExplainTrace? Explain,
    IReadOnlyList<SourceAnchor> SourceAnchors,
    string? UncertaintyReason);
