using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>Application-local evaluation, not a persisted or transport DTO.</summary>
public sealed record CharacterCareerMagicEvaluation(
    bool IsAdept, bool IsMagician, bool SeparateAdeptMagic,
    int Grade, int Magic, int MagicMaximum, int? AdeptMagic, int? AdeptMagicMaximum,
    bool InitiationDisabled, IReadOnlyList<string> Blockers)
{
    public bool IsSupported => Blockers.Count == 0;
    public int EffectivePowerMagic => SeparateAdeptMagic ? AdeptMagic.GetValueOrDefault() : Magic;
}

/// <summary>
/// Shared Career Magic authority. Callers retain owner, source, revision and atomic-store
/// admission. This evaluator never trusts a display total or mutates its inputs.
/// </summary>
public static class CharacterCareerMagicEvaluator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] MagicNames = ["MAG", "MAGAdept"];
    private static readonly HashSet<string> NativeFields = new(StringComparer.Ordinal)
    {
        "unique", "target", "improvedname", "sourcename", "min", "max", "aug", "augmax",
        "val", "rating", "exclude", "condition", "improvementttype", "improvementsource",
        "custom", "customname", "customid", "customgroup", "addtorating", "enabled",
        "order", "notes", "notesColor"
    };

    public static CharacterCareerMagicEvaluation Evaluate(
        XElement character, XElement settings, XElement? metatype = null)
    {
        try
        {
            Need(character.Name == "character" && !character.HasAttributes
                && settings.Name == "settings" && !settings.HasAttributes, "magic_document_invalid");
            Need(Flag(character, "created") && Flag(character, "magenabled"),
                "magic_requires_awakened_career");
            bool adept = Flag(character, "adept"), magician = Flag(character, "magician");
            Need(adept || magician, "magic_awakened_talent_unavailable");
            bool separate = adept && magician && Flag(settings, "mysadeptsecondmagattribute");
            Need(!Flag(settings, "freespiritpowerpointsmag", false)
                && !Flag(character, "critter", false) && !Flag(character, "critterenabled", false)
                && !Flag(character, "iscritter", false)
                && Text(character, "metatypecategory", "") is not ("Cyberzombie" or "Shapeshifter")
                && !Box(character, "cyberwares").HasElements && !Box(character, "biowares").HasElements
                && Number(character, "essencehole", 0) == 0
                && Number(character, "essencepenalty", 0) == 0,
                "magic_essence_or_alternate_form_unsupported");

            int grade = ValidateHistoryAndCaps(character, out XElement[] caps);
            bool disabled = Flag(character, "initiationdisabled", false);
            var free = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["MAG"] = 0, ["MAGAdept"] = 0 };
            var enableIdentities = new HashSet<(string Source, string Name)>();
            foreach (XElement effect in Effects(character))
            {
                string type = Text(effect, "improvementttype"), target = Text(effect, "improvedname", "");
                // ValidateHistoryAndCaps has already authenticated ALL Initiation rows,
                // including disabled rows. Never silently discard a broken cap.
                if (caps.Contains(effect)) continue;
                if (!Flag(effect, "enabled")) continue;
                // These native effects change attribute purchase prices, not the
                // current value/cap or initiation price. Their consumer owns pricing.
                if (type is "AttributeKarmaCost" or "AttributeKarmaCostMultiplier"
                    or "AttributePointCost" or "AttributePointCostMultiplier") continue;
                if (type == "SpecialTab" && target == "Initiation")
                {
                    if (Text(effect, "unique", "") == "disabletab") disabled = true;
                    // Conditional disabling cannot safely be interpreted as permission.
                    else Need(Text(effect, "unique", "") == "enabletab",
                        "magic_initiation_tab_semantics_unsupported");
                    continue;
                }
                if (type == "Attribute" && target == "MAG"
                    && Text(effect, "unique", "") == "enableattribute")
                {
                    ValidateNeutralRecord(effect);
                    foreach (string field in new[] { "min", "max", "aug", "augmax", "val", "rating" })
                        Need(Number(effect, field) == 0, "magic_enable_marker_invalid");
                    string source = Text(effect, "improvementsource");
                    string sourceName = Text(effect, "sourcename");
                    Need(source == source.Trim() && sourceName == sourceName.Trim(),
                        "magic_enable_marker_identity_invalid");
                    if (Guid.TryParse(sourceName, out var sourceId))
                    {
                        Need(sourceId != Guid.Empty, "magic_enable_marker_identity_invalid");
                        sourceName = sourceId.ToString("D");
                    }
                    Need(source.Length > 0 && sourceName.Length > 0
                        && enableIdentities.Add((source, sourceName)), "magic_enable_marker_identity_invalid");
                    // The marker enables no numerical authority; magenabled/talent
                    // and the actual allocation above remain separately required.
                    continue;
                }
                if (type == "Attributelevel" && MagicNames.Contains(target))
                {
                    ValidateNeutralRecord(effect);
                    Need(Text(effect, "unique", "").Length == 0, "magic_attribute_level_unsupported");
                    foreach (string field in new[] { "min", "max", "aug", "augmax" })
                        Need(Number(effect, field, 0) == 0, "magic_attribute_level_unsupported");
                    decimal value = Number(effect, "val");
                    Need(value >= 0, "magic_attribute_level_unsupported");
                    free[target] = checked(free[target] + value);
                    continue;
                }
                bool magicTarget = target.Length == 0 || target.StartsWith("MAG", StringComparison.Ordinal)
                    || target.StartsWith("ESS", StringComparison.Ordinal);
                Need(!(magicTarget && (type.StartsWith("Attribute", StringComparison.Ordinal)
                        || type == "ReplaceAttribute"))
                    && !type.Contains("Essence", StringComparison.OrdinalIgnoreCase)
                    && !(type.Contains("Magic", StringComparison.Ordinal) && type != "MagiciansWayDiscount")
                    && type is not ("CyberadeptDaemon" or "FreeSpiritPowerPoints"),
                    "magic_effect_semantics_unsupported");
            }
            var mag = Attribute(character, "MAG", caps, free["MAG"], metatype);
            var ade = separate ? Attribute(character, "MAGAdept", caps, free["MAGAdept"], metatype) : default;
            return new(adept, magician, separate, grade, mag.Value, mag.Maximum,
                separate ? ade.Value : null, separate ? ade.Maximum : null, disabled, Array.Empty<string>());
        }
        catch (InvalidDataException e)
        {
            return Failed(e.Message);
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidOperationException
            or ArgumentException or NullReferenceException)
        {
            return Failed("magic_state_invalid");
        }
    }

    /// <summary>
    /// Mutates only a caller-owned candidate, after validating the old native state.
    /// Caller must persist this with scalar, grade, benefit, expense and receipt atomically.
    /// </summary>
    public static void AdvanceInitiationCaps(XElement character, int expectedGrade, int nextGrade)
    {
        int current = ValidateHistoryAndCaps(character, out XElement[] caps);
        Need(current == expectedGrade && nextGrade == checked(expectedGrade + 1)
            && nextGrade is > 0 and <= 1000, "magic_initiation_transition_invalid");
        if (caps.Length != 0)
        {
            // No replacement of the collection or reconstruction of existing rows:
            // notes, legacy boolean encodings and unrelated effects survive.
            foreach (XElement cap in caps) cap.SetElementValue("rating", nextGrade);
            return;
        }
        XElement improvements = Box(character, "improvements");
        if (improvements.Parent is null) character.Add(improvements);
        foreach (string name in MagicNames)
            improvements.Add(new XElement("improvement",
                new XElement("target", ""), new XElement("improvedname", name), new XElement("sourcename", ""),
                new XElement("min", 0), new XElement("max", 1), new XElement("aug", 0), new XElement("augmax", 0),
                new XElement("val", 0), new XElement("rating", nextGrade), new XElement("exclude", ""),
                new XElement("condition", ""), new XElement("improvementttype", "Attribute"),
                new XElement("improvementsource", "Initiation"), new XElement("custom", false),
                new XElement("customname", ""), new XElement("customid", ""), new XElement("customgroup", ""),
                new XElement("addtorating", 0), new XElement("enabled", 1), new XElement("order", 0),
                new XElement("notes", ""), new XElement("notesColor", "#000000")));
    }

    private static CharacterCareerMagicEvaluation Failed(string blocker)
        => new(false, false, false, 0, 0, 0, null, null, false, Array.AsReadOnly(new[] { blocker }));

    internal static int ValidateInitiationState(XElement character)
        => ValidateHistoryAndCaps(character, out _);

    private static int ValidateHistoryAndCaps(XElement root, out XElement[] caps)
    {
        Need(root.Name == "character" && !root.HasAttributes, "magic_document_invalid");
        int grade = Integer(root, "initiategrade");
        Need(grade is >= 0 and <= 1000, "magic_saved_grade_inconsistent");
        XElement history = Box(root, "initiationgrades");
        Need(history.Elements().All(e => e.Name == "initiationgrade" && !e.HasAttributes),
            "magic_saved_grade_inconsistent");
        var grades = history.Elements().Where(e => !Flag(e, "res")).ToArray();
        Need(grades.Length == grade
            && grades.Select(e => Integer(e, "grade")).OrderBy(g => g).SequenceEqual(Enumerable.Range(1, grade)),
            "magic_saved_grade_inconsistent");
        var ids = new HashSet<Guid>();
        foreach (var row in grades)
        {
            Need(Guid.TryParse(Text(row, "guid"), out Guid id) && id != Guid.Empty && ids.Add(id)
                && root.Descendants("guid").Count(e => Guid.TryParse(e.Value, out Guid other) && other == id) == 1,
                "magic_saved_grade_inconsistent");
        }
        caps = Effects(root).Where(e => Text(e, "improvementsource", "") == "Initiation").ToArray();
        Need(caps.Length == (grade == 0 ? 0 : 2), "magic_initiation_caps_invalid");
        if (grade == 0) return grade;
        Need(caps.Select(e => Text(e, "improvedname")).OrderBy(n => n, StringComparer.Ordinal)
            .SequenceEqual(MagicNames), "magic_initiation_caps_invalid");
        foreach (var cap in caps)
        {
            ValidateNeutralRecord(cap);
            Need(Text(cap, "improvementttype") == "Attribute" && Text(cap, "sourcename").Length == 0
                && Text(cap, "unique", "").Length == 0 && Flag(cap, "enabled")
                && Integer(cap, "rating") == grade && Integer(cap, "max") == 1,
                "magic_initiation_caps_invalid");
            foreach (string field in new[] { "min", "aug", "augmax", "val" })
                Need(Number(cap, field) == 0, "magic_initiation_caps_invalid");
        }
        return grade;
    }

    private static (int Value, int Maximum) Attribute(
        XElement root, string name, XElement[] caps, decimal free, XElement? metatype)
    {
        XElement attributes = Box(root, "attributes");
        Need(attributes.Elements().All(e => e.Name == "attribute" && !e.HasAttributes),
            "magic_attribute_identity_invalid");
        var rows = attributes.Elements().Where(e => Text(e, "name") == name).ToArray();
        Need(rows.Length == 1, "magic_attribute_identity_invalid");
        XElement a = rows[0];
        Need(!a.HasAttributes && Text(a, "metatypecategory", "Standard") == "Standard",
            "magic_alternate_attribute_unsupported");
        int minimum = Integer(a, "metatypemin"), maximum = Integer(a, "metatypemax");
        int augmented = Integer(a, "metatypeaugmax");
        int paid = Integer(a, "base"), karma = Integer(a, "karma");
        Need(minimum >= 0 && maximum >= minimum && maximum <= 1000
            && augmented >= maximum && augmented <= 2000 && paid >= 0 && karma >= 0,
            "magic_attribute_allocation_invalid");
        if (metatype is not null)
            Need(minimum >= Integer(metatype, "magmin") && maximum == Integer(metatype, "magmax"),
                "magic_metatype_binding_invalid");
        int modifier = caps.Where(e => Text(e, "improvedname") == name)
            .Sum(e => checked(Integer(e, "max") * Integer(e, "rating")));
        int totalMaximum = checked(maximum + modifier);
        // Attribute.Core: FreeBase clamps against METATYPE maximum, not the
        // initiation-adjusted maximum. Value then adds Karma and clamps once.
        int freeBase = decimal.ToInt32(decimal.Ceiling(Math.Min(free, maximum - minimum)));
        int allocated = checked(Math.Max(checked(paid + freeBase + minimum), minimum) + karma);
        // Overallocated saves could gain actual Magic solely by raising the cap.
        // They require correction authority rather than a free grade-side increase.
        Need(allocated <= totalMaximum, "magic_allocation_exceeds_cap");
        int effective = Math.Max(0, Math.Min(allocated, Math.Min(totalMaximum, checked(augmented + modifier))));
        string displayed = Text(a, "totalvalue", "");
        Need(displayed.Length == 0 || int.TryParse(displayed, NumberStyles.AllowLeadingSign, Invariant, out int value)
            && value == effective, "magic_projection_mismatch");
        return (effective, totalMaximum);
    }

    private static void ValidateNeutralRecord(XElement effect)
    {
        Need(!effect.HasAttributes && effect.Elements().All(e => NativeFields.Contains(e.Name.LocalName)),
            "magic_effect_record_unsupported");
        foreach (string field in new[] { "target", "condition", "exclude", "customname", "customid", "customgroup" })
            Need(Text(effect, field, "").Length == 0, "magic_effect_record_unsupported");
        Need(!Flag(effect, "custom", false) && !Flag(effect, "addtorating", false)
            && Number(effect, "order", 0) == 0,
            "magic_effect_record_unsupported");
    }

    private static XElement[] Effects(XElement root)
    {
        XElement box = Box(root, "improvements");
        var rows = box.Elements().ToArray();
        Need(rows.Length <= 100_000 && rows.All(e => e.Name == "improvement" && !e.HasAttributes),
            "magic_improvement_identity_invalid");
        foreach (var row in rows)
            Need(row.Elements().All(e => e.Name.Namespace == XNamespace.None && !e.HasAttributes && !e.HasElements)
                && row.Elements().GroupBy(e => e.Name.LocalName, StringComparer.Ordinal).All(g => g.Count() == 1),
                "magic_improvement_identity_invalid");
        return rows;
    }

    private static XElement Box(XElement parent, string name)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Need(rows.Length <= 1 && rows.All(e => e.Name == name && !e.HasAttributes), "magic_ambiguous_" + name);
        return rows.SingleOrDefault() ?? new XElement(name);
    }

    private static string Text(XElement parent, string name, string? fallback = null)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        if (rows.Length == 0 && fallback is not null) return fallback;
        Need(rows.Length == 1 && rows[0].Name == name && !rows[0].HasAttributes && !rows[0].HasElements,
            "magic_ambiguous_" + name);
        return rows[0].Value;
    }

    private static bool Flag(XElement parent, string name, bool? fallback = null)
    {
        string value = Text(parent, name, fallback?.ToString());
        if (value == "1") return true;
        if (value == "0") return false;
        Need(bool.TryParse(value, out bool result), "magic_boolean_" + name);
        return result;
    }

    private static decimal Number(XElement parent, string name, decimal? fallback = null)
        => decimal.Parse(Text(parent, name, fallback?.ToString(Invariant)),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant);
    private static int Integer(XElement parent, string name)
        => int.Parse(Text(parent, name), NumberStyles.AllowLeadingSign, Invariant);
    private static void Need(bool condition, string blocker)
    {
        if (!condition) throw new InvalidDataException(blocker);
    }
}
