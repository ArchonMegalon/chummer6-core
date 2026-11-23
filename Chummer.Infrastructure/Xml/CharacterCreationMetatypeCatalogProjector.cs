using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

internal static partial class CharacterCreationMetatypeCatalogProjector
{
    private const string HumanId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7";
    private const string ElfId = "b3259991-b315-4dbe-ae3c-51f71a1116e2";
    private const string OrkId = "8ed6892f-88e6-42d0-a704-b805778ec13e";
    private const string DwarfId = "08f2c9cc-f9f8-4f1a-9efb-63555af71788";
    private const string TrollId = "77fa1ed8-f4e6-4763-9f0b-f125318b9782";

    private static readonly (string Id, string Name)[] SupportedMetatypes =
    [
        (HumanId, "Human"),
        (ElfId, "Elf"),
        (OrkId, "Ork")
    ];

    private static readonly (string Id, string Prefix)[] AttributeFields =
    [
        ("BOD", "bod"),
        ("AGI", "agi"),
        ("REA", "rea"),
        ("STR", "str"),
        ("CHA", "cha"),
        ("INT", "int"),
        ("LOG", "log"),
        ("WIL", "wil"),
        ("EDG", "edg"),
        ("MAG", "mag"),
        ("RES", "res"),
        ("ESS", "ess"),
        ("DEP", "dep")
    ];

    private static readonly IReadOnlySet<string> SupportedBaseFields = new HashSet<string>(
        new[]
        {
            "id", "name", "karma", "category",
            "inimin", "inimax", "iniaug",
            "walk", "run", "sprint",
            "qualities", "bonus", "source", "page", "metavariants"
        }.Concat(AttributeFields.SelectMany(field => new[]
        {
            $"{field.Prefix}min",
            $"{field.Prefix}max",
            $"{field.Prefix}aug"
        })),
        StringComparer.Ordinal);

    // Current v2 catalog observation. Every source row remains visible, while
    // unsupported rows and every variant remain explicitly unselectable. The
    // historical Project method below keeps its original admission contract.
    internal static CharacterCreationMetatypeCatalogAuthority ProjectCurrent(
        XDocument document, CharacterCreationMetatypeSourceContextAuthority sourceContext, string? sourceExecutionSchema = null,
        bool careerCondition5497 = false, bool importedSprite5732 = false)
    {
        if (!sourceContext.IsAuthoritative || sourceContext.Blockers.Count != 0
            || sourceContext.MetatypeKarmaMultiplier is not int multiplier || multiplier < 0
            || sourceContext.MinimumInitiativeDiceFallback is not int dice || dice < 0
            || document.Root is not { Name.LocalName: "chummer" } root
            || root.Name.Namespace != XNamespace.None
            || !root.Attributes().All(CurrentStockXmlMetadata.IsSupported)
            || root.Elements("metatypes").ToArray() is not [var container]
            || container.HasAttributes || container.Elements().Any(row => row.Name != "metatype"))
            return Blocked(sourceContext, CharacterCreationMetatypeCatalogBlockers.AuthorityUnavailable);
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<CharacterCreationMetatypeOptionProjection>();
        foreach (var row in container.Elements())
        {
            // Malformed/ambiguous locators cannot be safely made unavailable.
            if (!TryReadSingle(row, "id", out string id) || !Guid.TryParseExact(id, "D", out var guid)
                || guid == Guid.Empty || !ids.Add(guid) || !TryReadSingle(row, "name", out string name)
                || string.IsNullOrWhiteSpace(name) || !names.Add(name))
                return Blocked(sourceContext, CharacterCreationMetatypeCatalogBlockers.BaseEntryDuplicate);
            if (TryProjectCurrentOption(row, multiplier, dice, sourceContext.EnabledSourcebooks,
                    out var option, sourceExecutionSchema, careerCondition5497, importedSprite5732))
                options.Add(option);
            else
                options.Add(new(guid.ToString("D"), name, Read(row, "category"), Read(row, "source"),
                    0, 0, 0, [], new(0, 0, 0, 0), CharacterCreationMetatypeMovementProjection.Unavailable,
                    [], [], false, [CharacterCreationMetatypeCatalogBlockers.SpecialSemanticsUnsupported],
                    [$"metatypes.xml#metatype:{guid:D}"]));
        }
        return new(CharacterCreationMetatypeCatalogSchemas.CatalogV1, sourceContext,
            options.ToArray(), [], IsAuthoritative: true);
    }

    internal static bool TryProjectCurrentOption(XElement entry, int multiplier, int dice,
        IReadOnlyList<string> books, out CharacterCreationMetatypeOptionProjection option,
        string? sourceExecutionSchema = null, bool careerCondition5497 = false, bool importedSprite5732 = false)
    {
        option = null!;
        string name = Read(entry, "name");
        string id = name switch
        { "Human" => HumanId, "Elf" => ElfId, "Ork" => OrkId, "Dwarf" => DwarfId, "Troll" => TrollId, _ => string.Empty };
        bool sprite = importedSprite5732 && Read(entry, "category").EndsWith("Sprites", StringComparison.Ordinal);
        if (sprite && Guid.TryParseExact(Read(entry, "id"), "D", out var sourceId) && sourceId != Guid.Empty)
            id = sourceId.ToString("D");
        return id.Length != 0 && entry.Name == "metatype" && multiplier >= 0 && dice >= 0
            && !entry.DescendantsAndSelf().Any(node => node.Name.Namespace != XNamespace.None
                || node.Nodes().Any(item => item is not XElement && item is not XComment
                    && (item is not XText text || text.GetType() != typeof(XText)))
                || node.HasElements && node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            && !entry.Elements().Any(element => !SupportedBaseFields.Contains(element.Name.LocalName)
                && !(sourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4 && element.Name == "initiativedice"))
            && !entry.Elements().GroupBy(element => element.Name).Any(group => group.Count() != 1)
            && TryProjectOption(entry, id, name, multiplier, dice, books, id is DwarfId or TrollId,
                out option, sourceExecutionSchema, careerCondition5497, sprite);
    }

    // Life Modules materializes the same source-defined base racial bonuses as
    // Karma through its own whole-character planner. Keep the legacy default
    // and Priority/Sum-to-Ten admission unchanged.
    internal static CharacterCreationMetatypeCatalogAuthority ProjectLegacyForBuildMethod(
        XDocument document, CharacterCreationMetatypeSourceContextAuthority sourceContext,
        string buildMethod, string? sourceExecutionSchema = null)
        => Project(document, sourceContext,
            includeKarmaBaseBonuses: buildMethod is CharacterCreationBuildMethods.Karma
                or CharacterCreationBuildMethods.LifeModules,
            sourceExecutionSchema: sourceExecutionSchema);

    public static CharacterCreationMetatypeCatalogAuthority Project(
        XDocument document,
        CharacterCreationMetatypeSourceContextAuthority sourceContext,
        bool includeKarmaBaseBonuses = false, string? sourceExecutionSchema = null)
    {
        if (!sourceContext.IsAuthoritative
            || sourceContext.MetatypeKarmaMultiplier is not int karmaMultiplier
            || sourceContext.MinimumInitiativeDiceFallback is not int initiativeFallback
            || document.Root is null)
        {
            IReadOnlyList<string> blockers = sourceContext.Blockers.Count == 0
                ? [CharacterCreationMetatypeCatalogBlockers.AuthorityUnavailable]
                : sourceContext.Blockers;
            return new CharacterCreationMetatypeCatalogAuthority(
                CharacterCreationMetatypeCatalogSchemas.CatalogV1,
                sourceContext,
                Array.Empty<CharacterCreationMetatypeOptionProjection>(),
                blockers,
                IsAuthoritative: false);
        }

        XElement[] containers = document.Root.Elements("metatypes").Take(2).ToArray();
        if (containers.Length != 1)
        {
            return Blocked(
                sourceContext,
                CharacterCreationMetatypeCatalogBlockers.BaseEntryInvalid);
        }

        XElement[] entries = containers[0].Elements("metatype").ToArray();
        var catalogBlockers = new List<string>();
        var options = new List<CharacterCreationMetatypeOptionProjection>();
        // Life Modules retains its existing admitted catalog until its own
        // finalizer consumes base-metatype effects. Never advertise a lossy path.
        (string Id, string Name)[] supported = includeKarmaBaseBonuses
            ? [.. SupportedMetatypes, (DwarfId, "Dwarf"), (TrollId, "Troll")]
            : SupportedMetatypes;
        foreach ((string expectedId, string expectedName) in supported)
        {
            XElement[] matches = entries.Where(candidate =>
                    string.Equals(Read(candidate, "id"), expectedId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Read(candidate, "name"), expectedName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
            {
                catalogBlockers.Add(CharacterCreationMetatypeCatalogBlockers.BaseEntryMissing);
                continue;
            }
            if (matches.Length != 1)
            {
                catalogBlockers.Add(CharacterCreationMetatypeCatalogBlockers.BaseEntryDuplicate);
                continue;
            }
            if (matches[0].Elements().Any(element => !SupportedBaseFields.Contains(element.Name.LocalName)
                && !(sourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4 && element.Name == "initiativedice")))
            {
                catalogBlockers.Add(CharacterCreationMetatypeCatalogBlockers.UnknownSemantics);
                continue;
            }
            if (matches[0].Elements()
                .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
            {
                catalogBlockers.Add(CharacterCreationMetatypeCatalogBlockers.BaseEntryDuplicate);
                continue;
            }

            if (!TryProjectOption(
                    matches[0],
                    expectedId,
                    expectedName,
                    karmaMultiplier,
                    initiativeFallback,
                    sourceContext.EnabledSourcebooks,
                    includeKarmaBaseBonuses && expectedId is DwarfId or TrollId,
                    out CharacterCreationMetatypeOptionProjection? option, sourceExecutionSchema))
            {
                catalogBlockers.Add(CharacterCreationMetatypeCatalogBlockers.BaseEntryInvalid);
                continue;
            }
            options.Add(option);
            catalogBlockers.AddRange(option.Blockers);
        }

        string[] distinctBlockers = catalogBlockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (distinctBlockers.Length != 0)
        {
            options = options.Select(option => option with
                {
                    IsEnabled = false,
                    Blockers = option.Blockers
                        .Concat(distinctBlockers)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(item => item, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToList();
        }
        bool isAuthoritative = distinctBlockers.Length == 0
            && options.Count == supported.Length
            && options.All(option => option.IsEnabled);
        return new CharacterCreationMetatypeCatalogAuthority(
            CharacterCreationMetatypeCatalogSchemas.CatalogV1,
            sourceContext,
            options,
            distinctBlockers,
            isAuthoritative);
    }

    internal static bool TryProjectPriorityRacialOption(XElement entry,
        IReadOnlyList<string> enabledSourcebooks, out CharacterCreationMetatypeOptionProjection option, string? sourceExecutionSchema = null)
    {
        option = null!;
        string name = Read(entry, "name");
        string id = name switch { "Elf" => ElfId, "Ork" => OrkId, "Dwarf" => DwarfId, _ => string.Empty };
        // Attribute/modifier-bearing races and variants require their complete
        // budget/write paths before admission. Share the strict source parser.
        // Dwarf is admitted source-bound: its single bonus element must reduce
        // to exactly the 20% lifestyle surcharge (no armor/reach semantics),
        // parsed by the same strict TryReadBaseBonuses grammar the Karma/Life
        // Module paths already use.
        bool dwarfAdmission = id == DwarfId;
        bool allowBonuses = dwarfAdmission;
        if (id.Length != 0
            && !entry.Elements().Any(element => element.Name.Namespace != XNamespace.None
                || !SupportedBaseFields.Contains(element.Name.LocalName)
                && !(sourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4 && element.Name == "initiativedice"))
            && !entry.Elements().GroupBy(element => element.Name).Any(group => group.Count() != 1)
            && TryProjectOption(entry, id, name, 1, 1, enabledSourcebooks, allowBonuses, out option, sourceExecutionSchema)
            && option.IsEnabled && option.Blockers.Count == 0
            && (dwarfAdmission
                ? option.BaseBonuses is { } baseBonuses && IsDwarfLifestyleSurcharge(baseBonuses)
                : option.BaseBonuses is null))
        {
            return true;
        }
        option = null!;
        return false;
    }

    // Source-bound admission predicate: the metatype's parsed base bonuses must
    // be exactly the lifestyle surcharge. Any armor/reach or unexpected extra
    // semantics keep the row excluded (strict grammar, no whitelist shortcut).
    internal static bool IsDwarfLifestyleSurcharge(CharacterCreationMetatypeBaseBonuses bonuses)
    {
        return bonuses.Armor == 0
            && bonuses.Reach == 0
            && bonuses.LifestyleCostPercent == 20
            && bonuses.ConditionOverflow5497 is null;
    }

    private static bool TryProjectOption(
        XElement entry,
        string expectedId,
        string expectedName,
        int karmaMultiplier,
        int initiativeFallback,
        IReadOnlyList<string> enabledSourcebooks,
        bool allowBaseBonuses,
        out CharacterCreationMetatypeOptionProjection option, string? sourceExecutionSchema = null,
        bool careerCondition5497 = false, bool importedSprite5732 = false, bool metavariant = false)
    {
        option = null!;
        var blockers = new List<string>();
        if (entry.HasAttributes
            || !TryReadSingle(entry, "id", out string id)
            || !Guid.TryParseExact(id, "D", out Guid parsedId)
            || parsedId == Guid.Empty
            || !string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase)
            || !TryReadSingle(entry, "name", out string name)
            || !string.Equals(name, expectedName, StringComparison.Ordinal)
            || !TryReadSourceReference999(entry, out string category, out string sourceBook, out int sourcePage, importedSprite5732, metavariant)
            || !TryReadNonNegativeInt(entry, "karma", out int baseKarma)
            || !TryReadAttributes(entry, out CharacterCreationMetatypeAttributeProjection[] attributes)
            || !TryReadAttributeRange(entry, "ini", out int initiativeMinimum, out int initiativeMaximum, out int initiativeAugmented)
            || !TryReadMovement(entry, out CharacterCreationMetatypeMovementProjection? movement)
            || !TryReadQualities(entry, expectedId, out CharacterCreationMetatypeGrantedQualityProjection[] qualities, out string? qualityBlocker)
            || !TryReadMetavariants(entry, expectedId, out CharacterCreationMetatypeExcludedChoice[] excludedMetavariants))
        {
            return false;
        }

        int? directDice = null;
        if (entry.Elements("initiativedice").Any())
        {
            if (sourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                || entry.Elements("initiativedice").DescendantNodes().OfType<XProcessingInstruction>().Any()
                || !TryReadSingle(entry, "initiativedice", out string rawDice)
                || !int.TryParse(rawDice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dice)) return false;
            directDice = dice;
        }
        XElement[] bonuses = entry.Elements("bonus").Take(2).ToArray();
        CharacterCreationMetatypeBaseBonuses? baseBonuses = null;
        if (bonuses.Length != 1 || !TryReadBaseBonuses(bonuses[0], allowBaseBonuses, out baseBonuses, careerCondition5497))
        {
            blockers.Add(CharacterCreationMetatypeCatalogBlockers.SpecialSemanticsUnsupported);
        }
        if (qualityBlocker is not null)
        {
            blockers.Add(qualityBlocker);
        }
        if (!enabledSourcebooks.Contains(sourceBook, StringComparer.OrdinalIgnoreCase))
        {
            blockers.Add(CharacterCreationMetatypeCatalogBlockers.SourceDisabled);
        }

        int karmaCost;
        try
        {
            karmaCost = checked(baseKarma * karmaMultiplier);
        }
        catch (OverflowException)
        {
            return false;
        }

        string anchor = $"metatypes.xml#metatype:{expectedId}";
        string[] distinctBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        option = new CharacterCreationMetatypeOptionProjection(
            OptionId: parsedId.ToString("D"),
            Label: name,
            Category: category,
            SourceBook: sourceBook,
            SourcePage: sourcePage,
            BaseKarma: baseKarma,
            KarmaCost: karmaCost,
            Attributes: attributes,
            Initiative: new CharacterCreationMetatypeInitiativeProjection(
                initiativeMinimum,
                initiativeMaximum,
                initiativeAugmented,
                initiativeFallback) { DirectDice886 = directDice },
            Movement: movement,
            GrantedQualities: qualities,
            ExcludedMetavariants: excludedMetavariants,
            IsEnabled: distinctBlockers.Length == 0,
            Blockers: distinctBlockers,
            SourceAnchorIds: [anchor]) { BaseBonuses = baseBonuses };
        return true;
    }

    private static bool TryReadBaseBonuses(XElement bonus, bool allowed,
        out CharacterCreationMetatypeBaseBonuses? projection, bool careerCondition5497 = false)
    {
        projection = null;
        if (bonus.HasAttributes || bonus.Name != "bonus"
            || bonus.Nodes().Any(node => node is not XElement && (node is not XText text || !string.IsNullOrWhiteSpace(text.Value))))
            return false;
        if (!bonus.HasElements) return true;
        if (bonus.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)) return false;
        int armor = 0, reach = 0, lifestyle = 0;
        int? overflow = null;
        foreach (var effect in bonus.Elements())
        {
            if (careerCondition5497 && effect.Name == "conditionmonitor")
            {
                if (!CharacterCareerMetatypeConditionSource5497.TryReadOverflow(effect, out int overflowValue)) return false;
                overflow = overflowValue;
                continue;
            }
            if (!allowed) return false;
            if (effect.Name.Namespace != XNamespace.None || effect.HasElements
                || !int.TryParse(effect.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value < 0) return false;
            if (effect.Name == "armor")
            {
                // Legacy armor group="0" becomes Improvement.unique="group0".
                if (effect.Attributes().Count() != 1 || effect.Attribute("group")?.Value != "0") return false;
                armor = value;
            }
            else
            {
                if (effect.HasAttributes) return false;
                if (effect.Name == "reach") reach = value;
                else if (effect.Name == "lifestylecost") lifestyle = value;
                else return false;
            }
        }
        projection = new(armor, reach, lifestyle) { ConditionOverflow5497 = overflow };
        return true;
    }

    private static bool TryReadAttributes(
        XElement entry,
        out CharacterCreationMetatypeAttributeProjection[] attributes)
    {
        var result = new List<CharacterCreationMetatypeAttributeProjection>(AttributeFields.Length);
        foreach ((string id, string prefix) in AttributeFields)
        {
            if (!TryReadAttributeRange(entry, prefix, out int minimum, out int maximum, out int augmented))
            {
                attributes = [];
                return false;
            }
            result.Add(new CharacterCreationMetatypeAttributeProjection(id, minimum, maximum, augmented));
        }
        attributes = result.ToArray();
        return true;
    }

    private static bool TryReadAttributeRange(
        XElement entry,
        string prefix,
        out int minimum,
        out int maximum,
        out int augmented)
    {
        minimum = 0;
        maximum = 0;
        augmented = 0;
        return TryReadNonNegativeInt(entry, $"{prefix}min", out minimum)
            && TryReadNonNegativeInt(entry, $"{prefix}max", out maximum)
            && TryReadNonNegativeInt(entry, $"{prefix}aug", out augmented)
            && minimum <= maximum
            && maximum <= augmented;
    }

    private static bool TryReadMovement(
        XElement entry,
        out CharacterCreationMetatypeMovementProjection movement)
    {
        movement = CharacterCreationMetatypeMovementProjection.Unavailable;
        XElement[] special = entry.Elements("movement").Take(2).ToArray();
        if (special.Length != 0)
        {
            if (special.Length != 1
                || special[0].HasAttributes
                || special[0].HasElements
                || !string.Equals(special[0].Value, "Special", StringComparison.Ordinal)
                || entry.Elements("walk").Any()
                || entry.Elements("run").Any()
                || entry.Elements("sprint").Any())
            {
                return false;
            }
            movement = CharacterCreationMetatypeMovementProjection.Special;
            return true;
        }
        if (!TryReadMovementRate(entry, "walk", out CharacterCreationMetatypeMovementRate? walk)
            || !TryReadMovementRate(entry, "run", out CharacterCreationMetatypeMovementRate? run)
            || !TryReadMovementRate(entry, "sprint", out CharacterCreationMetatypeMovementRate? sprint))
        {
            return false;
        }
        movement = new CharacterCreationMetatypeMovementProjection(walk, run, sprint);
        return true;
    }

    private static bool TryReadMovementRate(
        XElement entry,
        string field,
        out CharacterCreationMetatypeMovementRate rate)
    {
        rate = null!;
        if (!TryReadSingle(entry, field, out string raw))
        {
            return false;
        }
        string[] parts = raw.Split('/', StringSplitOptions.None);
        if (parts.Length != 3
            || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal ground)
            || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal swim)
            || !decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fly)
            || ground < 0m
            || swim < 0m
            || fly < 0m)
        {
            return false;
        }
        rate = new CharacterCreationMetatypeMovementRate(ground, swim, fly);
        return true;
    }

    private static bool TryReadQualities(
        XElement entry,
        string metatypeId,
        out CharacterCreationMetatypeGrantedQualityProjection[] qualities,
        out string? blocker)
    {
        qualities = [];
        blocker = null;
        XElement[] containers = entry.Elements("qualities").Take(2).ToArray();
        if (containers.Length > 1)
        {
            return false;
        }
        if (containers.Length == 0)
        {
            return true;
        }
        if (containers[0].HasAttributes
            || containers[0].Elements().Any(element => element.Name.LocalName is not "positive" and not "negative")
            || containers[0].Elements().GroupBy(element => element.Name.LocalName, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            blocker = CharacterCreationMetatypeCatalogBlockers.UnknownSemantics;
            return true;
        }

        var result = new List<CharacterCreationMetatypeGrantedQualityProjection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement polarity in containers[0].Elements())
        {
            if (polarity.HasAttributes || polarity.Elements().Any(child => child.Name.LocalName != "quality"))
            {
                blocker = CharacterCreationMetatypeCatalogBlockers.UnknownSemantics;
                return true;
            }
            foreach (XElement quality in polarity.Elements("quality"))
            {
                if (quality.Attribute("select") is not null)
                {
                    blocker = CharacterCreationMetatypeCatalogBlockers.SelectorSemanticsUnsupported;
                    return true;
                }
                if (quality.HasAttributes || quality.Elements().Any())
                {
                    blocker = CharacterCreationMetatypeCatalogBlockers.SpecialSemanticsUnsupported;
                    return true;
                }
                string name = quality.Value.Trim();
                string key = $"{polarity.Name.LocalName}:{name}";
                if (name.Length == 0 || !seen.Add(key))
                {
                    return false;
                }
                result.Add(new CharacterCreationMetatypeGrantedQualityProjection(
                    name,
                    polarity.Name.LocalName,
                    [$"metatypes.xml#metatype:{metatypeId}/qualities/{polarity.Name.LocalName}:{name}"]));
            }
        }
        qualities = result.ToArray();
        return true;
    }

    private static bool TryReadMetavariants(
        XElement entry,
        string metatypeId,
        out CharacterCreationMetatypeExcludedChoice[] variants)
    {
        variants = [];
        XElement[] containers = entry.Elements("metavariants").Take(2).ToArray();
        if (containers.Length > 1)
        {
            return false;
        }
        if (containers.Length == 0)
        {
            return true;
        }

        var result = new List<CharacterCreationMetatypeExcludedChoice>();
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement variant in containers[0].Elements())
        {
            if (variant.Name.LocalName != "metavariant"
                || !TryReadSingle(variant, "id", out string id)
                || !Guid.TryParseExact(id, "D", out Guid parsedId)
                || parsedId == Guid.Empty
                || !ids.Add(parsedId)
                || !TryReadSingle(variant, "name", out string name)
                || string.IsNullOrWhiteSpace(name)
                || !names.Add(name))
            {
                return false;
            }
            string sourceBook = Read(variant, "source");
            int? sourcePage = TryReadPositiveInt(variant, "page", out int page) ? page : null;
            result.Add(new CharacterCreationMetatypeExcludedChoice(
                parsedId.ToString("D"),
                name,
                sourceBook,
                sourcePage,
                [CharacterCreationMetatypeCatalogBlockers.MetavariantUnsupported],
                [$"metatypes.xml#metatype:{metatypeId}/metavariant:{parsedId:D}"]));
        }
        variants = result.ToArray();
        return true;
    }

    private static CharacterCreationMetatypeCatalogAuthority Blocked(
        CharacterCreationMetatypeSourceContextAuthority sourceContext,
        string blocker)
        => new(
            CharacterCreationMetatypeCatalogSchemas.CatalogV1,
            sourceContext,
            Array.Empty<CharacterCreationMetatypeOptionProjection>(),
            [blocker],
            IsAuthoritative: false);

    // The catalogue and Complete source accounting share the same admission
    // category and citation readers. This does not project a Karma-method cost
    // or reinterpret the versioned initiative fields for a Priority character.
    internal static bool TryReadSourceReference999(XElement entry,
        out string category, out string sourceBook, out int sourcePage, bool importedSprite5732 = false, bool metavariant = false)
    {
        category = sourceBook = string.Empty;
        sourcePage = 0;
        return TryReadSingle(entry, "category", out category)
            && (string.Equals(category, "Metahuman", StringComparison.Ordinal)
                || importedSprite5732 && category.EndsWith("Sprites", StringComparison.Ordinal)
                || metavariant && category == "Metavariant")
            && TryReadSingle(entry, "source", out sourceBook)
            && TryReadPositiveInt(entry, "page", out sourcePage);
    }

    private static bool TryReadNonNegativeInt(XElement parent, string field, out int value)
    {
        value = 0;
        return TryReadSingle(parent, field, out string raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }

    private static bool TryReadPositiveInt(XElement parent, string field, out int value)
        => TryReadNonNegativeInt(parent, field, out value) && value > 0;

    private static bool TryReadSingle(XElement parent, XName field, out string value)
    {
        value = string.Empty;
        XElement[] elements = parent.Elements(field).Take(2).ToArray();
        if (elements.Length != 1 || elements[0].HasAttributes || elements[0].Elements().Any())
        {
            return false;
        }
        value = elements[0].Value.Trim();
        return value.Length != 0;
    }

    private static string Read(XElement parent, XName field)
        => parent.Element(field)?.Value.Trim() ?? string.Empty;
}
