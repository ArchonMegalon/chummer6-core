using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

// Append-only identifiers. Unknown source names, paths, expressions and values never
// cross this boundary. These are observations, not permissions or a rules engine.
public enum CharacterCreationObservedSetting
{
    Opaque, AttributeKarma, MaximumAttributes, AlternateAttributeKarma, ReverseAttributeOrder,
    KnowledgePoints, BrokenGroups, StrictGroups, SpecializationsBreakGroups,
    NewActiveSkill, ImproveActiveSkill, NewKnowledgeSkill, ImproveKnowledgeSkill,
    NewSkillGroup, ImproveSkillGroup, ActiveSpecialization, KnowledgeSpecialization,
    QualityLimit, ExceedPositiveQualities, ExceedNegativeQualities,
    ContactPoints, GroupContactKarma, ResourceKarmaMaximum, ResourceKarmaRate,
    ResourceExpression, UnrestrictedNuyen, MaximumAvailability, FreeGrids,
    MysticAdeptKarma, MysticAdeptSpellExchange, SeparateAdeptMagic, SecondMaximumAttribute,
    QualityKarma
}

public enum CharacterCreationSettingValueKind { Integer, Boolean, Decimal, Expression }
public enum CharacterCreationSettingObservationState { Present, Absent, Opaque, Invalid, Duplicate }
public enum CharacterCreationSettingConsumer { None, Attributes, Skills, Qualities, Contacts, Resources, Gear, Lifestyles, Magic, Loadout }
public enum CharacterCreationSettingConsumptionReason
{
    Evaluated, OpaqueSource, AbsentSourceDefaultUnproven, InvalidSource, DuplicateSource,
    ConsumerInputUnavailable, SourceValueMismatch, EvaluationUnavailable, EvaluatedUseUnavailable
}

public sealed record CharacterCreationSettingDefinition(CharacterCreationObservedSetting Key,
    string Element, string? Container, CharacterCreationSettingValueKind Kind);

public sealed record CharacterCreationSettingObservation(CharacterCreationObservedSetting Key,
    string SettingId, string? SourceValueDigest, string? ValueDigest, CharacterCreationSettingObservationState State);

public sealed record CharacterCreationSettingsObservation(string Schema, string ProfileInputsDigest,
    IReadOnlyList<CharacterCreationSettingObservation> Settings)
{
    public const string SchemaV1 = "chummer.creation-settings-observation.v1";
    public const string SchemaV2 = "chummer.creation-settings-observation.v2";

    // Keep the original capture API's definition set and bytes for historical callers.
    public static bool TryCapture(string canonicalProfileXml, string profileInputsDigest,
        out CharacterCreationSettingsObservation? observation)
        => TryCapture(canonicalProfileXml, profileInputsDigest, SchemaV1, out observation);

    public static bool TryCapture(string canonicalProfileXml, string profileInputsDigest, string schema,
        out CharacterCreationSettingsObservation? observation)
    {
        observation = null;
        var definitions = CharacterCreationSettingDefinitions.ForSchema(schema);
        if (definitions is null || !CharacterCreationFinalizationDigest.IsCanonical(profileInputsDigest)
            || string.IsNullOrWhiteSpace(canonicalProfileXml) || canonicalProfileXml.Length > 262144) return false;
        try
        {
            using var input = new StringReader(canonicalProfileXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            var settings = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            if (settings.Name != "setting" || settings.ToString(SaveOptions.DisableFormatting) != canonicalProfileXml) return false;
            string[] represented = ["id", "name", "buildmethod", "books", "customdatadirectorynames",
                "maxskillratingcreate", "maxskillrating", "maxknowledgeskillratingcreate", "maxknowledgeskillrating"];
            var nodes = settings.Elements().Where(node => node.Name.NamespaceName.Length != 0
                    || !represented.Contains(node.Name.LocalName, StringComparer.Ordinal))
                .SelectMany(node => node.DescendantsAndSelf().Where(child => !child.HasElements)).ToArray();
            if (nodes.Length > CharacterCreationEffectivePolicyContext.MaximumSettings - 3) return false;
            var rows = new List<CharacterCreationSettingObservation>();
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var definition = definitions.SingleOrDefault(row =>
                    node.Name == row.Element && (row.Container is null ? node.Parent == settings
                        : node.Parent?.Name == row.Container && node.Parent.Parent == settings));
                string? value = definition is null ? null : CharacterCreationSettingDefinitions.ValueDigest(definition.Kind, node.Value);
                bool scalar = !node.HasAttributes && node.Nodes().All(child => child is XText)
                    && !settings.HasAttributes && (definition?.Container is null
                        || node.Parent is { HasAttributes: false } && settings.Elements(definition.Container).Count() == 1);
                string identity = CharacterCreationFinalizationDigest.Compute(new
                    { Order = i, Name = node.Name.ToString(), Parent = node.Parent?.Name.ToString() });
                rows.Add(new(definition?.Key ?? CharacterCreationObservedSetting.Opaque, "legacy:" + identity,
                    CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting)),
                    scalar ? value : null, definition is null ? CharacterCreationSettingObservationState.Opaque
                        : scalar && value is not null ? CharacterCreationSettingObservationState.Present
                            : CharacterCreationSettingObservationState.Invalid));
            }
            var duplicates = rows.Where(row => row.Key != CharacterCreationObservedSetting.Opaque)
                .GroupBy(row => row.Key).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            rows = rows.Select(row => duplicates.Contains(row.Key)
                ? row with { State = CharacterCreationSettingObservationState.Duplicate, ValueDigest = null } : row).ToList();
            foreach (var definition in definitions)
                if (!rows.Any(row => row.Key == definition.Key))
                    rows.Add(new(definition.Key, "absent:" + definition.Key, null, null, CharacterCreationSettingObservationState.Absent));
            observation = new(schema, profileInputsDigest, rows.ToArray());
            return true;
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException) { return false; }
    }
}

/// <summary>One obligation for one source occurrence and consumer. An evaluated row
/// binds the exact supplied value, consumer input and actual result. Unresolved rows
/// have no evaluation identity. No row implies full character completion.</summary>
public sealed record CharacterCreationSettingConsumption(CharacterCreationObservedSetting Key, string SettingId,
    string? SourceValueDigest, CharacterCreationSettingConsumer Consumer, CharacterCreationSettingConsumptionReason Reason,
    string? InputValueDigest, string? ConsumerInputsDigest, string? EvaluationDigest);

/// <summary>A bounded source occurrence submitted to a concrete consumer/target.
/// Evaluated means the consumer handled this occurrence (including its conditions),
/// not that it necessarily changed the price or covers every other selected effect.</summary>
public sealed record CharacterCreationPolicyEffectEvaluation(CharacterCreationConsumerPolicyOperation Operation,
    string SourceOccurrenceDigest, string TargetDigest, CharacterCreationSettingConsumptionReason Reason,
    string ConsumerInputsDigest, string? EvaluationDigest);

public sealed record CharacterCreationPolicyConsumption(string Schema, string ObservationsDigest,
    string InputsSnapshotDigest, string SelectionsDigest, IReadOnlyList<CharacterCreationSettingConsumption> Settings,
    IReadOnlyList<CharacterCreationConsumerPolicyOperation> RemainingOperations)
{
    public const string SchemaV1 = "chummer.creation-policy-consumption.v1";
    public IReadOnlyList<CharacterCreationPolicyEffectEvaluation> Effects { get; init; } = [];
}

public static class CharacterCreationSettingDefinitions
{
    // Frozen V1 definitions. Extending this list would change historical observations.
    public static IReadOnlyList<CharacterCreationSettingDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        D(CharacterCreationObservedSetting.AttributeKarma, "karmaattribute", "karmacost"),
        D(CharacterCreationObservedSetting.MaximumAttributes, "maxnumbermaxattributescreate"),
        D(CharacterCreationObservedSetting.SecondMaximumAttribute, "allow2ndmaxattribute", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.AlternateAttributeKarma, "alternatemetatypeattributekarma", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.ReverseAttributeOrder, "reverseattributepriorityorder", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.KnowledgePoints, "knowledgepointsexpression", kind: CharacterCreationSettingValueKind.Expression),
        D(CharacterCreationObservedSetting.BrokenGroups, "usepointsonbrokengroups", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.StrictGroups, "breakskillgroupsincreatemode", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.SpecializationsBreakGroups, "specializationsbreakskillgroups", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.NewActiveSkill, "karmanewactiveskill", "karmacost"),
        D(CharacterCreationObservedSetting.ImproveActiveSkill, "karmaimproveactiveskill", "karmacost"),
        D(CharacterCreationObservedSetting.NewKnowledgeSkill, "karmanewknowledgeskill", "karmacost"),
        D(CharacterCreationObservedSetting.ImproveKnowledgeSkill, "karmaimproveknowledgeskill", "karmacost"),
        D(CharacterCreationObservedSetting.NewSkillGroup, "karmanewskillgroup", "karmacost"),
        D(CharacterCreationObservedSetting.ImproveSkillGroup, "karmaimproveskillgroup", "karmacost"),
        D(CharacterCreationObservedSetting.ActiveSpecialization, "karmaspecialization", "karmacost"),
        D(CharacterCreationObservedSetting.KnowledgeSpecialization, "karmaknospecialization", "karmacost"),
        D(CharacterCreationObservedSetting.QualityLimit, "qualitykarmalimit"),
        D(CharacterCreationObservedSetting.ExceedPositiveQualities, "exceedpositivequalities", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.ExceedNegativeQualities, "exceednegativequalities", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.ContactPoints, "contactpointsexpression", kind: CharacterCreationSettingValueKind.Expression),
        D(CharacterCreationObservedSetting.GroupContactKarma, "karmacontact", "karmacost"),
        D(CharacterCreationObservedSetting.ResourceKarmaMaximum, "nuyenmaxbp", kind: CharacterCreationSettingValueKind.Decimal),
        D(CharacterCreationObservedSetting.ResourceKarmaRate, "nuyenperbpwftm", kind: CharacterCreationSettingValueKind.Decimal),
        D(CharacterCreationObservedSetting.ResourceExpression, "chargenkarmatonuyenexpression", kind: CharacterCreationSettingValueKind.Expression),
        D(CharacterCreationObservedSetting.UnrestrictedNuyen, "unrestrictednuyen", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.MaximumAvailability, "availability"),
        D(CharacterCreationObservedSetting.FreeGrids, "allowfreegrids", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.MysticAdeptKarma, "karmamysadpp", "karmacost"),
        D(CharacterCreationObservedSetting.MysticAdeptSpellExchange, "priorityspellsasadeptpowers", kind: CharacterCreationSettingValueKind.Boolean),
        D(CharacterCreationObservedSetting.SeparateAdeptMagic, "mysadeptsecondmagattribute", kind: CharacterCreationSettingValueKind.Boolean)
    });

    private static IReadOnlyList<CharacterCreationSettingDefinition> V2 { get; } = Array.AsReadOnly(
        All.Append(D(CharacterCreationObservedSetting.QualityKarma, "karmaquality", "karmacost")).ToArray());

    public static IReadOnlyList<CharacterCreationSettingDefinition>? ForSchema(string schema) => schema switch
    {
        CharacterCreationSettingsObservation.SchemaV1 => All,
        CharacterCreationSettingsObservation.SchemaV2 => V2,
        _ => null
    };

    private static CharacterCreationSettingDefinition D(CharacterCreationObservedSetting key, string element,
        string? container = null, CharacterCreationSettingValueKind kind = CharacterCreationSettingValueKind.Integer)
        => new(key, element, container, kind);

    // Only syntax normalization, never expression evaluation or default inference.
    public static string? ValueDigest(CharacterCreationSettingValueKind kind, string value)
    {
        string canonical;
        switch (kind)
        {
            case CharacterCreationSettingValueKind.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer) || integer < 0) return null;
                canonical = integer.ToString(CultureInfo.InvariantCulture); break;
            case CharacterCreationSettingValueKind.Decimal:
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
                        | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number) || number < 0) return null;
                canonical = number.ToString("G29", CultureInfo.InvariantCulture); break;
            case CharacterCreationSettingValueKind.Boolean:
                if (!bool.TryParse(value, out bool flag)) return null;
                canonical = flag ? "true" : "false"; break;
            case CharacterCreationSettingValueKind.Expression:
                if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) return null;
                canonical = value; break;
            default: return null;
        }
        return CharacterCreationFinalizationDigest.Compute(new { Kind = kind, Value = canonical });
    }
}
