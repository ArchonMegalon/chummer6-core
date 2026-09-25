using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationFoundationDraftLedgerIntegrity
{
    private const string Sha256Prefix = "sha256:";

    public static string ComputeRawCharacterXmlDigest(string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml ?? string.Empty);
        return Sha256Prefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string ComputeDigest(CharacterCreationFoundationDraftLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return Sha256Prefix + ComputeCanonicalSha256(ledger with { DraftDigest = string.Empty });
    }

    public static bool IsCanonicalDigest(string? value)
    {
        if (value is not { Length: 71 }
            || !value.StartsWith(Sha256Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in value.AsSpan(Sha256Prefix.Length))
        {
            bool isDigit = character is >= '0' and <= '9';
            bool isLowerHex = character is >= 'a' and <= 'f';
            if (!isDigit && !isLowerHex)
                return false;
        }

        return true;
    }

    public static bool IsValidPending(
        CharacterCreationFoundationDraftLedger? ledger,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        string sourceDigest)
    {
        if (ledger is null
            || !string.Equals(
                ledger.Schema,
                CharacterCreationFoundationSchemas.DraftLedgerV1,
                StringComparison.Ordinal)
            || ledger.WorkspaceId != workspaceId
            || ledger.DraftRevision <= 0
            || ledger.BaseContentRevision <= 0
            || ledger.BaseContentRevision >= persistedContentRevision
            || !IsCanonicalDigest(ledger.BaseRawCharacterXmlDigest)
            || !IsCanonicalDigest(ledger.SourceDigest)
            || !FixedTimeEquals(ledger.BaseRawCharacterXmlDigest, rawCharacterXmlDigest)
            || !FixedTimeEquals(ledger.SourceDigest, sourceDigest)
            || !IsNormalizedNonEmpty(ledger.RequestedMetatype)
            || ledger.Selection is null
            || !IsNormalizedNonEmpty(ledger.Selection.ModuleId)
            || (ledger.Selection.VersionId is not null
                && !IsNormalizedNonEmpty(ledger.Selection.VersionId))
            || ledger.RequirementEvaluations is null
            || ledger.ProjectedEffects is null
            || ledger.FollowUpValues is null
            || ledger.SourceAnchorIds is null
            || !string.Equals(
                ledger.CompilationStatus,
                CharacterCreationFoundationDraftStatuses.PendingFinalization,
                StringComparison.Ordinal)
            || ledger.CharacterEffectsApplied
            || !IsCanonicalDigest(ledger.DraftDigest)
            || !FixedTimeEquals(ledger.DraftDigest, ComputeDigest(ledger)))
        {
            return false;
        }

        return (!ledger.ModuleSelectionFinished || LifeModuleJourneyStageOrders.Required
                   .Where(stage => stage != LifeModuleJourneyStageOrders.Nationality)
                   .All(stage => ledger.AdditionalModules?.Any(entry => entry?.StageOrder == stage) == true))
               && ledger.RequirementEvaluations.All(IsStructurallyValid)
               && ledger.ProjectedEffects.All(IsStructurallyValid)
               && ledger.FollowUpValues.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null)
               && ledger.SourceAnchorIds.All(IsNormalizedNonEmpty)
               && (ledger.AdditionalModules is null
                   || (ledger.AdditionalModules.Count is > 0 and <= 128
                       && ledger.AdditionalModules.All(entry => entry is not null
                           && entry.StageOrder is >= LifeModuleJourneyStageOrders.FormativeYears and <= LifeModuleJourneyStageOrders.RealLife
                           && IsNormalizedNonEmpty(entry.StageId)
                           && entry.Selection is not null
                           && IsNormalizedNonEmpty(entry.Selection.ModuleId)
                           && (entry.Selection.VersionId is null || IsNormalizedNonEmpty(entry.Selection.VersionId))
                           && entry.KarmaCost >= 0
                           && entry.RequirementEvaluations is not null
                           && entry.RequirementEvaluations.All(IsStructurallyValid)
                           && entry.ProjectedEffects is not null
                           && entry.ProjectedEffects.All(IsStructurallyValid)
                           && entry.FollowUpValues is not null
                           && entry.FollowUpValues.All(item => IsNormalizedNonEmpty(item.Key) && item.Value is not null)
                           && entry.SourceAnchorIds is not null
                           && entry.SourceAnchorIds.Count > 0
                           && entry.SourceAnchorIds.All(IsNormalizedNonEmpty)
                           && entry.StoryTemplate is not null)));
    }

    public static bool HasSameLogicalPayload(
        CharacterCreationFoundationDraftLedger left,
        CharacterCreationFoundationDraftLedger right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        CharacterCreationFoundationDraftLedger normalizedLeft = left with
        {
            DraftRevision = 0,
            BaseContentRevision = 0,
            DraftDigest = string.Empty
        };
        CharacterCreationFoundationDraftLedger normalizedRight = right with
        {
            DraftRevision = 0,
            BaseContentRevision = 0,
            DraftDigest = string.Empty
        };
        return FixedTimeEquals(
            ComputeCanonicalSha256(normalizedLeft),
            ComputeCanonicalSha256(normalizedRight));
    }

    public static string ComputeCanonicalDigest<T>(T value)
    {
        return Sha256Prefix + ComputeCanonicalSha256(value);
    }

    public static bool CanonicallyEquals<T>(T left, T right)
    {
        return FixedTimeEquals(
            ComputeCanonicalSha256(left),
            ComputeCanonicalSha256(right));
    }

    private static bool IsStructurallyValid(LifeModuleRequirementProjectionDto? requirement)
    {
        return requirement is not null
               && IsNormalizedNonEmpty(requirement.RequirementId)
               && requirement.DisableReasonArguments is not null
               && requirement.SourceAnchorIds is not null
               && IsNormalizedNonEmpty(requirement.Operator)
               && IsNormalizedNonEmpty(requirement.SubjectKind)
               && requirement.AcceptedValues is not null
               && requirement.RawXml is not null
               && requirement.DisableReasonArguments.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null)
               && requirement.SourceAnchorIds.All(IsNormalizedNonEmpty)
               && requirement.AcceptedValues.All(value => value is not null);
    }

    private static bool IsStructurallyValid(LifeModuleEffectProjectionDto? effect)
    {
        return effect is not null
               && IsNormalizedNonEmpty(effect.EffectId)
               && IsNormalizedNonEmpty(effect.Domain)
               && IsNormalizedNonEmpty(effect.TargetId)
               && effect.SourceAnchorIds is not null
               && effect.Parameters is not null
               && effect.RawXml is not null
               && effect.SourceAnchorIds.All(IsNormalizedNonEmpty)
               && effect.Parameters.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null);
    }

    private static bool IsNormalizedNonEmpty(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static string ComputeCanonicalSha256<T>(T value)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(value);
        // Keep the canonical v1 bytes, but hash them as they are emitted rather
        // than retaining another full copy of the large Creation/archive graph.
        using var output = new CanonicalHashBufferWriter();
        using (Utf8JsonWriter writer = new(output))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return output.GetDigest();
    }

    // Utf8JsonWriter(Stream) owns a fresh growable output array. Replaying the
    // large Creation catalogs repeatedly allocated those arrays even though the
    // bytes went straight into a hash. Hash each committed segment instead and
    // return the cleared rental after this invocation. No domain result or
    // caller-owned value is cached; every comparison still serializes both sides.
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
                throw new InvalidOperationException("Unsupported foundation-draft JSON value kind.");
        }

        // Utf8JsonWriter buffers until Flush. Bound that buffer between
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

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
