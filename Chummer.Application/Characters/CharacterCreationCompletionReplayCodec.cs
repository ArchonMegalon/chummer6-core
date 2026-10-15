using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded, lossless storage only. Decoding returns untrusted historical inputs;
/// the caller must independently validate their source, history and replay.
/// This does not replace the enclosing continuation transport's byte limit.
/// </summary>
internal static class CharacterCreationCompletionReplayCodec
{
    internal const string SchemaV1 = CharacterCreationCompletionReplayArchive.SchemaV1;
    internal const string EncodingName = "br-base64";
    internal const string LoadoutEncodingName = "br-base64url";
    internal const int MaximumUncompressedBytes = 8 * 1024 * 1024;
    internal const int MaximumCompressedBytes = 384 * 1024;
    private const int MaximumBase64Characters = ((MaximumCompressedBytes + 2) / 3) * 4;
    private const int MaximumJsonDepth = 128;
    private const int CompressionQuality = 9;
    private const int CompressionWindow = 22;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal sealed record Payload(
        CharacterCreationCompletionBootstrapSnapshot Bootstrap,
        CharacterCreationSelectedCompletionWitness Witness);

    internal static bool TryEncode(
        CharacterCreationCompletionBootstrapSnapshot? bootstrap,
        CharacterCreationSelectedCompletionWitness? witness,
        [NotNullWhen(true)] out CharacterCreationCompletionReplayArchive? archive)
    {
        archive = null;
        if (bootstrap is null || witness is null)
            return false;
        try
        {
            byte[] serialized = Serialize(new(bootstrap, witness));
            // A successful encoder must produce a payload the typed decoder can
            // retain exactly, including computed read-only JSON properties.
            if (!TryReadPayload(serialized, out _))
                return false;
            byte[] compressed = new byte[MaximumCompressedBytes];
            if (!BrotliEncoder.TryCompress(serialized, compressed, out int written,
                    CompressionQuality, CompressionWindow) || written <= 0)
                return false;
            // Keep legacy V1 witness bytes unchanged. New loadout witnesses use
            // unpadded base64url so the enclosing strict JSON serializer need
            // not expand '+' into six-byte escapes. The Brotli bytes, exact
            // uncompressed digest, size limits and retained history are identical.
            bool urlEncoding = witness.Schema == CharacterCreationSelectedCompletionWitness.SchemaV2;
            string data = Convert.ToBase64String(compressed, 0, written);
            archive = new(SchemaV1, urlEncoding ? LoadoutEncodingName : EncodingName,
                serialized.Length, Digest(serialized), urlEncoding ? ToBase64Url(data) : data);
            return true;
        }
        catch (Exception error) when (IsMalformed(error))
        {
            return false;
        }
    }

    internal static bool TryDecode(
        CharacterCreationCompletionReplayArchive? archive,
        [NotNullWhen(true)] out CharacterCreationCompletionBootstrapSnapshot? bootstrap,
        [NotNullWhen(true)] out CharacterCreationSelectedCompletionWitness? witness)
    {
        bootstrap = null;
        witness = null;
        if (archive is null || archive.Schema != SchemaV1
            || archive.Encoding is not (EncodingName or LoadoutEncodingName)
            || archive.UncompressedBytes is <= 0 or > MaximumUncompressedBytes
            || !IsDigest(archive.UncompressedDigest)
            || archive.Data is not { Length: > 0 and <= MaximumBase64Characters })
            return false;
        try
        {
            // Check the wire length before allocating or decoding. Canonical
            // base64 below also rejects whitespace and nonzero padding bits.
            bool urlEncoding = archive.Encoding == LoadoutEncodingName;
            string base64 = archive.Data;
            if (urlEncoding)
            {
                if (base64.Length % 4 == 1 || base64.Any(character => character is not
                    (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
                    return false;
                base64 = base64.Replace('-', '+').Replace('_', '/')
                    .PadRight((base64.Length + 3) / 4 * 4, '=');
            }
            else if (base64.Length % 4 != 0) return false;
            byte[] compressed = new byte[base64.Length / 4 * 3];
            if (!Convert.TryFromBase64String(base64, compressed, out int compressedLength)
                || compressedLength is <= 0 or > MaximumCompressedBytes
                || !string.Equals(archive.Data, urlEncoding
                    ? ToBase64Url(Convert.ToBase64String(compressed, 0, compressedLength))
                    : Convert.ToBase64String(compressed, 0, compressedLength), StringComparison.Ordinal))
                return false;

            // The declared decoded length was bounded before any output buffer
            // or JSON object graph is allocated. Never grow it on decoder demand.
            byte[] serialized = new byte[archive.UncompressedBytes];
            BrotliDecoder decoder = default;
            try
            {
                OperationStatus status = decoder.Decompress(compressed.AsSpan(0, compressedLength), serialized,
                    out int consumed, out int written);
                if (status != OperationStatus.Done || consumed != compressedLength || written != serialized.Length)
                    return false;
            }
            finally
            {
                decoder.Dispose();
            }
            // Done alone is insufficient: exact consumption rejects trailing
            // bytes and concatenated streams; exact output rejects length lies.
            if (!string.Equals(archive.UncompressedDigest, Digest(serialized), StringComparison.Ordinal)
                || !TryReadPayload(serialized, out var payload)
                || urlEncoding && payload.Witness.Schema != CharacterCreationSelectedCompletionWitness.SchemaV2)
                return false;
            bootstrap = payload.Bootstrap;
            witness = payload.Witness;
            return true;
        }
        catch (Exception error) when (IsMalformed(error))
        {
            return false;
        }
    }

    private static string ToBase64Url(string base64) => base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryReadPayload(byte[] serialized, [NotNullWhen(true)] out Payload? payload)
    {
        payload = null;
        if (serialized.Length is <= 0 or > MaximumUncompressedBytes)
            return false;
        using JsonDocument document = JsonDocument.Parse(serialized,
            new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        RejectDuplicateProperties(document.RootElement);
        Payload? decoded = document.RootElement.Deserialize<Payload>(Options);
        if (decoded?.Bootstrap is null || decoded.Witness is null
            || !HasVersionedShape(decoded.Witness)
            || !serialized.AsSpan().SequenceEqual(Serialize(decoded)))
            return false;
        // Exact typed reserialization rejects ignored/defaulted fields and
        // altered computed values, not just unknown or duplicate properties.
        payload = decoded;
        return true;
    }

    // This is only a format boundary, not rules/source admission. Keeping the
    // optional field absent preserves old V1 bytes; presence requires the new
    // witness version, so a rehashed mixed envelope cannot borrow old semantics.
    private static bool HasVersionedShape(CharacterCreationSelectedCompletionWitness witness)
        => witness.Schema switch
        {
            CharacterCreationSelectedCompletionWitness.SchemaV1 => witness.Loadout is null,
            CharacterCreationSelectedCompletionWitness.SchemaV2 => witness.Loadout is not null,
            _ => false
        };

    private static byte[] Serialize(Payload payload)
    {
        using BoundedBuffer buffer = new();
        JsonSerializer.Serialize(buffer, payload, Options);
        return buffer.ToArray();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            IgnoreReadOnlyProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = MaximumJsonDepth,
            Converters = { new StrictStringConverter() }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new JsonException("Duplicate replay archive property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }

    private static string Digest(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsMalformed(Exception error)
        => error is JsonException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or NotSupportedException;

    private sealed class StrictStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (value is not null) Validate(value);
            return value;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Validate(value);
            writer.WriteStringValue(value);
        }

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string value = reader.GetString() ?? throw new JsonException("Null replay archive property.");
            Validate(value);
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Validate(value);
            writer.WritePropertyName(value);
        }

        private static void Validate(string value)
        {
            // Avoid both an unbounded escaping reservation and replacement of
            // unmatched UTF-16 surrogates by STJ's ordinary string writer.
            if (value.Length > MaximumUncompressedBytes || StrictUtf8.GetByteCount(value) > MaximumUncompressedBytes)
                throw new JsonException("Replay archive string exceeds the byte limit.");
        }
    }

    private sealed class BoundedBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureBound(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureBound(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureBound(1);
            base.WriteByte(value);
        }

        private void EnsureBound(int count)
        {
            if (count < 0 || Position > MaximumUncompressedBytes - (long)count)
                throw new JsonException("Replay archive exceeds the byte limit.");
        }
    }
}
