using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Reproduces the bounded, prompt-free Chummer5 Quality.Create/Save and Gear.Create/Save
/// shapes accepted by the typed SR5 creation authorities. Unsupported source semantics fail
/// closed; callers never synthesize a partial legacy instance from display-only catalog data.
/// </summary>
public static class CharacterCreationLegacySourceProjector
{
    private const string DefaultNotesColor = "#000000";

    private static readonly IReadOnlySet<string> s_QualitySourceChildren =
        new HashSet<string>(
        [
            "id", "name", "translate", "karma", "category", "limit", "nolevels",
            "metagenic", "metagenetic", "altnotes", "notes", "notesColor",
            "doublecareer", "canbuywithspellpoints", "print", "implemented",
            "contributetobp", "contributetolimit", "stagedpurchase", "source", "page",
            "altpage", "mutant", "bonus", "firstlevelbonus", "naturalweapons",
            "careeronly", "onlyprioritygiven"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> s_GearSourceChildren =
        new HashSet<string>(
        [
            "id", "name", "translate", "category", "avail", "capacity", "armorcapacity",
            "costfor", "cost", "weight", "ratinglabel", "rating", "minrating",
            "altnotes", "notes", "notesColor", "devicerating", "matrixcmbonus", "source",
            "page", "altpage", "canformpersona", "ammoforweapontype",
            "childcostmultiplier", "childavailmodifier", "allowrename", "stolen",
            "isflechetteammo", "attributearray", "attack", "sleaze", "dataprocessing",
            "firewall", "modattack", "modsleaze", "moddataprocessing", "modfirewall",
            "modattributearray", "programs"
        ],
        StringComparer.Ordinal);

    public static bool IsQualitySourceProjectable(string sourceNodeXml)
    {
        if (sourceNodeXml is not
            { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength })
            return false;
        XElement? source = ParseBoundedSource(
            sourceNodeXml,
            "quality",
            s_QualitySourceChildren);
        return source is not null && TryReadQualityDefinition(source, out _);
    }

    public static bool IsGearSourceProjectable(string sourceNodeXml)
    {
        if (sourceNodeXml is not
            { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength })
            return false;
        XElement? source = ParseBoundedSource(sourceNodeXml, "gear", s_GearSourceChildren);
        return source is not null && TryReadGearDefinition(source, out _);
    }

    internal static bool TryBuildQualityGraph(
        CharacterCreationQualitySelection selection,
        string draftDigest,
        out XElement[] qualities,
        out XElement[] improvements)
    {
        qualities = [];
        improvements = [];
        if (selection is null
            || selection.SourceNodeXml is not
                { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength }
            || !CharacterCreationQualitiesRules.DigestsEqual(
                selection.SourceNodeDigest,
                CharacterCreationQualitiesRules.ComputeSourceNodeDigest(selection.SourceNodeXml))
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(draftDigest))
            return false;

        XElement? source = ParseBoundedSource(
            selection.SourceNodeXml,
            "quality",
            s_QualitySourceChildren);
        if (source is null
            || !TryReadQualityDefinition(source, out QualityDefinition definition)
            || definition.SourceId != selection.SourceId
            || !string.Equals(definition.Name, selection.Name, StringComparison.Ordinal)
            || definition.Type != selection.Type
            || definition.Metagenic != selection.IsMetagenic
            || definition.ContributeToLimit != selection.CountsAgainstQualityLimit
            || definition.ContributeToBuildPoints != selection.CountsAgainstKarma
            || selection.IsFreeOrGranted
            || selection.FollowUpChoiceId is not null
            || selection.FollowUpChoiceLabel is not null
            || selection.Rating is < 1
            || selection.Rating > definition.MaximumRating)
            return false;

        int expectedCost;
        try
        {
            expectedCost = checked(definition.Karma * selection.Rating);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (expectedCost != selection.KarmaCost)
            return false;

        var savedQualities = new List<XElement>(selection.Rating);
        var savedImprovements = new List<XElement>();
        for (int level = 1; level <= selection.Rating; level++)
        {
            string qualityId = StableGuid(
                $"quality:{selection.OptionId}:{selection.SourceNodeDigest}:{draftDigest}:{level}")
                .ToString("D", CultureInfo.InvariantCulture);
            savedQualities.Add(BuildSavedQuality(source, definition, qualityId));
            foreach (CompiledEffect effect in definition.BonusEffects)
                savedImprovements.Add(BuildImprovement(effect, qualityId, definition.NotesColor));
            if (level == 1)
            {
                foreach (CompiledEffect effect in definition.FirstLevelEffects)
                    savedImprovements.Add(BuildImprovement(effect, qualityId, definition.NotesColor));
            }
        }

        qualities = savedQualities.ToArray();
        improvements = savedImprovements.ToArray();
        return true;
    }

    internal static bool TryBuildGear(
        CharacterCreationGearLine line,
        string draftDigest,
        out XElement gear)
    {
        gear = new XElement("gear");
        if (line is null
            || line.SourceNodeXml is not
                { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }
            || !CharacterCreationGearRules.DigestsEqual(
                line.SourceNodeDigest,
                CharacterCreationGearRules.ComputeSourceNodeDigest(line.SourceNodeXml))
            || !CharacterCreationGearRules.IsCanonicalDigest(draftDigest))
            return false;
        XElement? source = ParseBoundedSource(line.SourceNodeXml, "gear", s_GearSourceChildren);
        if (source is null
            || !TryReadGearDefinition(source, out GearDefinition definition)
            || definition.SourceId != line.SourceId
            || !string.Equals(definition.Name, line.Name, StringComparison.Ordinal)
            || !string.Equals(definition.Category, line.Category, StringComparison.Ordinal)
            || definition.PackageCost != line.PackageCost
            || definition.PackageQuantity != line.PackageQuantity
            || definition.Availability != line.Availability
            || !string.Equals(definition.Legality, line.Legality, StringComparison.Ordinal)
            || !string.Equals(definition.SourceBook, line.SourceBook, StringComparison.Ordinal)
            || !string.Equals(definition.Page, line.Page, StringComparison.Ordinal)
            || line.Quantity <= 0)
            return false;

        decimal expectedTotal;
        try
        {
            expectedTotal = checked(line.PackageCost * line.Quantity / line.PackageQuantity);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (expectedTotal != line.TotalCost)
            return false;

        string gearId = StableGuid(
            $"gear:{line.OptionId}:{line.SourceNodeDigest}:{draftDigest}")
            .ToString("D", CultureInfo.InvariantCulture);
        gear = BuildSavedGear(definition, gearId, line.Quantity);
        return true;
    }

    private static XElement? ParseBoundedSource(
        string sourceNodeXml,
        string expectedName,
        IReadOnlySet<string> allowedChildren)
    {
        try
        {
            XElement source = XElement.Parse(sourceNodeXml ?? string.Empty, LoadOptions.None);
            if (source.Name.NamespaceName.Length != 0
                || !string.Equals(source.Name.LocalName, expectedName, StringComparison.Ordinal)
                || source.HasAttributes
                || source.Elements().Any(child => child.Name.NamespaceName.Length != 0)
                || source.Elements().Any(child => !allowedChildren.Contains(child.Name.LocalName))
                || source.Elements().Any(child => child.Name.LocalName
                       is not ("bonus" or "firstlevelbonus" or "naturalweapons")
                       && (child.HasAttributes || child.Elements().Any()))
                || source.Elements().GroupBy(child => child.Name.LocalName, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
                return null;
            return source;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or XmlException)
        {
            return null;
        }
    }

    private static bool TryReadQualityDefinition(
        XElement source,
        out QualityDefinition definition)
    {
        definition = null!;
        if (!TryReadGuid(source, "id", out Guid sourceId)
            || !TryReadRequiredScalar(source, "name", out string name)
            || !TryReadRequiredScalar(source, "category", out string category)
            || category is not ("Positive" or "Negative")
            || !TryReadRequiredInt(source, "karma", out int karma)
            || category == "Positive" && karma < 0
            || category == "Negative" && karma > 0
            || !TryReadRequiredScalar(source, "source", out string sourceBook)
            || !TryReadRequiredScalar(source, "page", out string page)
            || !TryReadBoolean(source, "implemented", true, out bool implemented)
            || !TryReadBoolean(source, "contributetobp", true, out bool contributeToBp)
            || !TryReadBoolean(source, "contributetolimit", true, out bool contributeToLimit)
            || !TryReadBoolean(source, "stagedpurchase", false, out bool stagedPurchase)
            || !TryReadBoolean(source, "doublecareer", true, out bool doubleCareer)
            || !TryReadBoolean(source, "canbuywithspellpoints", false, out bool spellPoints)
            || !TryReadBoolean(source, "print", true, out bool print)
            || !TryReadBooleanAlias(source, "metagenic", "metagenetic", out bool metagenic)
            || !TryReadMaximumRating(source, out int maximumRating)
            || !TryReadEffectContainer(source.Element("bonus"), out CompiledEffect[] bonusEffects)
            || !TryReadEffectContainer(
                source.Element("firstlevelbonus"),
                out CompiledEffect[] firstLevelEffects)
            || source.Element("naturalweapons") is XElement naturalWeapons
               && (naturalWeapons.HasAttributes
                   || naturalWeapons.Elements().Any()
                   || !string.IsNullOrWhiteSpace(naturalWeapons.Value)))
            return false;

        string notes = ReadOptionalScalar(source, "altnotes")
                       ?? ReadOptionalScalar(source, "notes")
                       ?? string.Empty;
        string notesColor = ReadOptionalScalar(source, "notesColor") ?? DefaultNotesColor;
        if (string.IsNullOrWhiteSpace(notesColor)
            || !string.Equals(notesColor, notesColor.Trim(), StringComparison.Ordinal))
            return false;
        definition = new QualityDefinition(
            sourceId,
            name,
            category == "Positive"
                ? CharacterCreationQualityType.Positive
                : CharacterCreationQualityType.Negative,
            karma,
            maximumRating,
            implemented,
            contributeToBp,
            contributeToLimit,
            stagedPurchase,
            doubleCareer,
            spellPoints,
            metagenic,
            print,
            source.Element("mutant") is not null,
            sourceBook,
            page,
            notes,
            notesColor,
            bonusEffects,
            firstLevelEffects);
        return true;
    }

    private static bool TryReadGearDefinition(XElement source, out GearDefinition definition)
    {
        definition = null!;
        if (!TryReadGuid(source, "id", out Guid sourceId)
            || !TryReadRequiredScalar(source, "name", out string name)
            || !TryReadRequiredScalar(source, "category", out string category)
            || !string.Equals(ReadOptionalScalar(source, "rating"), "0", StringComparison.Ordinal)
            || source.Element("minrating") is not null
            || !TryReadRequiredDecimal(source, "cost", out decimal cost)
            || cost < 0m
            || !TryReadAvailability(source, out string availabilityExpression, out int availability,
                out string legality)
            || !TryReadRequiredScalar(source, "source", out string sourceBook)
            || !TryReadRequiredScalar(source, "page", out string page)
            || !TryReadOptionalPositiveInt(source, "costfor", 1, out int packageQuantity)
            || !TryReadOptionalInt(source, "matrixcmbonus", 0, out int matrixCmBonus)
            || !TryReadOptionalInt(source, "childcostmultiplier", 1,
                out int childCostMultiplier)
            || childCostMultiplier <= 0
            || !TryReadOptionalInt(source, "childavailmodifier", 0,
                out int childAvailabilityModifier)
            || !TryReadBoolean(source, "allowrename", false, out bool allowRename)
            || !TryReadBoolean(source, "stolen", false, out bool stolen)
            || !TryReadBoolean(source, "isflechetteammo", false, out bool isFlechetteAmmo))
            return false;

        string attributeArray = ReadOptionalScalar(source, "attributearray") ?? string.Empty;
        string attack = ReadOptionalScalar(source, "attack") ?? string.Empty;
        string sleaze = ReadOptionalScalar(source, "sleaze") ?? string.Empty;
        string dataProcessing = ReadOptionalScalar(source, "dataprocessing") ?? string.Empty;
        string firewall = ReadOptionalScalar(source, "firewall") ?? string.Empty;
        bool canSwapAttributes = attributeArray.Length != 0;
        if (canSwapAttributes)
        {
            string[] values = attributeArray.Split(',');
            if (values.Length != 4 || values.Any(string.IsNullOrWhiteSpace))
                return false;
            attack = values[0];
            sleaze = values[1];
            dataProcessing = values[2];
            firewall = values[3];
        }

        string notes = ReadOptionalScalar(source, "altnotes")
                       ?? ReadOptionalScalar(source, "notes")
                       ?? string.Empty;
        string notesColor = ReadOptionalScalar(source, "notesColor") ?? DefaultNotesColor;
        if (string.IsNullOrWhiteSpace(notesColor)
            || !string.Equals(notesColor, notesColor.Trim(), StringComparison.Ordinal))
            return false;

        definition = new GearDefinition(
            sourceId,
            name,
            category,
            ReadOptionalScalar(source, "capacity") ?? string.Empty,
            ReadOptionalScalar(source, "armorcapacity") ?? string.Empty,
            availabilityExpression,
            availability,
            legality,
            packageQuantity,
            cost,
            ReadOptionalScalar(source, "cost")!,
            ReadOptionalScalar(source, "weight") ?? string.Empty,
            sourceBook,
            page,
            isFlechetteAmmo,
            ReadOptionalScalar(source, "ammoforweapontype") ?? string.Empty,
            ReadOptionalScalar(source, "canformpersona") ?? string.Empty,
            ReadOptionalScalar(source, "devicerating") ?? string.Empty,
            matrixCmBonus,
            allowRename,
            stolen,
            childCostMultiplier,
            childAvailabilityModifier,
            notes,
            notesColor,
            ReadOptionalScalar(source, "programs") ?? string.Empty,
            attack,
            sleaze,
            dataProcessing,
            firewall,
            attributeArray,
            ReadOptionalScalar(source, "modattack") ?? string.Empty,
            ReadOptionalScalar(source, "modsleaze") ?? string.Empty,
            ReadOptionalScalar(source, "moddataprocessing") ?? string.Empty,
            ReadOptionalScalar(source, "modfirewall") ?? string.Empty,
            ReadOptionalScalar(source, "modattributearray") ?? string.Empty,
            canSwapAttributes);
        return true;
    }

    private static bool TryReadEffectContainer(
        XElement? container,
        out CompiledEffect[] effects)
    {
        effects = [];
        if (container is null)
            return true;
        if (container.HasAttributes
            || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            return false;
        var compiled = new List<CompiledEffect>();
        foreach (XElement effect in container.Elements())
        {
            if (effect.Name.NamespaceName.Length != 0
                || effect.HasAttributes
                || effect.Elements().Any()
                || !string.IsNullOrWhiteSpace(effect.Value))
                return false;
            string improvementType = effect.Name.LocalName switch
            {
                "ambidextrous" => "Ambidextrous",
                "friendsinhighplaces" => "FriendsInHighPlaces",
                "erased" => "Erased",
                "overclocker" => "Overclocker",
                _ => string.Empty
            };
            if (improvementType.Length == 0)
                return false;
            compiled.Add(new CompiledEffect(improvementType, string.Empty, 0m));
        }
        effects = compiled.ToArray();
        return true;
    }

    private static XElement BuildSavedQuality(
        XElement source,
        QualityDefinition definition,
        string qualityId) => new(
        "quality",
        new XElement("sourceid", definition.SourceId.ToString("D", CultureInfo.InvariantCulture)),
        new XElement("guid", qualityId),
        new XElement("name", definition.Name),
        new XElement("extra", string.Empty),
        new XElement("bp", definition.Karma.ToString(CultureInfo.InvariantCulture)),
        new XElement("implemented", LegacyBoolean(definition.Implemented)),
        new XElement("contributetobp", LegacyBoolean(definition.ContributeToBuildPoints)),
        new XElement("contributetolimit", LegacyBoolean(definition.ContributeToLimit)),
        new XElement("stagedpurchase", LegacyBoolean(definition.StagedPurchase)),
        new XElement("doublecareer", LegacyBoolean(definition.DoubleCareer)),
        new XElement("canbuywithspellpoints", LegacyBoolean(definition.CanBuyWithSpellPoints)),
        new XElement("metagenic", LegacyBoolean(definition.Metagenic)),
        new XElement("print", LegacyBoolean(definition.Print)),
        new XElement("qualitytype", definition.Type.ToString()),
        new XElement("qualitysource", "Selected"),
        new XElement("mutant", LegacyBoolean(definition.Mutant)),
        new XElement("source", definition.SourceBook),
        new XElement("page", definition.Page),
        new XElement("sourcename", string.Empty),
        CloneContainer(source, "bonus"),
        CloneContainer(source, "firstlevelbonus"),
        CloneContainer(source, "naturalweapons"),
        new XElement("notes", definition.Notes),
        new XElement("notesColor", definition.NotesColor));

    private static XElement BuildSavedGear(
        GearDefinition definition,
        string gearId,
        int quantity)
    {
        var gear = new XElement(
            "gear",
            new XElement("sourceid", definition.SourceId.ToString("D", CultureInfo.InvariantCulture)),
            new XElement("guid", gearId),
            new XElement("name", definition.Name),
            new XElement("category", definition.Category),
            new XElement("capacity", definition.Capacity),
            new XElement("armorcapacity", definition.ArmorCapacity),
            new XElement("minrating", string.Empty),
            new XElement("maxrating", string.Empty),
            new XElement("rating", "0"),
            new XElement("qty", quantity.ToString(CultureInfo.InvariantCulture)),
            new XElement("avail", definition.AvailabilityExpression));
        if (definition.PackageQuantity > 1)
            gear.Add(new XElement("costfor", definition.PackageQuantity.ToString(CultureInfo.InvariantCulture)));
        gear.Add(
            new XElement("cost", definition.CostExpression),
            new XElement("weight", definition.Weight),
            new XElement("extra", string.Empty),
            new XElement("bonded", "False"),
            new XElement("equipped", "True"),
            new XElement("wirelesson", "False"),
            new XElement("stolen", LegacyBoolean(definition.Stolen)),
            new XElement("bonus", string.Empty),
            new XElement("wirelessbonus", string.Empty),
            new XElement("weaponbonus", string.Empty),
            new XElement("flechetteweaponbonus", string.Empty),
            new XElement("source", definition.SourceBook),
            new XElement("page", definition.Page),
            new XElement("isflechetteammo", LegacyBoolean(definition.IsFlechetteAmmo)),
            new XElement("ammoforweapontype", definition.AmmoForWeaponType),
            new XElement("canformpersona", definition.CanFormPersona),
            new XElement("devicerating", definition.DeviceRating),
            new XElement("gearname", string.Empty),
            new XElement("forcedvalue", string.Empty),
            new XElement("matrixcmfilled", "0"),
            new XElement("matrixcmbonus", definition.MatrixCmBonus.ToString(CultureInfo.InvariantCulture)),
            new XElement("parentid", string.Empty),
            new XElement("allowrename", LegacyBoolean(definition.AllowRename)));
        if (definition.ChildCostMultiplier != 1)
        {
            gear.Add(new XElement("childcostmultiplier",
                definition.ChildCostMultiplier.ToString(CultureInfo.InvariantCulture)));
        }
        if (definition.ChildAvailabilityModifier != 0)
        {
            gear.Add(new XElement("childavailmodifier",
                definition.ChildAvailabilityModifier.ToString(CultureInfo.InvariantCulture)));
        }
        gear.Add(
            new XElement("children"),
            new XElement("location", string.Empty),
            new XElement("notes", definition.Notes),
            new XElement("notesColor", definition.NotesColor),
            new XElement("discountedcost", "False"),
            new XElement("programlimit", definition.ProgramLimit),
            new XElement("overclocked", "None"),
            new XElement("attack", definition.Attack),
            new XElement("sleaze", definition.Sleaze),
            new XElement("dataprocessing", definition.DataProcessing),
            new XElement("firewall", definition.Firewall),
            new XElement("attributearray", definition.AttributeArray),
            new XElement("modattack", definition.ModAttack),
            new XElement("modsleaze", definition.ModSleaze),
            new XElement("moddataprocessing", definition.ModDataProcessing),
            new XElement("modfirewall", definition.ModFirewall),
            new XElement("modattributearray", definition.ModAttributeArray),
            new XElement("canswapattributes", LegacyBoolean(definition.CanSwapAttributes)),
            new XElement("active", "False"),
            new XElement("homenode", "False"),
            new XElement("sortorder", "0"));
        return gear;
    }

    private static XElement BuildImprovement(
        CompiledEffect effect,
        string sourceName,
        string notesColor) => new(
        "improvement",
        new XElement("target", string.Empty),
        new XElement("improvedname", effect.ImprovedName),
        new XElement("sourcename", sourceName),
        new XElement("min", "0"),
        new XElement("max", "0"),
        new XElement("aug", "0"),
        new XElement("augmax", "0"),
        new XElement("val", effect.Value.ToString(CultureInfo.InvariantCulture)),
        new XElement("rating", "1"),
        new XElement("exclude", string.Empty),
        new XElement("condition", string.Empty),
        new XElement("improvementttype", effect.ImprovementType),
        new XElement("improvementsource", "Quality"),
        new XElement("custom", "False"),
        new XElement("customname", string.Empty),
        new XElement("customid", string.Empty),
        new XElement("customgroup", string.Empty),
        new XElement("addtorating", "0"),
        new XElement("enabled", "1"),
        new XElement("order", "0"),
        new XElement("notes", string.Empty),
        new XElement("notesColor", notesColor));

    private static XElement CloneContainer(XElement source, string name) =>
        new(name, source.Element(name)?.Nodes());

    private static bool TryReadMaximumRating(XElement source, out int maximumRating)
    {
        maximumRating = 1;
        if (source.Element("nolevels") is not null)
            return IsEmptyMarker(source.Element("nolevels")!);
        string? limit = ReadOptionalScalar(source, "limit");
        return limit is null
               || string.Equals(limit, "False", StringComparison.OrdinalIgnoreCase)
               || int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture,
                   out maximumRating) && maximumRating is > 0 and <= 100;
    }

    private static bool TryReadAvailability(
        XElement source,
        out string expression,
        out int availability,
        out string legality)
    {
        expression = ReadOptionalScalar(source, "avail") ?? string.Empty;
        availability = 0;
        legality = CharacterCreationGearLegality.Legal;
        if (expression.EndsWith('R'))
        {
            legality = CharacterCreationGearLegality.Restricted;
            expression = expression[..^1] + "R";
        }
        else if (expression.EndsWith('F'))
        {
            legality = CharacterCreationGearLegality.Forbidden;
            expression = expression[..^1] + "F";
        }
        ReadOnlySpan<char> numeric = expression.AsSpan(0, expression.Length
            - (legality == CharacterCreationGearLegality.Legal ? 0 : 1));
        return int.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture,
                   out availability)
               && availability >= 0;
    }

    private static bool TryReadGuid(XElement source, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryReadRequiredScalar(source, name, out string text)
               && Guid.TryParseExact(text, "D", out value)
               && value != Guid.Empty;
    }

    private static bool TryReadRequiredInt(XElement source, string name, out int value)
    {
        value = 0;
        return TryReadRequiredScalar(source, name, out string text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadRequiredDecimal(XElement source, string name, out decimal value)
    {
        value = 0m;
        return TryReadRequiredScalar(source, name, out string text)
               && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalInt(
        XElement source,
        string name,
        int defaultValue,
        out int value)
    {
        string? text = ReadOptionalScalar(source, name);
        value = defaultValue;
        return text is null
               || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalPositiveInt(
        XElement source,
        string name,
        int defaultValue,
        out int value) =>
        TryReadOptionalInt(source, name, defaultValue, out value) && value > 0;

    private static bool TryReadRequiredScalar(
        XElement source,
        string name,
        out string value)
    {
        value = ReadOptionalScalar(source, name) ?? string.Empty;
        return value.Length != 0;
    }

    private static string? ReadOptionalScalar(XElement source, string name)
    {
        XElement? element = source.Element(name);
        if (element is null)
            return null;
        if (element.HasAttributes || element.Elements().Any())
            return null;
        string value = element.Value;
        return string.Equals(value, value.Trim(), StringComparison.Ordinal) ? value : null;
    }

    private static bool TryReadBoolean(
        XElement source,
        string name,
        bool defaultValue,
        out bool value)
    {
        string? raw = ReadOptionalScalar(source, name);
        value = defaultValue;
        return raw is null || bool.TryParse(raw, out value);
    }

    private static bool TryReadBooleanAlias(
        XElement source,
        string current,
        string legacy,
        out bool value)
    {
        if (source.Element(current) is not null && source.Element(legacy) is not null)
        {
            value = false;
            return false;
        }
        string? raw = ReadOptionalScalar(source, current) ?? ReadOptionalScalar(source, legacy);
        value = false;
        return raw is null || bool.TryParse(raw, out value);
    }

    private static bool IsEmptyMarker(XElement element) =>
        !element.HasAttributes && !element.Elements().Any()
        && string.IsNullOrWhiteSpace(element.Value);

    private static string LegacyBoolean(bool value) => value ? "True" : "False";

    private static Guid StableGuid(string seed)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private sealed record QualityDefinition(
        Guid SourceId,
        string Name,
        CharacterCreationQualityType Type,
        int Karma,
        int MaximumRating,
        bool Implemented,
        bool ContributeToBuildPoints,
        bool ContributeToLimit,
        bool StagedPurchase,
        bool DoubleCareer,
        bool CanBuyWithSpellPoints,
        bool Metagenic,
        bool Print,
        bool Mutant,
        string SourceBook,
        string Page,
        string Notes,
        string NotesColor,
        IReadOnlyList<CompiledEffect> BonusEffects,
        IReadOnlyList<CompiledEffect> FirstLevelEffects);

    private sealed record CompiledEffect(
        string ImprovementType,
        string ImprovedName,
        decimal Value);

    private sealed record GearDefinition(
        Guid SourceId,
        string Name,
        string Category,
        string Capacity,
        string ArmorCapacity,
        string AvailabilityExpression,
        int Availability,
        string Legality,
        int PackageQuantity,
        decimal PackageCost,
        string CostExpression,
        string Weight,
        string SourceBook,
        string Page,
        bool IsFlechetteAmmo,
        string AmmoForWeaponType,
        string CanFormPersona,
        string DeviceRating,
        int MatrixCmBonus,
        bool AllowRename,
        bool Stolen,
        int ChildCostMultiplier,
        int ChildAvailabilityModifier,
        string Notes,
        string NotesColor,
        string ProgramLimit,
        string Attack,
        string Sleaze,
        string DataProcessing,
        string Firewall,
        string AttributeArray,
        string ModAttack,
        string ModSleaze,
        string ModDataProcessing,
        string ModFirewall,
        string ModAttributeArray,
        bool CanSwapAttributes);
}
