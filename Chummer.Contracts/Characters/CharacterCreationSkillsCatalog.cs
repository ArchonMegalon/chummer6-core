namespace Chummer.Contracts.Characters;

/// <summary>
/// Source catalog shared by creation methods. Contains no Priority budget,
/// rating limit, talent unlock or permission to spend. Weapon-derived
/// specializations are bound separately from skills.xml.
/// </summary>
public sealed record CharacterCreationSkillsCatalog(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string SkillsInputsDigest,
    string WeaponsInputsDigest,
    IReadOnlyList<CharacterCreationSkillCatalogEntry> ActiveSkills,
    IReadOnlyList<CharacterCreationSkillCatalogEntry> KnowledgeSkills,
    IReadOnlyList<CharacterCreationSkillGroupCatalogEntry> SkillGroups,
    IReadOnlyList<string> SourceAnchorIds,
    string CatalogDigest)
{
    public const string SchemaV1 = "chummer.character_creation_skills_catalog.v1";
}

/// <summary>Read-only access projection, not a saved skill allocation.</summary>
public sealed record CharacterCreationKarmaSkillAccess(
    string Schema,
    string CatalogDigest,
    string TalentSourceDigest,
    string MetatypeDigest,
    string? SelectedUnlock,
    CharacterCreationMovementCapability Movement,
    IReadOnlyList<string> RequiredUnlockChoices,
    IReadOnlyList<string> AllowedActiveSkillSourceIds,
    IReadOnlyList<string> AllowedSkillGroupIds,
    IReadOnlyList<string> Blockers,
    string AccessDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_skill_access.v1";
    public const string UnlockRequired = "creation-karma-skills-unlock-required";
    public const string UnlockInvalid = "creation-karma-skills-unlock-invalid";
    public bool IsReady => Blockers.Count == 0;
}
