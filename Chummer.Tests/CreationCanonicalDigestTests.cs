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
    public void Skill_projection_digest_preserves_legacy_scalars_nested_arrays_and_flags()
    {
        foreach (string? text in new[] { null, "", "Zoë 東京 😀 e\u0301 é <>&\"\\\r\n\t", "\ud800",
                     new string('x', 300_000) + "é😀\"" })
        {
            var input = SkillProjection(text!);
            string expected = "sha256:" + LegacyDigest(input);
            Assert.AreEqual(expected, input.Digest());
            Parallel.For(0, 4, _ => Assert.AreEqual(expected, input.Digest()));
        }
        var digests = new HashSet<string>(StringComparer.Ordinal);
        for (int flags = 0; flags < 128; flags++)
        {
            var input = SkillProjection("Skill") with
            {
                IsExotic = (flags & 1) != 0, CanDefault = (flags & 2) != 0,
                IgnoresSourceDisabled = (flags & 4) != 0, RequiresGroundMovement = (flags & 8) != 0,
                RequiresSwimMovement = (flags & 16) != 0, RequiresFlyMovement = (flags & 32) != 0,
                CanBeNativeLanguage = (flags & 64) != 0, Specializations = [], SourceAnchorIds = []
            };
            Assert.AreEqual("sha256:" + LegacyDigest(input), input.Digest());
            Assert.IsTrue(digests.Add(input.Digest()), "Every capability bit must remain bound.");
        }
    }

    [TestMethod]
    public void Skill_projection_digest_recomputes_mutable_inputs_and_preserves_array_order()
    {
        var input = SkillProjection("Skill");
        var specializations = (CharacterCreationSkillSpecializationOption[])input.Specializations;
        var anchors = (string[])input.SourceAnchorIds;
        string before = input.Digest();
        specializations[0] = specializations[0] with { Name = "Changed specialization" };
        Assert.AreNotEqual(before, input.Digest());
        Assert.AreEqual("sha256:" + LegacyDigest(input), input.Digest());
        before = input.Digest();
        Array.Reverse(specializations);
        Assert.AreNotEqual(before, input.Digest());
        Assert.AreEqual("sha256:" + LegacyDigest(input), input.Digest());
        before = input.Digest();
        anchors[0] = "changed source";
        Assert.AreNotEqual(before, input.Digest());
        Assert.AreEqual("sha256:" + LegacyDigest(input), input.Digest());
        before = input.Digest();
        Array.Reverse(anchors);
        Assert.AreNotEqual(before, input.Digest());
        Assert.AreEqual("sha256:" + LegacyDigest(input), input.Digest());
        Assert.AreNotEqual(input.Digest(), (input with { SkillGroup = string.Empty }).Digest());
        Assert.ThrowsExactly<ArgumentNullException>(() => (input with { Specializations = null! }).Digest());
        Assert.ThrowsExactly<ArgumentNullException>(() => (input with { SourceAnchorIds = null! }).Digest());
    }

    [TestMethod]
    public void Skill_projection_digest_avoids_JSON_DOM_and_output_copy_allocations()
    {
        var input = SkillProjection(new string('x', 4096) + "é😀");
        string expected = "sha256:" + LegacyDigest(input);
        Assert.AreEqual(expected, input.Digest());
        long legacy = Allocations(() => LegacyDigest(input));
        long direct = Allocations(input.Digest);
        Console.WriteLine($"Skill projection warm allocation: direct={direct}; legacy={legacy}.");
        Assert.IsTrue(direct < legacy / 4, $"Skill projection {direct:N0}; legacy {legacy:N0}.");
        Assert.AreEqual(expected, input.Digest());
    }

    private static SkillProjectionInput SkillProjection(string text) => new(
        "sha256:" + new string('a', 64), "source-id", "active", text, "Combat Active", "AGI", null, false,
        new CharacterCreationSkillSpecializationOption[] { new("option-z", text, "source-z"), null!, new("option-a", "", null!) },
        new string[] { "source-z", null!, "source-a", "source-z" });

    private sealed record SkillProjectionInput(
        string EffectiveSkillsInputsDigest, string SourceSkillId, string Kind, string Name,
        string Category, string DefaultAttribute, string? SkillGroup, bool IsExotic,
        IReadOnlyList<CharacterCreationSkillSpecializationOption> Specializations,
        IReadOnlyList<string> SourceAnchorIds, bool CanDefault = false, bool IgnoresSourceDisabled = false,
        bool RequiresGroundMovement = false, bool RequiresSwimMovement = false,
        bool RequiresFlyMovement = false, bool CanBeNativeLanguage = false)
    {
        public string Schema => CharacterCreationSkillsSchemas.CatalogProjectionV2;

        public string Digest() => CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
            EffectiveSkillsInputsDigest, SourceSkillId, Kind, Name, Category, DefaultAttribute, SkillGroup,
            IsExotic, Specializations, SourceAnchorIds, CanDefault, IgnoresSourceDisabled,
            RequiresGroundMovement, RequiresSwimMovement, RequiresFlyMovement, CanBeNativeLanguage);
    }

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
        Assert.AreEqual("sha256:" + LegacyDigest(state),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
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

    [TestMethod]
    public void Foundation_digest_reuses_output_buffer_for_large_single_values()
    {
        var value = new { text = new string('x', 300_000) + "é😀\"", tail = new[] { 1, 2, 3 } };
        string expected = "sha256:" + LegacyDigest(value);
        Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        long allocated = Allocations(() => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        // Neither scalar traversal nor the UTF-8 output buffer should require
        // another large text/output copy after the scratch buffers are warm.
        Assert.IsTrue(allocated < 1_000_000, $"Canonical digest allocated {allocated:N0} bytes.");
        for (int i = 0; i < 3; i++)
        {
            Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
            Assert.AreEqual("sha256:" + LegacyDigest(new { small = i }),
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new { small = i }));
        }
        Parallel.For(0, 4, _ => Assert.AreEqual(expected,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value)));
    }

    [TestMethod]
    public void Foundation_digest_keeps_stable_duplicate_property_order()
    {
        using var document = JsonDocument.Parse("""
            {"z":0,"a":1,"a":2,"b":3,"a":4,"d":5,"a":6,"f":7,"a":8,
             "h":9,"a":10,"j":11,"a":12,"l":13,"a":14,"n":15,"a":16,
             "nested":{"é":1,"\u00e9":2,"z":3,"é":4},"A":17,"a":18}
            """);
        string expected = "sha256:" + LegacyDigest(document.RootElement);
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(expected,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(document.RootElement));
        Parallel.For(0, 8, _ => Assert.AreEqual(expected,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(document.RootElement)));
    }

    [TestMethod]
    public void Foundation_digest_keeps_nested_properties_across_scratch_buffer_sizes()
    {
        foreach (int count in new[] { 0, 1, 15, 16, 17, 128, 1, 0 })
        {
            var properties = Enumerable.Range(0, count).Reverse().ToDictionary(
                i => "key-" + i, i => new { z = i, a = new { value = i }, empty = new { } });
            var value = new { z = properties, a = new[] { properties, properties } };
            string expected = "sha256:" + LegacyDigest(value);
            Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        }
    }

    [TestMethod]
    public void Foundation_digest_bounds_allocations_for_many_small_catalog_objects()
    {
        var value = Enumerable.Range(0, 1024).Select(i => new
        {
            z = i, y = i + 1, x = i + 2, w = i + 3,
            nested = new { d = true, c = false, b = i, a = "catalog" },
            b = "name", a = "source"
        }).ToArray();
        string expected = "sha256:" + LegacyDigest(value);
        Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        long legacy = Allocations(() => LegacyDigest(value));
        long allocated = Allocations(() => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
        Assert.IsTrue(allocated < legacy * 0.65,
            $"Catalog digest allocated {allocated:N0}; original canonical traversal {legacy:N0}.");
        Assert.AreEqual(expected, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value));
    }

    [TestMethod]
    public void Gear_direct_digests_preserve_canonical_bytes_across_large_values_and_reused_buffers()
    {
        foreach (int length in new[] { 0, 15, 4096, 65536, 300000, 1 })
        {
            string xml = new string('x', length) + "Zoë 東京 😀 e\u0301 é <>&\"\\\r\n\t";
            var option = GearDigestOption(xml);
            Assert.AreEqual(CharacterCreationGearRules.Compute(new
                { Schema = "chummer.sr5.creation-gear.source-node.v1", Xml = xml }),
                CharacterCreationGearRules.ComputeSourceNodeDigest(xml));
            Assert.AreEqual(CharacterCreationGearRules.Compute(option with { OptionDigest = string.Empty }),
                CharacterCreationGearRules.ComputeOptionDigest(option));
            var authority = GearDigestAuthority([option, null!]);
            string expected = CharacterCreationGearRules.Compute(authority with { AuthorityDigest = string.Empty });
            Assert.AreEqual(expected, CharacterCreationGearRules.ComputeAuthorityDigest(authority));
            Parallel.For(0, 4, _ => Assert.AreEqual(expected,
                CharacterCreationGearRules.ComputeAuthorityDigest(authority)));
        }
    }

    [TestMethod]
    public void Auxiliary_digest_reuses_output_buffer_for_large_single_values()
    {
        var state = State(1);
        var values = (Dictionary<string, string>)state.CharacterCreationFoundationDraft!.FollowUpValues;
        values["long"] = new string('x', 300_000) + "é😀";
        string expected = LegacyDigest(state);
        Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        long allocated = Allocations(() => WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Console.WriteLine($"Auxiliary digest warm allocation: {allocated:N0} bytes.");
        Assert.IsTrue(allocated < 1_000_000, $"Auxiliary digest allocated {allocated:N0} bytes after warmup.");
        Parallel.For(0, 4, _ => Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state)));
        values["long"] += "changed";
        Assert.AreNotEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual(LegacyDigest(state), WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
    }

    [TestMethod]
    public void Gear_catalog_digest_does_not_allocate_a_complete_output_copy_or_cache_mutable_inputs()
    {
        var options = Enumerable.Repeat(GearDigestOption(new string('x', 16384)), 128).ToArray();
        var authority = GearDigestAuthority(options);
        string expected = CharacterCreationGearRules.Compute(authority with { AuthorityDigest = string.Empty });
        Assert.AreEqual(expected, CharacterCreationGearRules.ComputeAuthorityDigest(authority));
        long bytes = Allocations(() => CharacterCreationGearRules.ComputeAuthorityDigest(authority));
        Console.WriteLine($"Gear catalog digest warm allocation: {bytes:N0} bytes.");
        Assert.IsTrue(bytes < 256_000, $"Gear catalog digest allocated {bytes:N0} bytes after warmup.");
        options[0] = options[0] with { Name = "Changed after the first digest" };
        Assert.AreNotEqual(expected, CharacterCreationGearRules.ComputeAuthorityDigest(authority));
        Assert.AreEqual(CharacterCreationGearRules.Compute(authority with { AuthorityDigest = string.Empty }),
            CharacterCreationGearRules.ComputeAuthorityDigest(authority));
    }

    [TestMethod]
    public void Canonical_digest_writes_JSON_scalars_without_allocating_decoded_text_copies()
    {
        var state = State(128);
        var values = (Dictionary<string, string>)state.CharacterCreationFoundationDraft!.FollowUpValues;
        values["large"] = new string('x', 300_000) + "é😀<>&\"\\\r\n\t";
        string expected = LegacyDigest(state);
        Assert.AreEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual("sha256:" + expected,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
        long auxiliary = Allocations(() => WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        long foundation = Allocations(() => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
        Console.WriteLine($"Scalar traversal allocations: auxiliary={auxiliary}; foundation={foundation}.");
        Assert.IsTrue(auxiliary < 100_000, $"Auxiliary scalar traversal allocated {auxiliary:N0} bytes.");
        Assert.IsTrue(foundation < 100_000, $"Foundation scalar traversal allocated {foundation:N0} bytes.");
        values["large"] += "changed";
        Assert.AreNotEqual(expected, WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual(LegacyDigest(state), WorkspaceDocumentAuxiliaryStateDigest.Compute(state));
        Assert.AreEqual("sha256:" + LegacyDigest(state),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state));
    }

    [TestMethod]
    public void Foundation_digest_preserves_escaped_scalar_values_and_numeric_spellings()
    {
        foreach (string json in new[]
        {
            """{"value":"\u0061\u002f\u0022\u005c\u000a","number":-0.00}""",
            """{"value":"\u00e9\uD83D\uDE00\u003c\u003e\u0026","number":1.234567890123456789e+100}""",
            """{"value":"é😀/\t\b\f\r\n","number":1E-100}""",
            """{"value":"","number":18446744073709551616}"""
        })
        {
            using var document = JsonDocument.Parse(json);
            string expected = "sha256:" + LegacyDigest(document.RootElement);
            Assert.AreEqual(expected,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(document.RootElement));
        }
        using var escaped = JsonDocument.Parse("""{"value":"\u0061"}""");
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new { value = "a" }),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(escaped.RootElement));
        foreach (string json in new[] { """{"value":"\uD800"}""", """{"value":"\uDC00"}""" })
        {
            using var invalid = JsonDocument.Parse(json);
            Assert.ThrowsExactly<JsonException>(() => LegacyDigest(invalid.RootElement));
            Assert.ThrowsExactly<JsonException>(() =>
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(invalid.RootElement));
        }
    }

    private static CharacterCreationGearCatalogOption GearDigestOption(string xml) => new(
        "digest-gear", Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111"), "Zoë 東京", "Gear", 12.50m, 3, 2,
        CharacterCreationGearLegality.Restricted, "SR5", "123", true, true, true, [], ["source-b", "source-a"],
        xml, "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

    private static CharacterCreationGearAuthority GearDigestAuthority(CharacterCreationGearCatalogOption[] options) => new(
        CharacterCreationGearSchemas.AuthorityV1, "sr5", "settings", 12, 4096, 1000000, options,
        ["source-z", "source-a"], [], true, "source", "profile", "rules", "runtime", "old-authority-digest");

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
