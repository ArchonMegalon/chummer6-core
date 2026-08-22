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
    IReadOnlyList<string> SourceAnchorIds);

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
    public string RawMetatypesXmlDigest { get; init; } = string.Empty;

    public string EffectiveMetatypesInputsDigest { get; init; } = string.Empty;

    public int? MaxNumberMaxAttributesCreate { get; init; }

    public int? KarmaAttribute { get; init; }

    public bool? AlternateMetatypeAttributeKarma { get; init; }

    public bool? ReverseAttributePriorityOrder { get; init; }

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
    IReadOnlyList<string> SourceAnchorIds);

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
}

public sealed record CharacterCreationPrerequisiteConfirmRequest(
    CharacterCreationPrerequisiteBinding Binding,
    IReadOnlyDictionary<string, string> PriorityAssignments,
    string PreviewDigest,
    bool ExplicitlyConfirmed)
{
    public string? HeritageSelectionId { get; init; }

    public string? TalentSelectionId { get; init; }
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
