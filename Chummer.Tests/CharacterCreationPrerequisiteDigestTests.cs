using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPrerequisiteDigestTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Complete_representative_graph_preserves_legacy_bytes_and_public_digest(string buildMethod)
    {
        CharacterCreationPrerequisiteAuthority authority = CreateFixture(buildMethod);
        Assert.IsTrue(CurrentOptions().IsReadOnly);
        Assert.IsNotNull(CreateOptions(typeof(CharacterCreationPrerequisiteAuthority)));
        AssertEquivalent(authority);

        Type[] expected = [
            typeof(CharacterCreationPrerequisiteAuthority),
            typeof(CharacterCreationPriorityRankWeight),
            typeof(CharacterCreationPriorityOptionProjection),
            typeof(CharacterCreationPriorityHeritageOptionProjection),
            typeof(CharacterCreationMetatypeAttributeProjection),
            typeof(CharacterCreationMetatypeMovementProjection),
            typeof(CharacterCreationMetatypeMovementRate),
            typeof(CharacterCreationPriorityTalentOptionProjection),
            typeof(CharacterCreationTalentActiveSkillGrantProjection),
            typeof(CharacterCreationTalentActiveSkillChoiceProjection),
            typeof(CharacterCreationTalentSkillGroupGrantProjection),
            typeof(CharacterCreationTalentSkillGroupChoiceProjection)
        ];
        var actual = new HashSet<Type>();
        CollectRecordTypes(typeof(CharacterCreationPrerequisiteAuthority), actual);
        CollectionAssert.AreEquivalent(expected, actual.ToArray(), "Review every new declared graph type.");
        CharacterCreationPriorityOptionProjection option = authority.Options[0];
        Assert.HasCount(2, option.HeritageOptions[0].Attributes);
        Assert.IsNotNull(option.HeritageOptions[0].Movement.Walk);
        Assert.HasCount(2, option.TalentOptions[0].ActiveSkillGrant!.Options);
        Assert.HasCount(2, option.TalentOptions[0].SkillGroupGrant!.Options);
    }

    [TestMethod]
    public void Only_the_outer_authority_digest_is_blank_in_the_preimage()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateFixture(CharacterCreationBuildMethods.Priority);
        byte[] original = AssertEquivalent(authority);
        CollectionAssert.AreEqual(original, AssertEquivalent(authority with { AuthorityDigest = "another outer value" }));
        CharacterCreationPrerequisiteAuthority nestedChanged = ReplaceFirstOption(authority, option => option with
        {
            TalentOptions = [option.TalentOptions[0] with
            {
                ActiveSkillGrant = option.TalentOptions[0].ActiveSkillGrant! with { GrantDigest = "changed inner digest" }
            }, option.TalentOptions[1]]
        });
        Assert.IsFalse(original.AsSpan().SequenceEqual(AssertEquivalent(nestedChanged)));
        Assert.AreEqual("outer digest deliberately ignored", authority.AuthorityDigest);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("0.0000")]
    [DataRow("negative-zero")]
    [DataRow("1")]
    [DataRow("1.00")]
    [DataRow("-1.2300")]
    [DataRow("79228162514264337593543950335")]
    [DataRow("-79228162514264337593543950335")]
    [DataRow("0.0000000000000000000000000001")]
    public void Decimal_lexemes_in_resources_and_all_movement_rates_match_legacy(string representation)
    {
        decimal value = representation == "negative-zero"
            ? new decimal(0, 0, 0, isNegative: true, scale: 4)
            : decimal.Parse(representation, CultureInfo.InvariantCulture);
        CharacterCreationPrerequisiteAuthority authority = WithDecimal(
            CreateFixture(CharacterCreationBuildMethods.Priority), value);
        AssertEquivalent(authority);
        if (representation == "negative-zero")
            Assert.IsTrue((decimal.GetBits(value)[3] & int.MinValue) != 0);
    }

    [TestMethod]
    public void Decimal_scale_is_not_normalized_to_numeric_equality()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateFixture(CharacterCreationBuildMethods.Priority);
        byte[] integerScale = AssertEquivalent(WithDecimal(authority, 1m));
        byte[] fractionalScale = AssertEquivalent(WithDecimal(authority, 1.00m));
        Assert.IsFalse(integerScale.AsSpan().SequenceEqual(fractionalScale));
        StringAssert.Contains(Encoding.UTF8.GetString(fractionalScale), "\"BaseResourceNuyen\":1.00");
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("unicode")]
    [DataRow("supplementary")]
    [DataRow("unpaired-high")]
    [DataRow("unpaired-low")]
    [DataRow("controls")]
    [DataRow("raw-xml")]
    public void String_escaping_and_invalid_utf16_match_legacy_at_every_nested_shape(string scenario)
    {
        string value = scenario switch
        {
            "plain" => "quotes \" slash / backslash \\ <>&' + %",
            "unicode" => "ÄÖÜ ß 東京 العربية e\u0301 é \u2028\u2029",
            "supplementary" => "\U0001F600\U0001D11E",
            "unpaired-high" => "left\uD800right",
            "unpaired-low" => "left\uDC00right",
            "controls" => "\0\u0001\b\f\n\r\t\u001F",
            "raw-xml" => "<talent name=\"A &amp; B\"><![CDATA[<>&]]>\r\n</talent>",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        // Digest fixtures intentionally include non-domain strings: Compute's
        // byte contract, not source admission or mechanics validation, is tested.
        AssertEquivalent(CreateFixture(CharacterCreationBuildMethods.SumToTen, value));
    }

    [TestMethod]
    public void Null_empty_and_default_values_are_not_omitted_or_conflated()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateFixture(CharacterCreationBuildMethods.Priority);
        AssertEquivalent(CharacterCreationPrerequisiteAuthority.Unavailable);
        byte[] nulls = AssertEquivalent(authority with
        {
            Schema = null!, CreationKarmaTotal = null, SumToTenTarget = null,
            PriorityArray = null!, RankWeights = null!, Options = null!, Blockers = null!,
            MaxNumberMaxAttributesCreate = null, KarmaAttribute = null,
            AlternateMetatypeAttributeKarma = null, ReverseAttributePriorityOrder = null
        });
        byte[] empty = AssertEquivalent(authority with
        {
            Schema = string.Empty, CreationKarmaTotal = 0, SumToTenTarget = 0,
            PriorityArray = [], RankWeights = [], Options = [], Blockers = [],
            MaxNumberMaxAttributesCreate = 0, KarmaAttribute = 0,
            AlternateMetatypeAttributeKarma = false, ReverseAttributePriorityOrder = false
        });
        Assert.IsFalse(nulls.AsSpan().SequenceEqual(empty));
        StringAssert.Contains(Encoding.UTF8.GetString(nulls), "\"Options\":null");
        StringAssert.Contains(Encoding.UTF8.GetString(empty), "\"Options\":[]");
        AssertEquivalent(ReplaceFirstOption(authority, option => option with
        {
            BaseResourceNuyen = null, BaseNormalAttributePoints = int.MinValue,
            BaseActiveSkillPoints = int.MaxValue, BaseSkillGroupPoints = null,
            HeritageOptions = [option.HeritageOptions[0] with { Movement = null!, Attributes = [] }],
            TalentOptions = [option.TalentOptions[0] with
            {
                ActiveSkillGrant = null, SkillGroupGrant = null, RawTalentNode = string.Empty,
                Magic = null, Resonance = null, Depth = null, GrantedQualities = []
            }]
        }));
    }

    [TestMethod]
    [DataRow("priority-array")]
    [DataRow("rank-weights")]
    [DataRow("priority-options")]
    [DataRow("heritage-options")]
    [DataRow("talent-options")]
    [DataRow("attributes")]
    [DataRow("active-options")]
    [DataRow("group-options")]
    [DataRow("group-members")]
    [DataRow("anchors")]
    [DataRow("blockers")]
    public void Nested_list_order_and_duplicate_elements_remain_digest_significant(string path)
    {
        CharacterCreationPrerequisiteAuthority authority = CreateFixture(CharacterCreationBuildMethods.Priority);
        byte[] original = LegacyBytes(authority with { AuthorityDigest = string.Empty });
        foreach (bool duplicate in new[] { false, true })
        {
            CharacterCreationPrerequisiteAuthority changed = ChangeList(authority, path, duplicate);
            byte[] actual = AssertEquivalent(changed);
            Assert.IsFalse(original.AsSpan().SequenceEqual(actual), $"{path}, duplicate={duplicate}");
        }
    }

    [TestMethod]
    public async Task Parallel_independent_calls_preserve_bytes_without_shared_payload_state()
    {
        JsonSerializerOptions options = CurrentOptions();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
        {
            CharacterCreationPrerequisiteAuthority authority = CreateFixture(
                index % 2 == 0 ? CharacterCreationBuildMethods.Priority : CharacterCreationBuildMethods.SumToTen,
                "independent-" + index.ToString(CultureInfo.InvariantCulture));
            AssertEquivalent(authority);
            Assert.AreSame(options, CurrentOptions());
            Assert.IsTrue(options.IsReadOnly);
        })));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("de-AT")]
    [DataRow("tr-TR")]
    public void Final_json_names_override_declared_order_with_ordinal_not_culture_order(string culture)
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            JsonSerializerOptions? options = CreateOptions(typeof(RenamedProperties));
            Assert.IsNotNull(options);
            var value = new RenamedProperties(1, 2, 3, 4);
            byte[] actual = JsonSerializer.SerializeToUtf8Bytes(value, options);
            CollectionAssert.AreEqual(LegacyBytes(value), actual);
            using JsonDocument parsed = JsonDocument.Parse(actual);
            CollectionAssert.AreEqual(new[] { "A", "a", "z", "ä" },
                parsed.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            AssertEquivalent(WithDecimal(CreateFixture(CharacterCreationBuildMethods.Priority), 1234.500m));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [TestMethod]
    [DataRow("dictionary")]
    [DataRow("empty-dictionary")]
    [DataRow("object")]
    [DataRow("null-object")]
    [DataRow("empty-object-list")]
    [DataRow("json-element")]
    [DataRow("type-converter")]
    [DataRow("property-converter")]
    [DataRow("extension-data")]
    [DataRow("polymorphism")]
    [DataRow("number-handling")]
    [DataRow("serialization-callback")]
    public void Unsupported_declared_shapes_reject_fast_options_and_keep_legacy_writer(string scenario)
    {
        (object value, Type declaredType) = UnsupportedFixture(scenario);
        Assert.IsNull(CreateOptions(declaredType), scenario);
        JsonElement serialized = JsonSerializer.SerializeToElement(value, declaredType);
        byte[] expected = LegacyBytes(serialized);
        byte[] actual = ProductionFallbackBytes(serialized);
        CollectionAssert.AreEqual(expected, actual, scenario);
        // This proves factory rejection and the retained production writer.
        // It does not pretend a future member exists on today's sealed authority
        // or mutate readonly options to force Compute's fallback branch.
    }

    public static CharacterCreationPrerequisiteAuthority CreateFixture(string buildMethod, string text = "fixture")
    {
        string[] anchors = ["z-anchor:" + text, "a-anchor:" + text];
        var activeChoice = new CharacterCreationTalentActiveSkillChoiceProjection(
            "active-z:" + text, "skill-z", text, "physical", null, "node-z", "skills", anchors)
        {
            Attribute = text, IsExotic = true, IsEnabled = false, Blockers = ["z-block", "a-block"]
        };
        var active = new CharacterCreationTalentActiveSkillGrantProjection(
            2, 3, text, [activeChoice, activeChoice with { SelectionId = "active-a", SkillGroup = text }],
            "active-grant", true, ["z-block", "a-block"], anchors)
        {
            ImprovementKind = text, RawSelectorType = text, SelectorTypeSource = text,
            RawSelectorTypeQuery = text, SkillTypeQuery = text, SpecificSkillChoiceNames = [text, "second"]
        };
        var groupChoice = new CharacterCreationTalentSkillGroupChoiceProjection(
            "group-z", text, ["member-z", "member-a"], "group-digest", "skills", anchors);
        var group = new CharacterCreationTalentSkillGroupGrantProjection(
            1, 4, text, [groupChoice, groupChoice with { SelectionId = "group-a" }],
            "group-grant", false, ["z-block", "a-block"], anchors)
        {
            ImprovementKind = text, RawSelectorType = text, SelectorTypeSource = text,
            RawSelectorTypeQuery = text, CompatibilityMarker = text, RequestedGroupNames = [text, "second"]
        };
        var heritage = new CharacterCreationPriorityHeritageOptionProjection(
            "heritage-z", "metatype", "metatype-z", null, text, null, 3, 7, true,
            [new(text, 1, 6, 10), new("AGI", 2, 7, 11)], "priority-child", "metatype-node",
            false, ["z-block", "a-block"], anchors)
        {
            Movement = new(new(2.00m, 0.25m, 0m), new(4m, 0.50m, 1m), new(6m, 0.75m, 2m))
            { IsSpecial = true }
        };
        var talent = new CharacterCreationPriorityTalentOptionProjection(
            "talent-z", text, text, 5, 6, null, 0, [text, "second"], "talent-child",
            true, ["z-block", "a-block"], anchors)
        {
            ActiveSkillGrant = active, SkillGroupGrant = group,
            RawTalentNode = "<talent name=\"" + text + "\">\r\n" + text + "</talent>"
        };
        var option = new CharacterCreationPriorityOptionProjection(
            "heritage", text, "A", "priority-z", text, 4, 24, "priority-node", anchors)
        {
            BaseActiveSkillPoints = 46, BaseSkillGroupPoints = 10, BaseResourceNuyen = 450000.00m,
            HeritageOptions = [heritage, heritage with { SelectionId = "heritage-a", MetavariantSourceId = text, MetavariantName = text }],
            TalentOptions = [talent, talent with { SelectionId = "talent-a", Magic = null, Resonance = 6, Depth = null }]
        };
        return new CharacterCreationPrerequisiteAuthority(
            CharacterCreationPrerequisiteSchemas.AuthorityV1, text, buildMethod, 25,
            ["A", "B", "C", "D", "E"], text, 10,
            [new("A", 4, anchors), new("B", 3, anchors)],
            [option, option with { CategoryId = "talent", SourceId = "priority-a", Rank = "E" }],
            text, text, text, text, anchors, ["z-block", "a-block"], true, "outer digest deliberately ignored")
        {
            SelectedCustomDataInputsDigest = text, RawMetatypesXmlDigest = text,
            EffectiveMetatypesInputsDigest = text, MaxNumberMaxAttributesCreate = 1, KarmaAttribute = 5,
            AlternateMetatypeAttributeKarma = false, ReverseAttributePriorityOrder = true,
            RawSkillsXmlDigest = text, EffectiveSkillsInputsDigest = text
        };
    }

    private static CharacterCreationPrerequisiteAuthority WithDecimal(CharacterCreationPrerequisiteAuthority authority, decimal value)
        => ReplaceFirstOption(authority, option => option with
        {
            BaseResourceNuyen = value,
            HeritageOptions = [option.HeritageOptions[0] with
            {
                Movement = new(new(value, value, value), new(value, value, value), new(value, value, value))
            }, option.HeritageOptions[1]]
        });

    private static CharacterCreationPrerequisiteAuthority ReplaceFirstOption(
        CharacterCreationPrerequisiteAuthority authority,
        Func<CharacterCreationPriorityOptionProjection, CharacterCreationPriorityOptionProjection> change)
        => authority with { Options = [change(authority.Options[0]), .. authority.Options.Skip(1)] };

    private static IReadOnlyList<T> Change<T>(IReadOnlyList<T> values, bool duplicate)
        => duplicate ? [.. values, values[0]] : values.Reverse().ToArray();

    private static CharacterCreationPrerequisiteAuthority ChangeList(
        CharacterCreationPrerequisiteAuthority authority, string path, bool duplicate)
    {
        if (path == "priority-array") return authority with { PriorityArray = Change(authority.PriorityArray, duplicate) };
        if (path == "rank-weights") return authority with { RankWeights = Change(authority.RankWeights, duplicate) };
        if (path == "priority-options") return authority with { Options = Change(authority.Options, duplicate) };
        if (path == "anchors") return authority with { SourceAnchorIds = Change(authority.SourceAnchorIds, duplicate) };
        if (path == "blockers") return authority with { Blockers = Change(authority.Blockers, duplicate) };
        return ReplaceFirstOption(authority, option => path switch
        {
            "heritage-options" => option with { HeritageOptions = Change(option.HeritageOptions, duplicate) },
            "talent-options" => option with { TalentOptions = Change(option.TalentOptions, duplicate) },
            "attributes" => option with { HeritageOptions = [option.HeritageOptions[0] with
                { Attributes = Change(option.HeritageOptions[0].Attributes, duplicate) }, option.HeritageOptions[1]] },
            "active-options" => option with { TalentOptions = [option.TalentOptions[0] with
                { ActiveSkillGrant = option.TalentOptions[0].ActiveSkillGrant! with
                    { Options = Change(option.TalentOptions[0].ActiveSkillGrant!.Options, duplicate) } }, option.TalentOptions[1]] },
            "group-options" => option with { TalentOptions = [option.TalentOptions[0] with
                { SkillGroupGrant = option.TalentOptions[0].SkillGroupGrant! with
                    { Options = Change(option.TalentOptions[0].SkillGroupGrant!.Options, duplicate) } }, option.TalentOptions[1]] },
            "group-members" => option with { TalentOptions = [option.TalentOptions[0] with
                { SkillGroupGrant = option.TalentOptions[0].SkillGroupGrant! with
                    { Options = [option.TalentOptions[0].SkillGroupGrant!.Options[0] with
                        { MemberSkillSourceIds = Change(option.TalentOptions[0].SkillGroupGrant!.Options[0].MemberSkillSourceIds, duplicate) },
                        option.TalentOptions[0].SkillGroupGrant!.Options[1]] } }, option.TalentOptions[1]] },
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        });
    }

    private static byte[] AssertEquivalent(CharacterCreationPrerequisiteAuthority authority)
    {
        CharacterCreationPrerequisiteAuthority cleared = authority with { AuthorityDigest = string.Empty };
        byte[] expected = LegacyBytes(cleared);
        byte[] actual = JsonSerializer.SerializeToUtf8Bytes(cleared, CurrentOptions());
        CollectionAssert.AreEqual(expected, actual, "Canonical UTF-8 bytes changed.");
        string digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(expected));
        Assert.AreEqual(digest, CharacterCreationPrerequisiteAuthorityDigest.Compute(authority));
        return actual;
    }

    private static JsonSerializerOptions CurrentOptions()
    {
        FieldInfo? field = typeof(CharacterCreationPrerequisiteAuthorityDigest).GetField("CanonicalOptions", PrivateStatic);
        Assert.IsNotNull(field, "Bind the real private production options, not test-local options.");
        var options = field.GetValue(null) as JsonSerializerOptions;
        Assert.IsNotNull(options, "The current declared authority graph must use the verified fast path.");
        return options;
    }

    private static JsonSerializerOptions? CreateOptions(Type rootType)
    {
        MethodInfo? method = typeof(CharacterCreationPrerequisiteAuthorityDigest)
            .GetMethod("TryCreateCanonicalOptions", PrivateStatic, binder: null, types: [typeof(Type)], modifiers: null);
        Assert.IsNotNull(method);
        return (JsonSerializerOptions?)method.Invoke(null, [rootType]);
    }

    private static void CollectRecordTypes(Type type, HashSet<Type> records)
    {
        if (type == typeof(string) || type == typeof(int) || type == typeof(bool) || type == typeof(decimal)) return;
        if (Nullable.GetUnderlyingType(type) is Type underlying) { CollectRecordTypes(underlying, records); return; }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        { CollectRecordTypes(type.GetGenericArguments()[0], records); return; }
        Assert.IsTrue(type.IsClass && type.IsSealed, "Unexpected declared digest shape: " + type);
        if (!records.Add(type)) return;
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            CollectRecordTypes(property.PropertyType, records);
    }

    private static byte[] ProductionFallbackBytes(JsonElement root)
    {
        MethodInfo? method = typeof(CharacterCreationPrerequisiteAuthorityDigest).GetMethod("WriteCanonical", PrivateStatic);
        Assert.IsNotNull(method, "Keep the original writer for unsupported future graphs.");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) method.Invoke(null, [root, writer]);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] LegacyBytes<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteLegacyCanonical(root, writer);
        return buffer.WrittenSpan.ToArray();
    }

    // Independent verbatim legacy WriteCanonical algorithm from ee83fae5.
    // Never call production Compute, production options, or production writer
    // to derive this oracle's bytes or expected digest.
    private static void WriteLegacyCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteLegacyCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteLegacyCanonical(item, writer);
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
                throw new InvalidOperationException("Unsupported prerequisite-authority JSON value kind.");
        }
    }

    private static (object Value, Type DeclaredType) UnsupportedFixture(string scenario)
    {
        JsonElement element = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            { ["z"] = "<é>", ["a"] = 1.00m });
        return scenario switch
        {
            "dictionary" => (new DictionaryRoot(new Dictionary<string, int> { ["z"] = 1, ["a"] = 2 }), typeof(DictionaryRoot)),
            "empty-dictionary" => (new DictionaryRoot(new Dictionary<string, int>()), typeof(DictionaryRoot)),
            "object" => (new ObjectRoot(new Dictionary<string, int> { ["z"] = 1, ["a"] = 2 }), typeof(ObjectRoot)),
            "null-object" => (new ObjectRoot(null), typeof(ObjectRoot)),
            "empty-object-list" => (new ObjectListRoot([]), typeof(ObjectListRoot)),
            "json-element" => (new ElementRoot(element), typeof(ElementRoot)),
            "type-converter" => (new TypeConvertedValue(3), typeof(TypeConvertedValue)),
            "property-converter" => (new PropertyConverterRoot(3), typeof(PropertyConverterRoot)),
            "extension-data" => (new ExtensionRoot { Extra = new() { ["z"] = element, ["a"] = element } }, typeof(ExtensionRoot)),
            "polymorphism" => (new PolymorphicRoot(new PolymorphicChild { Z = 1, A = 2 }), typeof(PolymorphicRoot)),
            "number-handling" => (new NumberHandlingRoot(1.00m), typeof(NumberHandlingRoot)),
            "serialization-callback" => (new CallbackRoot(), typeof(CallbackRoot)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    public sealed record RenamedProperties(
        [property: JsonPropertyName("z"), JsonPropertyOrder(-10)] int First,
        [property: JsonPropertyName("A")] int Second,
        [property: JsonPropertyName("ä")] int Third,
        [property: JsonPropertyName("a")] int Fourth);
    public sealed record DictionaryRoot(IReadOnlyDictionary<string, int> Values);
    public sealed record ObjectRoot(object? Value);
    public sealed record ObjectListRoot(IReadOnlyList<object> Values);
    public sealed record ElementRoot(JsonElement Value);
    [JsonConverter(typeof(TypeConvertedValueConverter))]
    public sealed record TypeConvertedValue(int Value);
    public sealed record PropertyConverterRoot([property: JsonConverter(typeof(IntObjectConverter))] int Value);
    public sealed record NumberHandlingRoot([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal Value);
    public sealed class ExtensionRoot
    {
        public int Middle { get; init; } = 4;
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; init; } = [];
    }
    public sealed record PolymorphicRoot(PolymorphicBase Value);
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(PolymorphicChild), "child")]
    public class PolymorphicBase { public int Z { get; init; } }
    public sealed class PolymorphicChild : PolymorphicBase { public int A { get; init; } }
    public sealed class CallbackRoot : IJsonOnSerializing
    {
        public int Z { get; init; } = 1;
        public void OnSerializing() { }
    }
    public sealed class TypeConvertedValueConverter : JsonConverter<TypeConvertedValue>
    {
        public override TypeConvertedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, TypeConvertedValue value, JsonSerializerOptions options)
            => writer.WriteRawValue("{\"z\":\"\\u0061\",\"a\":1.00}");
    }
    public sealed class IntObjectConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteRawValue("{\"z\":2,\"a\":1}");
    }
}
