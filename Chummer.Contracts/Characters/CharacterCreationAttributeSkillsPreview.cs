namespace Chummer.Contracts.Characters;

public static class CharacterCreationAttributeSkillsPreviewSchemas
{
    public const string PreviewV1 = "chummer.creation-attribute-skills-proposal.v1";
    public const string SnapshotConflict = "creation-attribute-skills-proposal-snapshot-conflict";
}

/// <summary>
/// Hypothetical rules projection over two admitted original snapshots. Binding
/// identifies the original skills state, not a saved version of the proposed
/// attributes. This is deliberately not a SkillsPreview, draft or receipt and
/// cannot authorize Confirm. It neither applies choices nor enforces the outer
/// completion assistant's lock/preferences policy. That flow must bind those
/// permissions and obtain fresh combined validation before atomic application.
/// </summary>
public sealed record CharacterCreationAttributeSkillsPreview(
    string Schema,
    string AttributesSnapshotDigest,
    string SkillsSnapshotDigest,
    CharacterCreationAttributesPreview Attributes,
    CharacterCreationSkillsBinding Binding,
    IReadOnlyList<CharacterCreationSkillProjection> Skills,
    IReadOnlyList<CharacterCreationSkillGroupProjection> SkillGroups,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    CharacterCreationBudgetState ActiveSkillPointBudget,
    CharacterCreationBudgetState SkillGroupPointBudget,
    CharacterCreationBudgetState KnowledgeSkillPointBudget,
    int KnowledgePointOverflowToActive,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);
