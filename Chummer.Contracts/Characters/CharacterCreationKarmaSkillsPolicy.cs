namespace Chummer.Contracts.Characters;

/// <summary>
/// Profile-owned costs and limits for Karma spending, without Priority grants.
/// The schema distinguishes the Karma foundation from Life Modules; the historical
/// type name does not permit one method's quote to authorize the other.
/// This is not a skill catalog, a talent unlock, or permission to spend. A caller
/// must separately resolve the knowledge expression and all enabled house rules.
/// </summary>
public sealed record CharacterCreationKarmaSkillsPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    int KarmaNewActiveSkill,
    int KarmaImproveActiveSkill,
    int KarmaNewKnowledgeSkill,
    int KarmaImproveKnowledgeSkill,
    int KarmaNewSkillGroup,
    int KarmaImproveSkillGroup,
    int KarmaSpecialization,
    int KarmaKnowledgeSpecialization,
    int MaxActiveSkillRatingCreate,
    int MaxKnowledgeSkillRatingCreate,
    string KnowledgePointsExpression,
    bool UsePointsOnBrokenGroups,
    bool StrictSkillGroupsInCreateMode,
    bool SpecializationsBreakSkillGroups,
    bool AllowPointBuySpecializationsOnKarmaSkills,
    bool CompensateSkillGroupKarmaDifference,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_skills_policy.v1";
    public const string LifeModulesSchemaV1 = "chummer.character_creation_life_module_skills_policy.v1";

    // SR5 groups use the active-skill creation cap, not a separate fixed six.
    public int MaxSkillGroupRatingCreate => MaxActiveSkillRatingCreate;
}
