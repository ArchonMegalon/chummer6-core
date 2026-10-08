using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationFinalizationSchemas
{
    public const string StateV1 = "chummer.sr5.creation-finalization.state.v1";
    public const string ReviewV1 = "chummer.sr5.creation-finalization.review.v1";
    public const string PlanV1 = "chummer.sr5.creation-finalization.plan.v1";
    public const string ReceiptV1 = "chummer.sr5.creation-finalization.receipt.v1";
}

public static class CharacterCreationFinalizationOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Corrupt = "corrupt";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationFinalizationBlockers
{
    public const string WorkspaceUnavailable = "creation-finalization-workspace-unavailable";
    public const string RulesetSr5Required = "creation-finalization-ruleset-sr5-required";
    public const string CharacterAlreadyCreated = "creation-finalization-character-already-created";
    public const string BuildMethodUnsupported = "creation-finalization-build-method-unsupported";
    public const string BuildMethodNotReady = "creation-finalization-build-method-not-ready";
    public const string BootstrapBindingRequired = "creation-finalization-bootstrap-binding-required";
    public const string PrerequisiteDraftRequired = "creation-finalization-prerequisite-draft-required";
    public const string AttributesDraftRequired = "creation-finalization-attributes-draft-required";
    public const string SkillsDraftRequired = "creation-finalization-skills-draft-required";
    public const string MagicResonanceDraftRequired = "creation-finalization-magic-resonance-draft-required";
    public const string QualitiesDraftRequired = "creation-finalization-qualities-draft-required";
    public const string ResourcesDraftRequired = "creation-finalization-resources-draft-required";
    public const string GearDraftRequired = "creation-finalization-gear-draft-required";
    public const string FoundationDraftNotApplicable = "creation-finalization-foundation-draft-not-applicable";
    public const string LifeModuleDecisionHistoryNotApplicable =
        "creation-finalization-life-module-decision-history-not-applicable";
    public const string DraftAuthorityInvalid = "creation-finalization-draft-authority-invalid";
    public const string QualityEffectsNotProjectable = "creation-finalization-quality-effects-not-projectable";
    public const string GearEffectsNotProjectable = "creation-finalization-gear-effects-not-projectable";
    public const string AwakenedEffectsNotProjectable = "creation-finalization-awakened-effects-not-projectable";
    public const string TalentGrantsNotProjectable = "creation-finalization-talent-grants-not-projectable";
    public const string GlobalKarmaExceeded = "creation-finalization-global-karma-exceeded";
    public const string CarryoverPolicyUnavailable = "creation-finalization-carryover-policy-unavailable";
    public const string StartingCashUnavailable = "creation-finalization-starting-cash-unavailable";
    public const string StartingCashChoiceRequired = "creation-finalization-starting-cash-choice-required";
    public const string StartingCashChoiceInvalid = "creation-finalization-starting-cash-choice-invalid";
    public const string StaleWorkspaceRevision = "creation-finalization-stale-workspace-revision";
    public const string StaleRawCharacterXmlDigest = "creation-finalization-stale-character-digest";
    public const string StaleAuxiliaryStateDigest = "creation-finalization-stale-auxiliary-digest";
    public const string AuthorityDigestMismatch = "creation-finalization-authority-digest-mismatch";
    public const string PreviewDigestMismatch = "creation-finalization-preview-digest-mismatch";
    public const string PlanDigestMismatch = "creation-finalization-plan-digest-mismatch";
    public const string ExplicitConfirmationRequired = "creation-finalization-explicit-confirmation-required";
    public const string IdempotencyKeyInvalid = "creation-finalization-idempotency-key-invalid";
    public const string IdempotencyConflict = "creation-finalization-idempotency-conflict";
    public const string AtomicPersistenceRequired = "creation-finalization-atomic-persistence-required";
    public const string AtomicPersistenceRejected = "creation-finalization-atomic-persistence-rejected";
    public const string PostCommitReopenRequired = "creation-finalization-post-commit-reopen-required";
}

public static class CharacterCreationFinalizationDeltaKinds
{
    public const string Build = "build";
    public const string Metatype = "metatype";
    public const string Attribute = "attribute";
    public const string Skill = "skill";
    public const string SkillGroup = "skill-group";
    public const string Quality = "quality";
    public const string MagicResonance = "magic-resonance";
    public const string Gear = "gear";
    public const string Resources = "resources";
    public const string Lifecycle = "lifecycle";
}

public sealed record CharacterCreationFinalizationBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string BuildMethod,
    string AuthorityDigest);

public sealed record CharacterCreationFinalizationLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationFinalizationReviewRequest(
    CharacterCreationFinalizationBinding Binding)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
}

public sealed record CharacterCreationFinalizationConfirmRequest(
    CharacterCreationFinalizationBinding Binding,
    string PreviewDigest,
    string PlanDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
}

/// <summary>User-entered dice result bound to the exact displayed source, never an amount override.</summary>
public sealed record CharacterCreationStartingCashChoice(string SourceAuthorityDigest, int DiceTotal);

/// <summary>Core-captured source inputs retained for atomic transition validation, not a client write grant.</summary>
public sealed record CharacterCreationFinalizationStartingCash(
    CharacterCreationStartingNuyenSource Source,
    CharacterCreationLifestylesAuthority Lifestyles,
    CharacterCreationStartingCashChoice Choice,
    string AuthorityDigest);

public sealed record CharacterCreationFinalizationReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationFinalizationStep(
    string StepId,
    bool IsRequired,
    bool IsComplete,
    string? DraftDigest,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationFinalizationState(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    bool CharacterCreated,
    IReadOnlyList<CharacterCreationFinalizationStep> Steps,
    IReadOnlyList<string> Blockers,
    bool CanReview,
    CharacterCreationFinalizationReceipt? LastReceipt,
    string SnapshotDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingNuyenSource? StartingCashSource { get; init; }
}

public sealed record CharacterCreationFinalizationDelta(
    int Order,
    string DeltaId,
    string Kind,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    decimal KarmaCost,
    decimal NuyenCost,
    IReadOnlyList<string> SourceAnchorIds)
{
    /// <summary>The canonical name from the admitted draft/source, bound into
    /// the review digest. Not an identity or a client-side catalog lookup.
    /// Omitted on historical deltas to preserve their serialized bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }
}

/// <summary>
/// One sealed, whole-build write plan.  Every ordered delta is reviewable; the
/// replacement XML is deliberately not exposed to clients.  It is regenerated
/// from the persisted typed drafts immediately before the atomic CAS.
/// </summary>
public sealed record CharacterCreationFinalizationPlan(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    IReadOnlyList<CharacterCreationFinalizationDelta> OrderedDeltas,
    decimal KarmaRemaining,
    decimal StartingNuyen,
    decimal NuyenRemaining,
    IReadOnlyList<string> SourceAnchorIds,
    string ExpectedResultRawCharacterXmlDigest,
    string PlanDigest)
{
    /// <summary>Exact source-profile policy used by this review, never a client override.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaCarryoverPolicy? CarryoverPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartingCashAuthorityDigest { get; init; }
}

public sealed record CharacterCreationFinalizationReview(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    CharacterCreationFinalizationPlan? Plan,
    IReadOnlyList<CharacterCreationFinalizationDelta> OrderedDeltas,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationFinalizationReceipt(
    string Schema,
    string ReceiptId,
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKeyDigest,
    string CommandDigest,
    long PreviousContentRevision,
    long ContentRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string PreviousRawCharacterXmlDigest,
    string RawCharacterXmlDigest,
    string PreviousAuxiliaryStateDigest,
    string AuthorityDigest,
    string PreviewDigest,
    string PlanDigest,
    string BuildMethod,
    bool CharacterCreated,
    bool RequiresFreshCareerReopen,
    string PreviousReceiptDigest,
    string ReceiptDigest)
{
    // Absent on historical receipts. Omitting null preserves their original
    // digest and allows read-only recovery without retroactive balance changes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaCarryoverPolicy? CarryoverPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartingCashAuthorityDigest { get; init; }
}

public sealed record CharacterCreationFinalizationReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationFinalizationReceipt Receipt);

public sealed record CharacterCreationFinalizationResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationFinalizationOutcomes.Available
        or CharacterCreationFinalizationOutcomes.Applied
        or CharacterCreationFinalizationOutcomes.Replayed;
}

public static class CharacterCreationFinalizationDigest
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } =
        ComputeUtf8("chummer.sr5.creation-finalization.receipt-ledger.root.v1");

    public static string Compute<T>(T value)
    {
        // Finalized runners replay these hashes during ordinary roster reads.
        // Keep the exact v1 bytes, but avoid retaining a second complete JSON
        // graph and output buffer on every archived authority/receipt check.
        using JsonDocument document = JsonSerializer.SerializeToDocument(value);
        using var output = new CanonicalHashBufferWriter();
        using (var writer = new Utf8JsonWriter(output))
        {
            WriteCanonical(document.RootElement, writer);
        }
        return Prefix + output.GetDigest();
    }

    public static string ComputeUtf8(string value) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string ComputeIdempotencyKeyDigest(string idempotencyKey) =>
        ComputeUtf8(idempotencyKey.Trim());

    public static string ComputeReceiptDigest(CharacterCreationFinalizationReceipt receipt) =>
        Compute(receipt with { ReceiptDigest = string.Empty });

    public static bool IsCanonical(string? value) => value is { Length: 71 }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).ToString().All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool EqualsFixedTime(string? left, string? right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                WriteCanonicalProperties(element, writer);
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
                // Preserve escaping, invalid-Unicode rejection and numeric
                // spelling without allocating decoded scalar text copies.
                element.WriteTo(writer);
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
                throw new InvalidOperationException("Unsupported finalization JSON value kind.");
        }

        if (writer.BytesPending >= 64 * 1024)
            writer.Flush();
    }

    private static void WriteCanonicalProperties(JsonElement element, Utf8JsonWriter writer)
    {
        int count = 0;
        foreach (JsonProperty _ in element.EnumerateObject()) count++;
        if (count == 0) return;
        if (count == 1)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(property.Value, writer);
            }
            return;
        }

        CanonicalProperty[] properties = ArrayPool<CanonicalProperty>.Shared.Rent(count);
        try
        {
            int index = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                properties[index] = new(property, property.Name, index);
                index++;
            }
            Array.Sort(properties, 0, count, CanonicalPropertyComparer.Instance);
            for (int i = 0; i < count; i++)
            {
                writer.WritePropertyName(properties[i].Name);
                WriteCanonical(properties[i].Property.Value, writer);
            }
        }
        finally
        {
            // Do not retain character data or disposed document references.
            ArrayPool<CanonicalProperty>.Shared.Return(properties, clearArray: true);
        }
    }

    private readonly record struct CanonicalProperty(JsonProperty Property, string Name, int Ordinal);

    private sealed class CanonicalPropertyComparer : IComparer<CanonicalProperty>
    {
        public static readonly CanonicalPropertyComparer Instance = new();

        public int Compare(CanonicalProperty left, CanonicalProperty right)
        {
            int order = StringComparer.Ordinal.Compare(left.Name, right.Name);
            // Preserve the former stable OrderBy behavior for duplicate keys.
            return order != 0 ? order : left.Ordinal.CompareTo(right.Ordinal);
        }
    }

    private sealed class CanonicalHashBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        public void Advance(int count)
        {
            if ((uint)count > (uint)_buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            _hash.AppendData(_buffer.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            if (sizeHint <= _buffer.Length) return;
            byte[] replacement = ArrayPool<byte>.Shared.Rent(sizeHint);
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = replacement;
        }

        public string GetDigest() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public void Dispose()
        {
            _hash.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        }
    }
}
