using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationPrerequisiteSchemas
{
    public const string AuthorityV1 = "chummer.character_creation_prerequisite_authority.v1";
    public const string SnapshotV1 = "chummer.character_creation_prerequisite_snapshot.v1";
    public const string PreviewV1 = "chummer.character_creation_prerequisite_preview.v1";
    public const string DraftV1 = "chummer.character_creation_prerequisite_draft.v1";
    public const string TalentGrantPlanV1 =
        "chummer.character_creation_talent_grant_plan.v1";
}

public static class CharacterCreationTalentSkillGrantTypes
{
    public const int MaximumPromptSlots = 3;
    public const string Active = "active";
    public const string Default = "default";
    public const string Magic = "magic";
    public const string Resonance = "resonance";
    public const string Matrix = "matrix";
    public const string Specific = "specific";
    public const string XPath = "xpath";
    public const string Choices = "choices";
    public const string Grouped = "grouped";
    public const string GroupChoiceAliasCompatibility =
        "legacy-chummer5:9ead69da989c6582a86d4f2342f6ef275b5bf760:"
        + "skillgrouptype:choices-to-grouped";
    public const string PinnedXPathPredicate =
        "not(attribute = 'RES' or attribute = 'DEP') and "
        + "(not(category = 'Magical Active') or skillgroup = '' or not(skillgroup))";

    public static string NormalizeLegacySelectorType(
        string rawSelectorType,
        string selectorTypeSource)
    {
        string normalized = rawSelectorType.ToLowerInvariant();
        if (normalized == Choices
            && selectorTypeSource != CharacterCreationTalentGrantSelectorTypeSources.SkillGroupType)
        {
            return Default;
        }
        return normalized is Active or Default or Magic or Resonance or Matrix or Specific
            or XPath or Choices or Grouped
            ? normalized
            : Default;
    }
}

public static class CharacterCreationTalentGrantImprovementKinds
{
    public const string SkillBase = "SkillBase";
    public const string SkillGroupBase = "SkillGroupBase";
}

public static class CharacterCreationTalentGrantSelectorTypeSources
{
    public const string Missing = "missing";
    public const string SkillType = "skilltype";
    public const string SkillGroupType = "skillgrouptype";
}

public static class CharacterCreationPriorityChildKinds
{
    public const string Metatype = "metatype";
    public const string Metavariant = "metavariant";
    public const string Talent = "talent";
}

public static class CharacterCreationPriorityCategoryIds
{
    public const string Heritage = "heritage";
    public const string Talent = "talent";
    public const string Attributes = "attributes";
    public const string Skills = "skills";
    public const string Resources = "resources";

    public static IReadOnlyList<string> Ordered { get; } =
        [Heritage, Talent, Attributes, Skills, Resources];
}

public static class CharacterCreationPrerequisiteBlockers
{
    public const string AuthorityUnavailable = "creation-prerequisite-authority-unavailable";
    public const string BuildMethodUnsupported = "creation-prerequisite-build-method-unsupported";
    public const string BuildMethodMismatch = "creation-prerequisite-build-method-mismatch";
    public const string CharacterAlreadyCreated = "creation-prerequisite-character-already-created";
    public const string CharacterDocumentInvalid = "creation-prerequisite-character-document-invalid";
    public const string CreationKarmaAuthorityRequired = "creation-karma-authority-required";
    public const string CreationKarmaExceeded = "creation-prerequisite-creation-karma-exceeded";
    public const string CustomDataDrift = "creation-prerequisite-custom-data-drift";
    public const string DraftConflict = "creation-prerequisite-draft-conflict";
    public const string DraftDuplicate = "creation-prerequisite-draft-duplicate";
    public const string DraftInvalid = "creation-prerequisite-draft-invalid";
    public const string DependentAttributesDraftExists = "creation-prerequisite-dependent-attributes-draft-exists";
    public const string ExplicitConfirmationRequired = "creation-prerequisite-explicit-confirmation-required";
    public const string LegacyPriorityStateRequiresImport = "creation-prerequisite-legacy-priority-state-requires-import";
    public const string AttributeSettingsInvalid = "creation-prerequisite-attribute-settings-invalid";
    public const string HeritageSelectionIncomplete = "creation-prerequisite-heritage-selection-incomplete";
    public const string HeritageSelectionInvalid = "creation-prerequisite-heritage-selection-invalid";
    public const string HeritageSelectionUnsupported = "creation-prerequisite-heritage-selection-unsupported";
    public const string MetatypeCustomDataUnsupported = "creation-prerequisite-metatype-custom-data-unsupported";
    public const string MetatypeOverlayUnsupported = "creation-prerequisite-metatype-overlay-unsupported";
    public const string MetatypeSourceDrift = "creation-prerequisite-metatype-source-drift";
    public const string PersistenceAuthorityRequired = "creation-prerequisite-persistence-authority-required";
    public const string PreviewDigestMismatch = "creation-prerequisite-preview-digest-mismatch";
    public const string PrioritiesSourceDrift = "creation-prerequisite-priorities-source-drift";
    public const string PriorityArrayInvalid = "creation-prerequisite-priority-array-invalid";
    public const string PriorityCategoriesInvalid = "creation-prerequisite-priority-categories-invalid";
    public const string PriorityCustomDataUnsupported = "creation-prerequisite-priority-custom-data-unsupported";
    public const string PriorityRowsInvalid = "creation-prerequisite-priority-rows-invalid";
    public const string PriorityTableInvalid = "creation-prerequisite-priority-table-invalid";
    public const string PriorityWeightsInvalid = "creation-prerequisite-priority-weights-invalid";
    public const string RulesetSr5Required = "creation-prerequisite-ruleset-sr5-required";
    public const string SelectionIncomplete = "creation-prerequisite-selection-incomplete";
    public const string SelectionInvalid = "creation-prerequisite-selection-invalid";
    public const string SettingsProfileDrift = "creation-prerequisite-settings-profile-drift";
    public const string SumToTenMismatch = "creation-prerequisite-sum-to-ten-mismatch";
    public const string SumToTenTargetInvalid = "creation-prerequisite-sum-to-ten-target-invalid";
    public const string TalentSelectionIncomplete = "creation-prerequisite-talent-selection-incomplete";
    public const string TalentSelectionInvalid = "creation-prerequisite-talent-selection-invalid";
    public const string TalentSelectionUnsupported = "creation-prerequisite-talent-selection-unsupported";
    public const string TalentActiveSkillSelectionIncomplete =
        "creation-prerequisite-talent-active-skill-selection-incomplete";
    public const string TalentActiveSkillSelectionInvalid =
        "creation-prerequisite-talent-active-skill-selection-invalid";
    public const string TalentSkillGroupSelectionIncomplete =
        "creation-prerequisite-talent-skill-group-selection-incomplete";
    public const string TalentSkillGroupSelectionInvalid =
        "creation-prerequisite-talent-skill-group-selection-invalid";
    public const string TalentSkillGrantAuthorityUnsupported =
        "creation-prerequisite-talent-skill-grant-authority-unsupported";
    public const string TalentExoticSkillSpecializationRequired =
        "creation-prerequisite-talent-exotic-skill-specialization-required";
    public const string SkillCustomDataUnsupported =
        "creation-prerequisite-skill-custom-data-unsupported";
    public const string SkillsSourceDrift = "creation-prerequisite-skills-source-drift";
    public const string StaleRawCharacterXmlDigest = "creation-prerequisite-stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "creation-prerequisite-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-prerequisite-workspace-unavailable";
}

public sealed record CharacterCreationPriorityHeritageOptionProjection(
    string SelectionId,
    string Kind,
    string MetatypeSourceId,
    string? MetavariantSourceId,
    string MetatypeName,
    string? MetavariantName,
    int SpecialAttributePoints,
    int KarmaCost,
    bool HalvesNormalAttributePoints,
    IReadOnlyList<CharacterCreationMetatypeAttributeProjection> Attributes,
    string PriorityChildNodeDigest,
    string MetatypeSourceNodeDigest,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationPriorityTalentOptionProjection(
    string SelectionId,
    string Name,
    string Value,
    int SpecialAttributePoints,
    int? Magic,
    int? Resonance,
    int? Depth,
    IReadOnlyList<string> GrantedQualities,
    string PriorityChildNodeDigest,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds)
{
    public CharacterCreationTalentActiveSkillGrantProjection? ActiveSkillGrant
        { get; init; }

    public CharacterCreationTalentSkillGroupGrantProjection? SkillGroupGrant
        { get; init; }

    public string RawTalentNode { get; init; } = string.Empty;
}

public sealed record CharacterCreationTalentActiveSkillChoiceProjection(
    string SelectionId,
    string SourceId,
    string CanonicalName,
    string Category,
    string? SkillGroup,
    string SourceNodeDigest,
    string SkillsSourceDigest,
    IReadOnlyList<string> SourceAnchorIds)
{
    public string Attribute { get; init; } = string.Empty;

    public bool IsExotic { get; init; }

    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record CharacterCreationTalentSkillGroupChoiceProjection(
    string SelectionId,
    string CanonicalName,
    IReadOnlyList<string> MemberSkillSourceIds,
    string GroupDigest,
    string SkillsSourceDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationTalentActiveSkillGrantProjection(
    int Quantity,
    int BaseRating,
    string SkillType,
    IReadOnlyList<CharacterCreationTalentActiveSkillChoiceProjection> Options,
    string GrantDigest,
    bool IsSupported,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds)
{
    public string ImprovementKind { get; init; } = string.Empty;

    public string RawSelectorType { get; init; } = string.Empty;

    public string SelectorTypeSource { get; init; } = string.Empty;

    public string RawSelectorTypeQuery { get; init; } = string.Empty;

    public string SkillTypeQuery { get; init; } = string.Empty;

    public IReadOnlyList<string> SpecificSkillChoiceNames { get; init; } = [];
}

public sealed record CharacterCreationTalentSkillGroupGrantProjection(
    int Quantity,
    int BaseRating,
    string SkillGroupType,
    IReadOnlyList<CharacterCreationTalentSkillGroupChoiceProjection> Options,
    string GrantDigest,
    bool IsSupported,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds)
{
    public string ImprovementKind { get; init; } = string.Empty;

    public string RawSelectorType { get; init; } = string.Empty;

    public string SelectorTypeSource { get; init; } = string.Empty;

    public string RawSelectorTypeQuery { get; init; } = string.Empty;

    public string CompatibilityMarker { get; init; } = string.Empty;

    public IReadOnlyList<string> RequestedGroupNames { get; init; } = [];
}

public static class CharacterCreationTalentGrantAuthorityDigest
{
    public static string ComputeActiveGrant(
        int quantity,
        int baseRating,
        string skillType,
        string improvementKind,
        string rawSelectorType,
        string selectorTypeSource,
        string rawSelectorTypeQuery,
        string skillsSourceDigest,
        IEnumerable<string> orderedOptionIds) =>
        RawDigest($"active\0{quantity}\0{baseRating}\0{skillType}\0{improvementKind}\0"
                  + $"{rawSelectorType}\0{selectorTypeSource}\0{rawSelectorTypeQuery}\0"
                  + $"{skillsSourceDigest}\0"
                  + string.Join('\0', orderedOptionIds));

    public static string ComputeSkillGroupGrant(
        int quantity,
        int baseRating,
        string skillGroupType,
        string improvementKind,
        string rawSelectorType,
        string selectorTypeSource,
        string rawSelectorTypeQuery,
        string skillsSourceDigest,
        IEnumerable<string> orderedOptionIds) =>
        RawDigest($"group\0{quantity}\0{baseRating}\0{skillGroupType}\0{improvementKind}\0"
                  + $"{rawSelectorType}\0{selectorTypeSource}\0{rawSelectorTypeQuery}\0"
                  + $"{skillsSourceDigest}\0"
                  + string.Join('\0', orderedOptionIds));

    public static string ComputeSkillGroup(
        string skillsSourceDigest,
        string canonicalName,
        IEnumerable<string> memberSkillSourceIds) =>
        RawDigest($"skill-group\0{skillsSourceDigest}\0{canonicalName}\0"
                  + string.Join('\0', memberSkillSourceIds.OrderBy(
                      sourceId => sourceId,
                      StringComparer.Ordinal)));

    public static string ComputeSkillGroupSelectionId(string groupDigest) =>
        CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(groupDigest)
            ? $"skill-group:{groupDigest[7..]}"
            : string.Empty;

    public static string ComputeRawTalentNode(string rawTalentNode) =>
        RawDigest(rawTalentNode);

    private static string RawDigest(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record CharacterCreationPriorityRankWeight(
    string Rank,
    int Value,
    IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// The Attribute value is the raw priorities.xml grant, not the effective pool;
/// Heritage/metatype authority must still resolve halveattributepoints.
/// </summary>
public sealed record CharacterCreationPriorityOptionProjection(
    string CategoryId,
    string CategoryName,
    string Rank,
    string SourceId,
    string Label,
    int SumToTenValue,
    int? BaseNormalAttributePoints,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds)
{
    /// <summary>
    /// Exact values from the selected Skills priority row. They remain null for every
    /// non-Skills category so a client cannot infer a budget from the display label.
    /// </summary>
    public int? BaseActiveSkillPoints { get; init; }

    public int? BaseSkillGroupPoints { get; init; }

    public IReadOnlyList<CharacterCreationPriorityHeritageOptionProjection> HeritageOptions
        { get; init; } = [];

    public IReadOnlyList<CharacterCreationPriorityTalentOptionProjection> TalentOptions
        { get; init; } = [];
}

public sealed record CharacterCreationPrerequisiteAuthority(
    string Schema,
    string SettingsProfileId,
    string BuildMethod,
    int? CreationKarmaTotal,
    IReadOnlyList<string> PriorityArray,
    string PriorityTable,
    int? SumToTenTarget,
    IReadOnlyList<CharacterCreationPriorityRankWeight> RankWeights,
    IReadOnlyList<CharacterCreationPriorityOptionProjection> Options,
    string RawProfileInputsDigest,
    string RawPrioritiesXmlDigest,
    string EffectivePrioritiesInputsDigest,
    string SelectedPriorityCustomDataInputsDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string AuthorityDigest)
{
    public string SelectedCustomDataInputsDigest { get; init; } = string.Empty;

    public string RawMetatypesXmlDigest { get; init; } = string.Empty;

    public string EffectiveMetatypesInputsDigest { get; init; } = string.Empty;

    public int? MaxNumberMaxAttributesCreate { get; init; }

    public int? KarmaAttribute { get; init; }

    public bool? AlternateMetatypeAttributeKarma { get; init; }

    public bool? ReverseAttributePriorityOrder { get; init; }

    public string RawSkillsXmlDigest { get; init; } = string.Empty;

    public string EffectiveSkillsInputsDigest { get; init; } = string.Empty;

    public static CharacterCreationPrerequisiteAuthority Unavailable { get; } = new(
        Schema: CharacterCreationPrerequisiteSchemas.AuthorityV1,
        SettingsProfileId: string.Empty,
        BuildMethod: string.Empty,
        CreationKarmaTotal: null,
        PriorityArray: [],
        PriorityTable: string.Empty,
        SumToTenTarget: null,
        RankWeights: [],
        Options: [],
        RawProfileInputsDigest: string.Empty,
        RawPrioritiesXmlDigest: string.Empty,
        EffectivePrioritiesInputsDigest: string.Empty,
        SelectedPriorityCustomDataInputsDigest: string.Empty,
        SourceAnchorIds: [],
        Blockers: [CharacterCreationPrerequisiteBlockers.AuthorityUnavailable],
        IsAuthoritative: false,
        AuthorityDigest: string.Empty);
}

public static class CharacterCreationPrerequisiteAuthorityDigest
{
    private const string Prefix = "sha256:";

    public static string Compute(CharacterCreationPrerequisiteAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        JsonElement root = JsonSerializer.SerializeToElement(
            authority with { AuthorityDigest = string.Empty });
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static bool IsCanonical(string? digest)
    {
        if (digest is not { Length: 71 }
            || !digest.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        return digest.AsSpan(Prefix.Length).ToArray().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    public static bool EqualsFixedTime(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported prerequisite-authority JSON value kind.");
        }
    }
}

public sealed record CharacterCreationPriorityAssignment(
    int Order,
    string CategoryId,
    string Rank,
    string SourceId,
    string SourceNodeDigest,
    int SumToTenValue,
    int? BaseNormalAttributePoints,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationPriorityHeritageSelection(
    string SelectionId,
    string Kind,
    string PrioritySourceId,
    string MetatypeSourceId,
    string? MetavariantSourceId,
    string MetatypeName,
    string? MetavariantName,
    int SpecialAttributePoints,
    int KarmaCost,
    bool HalvesNormalAttributePoints,
    IReadOnlyList<CharacterCreationMetatypeAttributeProjection> Attributes,
    string PriorityChildNodeDigest,
    string MetatypeSourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationPriorityTalentSelection(
    string SelectionId,
    string PrioritySourceId,
    string Name,
    string Value,
    int SpecialAttributePoints,
    int? Magic,
    int? Resonance,
    int? Depth,
    IReadOnlyList<string> GrantedQualities,
    string PriorityChildNodeDigest,
    IReadOnlyList<string> SourceAnchorIds)
{
    public CharacterCreationTalentGrantPlanContribution? GrantPlan { get; init; }
}

public sealed record CharacterCreationTalentActiveSkillGrantPlanEntry(
    string SelectionId,
    string TargetKind,
    string SourceId,
    string CanonicalName,
    string Category,
    string? SkillGroup,
    int BaseRating,
    string ImprovementKind,
    string SourceNodeDigest,
    string SkillsSourceDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationTalentSkillGroupGrantPlanEntry(
    string SelectionId,
    string TargetKind,
    string CanonicalName,
    IReadOnlyList<string> MemberSkillSourceIds,
    int BaseRating,
    string ImprovementKind,
    string GroupDigest,
    string SkillsSourceDigest,
    IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// A deterministic contribution to the eventual single Creation write plan.
/// It carries no apply surface: incomplete Talent ledgers must still result in
/// zero character writes until finalization can compose every required ledger.
/// </summary>
public sealed record CharacterCreationTalentGrantPlanContribution(
    string Schema,
    IReadOnlyList<CharacterCreationTalentActiveSkillGrantPlanEntry> ActiveSkills,
    IReadOnlyList<CharacterCreationTalentSkillGroupGrantPlanEntry> SkillGroups,
    IReadOnlyList<string> SourceAnchorIds,
    string PlanDigest);

public sealed record CharacterCreationPrerequisiteDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    string AuthorityDigest,
    string BuildMethod,
    string SettingsProfileId,
    string PriorityTable,
    IReadOnlyList<string> PriorityArray,
    int? SumToTenTarget,
    IReadOnlyList<CharacterCreationPriorityAssignment> Assignments,
    int CreationKarmaTotal,
    int CreationKarmaUsed,
    IReadOnlyList<string> SourceAnchorIds,
    string DraftDigest)
{
    public CharacterCreationPriorityHeritageSelection? HeritageSelection { get; init; }

    public CharacterCreationPriorityTalentSelection? TalentSelection { get; init; }

    public int EffectiveNormalAttributePoints { get; init; }

    public int TotalSpecialAttributePoints { get; init; }
}

public sealed record CharacterCreationPrerequisiteBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string AuthorityDigest);

public sealed record CharacterCreationPrerequisiteLoadRequest(
    CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationPrerequisitePreviewRequest(
    CharacterCreationPrerequisiteBinding Binding,
    IReadOnlyDictionary<string, string> PriorityAssignments)
{
    public string? HeritageSelectionId { get; init; }

    public string? TalentSelectionId { get; init; }

    public IReadOnlyList<string> TalentActiveSkillSelectionIds { get; init; } = [];

    public IReadOnlyList<string> TalentSkillGroupSelectionIds { get; init; } = [];
}

public sealed record CharacterCreationPrerequisiteConfirmRequest(
    CharacterCreationPrerequisiteBinding Binding,
    IReadOnlyDictionary<string, string> PriorityAssignments,
    string PreviewDigest,
    bool ExplicitlyConfirmed)
{
    public string? HeritageSelectionId { get; init; }

    public string? TalentSelectionId { get; init; }

    public IReadOnlyList<string> TalentActiveSkillSelectionIds { get; init; } = [];

    public IReadOnlyList<string> TalentSkillGroupSelectionIds { get; init; } = [];
}

public sealed record CharacterCreationPrerequisiteState(
    string Schema,
    CharacterCreationPrerequisiteBinding Binding,
    string RulesetId,
    string BuildMethod,
    bool CharacterCreated,
    CharacterCreationPrerequisiteAuthority Authority,
    CharacterCreationBudgetState CreationKarmaBudget,
    CharacterCreationPrerequisiteDraft? PendingDraft,
    int? BaseNormalAttributePoints,
    bool RequiresMetatypeAttributeAdjustment,
    bool CanEnterAttributes,
    IReadOnlyList<string> Blockers,
    string SnapshotDigest)
{
    public int? EffectiveNormalAttributePoints { get; init; }

    public int? TotalSpecialAttributePoints { get; init; }
}

public sealed record CharacterCreationPrerequisitePreview(
    string Schema,
    CharacterCreationPrerequisiteBinding Binding,
    IReadOnlyList<CharacterCreationPriorityAssignment> Assignments,
    CharacterCreationBudgetState CreationKarmaBudget,
    int SumToTenUsed,
    int? SumToTenTarget,
    int BaseNormalAttributePoints,
    bool RequiresMetatypeAttributeAdjustment,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest)
{
    public CharacterCreationPriorityHeritageSelection? HeritageSelection { get; init; }

    public CharacterCreationPriorityTalentSelection? TalentSelection { get; init; }

    public int EffectiveNormalAttributePoints { get; init; }

    public int TotalSpecialAttributePoints { get; init; }
}

public sealed record CharacterCreationPrerequisiteReceipt(
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuthorityDigest,
    long DraftRevision,
    string DraftDigest,
    int CreationKarmaRemaining,
    int BaseNormalAttributePoints,
    bool CharacterDocumentChanged)
{
    public int EffectiveNormalAttributePoints { get; init; }

    public int TotalSpecialAttributePoints { get; init; }
}
