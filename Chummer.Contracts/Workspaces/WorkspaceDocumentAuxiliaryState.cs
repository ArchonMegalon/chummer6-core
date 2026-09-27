using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Durable workspace-owned state that is not part of the canonical character payload.
/// It must never be projected into a ruleset payload envelope or a character download.
/// </summary>
public sealed record WorkspaceDocumentAuxiliaryState(
    CharacterCreationFoundationDraftLedger? CharacterCreationFoundationDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationPrerequisiteDraft? CharacterCreationPrerequisiteDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationAttributesDraft? CharacterCreationAttributesDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationSkillsDraft? CharacterCreationSkillsDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationSkillsReceipt>? CharacterCreationSkillsReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationMagicResonanceDraft? CharacterCreationMagicResonanceDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationMagicResonanceReceipt>? CharacterCreationMagicResonanceReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? CharacterCreationContactReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationBootstrapBinding? CharacterCreationBootstrapBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationQualitiesDraft? CharacterCreationQualitiesDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? CharacterCreationQualitiesReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? CharacterAfterRunSettlementReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? CharacterCreationLifestyleReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationResourcesDraft? CharacterCreationResourcesDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? CharacterCreationResourcesReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationGearDraft? CharacterCreationGearDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? CharacterCreationGearReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Chummer.Contracts.LifeModules.LifeModuleDecisionAcceptance>? LifeModuleDecisionAcceptances = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? CharacterCreationFinalizationReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterAfterRunRewardReceipt>? CharacterAfterRunRewardReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCareerReputationReceipt>? CharacterCareerReputationReceipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationFinalizationArchive? CharacterCreationFinalizationArchive = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCreationKarmaMetatypeDecision>? CharacterCreationKarmaMetatypeDecisions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationContactsDraft? CharacterCreationContactsDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Sr6CreationFoundationDecision>? Sr6CreationFoundationDecisions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Sr6CreationFinalizationArchive? Sr6CreationFinalizationArchive = null)
{
    public static WorkspaceDocumentAuxiliaryState Empty { get; } = new();

    public bool IsEmpty => Sr6CreationFoundationDecisions is null
                           && Sr6CreationFinalizationArchive is null
                           && CharacterCreationFoundationDraft is null
                           && CharacterCreationContactsDraft is null
                           && CharacterCreationPrerequisiteDraft is null
                           && CharacterCreationAttributesDraft is null
                           && CharacterCreationSkillsDraft is null
                           && CharacterCreationSkillsReceipts is null
                           && CharacterCreationMagicResonanceDraft is null
                           && CharacterCreationMagicResonanceReceipts is null
                           && CharacterCreationContactReceipts is null
                           && CharacterCreationLifestyleReceipts is null
                           && CharacterCreationResourcesDraft is null
                           && CharacterCreationResourcesReceipts is null
                           && CharacterCreationGearDraft is null
                           && CharacterCreationGearReceipts is null
                           && LifeModuleDecisionAcceptances is null
                           && CharacterCreationBootstrapBinding is null
                           && CharacterCreationQualitiesDraft is null
                           && CharacterCreationQualitiesReceipts is null
                           && CharacterAfterRunSettlementReceipts is null
                           && CharacterCreationFinalizationReceipts is null
                           && CharacterAfterRunRewardReceipts is null
                           && CharacterCareerReputationReceipts is null
                           && CharacterCreationFinalizationArchive is null
                           && CharacterCreationKarmaMetatypeDecisions is null;
}

/// <summary>
/// The exact pre-finalization selection and receipt graph. It is history, never
/// an active Creation draft or part of a downloadable character. Nested archives
/// and pre-existing finalization receipts are forbidden by the store boundary.
/// Its canonical digest must match the finalization receipt's previous auxiliary
/// digest, which also binds the original workspace and revision.
/// </summary>
public sealed record CharacterCreationFinalizationArchive(
    WorkspaceDocumentAuxiliaryState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationKarmaFinalizationAuthority? KarmaAuthority = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLifeModuleFinalizationAuthority? LifeModuleAuthority { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationFinalizationStartingCash? StartingCash { get; init; }
}

public static class WorkspaceDocumentAuxiliaryStateDigest
{
    public const string Semantics = "canonical-workspace-document-auxiliary-state-json-sha256-v1";

    public static string Compute(WorkspaceDocumentAuxiliaryState? state)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            state ?? WorkspaceDocumentAuxiliaryState.Empty);
        // Keep the canonical v1 bytes, but hash them as they are emitted rather
        // than retaining another full copy of the large Creation/archive graph.
        using var output = new CanonicalHashBufferWriter();
        using (Utf8JsonWriter writer = new(output))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return output.GetDigest();
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
                throw new InvalidOperationException("Unsupported auxiliary-state JSON value kind.");
        }

        // Utf8JsonWriter(Stream) buffers until Flush. Bound that buffer between
        // values; a single large string is still written intact and unchanged.
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

        // Catalog graphs contain many small objects. Rent sorting scratch space
        // and decode each key once rather than allocating LINQ sorting arrays
        // and decoding each key again for output. Nothing survives this call.
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
            // Never retain character values or disposed JsonDocument references
            // in the shared rental, including when recursive writing throws.
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
            // LINQ OrderBy was stable. Preserve duplicate JSON property order
            // too; Array.Sort by name alone would change canonical bytes.
            return order != 0 ? order : left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}
