using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Resolves source-only values using the exact content profile selected by a saved character.
/// A null context or a false result means the caller must fail closed rather than guess.
/// </summary>
public interface ICharacterSourceDataResolver
{
    ICharacterSourceDataContext? TryCreateContext(string characterXml);
}

public interface ICharacterSourceDataContext
{
    /// <summary>
    /// Resolves one saved active-skill source GUID through the runner's exact enabled-book and
    /// ordered custom-data profile. The saved instance GUID must never be substituted here.
    /// </summary>
    bool TryResolveActiveSkillSource(
        string sourceSkillId,
        out CharacterActiveSkillSource source)
    {
        source = CharacterActiveSkillSource.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves one non-custom saved knowledge-skill source GUID through the runner's exact
    /// enabled-book and ordered custom-data profile. Custom knowledge skills intentionally
    /// carry no source GUID and must remain bound to their complete saved XML instead.
    /// </summary>
    bool TryResolveKnowledgeSkillSource(
        string sourceSkillId,
        out CharacterKnowledgeSkillSource source)
    {
        source = CharacterKnowledgeSkillSource.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the exact Career specialization costs and skill-group break policy from the
    /// settings profile selected by the saved runner. Missing or malformed profile values
    /// must leave specialization purchase unavailable.
    /// </summary>
    bool TryResolveCareerSkillSpecializationSettings(
        out CharacterCareerSkillSpecializationSettings settings,
        out string rawRuleState)
    {
        settings = new CharacterCareerSkillSpecializationSettings(0, 0, false);
        rawRuleState = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves the exact enabled-profile source row and deterministic source choices for
    /// an active or non-custom knowledge skill. Custom knowledge skills intentionally have
    /// no source GUID and use their complete saved XML plus an explicit custom selection.
    /// </summary>
    bool TryResolveCareerSkillSpecializationSource(
        string sourceSkillId,
        CharacterCareerSkillKind kind,
        out CharacterCareerSkillSpecializationSource source)
    {
        source = CharacterCareerSkillSpecializationSource.Unavailable;
        return false;
    }

    /// <summary>
    /// Projects the exact Priority/Sum-to-Ten rank authority and global creation
    /// Karma total selected by the saved character's settings profile. False
    /// means callers must not expose rank or Attribute-point choices.
    /// </summary>
    bool TryResolveCreationPrerequisiteAuthority(
        out CharacterCreationPrerequisiteAuthority authority)
    {
        authority = CharacterCreationPrerequisiteAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the exact SR5 Priority Skills catalog and creation policies from the
    /// runner's saved profile and effective Skills overlay. False means the Skills
    /// wizard must remain unavailable rather than using UI defaults.
    /// </summary>
    bool TryResolveCreationSkillsAuthority(out CharacterCreationSkillsAuthority authority)
    {
        authority = CharacterCreationSkillsAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Projects the bounded, digest-bound metatype choices proven by the saved
    /// character's source profile. False means no catalog authority exists;
    /// callers must never substitute their own source filters or defaults.
    /// </summary>
    bool TryResolveCreationMetatypeCatalog(
        out CharacterCreationMetatypeCatalogAuthority authority)
    {
        authority = CharacterCreationMetatypeCatalogAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the complete sourcebook set from the settings profile selected by
    /// the saved character and identifies the exact raw settings/profile inputs.
    /// False means creation option catalogs must remain empty rather than treating
    /// a caller-provided filter, or null, as permission to expose every source.
    /// </summary>
    bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority authority)
    {
        authority = CharacterCreationSourceProfileAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves whether the exact source profile saved by the runner enables a sourcebook.
    /// False means the profile could not prove the answer and callers must fail closed.
    /// </summary>
    bool TryIsBookEnabled(string sourceCode, out bool enabled)
    {
        enabled = false;
        return false;
    }

    bool TryResolveMaxNuyenDecimals(out int decimalPlaces)
    {
        decimalPlaces = 0;
        return false;
    }

    /// <summary>
    /// Resolves the exact KarmaJoinGroup and KarmaLeaveGroup values from the
    /// settings profile selected by the saved runner. False means callers must
    /// refuse a Career magician-group mutation rather than use defaults.
    /// </summary>
    bool TryResolveGroupMembershipKarmaCosts(out int joinCost, out int leaveCost)
    {
        joinCost = 0;
        leaveCost = 0;
        return false;
    }

    /// <summary>
    /// Resolves the exact NuyenPerBPWftP and NuyenPerBPWftM values used by the
    /// Career manual Karma dialog. False means exchange-capable editing must fail closed.
    /// </summary>
    bool TryResolveKarmaNuyenExchangeRates(
        out decimal workingForPeopleRate,
        out decimal workingForManRate)
    {
        workingForPeopleRate = 0m;
        workingForManRate = 0m;
        return false;
    }

    bool TryResolveCyberwareGradeDeviceRating(
        string gradeName,
        string improvementSource,
        out int deviceRating);

    /// <summary>
    /// Resolves the exact effective source entry, allowed grades, and Essence
    /// rounding settings needed by the bounded Cyberware Career commerce lane.
    /// False means custom/source/profile semantics were not proven and callers
    /// must refuse the mutation.
    /// </summary>
    bool TryResolveCyberwareCommerceSource(
        string sourceId,
        string name,
        string improvementSource,
        out CharacterCyberwareCommerceSource source)
    {
        source = CharacterCyberwareCommerceSource.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the bounded source metadata needed by the legacy quality-level
    /// numeric control. Callers must refuse editing when this cannot prove a
    /// unique, side-effect-free source entry.
    /// </summary>
    bool TryResolveQualityLevelSource(
        string sourceId,
        string name,
        out CharacterQualityLevelSource source)
    {
        source = CharacterQualityLevelSource.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the exact value set exposed by traditions.xml/drainattributes for
    /// the saved runner's active content-overlay profile.
    /// </summary>
    bool TryResolveTraditionDrainExpressions(out IReadOnlyList<string> expressions)
    {
        expressions = Array.Empty<string>();
        return false;
    }

    /// <summary>
    /// Resolves the exact names represented by the legacy All marker in a tradition or stream.
    /// Entity type must be Spirit or Sprite; false means the selector must stay unavailable.
    /// </summary>
    bool TryResolveSpiritCatalogNames(
        string entityType,
        out IReadOnlyList<string> names)
    {
        names = Array.Empty<string>();
        return false;
    }

    /// <summary>
    /// Resolves the exact active traditions.xml/streams.xml source row used by a non-custom
    /// saved tradition or stream, including the selected custom-data amendments.
    /// </summary>
    bool TryResolveTraditionSpiritNames(
        string entityType,
        string sourceId,
        out IReadOnlyList<string> names)
    {
        names = Array.Empty<string>();
        return false;
    }

    bool TryResolveVehicleModBonuses(
        string sourceId,
        string name,
        out CharacterVehicleModSourceBonuses bonuses);
}

public sealed record CharacterActiveSkillSource(
    string SourceSkillId,
    string Name,
    string SkillCategory,
    string SkillGroup,
    string DefaultAttribute,
    bool IsExotic,
    bool RequiresGroundMovement,
    bool RequiresSwimMovement,
    bool RequiresFlyMovement,
    string RawSourceXml)
{
    public static CharacterActiveSkillSource Unavailable { get; } = new(
        SourceSkillId: string.Empty,
        Name: string.Empty,
        SkillCategory: string.Empty,
        SkillGroup: string.Empty,
        DefaultAttribute: string.Empty,
        IsExotic: false,
        RequiresGroundMovement: false,
        RequiresSwimMovement: false,
        RequiresFlyMovement: false,
        RawSourceXml: string.Empty);
}

public sealed record CharacterKnowledgeSkillSource(
    string SourceSkillId,
    string Name,
    string SkillCategory,
    string DefaultAttribute,
    string RawSourceXml)
{
    public static CharacterKnowledgeSkillSource Unavailable { get; } = new(
        SourceSkillId: string.Empty,
        Name: string.Empty,
        SkillCategory: string.Empty,
        DefaultAttribute: string.Empty,
        RawSourceXml: string.Empty);
}

public sealed record CharacterCareerSkillSpecializationSource(
    string SourceSkillId,
    CharacterCareerSkillKind Kind,
    string Name,
    string SkillCategory,
    IReadOnlyList<CharacterCareerSkillSpecializationOption> Options,
    string RawSourceState)
{
    public static CharacterCareerSkillSpecializationSource Unavailable { get; } = new(
        SourceSkillId: string.Empty,
        Kind: CharacterCareerSkillKind.Active,
        Name: string.Empty,
        SkillCategory: string.Empty,
        Options: Array.Empty<CharacterCareerSkillSpecializationOption>(),
        RawSourceState: string.Empty);
}

public sealed record CharacterCreationSourceProfileAuthority(
    string SettingsProfileId,
    IReadOnlyList<string> EnabledSourcebooks,
    string BuildMethod,
    int? BuildPoints,
    bool LifeModuleBudgetIsExact,
    IReadOnlyList<string> BudgetBlockers,
    string RawProfileInputsDigest,
    IReadOnlyList<string> SourceAnchorIds)
{
    public static CharacterCreationSourceProfileAuthority Unavailable { get; } = new(
        SettingsProfileId: string.Empty,
        EnabledSourcebooks: Array.Empty<string>(),
        BuildMethod: string.Empty,
        BuildPoints: null,
        LifeModuleBudgetIsExact: false,
        BudgetBlockers: Array.Empty<string>(),
        RawProfileInputsDigest: string.Empty,
        SourceAnchorIds: Array.Empty<string>());
}

public sealed record CharacterQualityLevelSource(
    string SourceId,
    string Name,
    string QualityType,
    int MaximumLevel,
    bool NoLevels,
    bool UsesUnsupportedSemantics)
{
    public static CharacterQualityLevelSource Unavailable { get; } = new(
        SourceId: string.Empty,
        Name: string.Empty,
        QualityType: string.Empty,
        MaximumLevel: 0,
        NoLevels: true,
        UsesUnsupportedSemantics: true);
}

public sealed record CharacterCyberwareCommerceGradeSource(
    string Id,
    string Name,
    decimal CostMultiplier,
    decimal EssenceMultiplier,
    string Source,
    bool SpecialSemantics);

public sealed record CharacterCyberwareCommerceSource(
    string SourceId,
    string Name,
    string Source,
    string MinimumRatingExpression,
    string MaximumRatingExpression,
    string CostExpression,
    string EssenceExpression,
    string CapacityExpression,
    string ForcedGrade,
    IReadOnlyList<string> BannedGrades,
    IReadOnlyList<CharacterCyberwareCommerceGradeSource> Grades,
    int EssenceDecimals,
    bool DoNotRoundEssenceInternally,
    string EssenceModifierPostExpression,
    bool SourceEntryUsesGeneratedOrImprovementSemantics)
{
    public static CharacterCyberwareCommerceSource Unavailable { get; } = new(
        SourceId: string.Empty,
        Name: string.Empty,
        Source: string.Empty,
        MinimumRatingExpression: string.Empty,
        MaximumRatingExpression: string.Empty,
        CostExpression: string.Empty,
        EssenceExpression: string.Empty,
        CapacityExpression: string.Empty,
        ForcedGrade: string.Empty,
        BannedGrades: Array.Empty<string>(),
        Grades: Array.Empty<CharacterCyberwareCommerceGradeSource>(),
        EssenceDecimals: 0,
        DoNotRoundEssenceInternally: false,
        EssenceModifierPostExpression: string.Empty,
        SourceEntryUsesGeneratedOrImprovementSemantics: true);
}

public sealed record CharacterVehicleModSourceBonuses(
    string BodyExpression,
    string DeviceRatingExpression,
    string MatrixConditionExpression,
    string WirelessBodyExpression,
    string WirelessDeviceRatingExpression,
    string WirelessMatrixConditionExpression)
{
    public static CharacterVehicleModSourceBonuses Empty { get; } = new(
        BodyExpression: string.Empty,
        DeviceRatingExpression: string.Empty,
        MatrixConditionExpression: string.Empty,
        WirelessBodyExpression: string.Empty,
        WirelessDeviceRatingExpression: string.Empty,
        WirelessMatrixConditionExpression: string.Empty);
}
