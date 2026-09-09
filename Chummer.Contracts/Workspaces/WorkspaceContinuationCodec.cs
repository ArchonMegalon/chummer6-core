using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Lossless bounded encoding of complete continuation data. Decoding produces an
/// untrusted candidate, never a restore admission or a replay authorization.
/// Callers own their transport byte limit and must cap streams before buffering.
/// </summary>
public static class WorkspaceContinuationCodec
{
    private const int MaximumJsonDepth = 128;
    private static readonly JsonSerializerOptions Options = new()
    {
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = MaximumJsonDepth,
        Converters = { new StrictStringConverter() }
    };
    private static EncodingOptionsCache? _encodingOptions;

    public static byte[] Encode(WorkspaceContinuationExport export, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ValidateCandidate(export);
        // Bound individual strings before STJ reserves an escaping buffer; the
        // stream limit also bounds the complete graph across many small values.
        JsonSerializerOptions encodingOptions = GetEncodingOptions(maximumBytes);
        using BoundedBuffer serialized = new(maximumBytes);
        JsonSerializer.Serialize(serialized,
            new TransferEnvelope(WorkspaceContinuationSnapshot.ContractName, export.Snapshot, export.SnapshotDigest),
            encodingOptions);
        serialized.Position = 0;
        using JsonDocument document = JsonDocument.Parse(serialized,
            new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        JsonElement captured = document.RootElement.GetProperty("Snapshot");
        if (captured.GetProperty("DelegatedGmCharacterEdits").EnumerateArray()
                .Any(receipt => receipt.ValueKind == JsonValueKind.Null)
            || !string.Equals(export.SnapshotDigest,
                WorkspaceContinuationSnapshotDigest.ComputeSerializedSnapshot(captured), StringComparison.Ordinal))
            throw new JsonException("Complete continuation identity or content digest is invalid.");
        return CanonicalBytes(document.RootElement, maximumBytes);
    }

    public static bool TryDecodeCandidate(ReadOnlyMemory<byte> utf8Json, int maximumBytes,
        [NotNullWhen(true)] out WorkspaceContinuationExport? candidate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        candidate = null;
        if (utf8Json.IsEmpty || utf8Json.Length > maximumBytes)
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json,
                new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
            JsonElement root = document.RootElement;
            RejectDuplicateProperties(root);
            if (!string.Equals(RequiredString(root, "ContractName"),
                    WorkspaceContinuationSnapshot.ContractName, StringComparison.Ordinal))
                return false;

            JsonElement snapshot = root.GetProperty("Snapshot");
            JsonElement workspace = snapshot.GetProperty("Workspace");
            JsonElement wireDocument = workspace.GetProperty("Document");
            JsonElement state = wireDocument.GetProperty("State");
            WorkspaceDocumentAuxiliaryState? auxiliary = state.GetProperty("AuxiliaryState")
                .Deserialize<WorkspaceDocumentAuxiliaryState>(Options);
            IReadOnlyList<DelegatedGmCharacterEditAuditReceipt>? ledger = snapshot.GetProperty("DelegatedGmCharacterEdits")
                .Deserialize<DelegatedGmCharacterEditAuditReceipt[]>(Options);
            if (auxiliary is null || ledger is null)
                return false;

            WorkspaceDocument restoredDocument = new(new WorkspaceDocumentState(
                RequiredString(state, "RulesetId"),
                state.GetProperty("SchemaVersion").GetInt32(),
                RequiredString(state, "PayloadKind"),
                RequiredString(state, "Payload")) { AuxiliaryState = auxiliary },
                (WorkspaceDocumentFormat)wireDocument.GetProperty("Format").GetInt32());
            WorkspaceContinuationExport decoded = new(new(
                RequiredString(snapshot, "OwnerId"),
                new(new(RequiredString(workspace.GetProperty("Id"), "Value")), restoredDocument,
                    workspace.GetProperty("LastUpdatedUtc").GetDateTimeOffset(),
                    workspace.GetProperty("ContentRevision").GetInt64(),
                    workspace.GetProperty("SavedRevision").GetInt64()), ledger),
                RequiredString(root, "SnapshotDigest"));

            // Re-encoding proves there were no ignored, defaulted, normalized,
            // shadowed, or dropped fields at any depth. Object order/whitespace
            // and equivalent JSON string escaping are not data changes.
            byte[] encoded = Encode(decoded, maximumBytes);
            if (!encoded.AsSpan().SequenceEqual(CanonicalBytes(root, maximumBytes)))
                return false;
            candidate = decoded;
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
                                      or KeyNotFoundException or FormatException or OverflowException
                                      or ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateCandidate(WorkspaceContinuationExport export)
    {
        WorkspaceContinuationSnapshot? snapshot = export.Snapshot;
        WorkspaceDocumentSnapshot? workspace = snapshot?.Workspace;
        WorkspaceDocumentState? state = workspace?.Document?.State;
        if (snapshot is null || workspace is null || state is null
            || string.IsNullOrWhiteSpace(snapshot.OwnerId)
            || !string.Equals(snapshot.OwnerId, new OwnerScope(snapshot.OwnerId).NormalizedValue, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(workspace.Id.Value)
            || workspace.Id.Value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_')
            || workspace.ContentRevision < 1 || workspace.SavedRevision < 0
            || workspace.SavedRevision > workspace.ContentRevision
            || !Enum.IsDefined(workspace.Document.Format)
            || state.SchemaVersion < 1
            || string.IsNullOrWhiteSpace(state.RulesetId)
            || !string.Equals(state.RulesetId, RulesetDefaults.NormalizeOptional(state.RulesetId), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(state.PayloadKind) || string.IsNullOrWhiteSpace(state.Payload)
            || state.AuxiliaryState is null || snapshot.DelegatedGmCharacterEdits is null
            || !IsDigest(export.SnapshotDigest))
            throw new JsonException("Complete continuation identity or content digest is invalid.");
    }

    private static bool IsDigest(string? digest) => digest is { Length: 64 }
        && digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new JsonException("A required continuation string is null.");

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new JsonException("Duplicate continuation property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }

    private static byte[] CanonicalBytes(JsonElement element, int maximumBytes)
    {
        using BoundedBuffer buffer = new(maximumBytes);
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { MaxDepth = MaximumJsonDepth }))
            WriteCanonical(element, writer);
        return buffer.ToArray();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
                WriteCanonical(item, writer);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
    }

    private sealed record TransferEnvelope(string ContractName, WorkspaceContinuationSnapshot Snapshot, string SnapshotDigest);

    private sealed record EncodingOptionsCache(int MaximumBytes, JsonSerializerOptions Options);

    private static JsonSerializerOptions GetEncodingOptions(int maximumBytes)
    {
        EncodingOptionsCache? cached = Volatile.Read(ref _encodingOptions);
        if (cached?.MaximumBytes == maximumBytes)
            return cached.Options;

        JsonSerializerOptions options = new(Options);
        options.Converters.Clear();
        options.Converters.Add(new StrictStringConverter(maximumBytes));
        options.MakeReadOnly(populateMissingResolver: true);
        // Only the most recent limit is retained; arbitrary caller budgets must
        // not grow an unbounded global cache. Concurrent calls keep immutable
        // private references, so replacing the cache cannot alter their limits.
        Volatile.Write(ref _encodingOptions, new(maximumBytes, options));
        return options;
    }

    // STJ's default writer can replace unmatched UTF-16 surrogates. That would
    // silently change a payload while still hashing the same replacement bytes.
    private sealed class StrictStringConverter(int maximumUtf8Bytes = int.MaxValue) : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (value is not null)
                Validate(value);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Validate(value);
            writer.WriteStringValue(value);
        }

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string value = reader.GetString() ?? throw new JsonException("Null continuation property name.");
            Validate(value);
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Validate(value);
            writer.WritePropertyName(value);
        }

        private void Validate(string value)
        {
            // UTF-16 code-unit count is a lower bound on valid JSON UTF-8 byte
            // length. This cheap check prevents allocating for enormous strings.
            if (value.Length > maximumUtf8Bytes)
                throw new JsonException("Continuation string exceeds the configured byte limit.");
            long utf8Bytes = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character <= 0x7f)
                    utf8Bytes++;
                else if (character <= 0x7ff)
                    utf8Bytes += 2;
                else if (!char.IsSurrogate(character))
                    utf8Bytes += 3;
                else
                {
                    if (!char.IsHighSurrogate(character) || index + 1 >= value.Length
                        || !char.IsLowSurrogate(value[index + 1]))
                        throw new JsonException("Continuation strings must contain valid Unicode.");
                    index++;
                    utf8Bytes += 4;
                }
                if (utf8Bytes > maximumUtf8Bytes)
                    throw new JsonException("Continuation string exceeds the configured byte limit.");
            }
        }
    }

    private sealed class BoundedBuffer(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityForWrite(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityForWrite(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityForWrite(1);
            base.WriteByte(value);
        }

        private void EnsureCapacityForWrite(int count)
        {
            if (count < 0 || Position > maximumBytes - (long)count)
                throw new JsonException("Complete continuation exceeds the configured byte limit.");
        }
    }
}
