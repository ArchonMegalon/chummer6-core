namespace Chummer.Contracts.Characters;

/// <summary>Explicit purchase after the complete module sequence; null is not Mundane.</summary>
public sealed record CharacterCreationLifeModuleTalentSelection(string OptionId, string? SkillUnlock = null);

/// <summary>Life Modules policy authority, reusing the source-quality option payload
/// shared with Karma. This is not a Karma foundation or Priority grant catalog.</summary>
public sealed record CharacterCreationLifeModuleTalentCatalog(
    string Schema, string SettingsProfileId, string RawProfileInputsDigest,
    string SourceInputsDigest, int KarmaQuality,
    IReadOnlyList<CharacterCreationKarmaTalentOption> Options,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SkillUnlockChoices,
    IReadOnlyList<string> SourceAnchorIds, string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_life_module_talent_catalog.v1";
    public const string SelectionRequired = "creation-life-module-talent-selection-required";
    public const string SelectionInvalid = "creation-life-module-talent-selection-invalid";
    public const string QualityConflict = "creation-life-module-talent-quality-conflict";
    public const string SkillUnlockRequired = "creation-life-module-talent-skill-unlock-required";
}

public sealed record CharacterCreationLifeModuleTalentWriteSummary(
    CharacterCreationLifeModuleTalentSelection Selection, string Name, int KarmaCost,
    string? EnabledAttribute, int QualityCount, int ImprovementCount, int GearCount,
    IReadOnlyList<string> Flags, IReadOnlyList<string> SourceAnchorIds, string PlanDigest);
