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
    /// Resolves the explicit public-awareness policy from the exact saved profile.
    /// False means missing, malformed, ambiguous or stale authority; consumers
    /// must not treat the returned placeholder as a default setting.
    /// </summary>
    bool TryResolveCareerReputationSettings(
        out CharacterCareerReputationSettings settings,
        out string rawRuleState)
    {
        settings = new CharacterCareerReputationSettings(false);
        rawRuleState = string.Empty;
        return false;
    }

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
    /// Resolves attribute costs and limits without requiring a Priority table.
    /// False means the selected profile is missing, malformed or has drifted;
    /// callers must not substitute a multiplier or enable special attributes.
    /// </summary>
    bool TryResolveCreationAttributePolicy(out CharacterCreationAttributePolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Pending Karma talent options from qualities, never Priority grants.</summary>
    bool TryResolveCreationKarmaTalents(out CharacterCreationKarmaTalentCatalog? catalog)
    {
        catalog = null;
        return false;
    }

    /// <summary>Effective Karma magic options and prices, without Priority grants or character mutation.</summary>
    bool TryResolveCreationKarmaMagicCatalog(out CharacterCreationKarmaMagicCatalog? catalog)
    {
        catalog = null;
        return false;
    }

    /// <summary>
    /// Resolves complete racial and purchased-talent source payloads, including
    /// nested gear, against the pending Karma runner's exact active profile.
    /// This does not apply effects or authorize finalization. An unavailable or
    /// ambiguous grant rejects the whole result, never a partial character.
    /// </summary>
    bool TryResolveCreationKarmaGrantSources(string metatypeOptionId, string talentOptionId,
        out IReadOnlyList<CharacterCreationTalentQualitySource> metatypeQualities,
        out CharacterCreationTalentQualitySource? talentQuality)
    {
        metatypeQualities = [];
        talentQuality = null;
        return false;
    }

    /// <summary>Complete racial grants for the selected Life Modules metatype.
    /// Does not choose a talent, apply a module, or authorize finalization.</summary>
    bool TryResolveCreationLifeModuleMetatypeSources(string metatypeOptionId,
        out IReadOnlyList<CharacterCreationTalentQualitySource> metatypeQualities)
    {
        metatypeQualities = [];
        return false;
    }

    /// <summary>Purchased talents for an exact Life Modules profile, without Priority grants.</summary>
    bool TryResolveCreationLifeModuleTalents(out CharacterCreationLifeModuleTalentCatalog? catalog)
    {
        catalog = null;
        return false;
    }

    /// <summary>Complete selected talent payload, including any source-owned free gear.
    /// Mundane succeeds with a null source; failure must not be interpreted as Mundane.</summary>
    bool TryResolveCreationLifeModuleTalentSource(string optionId, out CharacterCreationTalentQualitySource? source)
    {
        source = null;
        return false;
    }

    /// <summary>Profile costs and limits for Karma skills; never substitutes Priority points.</summary>
    bool TryResolveCreationKarmaSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Life Modules costs, caps and knowledge expression; not a Karma-foundation authority.</summary>
    bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Profile-bound Karma funding only, without Priority grants or later improvements.</summary>
    bool TryResolveCreationKarmaResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
    {
        policy = null;
        return false;
    }

    bool TryResolveCreationLifeModuleResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Costs for the cumulative pending Life Modules quality graph,
    /// not a catalog granting permission to purchase Karma-method qualities.</summary>
    bool TryResolveCreationLifeModuleQualitiesPolicy(out CharacterCreationKarmaQualitiesPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>
    /// Profile-owned contact allowance and group rate for Priority, Sum-to-Ten
    /// Karma and Life Modules. The historical DTO name does not combine their Karma budgets.
    /// </summary>
    bool TryResolveCreationContactsPolicy(out CharacterCreationKarmaContactsPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Source-owned free contact expression and group-contact Karma rate for Karma builds.</summary>
    bool TryResolveCreationKarmaContactsPolicy(out CharacterCreationKarmaContactsPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>
    /// Profile-owned completion limits shared by Priority, Sum-to-Ten and Karma.
    /// The historical policy DTO name does not authorize mixing creation budgets.
    /// </summary>
    bool TryResolveCreationCarryoverPolicy(out CharacterCreationKarmaCarryoverPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>Exact Karma completion carryover limits, not additional creation purchasing power.</summary>
    bool TryResolveCreationKarmaCarryoverPolicy(out CharacterCreationKarmaCarryoverPolicy? policy)
    {
        policy = null;
        return false;
    }

    /// <summary>
    /// Resolves legacy's default Street starting-cash source only when no lifestyle
    /// exists. Never substitutes Street for an existing or malformed lifestyle.
    /// This does not create the lifestyle or apply its other source effects.
    /// </summary>
    bool TryResolveCreationDefaultStartingNuyen(out CharacterCreationStartingNuyenSource? source)
    {
        source = null;
        return false;
    }

    /// <summary>Karma-specific entry point; keeps the build-method boundary.</summary>
    bool TryResolveCreationKarmaDefaultStartingNuyen(out CharacterCreationStartingNuyenSource? source)
    {
        source = null;
        return false;
    }

    /// <summary>
    /// Resolves the exact selected lifestyle source's starting-cash terms. This
    /// does not prove that a pending build purchased or selected that lifestyle;
    /// callers must bind it to the saved lifestyle quote before finalization.
    /// </summary>
    bool TryResolveCreationKarmaStartingNuyen(Guid lifestyleSourceId, out CharacterCreationStartingNuyenSource? source)
    {
        source = null;
        return false;
    }

    /// <summary>Source-owned ordinary Karma qualities, distinct from purchased talents.</summary>
    bool TryResolveCreationKarmaQualities(out CharacterCreationKarmaQualitiesCatalog? catalog)
    {
        catalog = null;
        return false;
    }

    /// <summary>Effective skill/spec/group identities without creation-method spending policy.</summary>
    bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
    {
        catalog = null;
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
    /// Resolves the SR5 Priority creation-quality catalog, profile limits and exact
    /// source/runtime binding. Catalog entries with unresolved requirements or follow-up
    /// prompts remain visible but disabled; callers must never invent a selectable fallback.
    /// </summary>
    bool TryResolveCreationQualitiesAuthority(out CharacterCreationQualitiesAuthority authority)
    {
        authority = CharacterCreationQualitiesAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the exact enabled SR5 lifestyle and lifestyle-quality catalog for the
    /// saved creation profile. False means the Contacts/Lifestyles step must expose no
    /// mutation route; callers may not substitute labels, prices, or unfiltered XML rows.
    /// </summary>
    bool TryResolveCreationLifestylesAuthority(
        out CharacterCreationLifestylesAuthority authority)
    {
        authority = CharacterCreationLifestylesAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Resolves the exact SR5 Priority/Sum-to-Ten Resources grants and the active
    /// profile's creation Karma-to-nuyen, maximum-investment, carryover, and
    /// availability settings. False means the Resources step must fail closed.
    /// </summary>
    bool TryResolveCreationResourcesAuthority(
        out CharacterCreationResourcesAuthority authority)
    {
        authority = CharacterCreationResourcesAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Projects the exact enabled SR5 gear catalog for the creation Resources commerce
    /// lane. Unsupported expressions, prompts, parent requirements, or side effects
    /// remain visible but disabled and must never receive guessed prices or legality.
    /// </summary>
    bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority authority)
    {
        authority = CharacterCreationGearAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Projects the exact enabled SR5 drug grades, Foundation/Block/Enhancer
    /// components and every source-defined effect level from the runner's
    /// effective drugcomponents.xml overlay. Unknown effect semantics or an
    /// ambiguous source identity make the complete catalog unavailable.
    /// </summary>
    bool TryResolveCustomDrugCatalog(out CharacterCustomDrugCatalogAuthority authority)
    {
        authority = CharacterCustomDrugCatalogAuthority.Unavailable;
        return false;
    }

    /// <summary>
    /// Projects the effective SR5 vehicles.xml catalog through the runner's exact
    /// enabled-book, settings, overlay, and ordered custom-data profile. Rows whose
    /// formulas, prerequisites, or complex factory children cannot be persisted losslessly
    /// remain present with Unsupported posture; callers must never make them selectable.
    /// </summary>
    bool TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog)
    {
        catalog = new CharacterVehicleWorkshopCatalog(
            new CharacterVehicleWorkshopSourceBinding(
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, false, 0m, false, 0m, false),
            [], [], [], string.Empty);
        return false;
    }

    /// <summary>
    /// Resolves the exact SR5 Standard Priority Talent, tradition, stream, adept-power,
    /// spell, and complex-form catalogs from the saved profile and effective overlays.
    /// Missing or unsupported source semantics must leave the step unavailable.
    /// </summary>
    bool TryResolveCreationMagicResonanceAuthority(
        out CharacterCreationMagicResonanceAuthority authority)
    {
        authority = CharacterCreationMagicResonanceAuthority.Unavailable;
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
    /// Effective skills/qualities inputs for Life Modules, captured under the
    /// saved profile. This internal engine input is not a client/export payload
    /// and never grants permission to apply an incomplete module sequence.
    /// </summary>
    bool TryResolveCreationFoundationEffectSources(out CharacterCreationFoundationEffectSources? sources)
    {
        sources = null;
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
    /// <summary>
    /// Citation metadata is additive so the legacy six-field source record and
    /// its deconstruction remain stable for existing editor callers.
    /// </summary>
    public string SourceBook { get; init; } = string.Empty;

    public int? SourcePage { get; init; }

    /// <summary>
    /// Digest of the selected effective quality node, when citation metadata is structurally
    /// complete. This identifies the resolved node only; it is not a source-pack or engine
    /// authority assertion.
    /// </summary>
    public string SourceNodeDigest { get; init; } = string.Empty;

    /// <summary>
    /// True only when the resolver supplied one usable source book and one positive
    /// page. This is structural metadata, not proof of source/workspace/engine
    /// authority; legacy source resolution may still succeed without it.
    /// </summary>
    public bool SourceCitationResolved => !string.IsNullOrWhiteSpace(SourceBook)
        && SourcePage is > 0;

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
