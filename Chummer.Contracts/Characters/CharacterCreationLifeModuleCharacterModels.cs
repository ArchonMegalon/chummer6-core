using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

// These are retained reconstruction inputs, not permission to apply a caller's
// projection. The commit path must freshly resolve the actual source data.
public sealed record CharacterCreationFoundationSequenceModuleOwner(string OccurrenceId, string SourceId, string QualityId, int KarmaCost);
public sealed record CharacterCreationFoundationSequencePushDisposition(string OccurrenceId, string EffectId,
    string InstructionDigest, string Group, string Disposition);
public sealed record CharacterCreationFoundationSequenceWritePlan(string Semantics, CharacterWorkspaceId WorkspaceId,
    long DraftRevision, string DraftDigest, string RawCharacterXmlDigest, string CompilationDigest, string SourceDigest,
    string SourceContextDigest, string CatalogRawXmlDigest, IReadOnlyList<CharacterCreationFoundationSequenceModuleOwner> ModuleOwners,
    IReadOnlyList<string> QualityXml, IReadOnlyList<string> ImprovementXml,
    IReadOnlyList<CharacterCreationFoundationSequencePushDisposition> PushDispositions,
    int DependentQualityCount, int GroupQualityCount, decimal ModuleKarmaCost, string PlanDigest)
{
    [JsonIgnore]
    public CharacterCreationLifeModuleEffectWriteSummary Summary => new(ModuleOwners.Count, ImprovementXml.Count,
        DependentQualityCount, GroupQualityCount, ModuleKarmaCost, PlanDigest);
}

public sealed record CharacterCreationLifeModuleMetatypeWritePlan(string Semantics, string DraftDigest,
    string EffectPlanDigest, CharacterCreationMetatypeOptionProjection Metatype,
    CharacterCreationMetatypeSourceContextAuthority SourceContext,
    IReadOnlyList<CharacterCreationTalentQualitySource> Sources, IReadOnlyList<string> QualityXml,
    IReadOnlyList<string> ImprovementXml, IReadOnlyList<string> GearXml, IReadOnlyList<string> Flags, string PlanDigest)
{
    [JsonIgnore]
    public CharacterCreationLifeModuleMetatypeWriteSummary Summary => new(Metatype.OptionId, Metatype.Label,
        Metatype.KarmaCost, QualityXml.Count, ImprovementXml.Count, GearXml.Count, PlanDigest);
}

public sealed record CharacterCreationLifeModuleTalentWritePlan(string Semantics,
    string EffectPlanDigest, string MetatypePlanDigest, CharacterCreationLifeModuleTalentCatalog Catalog,
    CharacterCreationLifeModuleTalentSelection Selection, CharacterCreationKarmaTalentOption Talent,
    CharacterCreationTalentQualitySource? Source, IReadOnlyList<string> QualityXml,
    IReadOnlyList<string> ImprovementXml, IReadOnlyList<string> GearXml, IReadOnlyList<string> Flags, string PlanDigest)
{
    [JsonIgnore]
    public CharacterCreationLifeModuleTalentWriteSummary Summary => new(Selection, Talent.Name, Talent.KarmaCost,
        Talent.EnabledAttribute, QualityXml.Count, ImprovementXml.Count, GearXml.Count, Flags,
        Catalog.SourceAnchorIds.Concat(Talent.SourceAnchorIds).Concat(Source?.SourceAnchorIds ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), PlanDigest);
}

public sealed record CharacterCreationLifeModuleCharacterParts(CharacterCreationFoundationSequenceWritePlan Effects,
    CharacterCreationLifeModuleMetatypeWritePlan Racial, CharacterCreationLifeModuleTalentWritePlan Talent,
    CharacterCreationLifeModuleAttributeQuote Attributes, CharacterCreationSkillsCatalog SkillsCatalog,
    CharacterCreationLifeModuleSkillsQuote Skills, CharacterCreationLifeModuleResourcesQuote Resources,
    CharacterCreationGearAuthority GearAuthority, CharacterCreationLifeModuleGearQuote Gear,
    CharacterCreationLifestylesAuthority LifestylesAuthority, CharacterCreationLifeModuleLifestylesQuote Lifestyles,
    CharacterCreationLifeModuleContactsQuote Contacts, CharacterCreationLifeModuleMagicCatalog MagicCatalog,
    CharacterCreationLifeModuleMagicQuote Magic, CharacterCreationLifeModuleFinalizationBudgetQuote Finances);

/// <summary>Historical reconstruction only. It never replaces fresh source
/// admission at the owner-bound commit boundary.</summary>
public sealed record CharacterCreationLifeModuleFinalizationAuthority(string RawCharacterXml,
    CharacterCreationFoundationFinalizationPreviewRequest Request,
    CharacterCreationFoundationFinalizationPreview Preview,
    CharacterCreationLifeModuleCharacterParts Parts);
