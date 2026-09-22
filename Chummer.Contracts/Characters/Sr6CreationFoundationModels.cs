using Chummer.Contracts.Workspaces;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationFoundationBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string AuxiliaryStateDigest,
    string BootstrapBindingDigest,
    string AuthorityDigest);

public sealed record Sr6CreationFoundationSelection(
    string MetatypeId,
    string TalentId,
    IReadOnlyList<Sr6CreationPriorityChoice> Assignments)
{
    // Absent on earlier foundation decisions; preserve their canonical bytes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationAttributeSelection? Attributes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationSkillSelection? Skills { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationKnowledgeSelection? Knowledge { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationPointBuySelection? PointBuy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationTalentSelection? TalentAllocation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationComplexFormSelection? ComplexForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationSpellSelection? Spells { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationAdeptPowerSelection? AdeptPowers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationKarmaSelection? Karma { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationQualitySelection? Qualities { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationContactSelection? Contacts { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationGearSelection? Gear { get; init; }
}

public sealed record Sr6CreationFoundationOption(string Id, IReadOnlyList<string> AllowedRanks);

public sealed record Sr6CreationFoundationState(
    Sr6CreationFoundationBinding Binding,
    string BuildMethod,
    IReadOnlyList<Sr6CreationFoundationOption> Metatypes,
    IReadOnlyList<Sr6CreationFoundationOption> Talents,
    Sr6CreationFoundationPreview? Selection)
{
    public IReadOnlyList<Sr6CreationAttributeOption>? AttributeOptions { get; init; }
    public IReadOnlyList<Sr6CreationSkillOption>? SkillOptions { get; init; }
    public int? KnowledgePointBudget { get; init; }
    public Sr6CreationPointBuyLimits? PointBuyLimits { get; init; }
    public Sr6CreationTalentOptions? TalentOptions { get; init; }
    public IReadOnlyList<Sr6CreationComplexFormOption>? ComplexFormOptions { get; init; }
    public IReadOnlyList<Sr6CreationSpellOption>? SpellOptions { get; init; }
    public IReadOnlyList<Sr6CreationAdeptPowerOption>? AdeptPowerOptions { get; init; }
    public Sr6CreationKarmaOptions? KarmaOptions { get; init; }
    public Sr6CreationKarmaSpecializationOptions? KarmaSpecializationOptions { get; init; }
    public Sr6CreationKarmaKnowledgeOptions? KarmaKnowledgeOptions { get; init; }
    public IReadOnlyList<Sr6CreationQualityOption>? QualityOptions { get; init; }
    public Sr6CreationContactOptions? ContactOptions { get; init; }
    public IReadOnlyList<Sr6CreationGearOption>? GearOptions { get; init; }
}

/// <summary>Pending choices and budgets, not applied character values or finalization permission.</summary>
public sealed record Sr6CreationFoundationPreview(
    Sr6CreationFoundationBinding Binding,
    Sr6CreationFoundationSelection Selection,
    Sr6CreationPriorityBudget Budget,
    int BaseMagic,
    int BaseResonance,
    IReadOnlyList<string> SourceAnchorIds,
    string PreviewDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationAttributePreview? Attributes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationSkillPreview? Skills { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationKnowledgePreview? Knowledge { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationPointBuyPreview? PointBuy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationTalentPreview? TalentAllocation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationComplexFormPreview? ComplexForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationSpellPreview? Spells { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationAdeptPowerPreview? AdeptPowers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationKarmaPreview? Karma { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationQualityPreview? Qualities { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationContactPreview? Contacts { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Sr6CreationGearPreview? Gear { get; init; }
}

public sealed record Sr6CreationFoundationConfirmRequest(
    Sr6CreationFoundationBinding Binding,
    Sr6CreationFoundationSelection Selection,
    string PreviewDigest,
    Guid OperationId,
    bool ExplicitlyConfirmed);

public sealed record Sr6CreationFoundationDecision(
    string Schema,
    Sr6CreationFoundationConfirmRequest Command,
    Sr6CreationFoundationPreview Preview,
    long CommittedContentRevision,
    string DecisionDigest)
{
    public const string SchemaV1 = "chummer.sr6.creation-foundation-decision.v1";
}

public sealed record Sr6CreationFoundationCommit(Sr6CreationFoundationDecision Decision, bool Replayed);

public static class Sr6CreationFoundationBlockers
{
    public const string WorkspaceUnavailable = "sr6-creation-foundation-workspace-unavailable";
    public const string PendingDraftRequired = "sr6-creation-foundation-pending-draft-required";
    public const string HistoryInvalid = "sr6-creation-foundation-history-invalid";
    public const string StaleBinding = "sr6-creation-foundation-stale-binding";
    public const string MetatypeUnavailable = "sr6-creation-foundation-metatype-unavailable";
    public const string TalentUnavailable = "sr6-creation-foundation-talent-unavailable";
    public const string ConfirmationRequired = "sr6-creation-foundation-confirmation-required";
    public const string PersistenceUnavailable = "sr6-creation-foundation-persistence-unavailable";
    public const string OperationConflict = "sr6-creation-foundation-operation-conflict";
}
