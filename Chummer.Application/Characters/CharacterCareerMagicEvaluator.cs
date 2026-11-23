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
    // Include in the consumer's source revision binding. This digest is evidence,
    // not a replacement for retaining/revalidating the resolver admission scope.
    public string? EssenceSourceDigest { get; init; }
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
        XElement character, XElement settings, XElement? metatype = null,
        ICharacterSourceDataContext? sourceContext = null)
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
                && Text(character, "metatypecategory", "") is not ("Cyberzombie" or "Shapeshifter" or "A.I.")
                && !Box(character, "biowares").HasElements
                && Number(character, "essencehole", 0) == 0
                && Number(character, "essencepenalty", 0) == 0,
                "magic_essence_or_alternate_form_unsupported");

            int grade = ValidateHistoryAndCaps(character, out XElement[] caps);
            var essence = ValidateImplants(character, settings, metatype, sourceContext, separate, adept && magician);
            metatype = essence.Metatype;
            bool disabled = Flag(character, "initiationdisabled", false);
            var free = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["MAG"] = 0, ["MAGAdept"] = 0 };
            var enableIdentities = new HashSet<(string Source, string Name)>();
            foreach (XElement effect in Effects(character))
            {
                string type = Text(effect, "improvementttype"), target = Text(effect, "improvedname", "");
                // ValidateHistoryAndCaps has already authenticated ALL Initiation rows,
                // including disabled rows. Never silently discard a broken cap.
                if (caps.Contains(effect)) continue;
                if (essence.Effects.Contains(effect)) continue;
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
                    && !type.Contains("Ess", StringComparison.OrdinalIgnoreCase)
                    && !(type.Contains("Magic", StringComparison.Ordinal) && type != "MagiciansWayDiscount")
                    && type is not ("CyberadeptDaemon" or "FreeSpiritPowerPoints"
                        or "SpecialAttBurn" or "SpecialAttTotalBurnMultiplier" or "PrototypeTranshuman"),
                    "magic_effect_semantics_unsupported");
            }
            var mag = Attribute(character, "MAG", caps, free["MAG"], metatype, essence.Reduction);
            var ade = separate ? Attribute(character, "MAGAdept", caps, free["MAGAdept"], metatype, essence.Reduction) : default;
            return new(adept, magician, separate, grade, mag.Value, mag.Maximum,
                separate ? ade.Value : null, separate ? ade.Maximum : null, disabled, Array.Empty<string>())
                { EssenceSourceDigest = essence.SourceDigest };
        }
        catch (InvalidDataException e)
        {
            return Failed(e.Message);
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidOperationException
            or ArgumentException or NullReferenceException or System.Xml.XmlException)
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

    // The commerce resolver authenticates fixed, standalone cyberware and its grade
    // against the selected source/profile. Saved ess/totaless and loss rows alone are
    // not authority. Callers must retain that source scope through their commit.
    private static (int Reduction, XElement? Metatype, XElement[] Effects, string? SourceDigest) ValidateImplants(
        XElement root, XElement settings, XElement? metatype, ICharacterSourceDataContext? context,
        bool separate, bool mystic)
    {
        XElement[] wares = Box(root, "cyberwares").Elements().ToArray();
        XElement[] losses = Effects(root).Where(e => Text(e, "improvementsource", "")
            is "EssenceLoss" or "EssenceLossChargen").ToArray();
        if (wares.Length == 0)
        {
            Need(losses.Length == 0, "magic_essence_authority_missing");
            return (0, metatype, losses, null);
        }
        Need(context is not null, "magic_essence_authority_missing");
        Need(context!.TryResolveCareerSettingsProfile(out var boundSettings, out var settingsAuthority)
            && settingsAuthority.Length > 0 && MatchesSourceXml(settings, boundSettings)
            && Text(root, "settings") == Text(settings, "id"),
            "magic_essence_settings_binding_invalid");
        Need(context.TryResolveCareerQualityMetatype(out var boundMetatype, out var metatypeAuthority)
            && metatypeAuthority.Length > 0, "magic_essence_metatype_unavailable");
        XElement heritage = XElement.Parse(boundMetatype);
        var sourceEvidence = new List<string> { "sr5-career-magic-essence/v1", settingsAuthority, metatypeAuthority };
        Need(Text(root, "metatypeguid") == Text(heritage, "id")
            && Text(root, "metatype") == Text(heritage, "name")
            && Text(root, "metavariantguid", "") is "" or "00000000-0000-0000-0000-000000000000"
            && Text(root, "metavariant", "") is "" or "None"
            && (metatype is null || MatchesSourceXml(metatype, boundMetatype))
            && !Box(heritage, "bonus").HasElements,
            "magic_essence_metatype_binding_invalid");
        // These paths need distinct Career burn/PP-history authority; the Creation
        // calculator must not be used to reconstruct their purchased allocations.
        Need(!Flag(settings, "esslossreducesmaximumonly")
            && !Flag(settings, "specialkarmacostbasedonshownvalue")
            && !Flag(settings, "unclampattributeminimum")
            && (!mystic || separate) && Number(root, "prototypetranshuman", 0) == 0,
            "magic_essence_policy_unsupported");
        decimal maximum = Number(heritage, "essmax"), wareEssence = 0;
        Need(maximum > 0 && Number(root, "essenceatspecialstart") == maximum,
            "magic_essence_acquisition_unsupported");
        int? decimals = null;
        bool unrounded = Flag(settings, "donotroundessenceinternally");
        var identities = new HashSet<Guid>();
        foreach (XElement ware in wares)
        {
            ValidateImplantShape(ware);
            Need(Guid.TryParse(Text(ware, "guid"), out var instance) && instance != Guid.Empty
                && identities.Add(instance)
                && root.Descendants("guid").Count(e => Guid.TryParse(e.Value, out var id) && id == instance) == 1,
                "magic_implant_identity_invalid");
            Need(Guid.TryParse(Text(ware, "sourceid"), out var sourceId) && sourceId != Guid.Empty
                && Text(ware, "improvementsource") == "Cyberware", "magic_implant_source_unsupported");
            Need(context.TryResolveCyberwareCommerceSource(sourceId.ToString("D"), Text(ware, "name"),
                "Cyberware", out var source), "magic_implant_source_unavailable");
            sourceEvidence.Add(System.Text.Json.JsonSerializer.Serialize(source));
            Need(Guid.TryParse(source.SourceId, out var resolvedId) && resolvedId == sourceId
                && source.Name == Text(ware, "name") && source.Source == Text(ware, "source")
                && !source.SourceEntryUsesGeneratedOrImprovementSemantics
                && source.EssenceModifierPostExpression is "" or "{Modifier}"
                && source.MinimumRatingExpression.Length == 0 && source.MaximumRatingExpression.Length == 0
                && Text(ware, "ess") == source.EssenceExpression
                && source.EssenceDecimals is >= 2 and <= 28
                && source.DoNotRoundEssenceInternally == unrounded
                && (decimals is null || decimals == source.EssenceDecimals),
                "magic_implant_source_binding_invalid");
            decimals = source.EssenceDecimals;
            Need(decimal.TryParse(source.EssenceExpression, NumberStyles.AllowDecimalPoint, Invariant,
                out decimal rawEssence) && rawEssence >= 0, "magic_implant_essence_unsupported");
            string gradeName = Text(ware, "grade");
            var grades = source.Grades.Where(g => g.Name == gradeName || g.Id == gradeName).ToArray();
            Need(grades.Length == 1 && !grades[0].SpecialSemantics && grades[0].EssenceMultiplier >= 0
                && (source.ForcedGrade.Length == 0 || source.ForcedGrade == grades[0].Name)
                && Text(ware, "forcegrade", "") == source.ForcedGrade,
                "magic_implant_grade_unsupported");
            // Reuse the canonical exact per-ware rounding operation; no display
            // rounding or centi-Essence purchase bookkeeping enters Magic math.
            wareEssence = checked(wareEssence + CharacterCreationEssenceRules1222.Ware(
                rawEssence, grades[0].EssenceMultiplier, 1m, decimals.Value, unrounded));
        }
        decimal essence = checked(maximum - wareEssence);
        if (!unrounded) essence = decimal.Round(essence, decimals!.Value, MidpointRounding.AwayFromZero);
        Need(essence > 0 && (Text(root, "totaless", "").Length == 0 || Number(root, "totaless") == essence),
            "magic_essence_projection_mismatch");
        int reduction = CharacterCreationEssenceRules1222.Round(checked(maximum - essence));
        // Native Character.RefreshEssenceLossImprovements retains history. Admit
        // only an intact, non-restored loss for each active Magic attribute. A split
        // chargen/Career history, augmented malus or PP burn needs its own authority.
        Need(losses.Length == (reduction == 0 ? 0 : separate ? 2 : 1)
            || !separate && reduction > 0 && losses.Length == 2, "magic_essence_loss_records_invalid");
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement loss in losses)
        {
            ValidateNeutralRecord(loss);
            string target = Text(loss, "improvedname");
            Need(MagicNames.Contains(target) && targets.Add(target)
                && Text(loss, "improvementttype") == "Attribute" && Flag(loss, "enabled")
                && Text(loss, "sourcename").Length == 0 && Text(loss, "unique", "").Length == 0
                && Integer(loss, "rating") == 1
                && Integer(loss, "min") == -reduction && Integer(loss, "max") == -reduction,
                "magic_essence_loss_records_invalid");
            foreach (string field in new[] { "aug", "augmax", "val" })
                Need(Number(loss, field) == 0, "magic_essence_loss_records_invalid");
        }
        Need(reduction == 0 || targets.Contains("MAG") && (!separate || targets.Contains("MAGAdept")),
            "magic_essence_loss_records_invalid");
        return (reduction, heritage, losses,
            Chummer.Contracts.Characters.CharacterCareerInitiationRules.Hash(string.Join('\0', sourceEvidence)));
    }

    private static bool MatchesSourceXml(XElement supplied, string boundXml)
        // XElement.Load(XmlReader) retains the reader's whitespace, whereas
        // XElement.Parse defaults to discarding it. Match only these two exact
        // representations of the admitted source; never trim caller values or
        // remove fields. The resolver's original authority/digest stays intact.
        => XNode.DeepEquals(supplied, XElement.Parse(boundXml, LoadOptions.PreserveWhitespace))
            || XNode.DeepEquals(supplied, XElement.Parse(boundXml));

    private static void ValidateImplantShape(XElement ware)
    {
        // Native serialization has many inert display/matrix fields. Unknown
        // fields and nonempty dependent graphs cannot silently confer authority.
        const string scalars = "guid sourceid name category ess capacity avail cost source page "
            + "limbslot limbslotcount modularmount plugsintomodularmount blocksmounts forced rating "
            + "minagility minstrength minrating maxrating ratinglabel subsystems wirelesson grade location extra "
            + "suite stolen essdiscount extraessadditivemultiplier extraessmultiplicativemultiplier forcegrade "
            + "matrixcmfilled matrixcmbonus prototypetranshuman improvementsource notes notesColor discountedcost "
            + "addtoparentess addtoparentcapacity isgeneware active homenode devicerating programlimit "
            + "overclocked canformpersona attack sleaze dataprocessing firewall attributearray modattack "
            + "modsleaze moddataprocessing modfirewall modattributearray canswapattributes sortorder";
        var allowed = scalars.Split(' ').ToHashSet(StringComparer.Ordinal);
        string[] empty = ["bonus", "pairbonus", "wirelessbonus", "wirelesspairbonus", "children", "gears"];
        Need(ware.Name == "cyberware" && !ware.HasAttributes
            && ware.Elements().GroupBy(e => e.Name.LocalName).All(g => g.Count() == 1),
            "magic_implant_shape_unsupported");
        foreach (var field in ware.Elements())
        {
            Need(field.Name.Namespace == XNamespace.None && !field.HasAttributes,
                "magic_implant_shape_unsupported");
            if (field.Name.LocalName is "pairinclude" or "wirelesspairinclude")
                Need(field.Elements().All(e => e.Name == "name" && !e.HasAttributes && !e.HasElements
                    && e.Value == Text(ware, "name")), "magic_implant_shape_unsupported");
            else
                Need(!field.HasElements && (allowed.Contains(field.Name.LocalName)
                    || empty.Contains(field.Name.LocalName) && string.IsNullOrWhiteSpace(field.Value)),
                    "magic_implant_shape_unsupported");
        }
        foreach (string flag in new[] { "suite", "isgeneware", "prototypetranshuman", "addtoparentess", "wirelesson" })
            Need(!Flag(ware, flag, false), "magic_implant_shape_unsupported");
        foreach (string field in new[] { "modularmount", "plugsintomodularmount", "limbslot", "subsystems", "extra" })
            Need(Text(ware, field, "").Length == 0, "magic_implant_shape_unsupported");
        Need(Integer(ware, "rating") == 0 && Number(ware, "essdiscount", 0) == 0
            && Number(ware, "extraessadditivemultiplier", 0) == 0
            && Number(ware, "extraessmultiplicativemultiplier", 1) == 1,
            "magic_implant_shape_unsupported");
    }

    internal static int ValidateInitiationState(XElement character)
        => ValidateHistoryAndCaps(character, out _);

    private static int ValidateHistoryAndCaps(XElement root, out XElement[] caps)
    {
        Need(root.Name == "character" && !root.HasAttributes, "magic_document_invalid");
        // Native loading leaves an absent grade at zero; present values stay strict.
        // The history and cap checks below still authenticate that zero state.
        int grade = int.Parse(Text(root, "initiategrade", "0"), NumberStyles.AllowLeadingSign, Invariant);
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
        XElement root, string name, XElement[] caps, decimal free, XElement? metatype, int essenceReduction)
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
        int totalMaximum = Math.Max(0, checked(maximum + modifier - essenceReduction));
        // Attribute.Core: FreeBase clamps against METATYPE maximum, not the
        // initiation-adjusted maximum. Value then adds Karma and clamps once.
        int freeBase = decimal.ToInt32(decimal.Ceiling(Math.Min(free, maximum - minimum)));
        int effectiveMinimum = Math.Max(0, checked(minimum - essenceReduction));
        int allocated = checked(Math.Max(checked(paid + freeBase + effectiveMinimum), effectiveMinimum) + karma);
        // Overallocated saves could gain actual Magic solely by raising the cap.
        // They require correction authority rather than a free grade-side increase.
        Need(allocated <= totalMaximum, "magic_allocation_exceeds_cap");
        int effective = Math.Max(0, Math.Min(allocated,
            Math.Min(totalMaximum, checked(augmented + modifier - essenceReduction))));
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
