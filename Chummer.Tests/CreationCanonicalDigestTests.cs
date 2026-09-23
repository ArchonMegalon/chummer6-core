using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CreationCanonicalDigestTests
{
    [TestMethod]
    public void Auxiliary_digest_preserves_legacy_bytes_for_empty_nested_and_large_states()
    {
        Assert.AreEqual(LegacyDigest(WorkspaceDocumentAuxiliaryState.Empty),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(null));
        foreach (var state in new[] { WorkspaceDocumentAuxiliaryState.Empty, State(3), State(256),
                     new WorkspaceDocumentAuxiliaryState(CharacterCreationFinalizationArchive: new(State(3))) })
        {
            string expected = LegacyDigest(state);
            Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
            Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(
                JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(JsonSerializer.Serialize(state))));
        }
    }

    [TestMethod]
    public void Foundation_digest_preserves_ordinal_keys_array_order_and_all_JSON_value_kinds()
    {
        using var document = JsonDocument.Parse("""
            {"z":null,"Z":true,"a":false,"é":"Zoë 東京 😀 e\u0301 é <>&\"\\\r\n\t",
             "nested":{"z":2.00,"a":-0,"A":1e+20},"items":[null,true,false,1,2.5,"text",{},[]]}
            """);
        var value = document.RootElement;
        string expected = "sha256:" + LegacyDigest(value);
        Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new { z = 2, a = 1 }),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new { a = 1, z = 2 }));
        Assert.AreNotEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new[] { 1, 2 }),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new[] { 2, 1 }));
        Parallel.For(0, 8, _ => Assert.AreEqual(expected,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value)));
    }

    [TestMethod]
    public void Canonical_digest_keeps_long_single_values_and_observes_caller_mutation()
    {
        var state = State(1);
        var values = (Dictionary<string, string>)state.CharacterCreationFoundationDraft!.FollowUpValues;
        values["long"] = new string('x', 300_000) + "é😀\"";
        string before = WorkspaceDocumentAuxiliaryStateDigest.Compute(state);
        Assert.AreEqual(LegacyDigest(state), before);
        Assert.AreEqual("sha256:" + before,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
        values["long"] += "changed";
        Assert.AreNotEqual(before, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual(LegacyDigest(state), WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
    }

    [TestMethod]
    public void Large_creation_digests_do_not_allocate_a_second_complete_output_buffer()
    {
        var state = State(512);
        string expected = LegacyDigest(state);
        // Warm serializer metadata and pooled buffers before comparing allocations,
        // not wall-clock timings. Fixture construction is outside the measurement.
        WorkspaceDocumentAuxiliaryStateDigest.Compute(state);
        CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state);
        long legacy = Allocations(() => LegacyDigest(state));
        long auxiliary = Allocations(() => WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        long foundation = Allocations(() => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
        Assert.IsTrue(auxiliary < legacy * 0.8, $"Auxiliary {auxiliary:N0}; buffered legacy {legacy:N0}.");
        Assert.IsTrue(foundation < legacy * 0.8, $"Foundation {foundation:N0}; buffered legacy {legacy:N0}.");
        Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual("sha256:" + expected,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
    }

    private static WorkspaceDocumentAuxiliaryState State(int count)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = count - 1; i >= 0; i--)
            values.Add("choice-" + i, new string('x', 4096) + "Zoë 東京 😀 e\u0301 é <>&\"\\\r\n\t");
        return new(new CharacterCreationFoundationDraftLedger(
            CharacterCreationFoundationSchemas.DraftLedgerV1, new("digest-fixture"), 2, 1,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64), "Human",
            new("nationality", null), [], [], values, ["source-z", "source-a"], "compiled", false, ""));
    }

    private static long Allocations(Func<string> action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        string digest = action();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(digest);
        return bytes;
    }

    // Independent retained pre-streaming implementation: changing the hash's
    // wire format would invalidate persisted draft/receipt/archive authority.
    private static string LegacyDigest<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(root, writer);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(value.GetRawText(), skipInputValidation: true); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException();
        }
    }
}
