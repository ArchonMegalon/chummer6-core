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
    public const string RuleInputsV2 = "chummer-private-rule-inputs-v2";
    public const string RuleInputsV3 = "chummer-private-rule-inputs-v3";
    public const int MaximumBytes = 160 * 1024 * 1024;
    public const int MaximumContinuationBytes = 64 * 1024 * 1024;
    public const int MaximumArchives = 4096;
    public const int MaximumCampaigns = 129;
    public override string ToString() => nameof(WorkspacePrivateRuleSourceClosureBinding);
}
