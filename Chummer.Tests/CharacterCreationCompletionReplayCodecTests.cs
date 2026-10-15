using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionReplayCodecTests
{
    // Capture real saved rules state once; transport mutations below never alter
    // that observation or claim to admit a changed rules authority.
    private static readonly Lazy<Fixture> Captured = new(Capture);

    [TestMethod]
    public void Replay_archive_round_trips_actual_selected_authority_with_canonical_bytes_and_no_writes()
    {
        var fixture = Captured.Value;
        Assert.AreEqual("chummer.creation-completion-replay-archive.v1", fixture.Archive.Schema);
        Assert.AreEqual("br-base64", fixture.Archive.Encoding);
        Assert.AreEqual(fixture.Json.Length, fixture.Archive.UncompressedBytes);
        Assert.AreEqual(Digest(fixture.Json), fixture.Archive.UncompressedDigest);
        Assert.IsTrue(fixture.Archive.UncompressedBytes <= CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes);
        Assert.IsTrue(Convert.FromBase64String(fixture.Archive.Data).Length
            <= CharacterCreationCompletionReplayCodec.MaximumCompressedBytes);

        var persisted = JsonSerializer.Deserialize<CharacterCreationCompletionReplayArchive>(
            JsonSerializer.Serialize(fixture.Archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(persisted, out var bootstrap, out var witness));
        Assert.IsNotNull(bootstrap);
        Assert.IsNotNull(witness);
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Bootstrap), JsonSerializer.Serialize(bootstrap));
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Witness), JsonSerializer.Serialize(witness));
        Assert.AreEqual(fixture.Witness.WitnessDigest, CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness!));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap!, witness!, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Archive), JsonSerializer.Serialize(repeated));
        CollectionAssert.AreEqual(fixture.Json, JsonSerializer.SerializeToUtf8Bytes(new
        {
            Bootstrap = bootstrap,
            Witness = witness
        }));
    }

    [TestMethod]
    public void Replay_archive_rejects_invalid_envelopes_lengths_hashes_and_base64_without_partial_results()
    {
        var fixture = Captured.Value;
        var valid = fixture.Archive;
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(null, fixture.Witness, out var absent));
        Assert.IsNull(absent);
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, null, out absent));
        Assert.IsNull(absent);
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(
            fixture.Bootstrap with { RawCharacterXmlDigest = "\ud800" }, fixture.Witness, out absent),
            "The transport must not silently replace invalid UTF-16 with replacement characters.");
        Assert.IsNull(absent);
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap with
        {
            RawCharacterXmlDigest = new string('a', CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes + 1)
        }, fixture.Witness, out absent));
        Assert.IsNull(absent);
        AssertRejected(null, "null archive");
        foreach (var candidate in new[]
        {
            valid with { Schema = "unknown" },
            valid with { Schema = null! },
            valid with { Encoding = "gzip-base64" },
            valid with { Encoding = null! },
            valid with { Encoding = CharacterCreationCompletionReplayCodec.LoadoutEncodingName,
                Data = valid.Data.TrimEnd('=').Replace('+', '-').Replace('/', '_') },
            valid with { UncompressedBytes = -1 },
            valid with { UncompressedBytes = 0 },
            valid with { UncompressedBytes = CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes + 1 },
            valid with { UncompressedBytes = valid.UncompressedBytes - 1 },
            valid with { UncompressedBytes = valid.UncompressedBytes + 1 },
            valid with { UncompressedDigest = new string('0', 64) },
            valid with { UncompressedDigest = valid.UncompressedDigest.ToUpperInvariant() },
            valid with { UncompressedDigest = "not-a-digest" },
            valid with { UncompressedDigest = null! },
            valid with { Data = "" },
            valid with { Data = "not base64!" },
            valid with { Data = null! },
            valid with { Data = Convert.ToBase64String(new byte[CharacterCreationCompletionReplayCodec.MaximumCompressedBytes + 1]) }
        }) AssertRejected(candidate, "invalid envelope");
    }

    [TestMethod]
    public void Replay_archive_rejects_truncated_trailing_concatenated_and_overexpanding_brotli()
    {
        var valid = Captured.Value.Archive;
        byte[] compressed = Convert.FromBase64String(valid.Data);
        Assert.IsTrue(compressed.Length > 2);
        foreach (var bytes in new[]
        {
            compressed[..(compressed.Length / 2)],
            compressed[..^1],
            compressed.Concat(new byte[] { 0 }).ToArray(),
            compressed.Concat(compressed).ToArray()
        }) AssertRejected(valid with { Data = Convert.ToBase64String(bytes) }, "non-single complete Brotli stream");

        byte[] oversized = new byte[CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes + 1];
        Array.Fill(oversized, (byte)' ');
        var bomb = Archive(oversized) with
        {
            UncompressedBytes = CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes
        };
        Assert.IsTrue(Convert.FromBase64String(bomb.Data).Length < CharacterCreationCompletionReplayCodec.MaximumCompressedBytes,
            "This must exercise decompressed-size enforcement, not only the compressed-size limit.");
        AssertRejected(bomb, "decompressed bytes exceed the admitted maximum");
    }

    [TestMethod]
    public void Replay_archive_rejects_duplicate_unknown_and_ignored_computed_json_even_with_matching_transport_hash()
    {
        var fixture = Captured.Value;
        using var document = JsonDocument.Parse(fixture.Json);
        var root = document.RootElement;
        var witness = root.GetProperty("Witness");
        var skills = witness.GetProperty("Skills");
        foreach (var json in new[]
        {
            AppendProperty(root, [], "Bootstrap", root.GetProperty("Bootstrap")),
            AppendProperty(root, ["Witness", "Skills"], "SnapshotDigest", skills.GetProperty("SnapshotDigest")),
            AppendProperty(root, [], "UnexpectedEnvelopeMember", JsonSerializer.SerializeToElement(true)),
            AppendProperty(root, ["Witness", "Skills", "Authority"], "UnexpectedAuthorityMember", JsonSerializer.SerializeToElement(true)),
            Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(fixture.Json)),
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Json) + "{}")
        }) AssertRejected(Archive(json), "noncanonical or ambiguous typed JSON");

        var changed = JsonNode.Parse(fixture.Json)!.AsObject();
        var attribute = changed["Witness"]!["Skills"]!["Attributes"]!["Attributes"]![0]!.AsObject();
        Assert.AreEqual(attribute["Current"]!.GetValue<int>(), attribute["UnaugmentedCurrent"]!.GetValue<int>());
        attribute["UnaugmentedCurrent"] = attribute["Current"]!.GetValue<int>() + 1;
        AssertRejected(Archive(JsonSerializer.SerializeToUtf8Bytes(changed)),
            "a known read-only computed property cannot be silently ignored during deserialization");

        var missing = JsonNode.Parse(fixture.Json)!.AsObject();
        Assert.IsTrue(missing.Remove("Witness"));
        AssertRejected(Archive(JsonSerializer.SerializeToUtf8Bytes(missing)), "missing required typed payload");
    }

    private sealed record Fixture(CharacterCreationCompletionBootstrapSnapshot Bootstrap,
        CharacterCreationSelectedCompletionWitness Witness, CharacterCreationCompletionReplayArchive Archive, byte[] Json);

    private static Fixture Capture()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var files = Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest,
            CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs));
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var selected = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.IsNotNull(selected.Value);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, selected.Value!, out var archive));
        Assert.IsNotNull(archive);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories));
        foreach (var file in files) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
        return new(bootstrap, selected.Value!, archive!, Inflate(archive!));
    }

    private static CharacterCreationCompletionReplayArchive Archive(byte[] json)
    {
        using var output = new MemoryStream();
        using (var encoder = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(json);
        return new(CharacterCreationCompletionReplayCodec.SchemaV1, CharacterCreationCompletionReplayCodec.EncodingName,
            json.Length, Digest(json), Convert.ToBase64String(output.ToArray()));
    }

    private static byte[] Inflate(CharacterCreationCompletionReplayArchive archive)
    {
        using var input = new MemoryStream(Convert.FromBase64String(archive.Data));
        using var decoder = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        return output.ToArray();
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void AssertRejected(CharacterCreationCompletionReplayArchive? archive, string reason)
    {
        Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var witness), reason);
        Assert.IsNull(bootstrap, reason);
        Assert.IsNull(witness, reason);
    }

    private static byte[] AppendProperty(JsonElement element, string[] path, string name, JsonElement value)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) Write(element, 0, writer);
        return output.ToArray();

        void Write(JsonElement current, int depth, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            bool found = depth == path.Length;
            foreach (var property in current.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (depth < path.Length && property.Name == path[depth])
                {
                    found = true;
                    Write(property.Value, depth + 1, writer);
                }
                else property.Value.WriteTo(writer);
            }
            Assert.IsTrue(found, "The real fixture must contain the targeted JSON path.");
            if (depth == path.Length)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
    }
}
