using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

internal static class CharacterCreationMetatypeCatalogProjector
{
    private const string HumanId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7";
    private const string ElfId = "b3259991-b315-4dbe-ae3c-51f71a1116e2";

    private static readonly (string Id, string Name)[] SupportedMetatypes =
    [
        (HumanId, "Human"),
        (ElfId, "Elf")
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

    public static CharacterCreationMetatypeCatalogAuthority Project(
        XDocument document,
        CharacterCreationMetatypeSourceContextAuthority sourceContext)
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
        foreach ((string expectedId, string expectedName) in SupportedMetatypes)
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
            if (matches[0].Elements().Any(element => !SupportedBaseFields.Contains(element.Name.LocalName)))
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
                    out CharacterCreationMetatypeOptionProjection? option))
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
            && options.Count == SupportedMetatypes.Length
            && options.All(option => option.IsEnabled);
        return new CharacterCreationMetatypeCatalogAuthority(
            CharacterCreationMetatypeCatalogSchemas.CatalogV1,
            sourceContext,
            options,
            distinctBlockers,
            isAuthoritative);
    }

    private static bool TryProjectOption(
        XElement entry,
        string expectedId,
        string expectedName,
        int karmaMultiplier,
        int initiativeFallback,
        IReadOnlyList<string> enabledSourcebooks,
        out CharacterCreationMetatypeOptionProjection option)
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
            || !TryReadSingle(entry, "category", out string category)
            || !string.Equals(category, "Metahuman", StringComparison.Ordinal)
            || !TryReadNonNegativeInt(entry, "karma", out int baseKarma)
            || !TryReadSingle(entry, "source", out string sourceBook)
            || !TryReadPositiveInt(entry, "page", out int sourcePage)
            || !TryReadAttributes(entry, out CharacterCreationMetatypeAttributeProjection[] attributes)
            || !TryReadAttributeRange(entry, "ini", out int initiativeMinimum, out int initiativeMaximum, out int initiativeAugmented)
            || !TryReadMovement(entry, out CharacterCreationMetatypeMovementProjection? movement)
            || !TryReadQualities(entry, expectedId, out CharacterCreationMetatypeGrantedQualityProjection[] qualities, out string? qualityBlocker)
            || !TryReadMetavariants(entry, expectedId, out CharacterCreationMetatypeExcludedChoice[] excludedMetavariants))
        {
            return false;
        }

        XElement[] bonuses = entry.Elements("bonus").Take(2).ToArray();
        if (bonuses.Length != 1 || bonuses[0].HasAttributes || bonuses[0].Nodes().Any())
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
                initiativeFallback),
            Movement: movement,
            GrantedQualities: qualities,
            ExcludedMetavariants: excludedMetavariants,
            IsEnabled: distinctBlockers.Length == 0,
            Blockers: distinctBlockers,
            SourceAnchorIds: [anchor]);
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
        movement = null!;
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
