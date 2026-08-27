using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationMagicResonanceSchemas
{
    public const string AuthorityV1 = "chummer.character_creation_magic_resonance_authority.v1";
    public const string CatalogOptionV1 = "chummer.character_creation_magic_resonance_option.v1";
    public const string SnapshotV1 = "chummer.character_creation_magic_resonance_snapshot.v1";
    public const string PreviewV1 = "chummer.character_creation_magic_resonance_preview.v1";
    public const string DraftV1 = "chummer.character_creation_magic_resonance_draft.v1";
    public const string ReceiptV1 = "chummer.character_creation_magic_resonance_receipt.v1";
    public const string RuntimeV1 = "chummer.sr5.standard_priority_magic_resonance_runtime.v1";
    public const string FinalizationSourceV1 =
        "chummer.character_creation_magic_resonance_finalization_source.v1";
    public const string FinalizationContributionV1 =
        "chummer.character_creation_magic_resonance_finalization_contribution.v1";
}

public static class CharacterCreationMagicResonanceKinds
{
    public const string Mundane = "mundane";
    public const string Adept = "adept";
    public const string Magician = "magician";
    public const string MysticAdept = "mystic-adept";
    public const string AspectedMagician = "aspected-magician";
    public const string Technomancer = "technomancer";
    public const string ArtificialIntelligence = "artificial-intelligence";
    public const string Unsupported = "unsupported";

    public const string Tradition = "tradition";
    public const string Stream = "stream";
    public const string AdeptPower = "adept-power";
    public const string Spell = "spell";
    public const string ComplexForm = "complex-form";
}

public static class CharacterCreationMagicResonanceBlockers
{
    public const string AuthorityUnavailable = "creation-magic-resonance-authority-unavailable";
    public const string AttributesDraftInvalid = "creation-magic-resonance-attributes-draft-invalid";
    public const string AttributesDraftRequired = "creation-magic-resonance-attributes-draft-required";
    public const string CustomDataDrift = "creation-magic-resonance-custom-data-drift";
    public const string DraftConflict = "creation-magic-resonance-draft-conflict";
    public const string DraftDuplicate = "creation-magic-resonance-draft-duplicate";
    public const string DraftInvalid = "creation-magic-resonance-draft-invalid";
    public const string ExplicitConfirmationRequired = "creation-magic-resonance-explicit-confirmation-required";
    public const string FinalizationContributionInvalid =
        "creation-magic-resonance-finalization-contribution-invalid";
    public const string FinalizationPayloadInvalid =
        "creation-magic-resonance-finalization-payload-invalid";
    public const string GmPolicyDrift = "creation-magic-resonance-gm-policy-drift";
    public const string IdempotencyConflict = "creation-magic-resonance-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-magic-resonance-idempotency-key-invalid";
    public const string MetatypeForbidden = "creation-magic-resonance-metatype-forbidden";
    public const string MetatypePrerequisiteUnresolved = "creation-magic-resonance-metatype-prerequisite-unresolved";
    public const string OptionDisabled = "creation-magic-resonance-option-disabled";
    public const string OptionDuplicate = "creation-magic-resonance-option-duplicate";
    public const string OptionInvalid = "creation-magic-resonance-option-invalid";
    public const string OptionSemanticsUnsupported = "creation-magic-resonance-option-semantics-unsupported";
    public const string PersistenceAuthorityRequired = "creation-magic-resonance-persistence-authority-required";
    public const string PowerBudgetExceeded = "creation-magic-resonance-power-budget-exceeded";
    public const string PowerBudgetIncomplete = "creation-magic-resonance-power-budget-incomplete";
    public const string PowerBudgetUnsupported = "creation-magic-resonance-power-budget-unsupported";
    public const string PowerSelectionNotAllowed = "creation-magic-resonance-power-selection-not-allowed";
    public const string PrerequisiteDraftInvalid = "creation-magic-resonance-prerequisite-draft-invalid";
    public const string PrerequisiteDraftRequired = "creation-magic-resonance-prerequisite-draft-required";
    public const string PrerequisiteSourceDrift = "creation-magic-resonance-prerequisite-source-drift";
    public const string PreviewDigestMismatch = "creation-magic-resonance-preview-digest-mismatch";
    public const string PriorityAssignmentMismatch = "creation-magic-resonance-priority-assignment-mismatch";
    public const string ReceiptLedgerInvalid = "creation-magic-resonance-receipt-ledger-invalid";
    public const string RuntimeDrift = "creation-magic-resonance-runtime-drift";
    public const string SourceDrift = "creation-magic-resonance-source-drift";
    public const string SpellBudgetExceeded = "creation-magic-resonance-spell-budget-exceeded";
    public const string SpellBudgetIncomplete = "creation-magic-resonance-spell-budget-incomplete";
    public const string SpellSelectionNotAllowed = "creation-magic-resonance-spell-selection-not-allowed";
    public const string StreamInvalid = "creation-magic-resonance-stream-invalid";
    public const string StreamRequired = "creation-magic-resonance-stream-required";
    public const string TalentUnsupported = "creation-magic-resonance-talent-unsupported";
    public const string TraditionInvalid = "creation-magic-resonance-tradition-invalid";
    public const string TraditionRequired = "creation-magic-resonance-tradition-required";
    public const string ComplexFormBudgetExceeded = "creation-magic-resonance-complex-form-budget-exceeded";
    public const string ComplexFormBudgetIncomplete = "creation-magic-resonance-complex-form-budget-incomplete";
    public const string ComplexFormSelectionNotAllowed = "creation-magic-resonance-complex-form-selection-not-allowed";
    public const string StaleRawCharacterXmlDigest = "creation-magic-resonance-stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "creation-magic-resonance-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-magic-resonance-workspace-unavailable";
}

public sealed record CharacterCreationMagicResonanceTalentIdentity(
    string PrioritySourceId,
    string TalentSelectionId,
    string TalentValue);

public sealed record CharacterCreationMagicResonanceOptionIdentity(
    string Kind,
    string SourceId);

public sealed record CharacterCreationMagicResonanceMetatypeCapability(
    string MetatypeSourceId,
    string MetatypeName,
    string MetatypeCategory,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceNodeDigest);

public sealed record CharacterCreationMagicResonanceTalentOption(
    CharacterCreationMagicResonanceTalentIdentity Identity,
    string Rank,
    string Name,
    string Kind,
    int Magic,
    int Resonance,
    int Depth,
    int SpellBudget,
    int ComplexFormBudget,
    decimal AdeptPowerPointBudget,
    bool RequiresTradition,
    bool RequiresStream,
    bool AllowsAdeptPowers,
    bool AllowsSpells,
    bool AllowsComplexForms,
    IReadOnlyList<string> RequiredMetatypeNames,
    IReadOnlyList<string> RequiredMetatypeCategories,
    IReadOnlyList<string> ForbiddenMetatypeNames,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsEnabled)
{
    /// <summary>
    /// Canonical effective priorities.xml talent node. It is evidence only: a
    /// finalizer must parse the typed contribution and must never append this
    /// source node directly to a character document.
    /// </summary>
    public string CanonicalSourceXml { get; init; } = string.Empty;

    public string CanonicalSourceXmlDigest { get; init; } = string.Empty;
}

public sealed record CharacterCreationMagicResonanceCatalogOption(
    string Schema,
    CharacterCreationMagicResonanceOptionIdentity Identity,
    string Name,
    string Category,
    decimal PointCost,
    int MaximumLevels,
    string SourceBook,
    string Page,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsEnabled)
{
    public string DrainExpression { get; init; } = string.Empty;

    /// <summary>
    /// Canonical effective source row used to derive this option. The row is
    /// digest-bound to the authority so source/custom-data drift fails closed.
    /// </summary>
    public string CanonicalSourceXml { get; init; } = string.Empty;

    public string CanonicalSourceXmlDigest { get; init; } = string.Empty;
}

public sealed record CharacterCreationMagicResonanceAuthority(
    string Schema,
    string SettingsProfileId,
    string PrerequisiteAuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    IReadOnlyList<CharacterCreationMagicResonanceTalentOption> Talents,
    IReadOnlyList<CharacterCreationMagicResonanceMetatypeCapability> Metatypes,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Traditions,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Streams,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> AdeptPowers,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Spells,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> ComplexForms,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string AuthorityDigest)
{
    public static CharacterCreationMagicResonanceAuthority Unavailable { get; } = new(
        CharacterCreationMagicResonanceSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        [], [], [], [], [], [], [], [],
        [CharacterCreationMagicResonanceBlockers.AuthorityUnavailable],
        false,
        string.Empty);
}

public sealed record CharacterCreationAdeptPowerAllocation(
    CharacterCreationMagicResonanceOptionIdentity Identity,
    int Levels);

public sealed record CharacterCreationMagicResonanceSelections(
    CharacterCreationMagicResonanceOptionIdentity? Tradition,
    CharacterCreationMagicResonanceOptionIdentity? Stream,
    IReadOnlyList<CharacterCreationAdeptPowerAllocation> AdeptPowers,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> Spells,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> ComplexForms);

public sealed record CharacterCreationMagicResonanceBudgetState(
    string Kind,
    decimal Total,
    decimal Used,
    decimal Remaining,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCreationMagicResonanceTalentFinalizationSource(
    string Schema,
    CharacterCreationMagicResonanceTalentIdentity Identity,
    string Kind,
    int AssignedMagic,
    int AssignedResonance,
    int AssignedDepth,
    string SourceNodeDigest,
    string CanonicalSourceXml,
    string CanonicalSourceXmlDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string ProjectionDigest);

public sealed record CharacterCreationMagicResonanceOptionFinalizationSource(
    string Schema,
    CharacterCreationMagicResonanceOptionIdentity Identity,
    string Name,
    string Category,
    int Levels,
    decimal PointCost,
    string SourceBook,
    string Page,
    string SourceNodeDigest,
    string CanonicalSourceXml,
    string CanonicalSourceXmlDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string ProjectionDigest);

/// <summary>
/// Source-bound input for the later whole-character finalizer. This is not a
/// write plan: it deliberately contains no generated character GUIDs and no
/// permission to mutate the character document.
/// </summary>
public sealed record CharacterCreationMagicResonanceFinalizationContribution(
    string Schema,
    string ExpectedRawCharacterXmlDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    long AttributesDraftRevision,
    string AttributesDraftDigest,
    string AuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    CharacterCreationMagicResonanceTalentFinalizationSource Talent,
    CharacterCreationMagicResonanceOptionFinalizationSource? Tradition,
    CharacterCreationMagicResonanceOptionFinalizationSource? Stream,
    IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> AdeptPowers,
    IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> Spells,
    IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> ComplexForms,
    IReadOnlyList<string> SourceAnchorIds,
    string ContributionDigest);

public sealed record CharacterCreationMagicResonanceBinding(
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
    string AuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest);

public sealed record CharacterCreationMagicResonanceLoadRequest(
    CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationMagicResonancePreviewRequest(
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationMagicResonanceSelections Selections);

public sealed record CharacterCreationMagicResonanceConfirmRequest(
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationMagicResonanceSelections Selections,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationMagicResonanceDraft(
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
    string AuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    CharacterCreationMagicResonanceTalentIdentity TalentIdentity,
    string TalentKind,
    int AssignedMagic,
    int AssignedResonance,
    int AssignedDepth,
    CharacterCreationMagicResonanceSelections Selections,
    CharacterCreationMagicResonanceBudgetState TraditionBudget,
    CharacterCreationMagicResonanceBudgetState StreamBudget,
    CharacterCreationMagicResonanceBudgetState AdeptPowerPointBudget,
    CharacterCreationMagicResonanceBudgetState SpellBudget,
    CharacterCreationMagicResonanceBudgetState ComplexFormBudget,
    IReadOnlyList<string> SourceAnchorIds,
    bool CharacterEffectsApplied,
    string LastIdempotencyKeyDigest,
    string LastPreviewDigest,
    string LastCommandDigest,
    string DraftDigest)
{
    public CharacterCreationMagicResonanceFinalizationContribution? FinalizationContribution
    {
        get;
        init;
    }
}

public sealed record CharacterCreationMagicResonanceState(
    string Schema,
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationMagicResonanceAuthority Authority,
    CharacterCreationPrerequisiteDraft? PrerequisiteDraft,
    CharacterCreationAttributesDraft? AttributesDraft,
    CharacterCreationMagicResonanceTalentOption? SelectedTalent,
    CharacterCreationMagicResonanceDraft? PendingDraft,
    CharacterCreationMagicResonanceBudgetState TraditionBudget,
    CharacterCreationMagicResonanceBudgetState StreamBudget,
    CharacterCreationMagicResonanceBudgetState AdeptPowerPointBudget,
    CharacterCreationMagicResonanceBudgetState SpellBudget,
    CharacterCreationMagicResonanceBudgetState ComplexFormBudget,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

public sealed record CharacterCreationMagicResonancePreview(
    string Schema,
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationMagicResonanceTalentOption Talent,
    CharacterCreationMagicResonanceSelections Selections,
    CharacterCreationMagicResonanceBudgetState TraditionBudget,
    CharacterCreationMagicResonanceBudgetState StreamBudget,
    CharacterCreationMagicResonanceBudgetState AdeptPowerPointBudget,
    CharacterCreationMagicResonanceBudgetState SpellBudget,
    CharacterCreationMagicResonanceBudgetState ComplexFormBudget,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest)
{
    public CharacterCreationMagicResonanceFinalizationContribution? FinalizationContribution
    {
        get;
        init;
    }
}

public sealed record CharacterCreationMagicResonanceReceipt(
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
    string AuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    string TalentKind,
    decimal AdeptPowerPointsRemaining,
    int SpellsRemaining,
    int ComplexFormsRemaining,
    bool CharacterDocumentChanged,
    string ReceiptDigest);

public static class CharacterCreationMagicResonanceDigest
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } =
        ComputeUtf8("chummer.character_creation_magic_resonance_receipt_ledger.root.v1");

    public static string Compute<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static string ComputeUtf8(string value) =>
        Prefix + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

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

    public static string ComputeReceipt(CharacterCreationMagicResonanceReceipt receipt) =>
        Compute(receipt with { ReceiptDigest = string.Empty });

    public static bool IsValidReceipt(
        CharacterCreationMagicResonanceReceipt? receipt,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
        => receipt is not null
           && string.Equals(receipt.Schema, CharacterCreationMagicResonanceSchemas.ReceiptV1, StringComparison.Ordinal)
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
           && IsCanonical(receipt.AuthorityDigest)
           && IsCanonical(receipt.SourceInputsDigest)
           && IsCanonical(receipt.CustomDataInputsDigest)
           && IsCanonical(receipt.GmPolicyDigest)
           && IsCanonical(receipt.RuntimeDigest)
           && !string.IsNullOrWhiteSpace(receipt.TalentKind)
           && receipt.AdeptPowerPointsRemaining >= 0m
           && receipt.SpellsRemaining >= 0
           && receipt.ComplexFormsRemaining >= 0
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
                {
                    WriteCanonical(item, writer);
                }
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
                throw new InvalidOperationException("Unsupported Magic/Resonance canonical JSON kind.");
        }
    }
}
