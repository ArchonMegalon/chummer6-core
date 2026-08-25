using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationSkillsSchemas
{
    public const string AuthorityV1 = "chummer.character_creation_skills_authority.v1";
    public const string SnapshotV1 = "chummer.character_creation_skills_snapshot.v1";
    public const string PreviewV1 = "chummer.character_creation_skills_preview.v1";
    public const string DraftV1 = "chummer.character_creation_skills_draft.v1";
    public const string ReceiptV1 = "chummer.character_creation_skills_receipt.v1";
}

public static class CharacterCreationSkillKinds
{
    public const string Active = "active";
    public const string Knowledge = "knowledge";
}

/// <summary>
/// Immutable SR5 Standard Priority Skills rules. Source XML proves which row was
/// selected, but it may not redefine the rulebook totals or runtime policy.
/// </summary>
public static class CharacterCreationStandardPrioritySkillsRules
{
    public const string KnowledgePointsExpression = "({INTUnaug} + {LOGUnaug}) * 2";
    public const int MaximumRatingAtCreation = 6;
    public const int BaseNativeLanguageCount = 1;
    public const int SpecializationPointCost = 1;

    public static bool IsSupportedAttribute(string? attribute) => attribute is
        "AGI" or "BOD" or "CHA" or "INT" or "LOG" or "MAG" or "REA" or "RES" or "STR" or "WIL";

    public static bool IsSupportedCategory(string? kind, string? category) => kind switch
    {
        CharacterCreationSkillKinds.Active => category is
            "Combat Active" or "Physical Active" or "Social Active" or "Magical Active"
            or "Pseudo-Magical Active" or "Resonance Active" or "Technical Active" or "Vehicle Active",
        CharacterCreationSkillKinds.Knowledge => category is
            "Academic" or "Interest" or "Language" or "Professional" or "Street",
        _ => false
    };

    public static string ComputeCatalogProjectionDigest(
        string effectiveSkillsInputsDigest,
        string sourceSkillId,
        string kind,
        string name,
        string category,
        string defaultAttribute,
        string? skillGroup,
        bool isExotic,
        IReadOnlyList<CharacterCreationSkillSpecializationOption> specializations,
        IReadOnlyList<string> sourceAnchorIds,
        bool canDefault = false,
        bool ignoresSourceDisabled = false) => CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-skill-catalog-projection.v1",
            EffectiveSkillsInputsDigest = effectiveSkillsInputsDigest,
            SourceSkillId = sourceSkillId,
            Kind = kind,
            Name = name,
            Category = category,
            DefaultAttribute = defaultAttribute,
            SkillGroup = skillGroup,
            IsExotic = isExotic,
            CanDefault = canDefault,
            IgnoresSourceDisabled = ignoresSourceDisabled,
            Specializations = specializations.ToArray(),
            SourceAnchorIds = sourceAnchorIds.ToArray()
        });

    public static bool TryGetBudget(string? rank, out int activePoints, out int groupPoints)
    {
        (activePoints, groupPoints) = rank switch
        {
            "A" => (46, 10),
            "B" => (36, 5),
            "C" => (28, 2),
            "D" => (22, 0),
            "E" => (18, 0),
            _ => (-1, -1)
        };
        return activePoints >= 0;
    }

    public static bool HasExactBudgetTable(
        IReadOnlyList<CharacterCreationPriorityOptionProjection>? options)
    {
        if (options is null)
            return false;
        CharacterCreationPriorityOptionProjection[] skills = options.Where(option =>
                string.Equals(
                    option.CategoryId,
                    CharacterCreationPriorityCategoryIds.Skills,
                    StringComparison.Ordinal))
            .ToArray();
        if (skills.Length != 5)
            return false;
        foreach (string rank in new[] { "A", "B", "C", "D", "E" })
        {
            CharacterCreationPriorityOptionProjection[] matches = skills.Where(option =>
                    string.Equals(option.Rank, rank, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1
                || !TryGetBudget(rank, out int active, out int groups)
                || matches[0].BaseActiveSkillPoints != active
                || matches[0].BaseSkillGroupPoints != groups)
            {
                return false;
            }
        }
        return true;
    }

    public static string ComputeRuntimeDigest(
        bool usePointsOnBrokenGroups,
        bool strictSkillGroupsInCreateMode,
        bool specializationsBreakSkillGroups) =>
        CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.sr5.standard-priority-skills-runtime.v1",
            ActiveSkillRatingMaximum = MaximumRatingAtCreation,
            KnowledgeSkillRatingMaximum = MaximumRatingAtCreation,
            SkillGroupRatingMaximum = MaximumRatingAtCreation,
            BaseNativeLanguageCount,
            SpecializationPointCost,
            KnowledgeOverflowConsumesActivePoints = true,
            ExoticRequiresDedicatedSpecializationAuthority = true,
            NoCharacterWriteBeforeFinalization = true,
            UsePointsOnBrokenGroups = usePointsOnBrokenGroups,
            StrictSkillGroupsInCreateMode = strictSkillGroupsInCreateMode,
            SpecializationsBreakSkillGroups = specializationsBreakSkillGroups
        });
}

public static class CharacterCreationSkillsBlockers
{
    public const string AllocationDuplicate = "creation-skills-allocation-duplicate";
    public const string AllocationInvalid = "creation-skills-allocation-invalid";
    public const string ActiveBudgetExceeded = "creation-skills-active-budget-exceeded";
    public const string AttributesDraftInvalid = "creation-skills-attributes-draft-invalid";
    public const string AttributesDraftRequired = "creation-skills-attributes-draft-required";
    public const string AuthorityUnavailable = "creation-skills-authority-unavailable";
    public const string DraftConflict = "creation-skills-draft-conflict";
    public const string DraftDuplicate = "creation-skills-draft-duplicate";
    public const string DraftInvalid = "creation-skills-draft-invalid";
    public const string ExplicitConfirmationRequired = "creation-skills-explicit-confirmation-required";
    public const string ExoticSkillUnsupported = "creation-skills-exotic-skill-unsupported";
    public const string GroupAllocationDuplicate = "creation-skills-group-allocation-duplicate";
    public const string GroupBudgetExceeded = "creation-skills-group-budget-exceeded";
    public const string GroupBroken = "creation-skills-group-broken";
    public const string GroupInvalid = "creation-skills-group-invalid";
    public const string IdempotencyConflict = "creation-skills-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-skills-idempotency-key-invalid";
    public const string KnowledgeBudgetExceeded = "creation-skills-knowledge-budget-exceeded";
    public const string KnowledgeContributionAuthorityUnsupported = "creation-skills-knowledge-contribution-authority-unsupported";
    public const string NativeLanguageInvalid = "creation-skills-native-language-invalid";
    public const string NativeLanguageLimitExceeded = "creation-skills-native-language-limit-exceeded";
    public const string NativeLanguageRequired = "creation-skills-native-language-required";
    public const string PersistenceAuthorityRequired = "creation-skills-persistence-authority-required";
    public const string PostCommitRefreshRequired = "creation-skills-post-commit-refresh-required";
    public const string PrerequisiteSourceDrift = "creation-skills-prerequisite-source-drift";
    public const string PreviewDigestMismatch = "creation-skills-preview-digest-mismatch";
    public const string RatingInvalid = "creation-skills-rating-invalid";
    public const string ReceiptLedgerInvalid = "creation-skills-receipt-ledger-invalid";
    public const string RuntimeDrift = "creation-skills-runtime-drift";
    public const string SkillsPriorityAuthorityInvalid = "creation-skills-priority-authority-invalid";
    public const string SkillsSourceDrift = "creation-skills-source-drift";
    public const string SpecializationInvalid = "creation-skills-specialization-invalid";
    public const string StaleRawCharacterXmlDigest = "creation-skills-stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "creation-skills-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-skills-workspace-unavailable";
}

public sealed record CharacterCreationSkillSpecializationOption(
    string OptionId,
    string Name,
    string SourceAnchorId);

public sealed record CharacterCreationSkillCatalogEntry(
    string SourceSkillId,
    string Kind,
    string Name,
    string Category,
    string DefaultAttribute,
    string? SkillGroup,
    bool IsExotic,
    string SourceNodeDigest,
    IReadOnlyList<CharacterCreationSkillSpecializationOption> Specializations,
    IReadOnlyList<string> SourceAnchorIds)
{
    public bool CanDefault { get; init; }

    public bool IgnoresSourceDisabled { get; init; }
}

public sealed record CharacterCreationSkillGroupCatalogEntry(
    string GroupId,
    string Name,
    IReadOnlyList<string> MemberSkillSourceIds,
    string GroupDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationKnowledgePointContribution(
    string ContributionId,
    int Points,
    string SourceCharacterXmlDigest,
    string SourceDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationSkillsAuthority(
    string Schema,
    string SettingsProfileId,
    string EffectiveSkillsInputsDigest,
    string RawProfileInputsDigest,
    string KnowledgePointsExpression,
    int MaxActiveSkillRatingCreate,
    int MaxKnowledgeSkillRatingCreate,
    int MaxSkillGroupRatingCreate,
    int BaseNativeLanguageLimit,
    bool UsePointsOnBrokenGroups,
    bool StrictSkillGroupsInCreateMode,
    bool SpecializationsBreakSkillGroups,
    IReadOnlyList<CharacterCreationSkillCatalogEntry> ActiveSkills,
    IReadOnlyList<CharacterCreationSkillCatalogEntry> KnowledgeSkills,
    IReadOnlyList<CharacterCreationSkillGroupCatalogEntry> SkillGroups,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationSkillsAuthority Unavailable { get; } = new(
        CharacterCreationSkillsSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        0,
        false,
        false,
        false,
        [],
        [],
        [],
        [],
        [],
        [CharacterCreationSkillsBlockers.AuthorityUnavailable],
        false,
        string.Empty,
        string.Empty);
}

public sealed record CharacterCreationSkillAllocation(
    string SourceSkillId,
    string Kind,
    int? Rating,
    string? SpecializationOptionId,
    bool IsNativeLanguage);

public sealed record CharacterCreationSkillGroupAllocation(
    string GroupId,
    int Rating);

public sealed record CharacterCreationSkillProjection(
    string SourceSkillId,
    string Kind,
    string Name,
    string Category,
    string DefaultAttribute,
    string? SkillGroup,
    int? Rating,
    int? EffectiveRating,
    int PointCost,
    string? SpecializationOptionId,
    string? SpecializationName,
    bool IsNativeLanguage,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationSkillGroupProjection(
    string GroupId,
    string Name,
    int Rating,
    int PointCost,
    IReadOnlyList<string> MemberSkillSourceIds,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationSkillsBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string PrerequisiteAuthorityDigest,
    long AttributesDraftRevision,
    string AttributesDraftDigest,
    string SkillsAuthorityDigest,
    string RuntimeDigest,
    string ContributionInputsDigest);

public sealed record CharacterCreationSkillsLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationSkillsPreviewRequest(
    CharacterCreationSkillsBinding Binding,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations);

public sealed record CharacterCreationSkillsConfirmRequest(
    CharacterCreationSkillsBinding Binding,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationSkillsDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string PrerequisiteAuthorityDigest,
    long AttributesDraftRevision,
    string AttributesDraftDigest,
    string SkillsAuthorityDigest,
    string RuntimeDigest,
    string ContributionInputsDigest,
    int ActivePointTotal,
    int ActivePointUsed,
    int SkillGroupPointTotal,
    int SkillGroupPointUsed,
    int KnowledgePointTotal,
    int KnowledgePointUsed,
    int KnowledgePointOverflowToActive,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    IReadOnlyList<CharacterCreationSkillProjection> Skills,
    IReadOnlyList<CharacterCreationSkillGroupProjection> SkillGroups,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    IReadOnlyList<string> SourceAnchorIds,
    bool CharacterEffectsApplied,
    string LastIdempotencyKeyDigest,
    string LastPreviewDigest,
    string LastCommandDigest,
    string DraftDigest);

public sealed record CharacterCreationSkillsState(
    string Schema,
    CharacterCreationSkillsBinding Binding,
    CharacterCreationSkillsAuthority Authority,
    CharacterCreationPrerequisiteDraft? PrerequisiteDraft,
    CharacterCreationAttributesDraft? AttributesDraft,
    CharacterCreationSkillsDraft? PendingDraft,
    IReadOnlyList<CharacterCreationSkillProjection> Skills,
    IReadOnlyList<CharacterCreationSkillGroupProjection> SkillGroups,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    CharacterCreationBudgetState ActiveSkillPointBudget,
    CharacterCreationBudgetState SkillGroupPointBudget,
    CharacterCreationBudgetState KnowledgeSkillPointBudget,
    int IntuitionUnaugmented,
    int LogicUnaugmented,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest)
{
    public int SelectedActiveSkillPoints { get; init; }
    public int SelectedSkillGroupPoints { get; init; }
}

public sealed record CharacterCreationSkillsPreview(
    string Schema,
    CharacterCreationSkillsBinding Binding,
    IReadOnlyList<CharacterCreationSkillProjection> Skills,
    IReadOnlyList<CharacterCreationSkillGroupProjection> SkillGroups,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    CharacterCreationBudgetState ActiveSkillPointBudget,
    CharacterCreationBudgetState SkillGroupPointBudget,
    CharacterCreationBudgetState KnowledgeSkillPointBudget,
    int KnowledgePointOverflowToActive,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationSkillsReceipt(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    long DraftRevision,
    string DraftDigest,
    string PreviewDigest,
    string IdempotencyKeyDigest,
    string CommandDigest,
    string PreviousReceiptDigest,
    string SkillsAuthorityDigest,
    string RuntimeDigest,
    int ActivePointsRemaining,
    int SkillGroupPointsRemaining,
    int KnowledgePointsRemaining,
    int KnowledgePointOverflowToActive,
    bool CharacterDocumentChanged,
    string ReceiptDigest);

/// <summary>
/// Shared canonical JSON digest used at every Skills trust boundary. Callers must sort
/// semantic sets before hashing; the Core service does this before constructing packets.
/// </summary>
public static class CharacterCreationSkillsDigest
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } =
        ComputeUtf8("chummer.character_creation_skills_receipt_ledger.root.v1");

    public static string Compute<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(root, writer);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static string ComputeUtf8(string value) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    public static bool IsCanonical(string? value) =>
        value is { Length: 71 }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).ToArray().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool EqualsFixedTime(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static string ComputeReceipt(CharacterCreationSkillsReceipt receipt) =>
        Compute(receipt with { ReceiptDigest = string.Empty });

    public static bool IsValidReceipt(
        CharacterCreationSkillsReceipt? receipt,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
        => receipt is not null
           && string.Equals(receipt.Schema, CharacterCreationSkillsSchemas.ReceiptV1, StringComparison.Ordinal)
           && receipt.WorkspaceId == workspaceId
           && receipt.PreviousContentRevision > 0
           && receipt.PreviousContentRevision < long.MaxValue
           && receipt.ContentRevision == receipt.PreviousContentRevision + 1
           && receipt.ContentRevision <= persistedContentRevision
           && receipt.SavedRevision == receipt.ContentRevision
           && receipt.DraftRevision > 0
           && IsCanonical(receipt.DraftDigest)
           && IsCanonical(receipt.PreviewDigest)
           && IsCanonical(receipt.IdempotencyKeyDigest)
           && IsCanonical(receipt.CommandDigest)
           && IsCanonical(receipt.PreviousReceiptDigest)
           && IsCanonical(receipt.SkillsAuthorityDigest)
           && IsCanonical(receipt.RuntimeDigest)
           && receipt.ActivePointsRemaining >= 0
           && receipt.SkillGroupPointsRemaining >= 0
           && receipt.KnowledgePointsRemaining >= 0
           && receipt.KnowledgePointOverflowToActive >= 0
           && !receipt.CharacterDocumentChanged
           && IsCanonical(receipt.ReceiptDigest)
           && EqualsFixedTime(receipt.ReceiptDigest, ComputeReceipt(receipt));

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
                throw new InvalidOperationException("Unsupported Skills canonical JSON value kind.");
        }
    }
}
