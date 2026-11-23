using System.Text.Json.Serialization;
using Chummer.Contracts.Content;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Identity of an owner-private observation carrier, never source provenance,
/// copy permission, campaign admission, local custody, or receipt replay authority.
/// Raw source bytes belong only in the separate bounded private carrier.
/// </summary>
public sealed record WorkspacePrivateRuleSourceClosureBinding(
    string Schema, string OwnerId, CharacterWorkspaceId WorkspaceId,
    long ContentRevision, long SavedRevision, string ContinuationDigest,
    string ClosureDigest, int ArchiveCount, long ArchiveBytes, int CampaignCount)
{
    public const string SchemaV1 = "chummer.private-rule-source-closure.v1";
    public const string SchemaV2 = "chummer.private-rule-source-closure.v2";
    // Optional for exact V1 compatibility. This identifies a separately encoded CURRENT
    // environment observation; neither its presence nor its digest grants execution.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentEnvironmentDigest { get; init; }
    public const string RuleInputsV2 = "chummer-private-rule-inputs-v2";
    public const string RuleInputsV3 = "chummer-private-rule-inputs-v3";
    public const int MaximumBytes = 160 * 1024 * 1024;
    public const int MaximumContinuationBytes = 64 * 1024 * 1024;
    public const int MaximumArchives = 4096;
    public const int MaximumCampaigns = 129;
    public override string ToString() => nameof(WorkspacePrivateRuleSourceClosureBinding);
}

/// <summary>
/// Exact current-character inputs, separate from historical receipt membership.
/// All inventories are derived again from original retained bytes on import. This is
/// an observation, never a portable mechanics permit, source-copy grant or activation.
/// </summary>
public sealed record WorkspaceCurrentRuleEnvironmentBinding(
    string Schema,
    string OwnerId,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string CharacterDigest,
    string CaptureDigest,
    string EffectivePolicyDigest,
    string ChosenRuleProfileId,
    string CompatibilityProfile,
    string ResolverSemantics,
    string SourceOrder,
    string RequiredPackageSetDigest,
    IReadOnlyList<ArtifactVersionReference> RequiredRulePacks,
    IReadOnlyList<RulePackAssetDescriptor> RequiredAssets,
    IReadOnlyList<RulePackAssetDescriptor> OrderedCustomSources)
{
    public const string SchemaV1 = "chummer.current-rule-environment.v1";
    // Absent for historical V1. New interpretations bind their exact definition;
    // private import reconstructs this value from the archived capture.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompatibilityDefinitionFingerprint { get; init; }
    // Only the canonical authority may supply these identities at export. Null means
    // an unadmitted observation. A receiver must match both to freshly issued execution.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionProfile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionProfileFingerprint { get; init; }
    // Reconstructed from the archive, including its exact original file bytes.
    // Never interpret this observation as an author-provided or Bootstrap GUID.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Chummer.Contracts.Characters.CharacterRetainedSettingsFile? RetainedSettingsFile { get; init; }
    public IReadOnlyList<string> AdmissionDiagnostics { get; init; } = [];
    public override string ToString() => nameof(WorkspaceCurrentRuleEnvironmentBinding);
}
