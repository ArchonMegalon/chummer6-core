using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Bounded, fixed-source purchase math shared by live and historical admission.</summary>
public static partial class CharacterCreationLoadoutRules
{
    public const int MaximumSourceNodeLength = 65_536;
    public const int MaximumBasketLines = 128;
    public const int MaximumQuantityPerLine = 20;
    private const int MaximumSelectedOptions = MaximumBasketLines * 2;
    private static readonly string[] s_RequiredTextFields = ["name", "category", "source", "page"];

    private static readonly HashSet<string> s_CommonFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "category", "avail", "cost", "source", "page", "altpage",
        "weight", "altnotes", "notes", "notesColor", "stolen", "rating", "ratinglabel"
    };
    private static readonly HashSet<string> s_WeaponFields = new(StringComparer.Ordinal)
    {
        "type", "conceal", "accuracy", "reach", "damage", "ap", "mode", "rc", "ammo",
        "accessorymounts", "spec", "spec2", "ammocategory", "range", "alternaterange",
        "weapontype", "sizecategory", "useskill", "useskillspec", "allowaccessory", "requireammo"
    };
    private static readonly HashSet<string> s_ArmorFields = new(StringComparer.Ordinal)
    { "armor", "armoroverride", "armorcapacity", "encumbrance" };

    public static string Compute<T>(T value) => CharacterCreationFinalizationDigest.Compute(value);
    public static string ComputeSourceNodeDigest(string xml) => Compute(new
    { Schema = "chummer.sr5.creation-loadout.source-node.v1", Xml = xml });
    public static string ComputeSourceCategoryDigest(string xml) => Compute(new
    { Schema = "chummer.sr5.creation-loadout.source-category.v1", Xml = xml });
    public static string ComputeOptionDigest(CharacterCreationLoadoutCatalogOption value)
        => Compute(value with { OptionDigest = string.Empty });
    public static string ComputeLineDigest(CharacterCreationLoadoutLine value)
        => Compute(value with { LineDigest = string.Empty });
    public static string ComputeAuthorityDigest(CharacterCreationLoadoutAuthority value)
        => Compute(value with { AuthorityDigest = string.Empty });
    public static string ComputeSelectedAuthorityDigest(CharacterCreationSelectedLoadoutAuthority value)
        => Compute(value with { WitnessDigest = string.Empty });

    public static bool IsValidAuthority(CharacterCreationLoadoutAuthority? authority)
        => authority is not null && authority.IsAuthoritative
           && authority.Schema == CharacterCreationLoadoutSchemas.AuthorityV1
           && ValidBinding(authority.RulesetId, authority.SettingsProfileId,
               authority.MaximumAvailability, authority.MaximumBasketLines, authority.MaximumQuantityPerLine,
               authority.SourceAnchorIds, authority.SourceDigest, authority.ProfileDigest,
               authority.RulesDigest, authority.RuntimeDigest, authority.AuthorityDigest)
           && authority.Blockers is { Count: 0 }
           && authority.Options is { Count: > 0 and <= 65_536 }
           && ValidOptions(authority.Options, authority.MaximumAvailability)
           && Equal(authority.AuthorityDigest, ComputeAuthorityDigest(authority));

    public static bool IsValidOption(CharacterCreationLoadoutCatalogOption? option)
    {
        if (option is null || option.Kind is not (CharacterCreationLoadoutKinds.Weapon or CharacterCreationLoadoutKinds.Armor)
            || option.SourceId == Guid.Empty || option.OptionId != $"{option.Kind}:{option.SourceId:D}"
            || string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Category)
            || option.UnitCost < 0m || option.Availability < 0
            || option.Legality is not (CharacterCreationGearLegality.Legal or CharacterCreationGearLegality.Restricted
                or CharacterCreationGearLegality.Forbidden)
            || option.Blockers is null || option.Blockers.Any(string.IsNullOrWhiteSpace)
            || option.IsSelectable != (option.Blockers.Count == 0)
            || option.SourceAnchorIds is not { Count: > 0 } || option.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || !Digest(option.SourceNodeDigest) || !Digest(option.SourceCategoryDigest) || !Digest(option.OptionDigest)
            || !Equal(option.SourceNodeDigest, ComputeSourceNodeDigest(option.SourceNodeXml))
            || !Equal(option.SourceCategoryDigest, ComputeSourceCategoryDigest(option.SourceCategoryXml))
            || !TryReadNode(option.SourceNodeXml, option.Kind, out XElement? source)
            || !Guid.TryParse(Scalar(source!, "id"), out Guid sourceId) || sourceId != option.SourceId
            || Scalar(source!, "name") != option.Name || Scalar(source!, "category") != option.Category
            || Scalar(source!, "source") != option.SourceBook || Scalar(source!, "page") != option.Page)
            return false;
        if (option.PricingIsExact && (!TryReadFixedCost(Scalar(source!, "cost"), out decimal cost) || cost != option.UnitCost))
            return false;
        if (option.AvailabilityIsExact && (!TryReadAvailability(Scalar(source!, "avail"), out int availability,
                out string legality) || availability != option.Availability || legality != option.Legality))
            return false;
        return (!option.IsSelectable || option.PricingIsExact && option.AvailabilityIsExact
                && IsSourceProjectable(option.Kind, option.SourceNodeXml, option.SourceCategoryXml))
               && Equal(option.OptionDigest, ComputeOptionDigest(option));
    }

    public static bool IsValidLine(CharacterCreationLoadoutLine? line)
    {
        if (line is null || line.Quantity is < 1 or > MaximumQuantityPerLine
            || line.Kind == CharacterCreationLoadoutKinds.Armor && line.Quantity != 1
            || !Digest(line.LineDigest)) return false;
        try
        {
            if (line.TotalCost != checked(line.UnitCost * line.Quantity)) return false;
            var option = new CharacterCreationLoadoutCatalogOption(line.OptionId, line.Kind, line.SourceId,
                line.Name, line.Category, line.UnitCost, line.Availability, line.Legality, line.SourceBook,
                line.Page, true, true, true, [], line.SourceAnchorIds, line.SourceNodeXml, line.SourceNodeDigest,
                line.SourceCategoryXml, line.SourceCategoryDigest, string.Empty);
            option = option with { OptionDigest = ComputeOptionDigest(option) };
            return IsValidOption(option) && Equal(line.LineDigest, ComputeLineDigest(line));
        }
        catch (OverflowException) { return false; }
    }

    public static bool TryProjectBasket(CharacterCreationLoadoutSelections? selections,
        CharacterCreationLoadoutAuthority? authority, out CharacterCreationLoadoutLine[] lines,
        out decimal cost, out string[] blockers)
        => Project(selections, authority?.Options, authority?.MaximumBasketLines ?? 0,
            authority?.MaximumQuantityPerLine ?? 0, IsValidAuthority(authority), out lines, out cost, out blockers);

    public static bool TryCaptureSelectedAuthority(CharacterCreationLoadoutAuthority authority,
        CharacterCreationLoadoutSelections originalSelections, CharacterCreationLoadoutSelections proposedSelections,
        out CharacterCreationSelectedLoadoutAuthority? selected)
    {
        selected = null;
        if (authority is null || originalSelections is null || proposedSelections is null) return false;
        try
        {
            // Retain the very arrays being validated, not caller-owned mutable lists.
            authority = JsonSerializer.Deserialize<CharacterCreationLoadoutAuthority>(JsonSerializer.SerializeToUtf8Bytes(authority))!;
            var original = CopySelections(originalSelections);
            var proposed = CopySelections(proposedSelections);
            if (!TryProjectBasket(original, authority, out _, out _, out _)
                || !TryProjectBasket(proposed, authority, out _, out _, out _)) return false;
            HashSet<string> required = Ids(original).Concat(Ids(proposed)).ToHashSet(StringComparer.Ordinal);
            var candidate = new CharacterCreationSelectedLoadoutAuthority(
                CharacterCreationSelectedLoadoutAuthority.SchemaV1, authority.AuthorityDigest,
                authority.RulesetId, authority.SettingsProfileId, authority.MaximumAvailability,
                authority.MaximumBasketLines, authority.MaximumQuantityPerLine, original, proposed,
                authority.Options.Where(option => required.Contains(option.OptionId)).OrderBy(option => option.OptionId,
                    StringComparer.Ordinal).ToArray(), authority.SourceAnchorIds.ToArray(), authority.SourceDigest,
                authority.ProfileDigest, authority.RulesDigest, authority.RuntimeDigest, string.Empty);
            candidate = candidate with { WitnessDigest = ComputeSelectedAuthorityDigest(candidate) };
            if (!IsValidSelectedAuthority(candidate)) return false;
            selected = candidate;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { return false; }
    }

    public static bool IsValidSelectedAuthority(CharacterCreationSelectedLoadoutAuthority? selected)
    {
        if (selected is null || selected.Schema != CharacterCreationSelectedLoadoutAuthority.SchemaV1
            || !ValidBinding(selected.RulesetId, selected.SettingsProfileId, selected.MaximumAvailability,
                selected.MaximumBasketLines, selected.MaximumQuantityPerLine, selected.SourceAnchorIds,
                selected.SourceDigest, selected.ProfileDigest, selected.RulesDigest, selected.RuntimeDigest,
                selected.AuthorityDigest)
            || selected.Options is not { Count: <= MaximumSelectedOptions }
            || !ValidOptions(selected.Options, selected.MaximumAvailability)
            || !Digest(selected.WitnessDigest)
            || !ValidSelections(selected.OriginalSelections, selected.MaximumBasketLines, selected.MaximumQuantityPerLine)
            || !ValidSelections(selected.ProposedSelections, selected.MaximumBasketLines, selected.MaximumQuantityPerLine)) return false;
        HashSet<string> required = Ids(selected.OriginalSelections).Concat(Ids(selected.ProposedSelections)).ToHashSet(StringComparer.Ordinal);
        if (!required.SetEquals(selected.Options.Select(option => option.OptionId))) return false;
        return Project(selected.OriginalSelections, selected.Options, selected.MaximumBasketLines,
                   selected.MaximumQuantityPerLine, true, out _, out _, out _)
               && Project(selected.ProposedSelections, selected.Options, selected.MaximumBasketLines,
                   selected.MaximumQuantityPerLine, true, out _, out _, out _)
               && Equal(selected.WitnessDigest, ComputeSelectedAuthorityDigest(selected));
    }

    public static bool TryProjectSelectedBasket(CharacterCreationLoadoutSelections? selections,
        CharacterCreationSelectedLoadoutAuthority? selected, out CharacterCreationLoadoutLine[] lines,
        out decimal cost, out string[] blockers)
        => Project(selections, selected?.Options, selected?.MaximumBasketLines ?? 0,
            selected?.MaximumQuantityPerLine ?? 0, IsValidSelectedAuthority(selected)
                && (Equal(Compute(selections), Compute(selected!.OriginalSelections))
                    || Equal(Compute(selections), Compute(selected.ProposedSelections))), out lines, out cost, out blockers);

    private static bool Project(CharacterCreationLoadoutSelections? selections,
        IReadOnlyList<CharacterCreationLoadoutCatalogOption>? options, int maximumLines, int maximumQuantity,
        bool authorityValid, out CharacterCreationLoadoutLine[] lines, out decimal cost, out string[] blockers)
    {
        lines = []; cost = 0m;
        if (!authorityValid)
        { blockers = [CharacterCreationLoadoutBlockers.AuthorityUnavailable]; return false; }
        if (!ValidSelections(selections, maximumLines, maximumQuantity))
        { blockers = [CharacterCreationLoadoutBlockers.InvalidBasket]; return false; }
        var result = new List<CharacterCreationLoadoutLine>();
        var findings = new List<string>();
        var catalog = options!.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        foreach (var selection in selections!.Weapons.Select(item => (item.OptionId, item.Quantity, Kind: CharacterCreationLoadoutKinds.Weapon))
                     .Concat(selections.Armor.Select(item => (item.OptionId, item.Quantity, Kind: CharacterCreationLoadoutKinds.Armor))))
        {
            if (!catalog.TryGetValue(selection.OptionId, out var option) || option.Kind != selection.Kind)
            { findings.Add(CharacterCreationLoadoutBlockers.InvalidOption); continue; }
            if (!option.IsSelectable)
            { findings.AddRange(option.Blockers); continue; }
            try
            {
                decimal total = checked(option.UnitCost * selection.Quantity);
                var line = new CharacterCreationLoadoutLine(option.OptionId, option.Kind, option.SourceId,
                    option.Name, option.Category, selection.Quantity, option.UnitCost, total, option.Availability,
                    option.Legality, option.SourceBook, option.Page, option.SourceAnchorIds.ToArray(),
                    option.SourceNodeXml, option.SourceNodeDigest, option.SourceCategoryXml, option.SourceCategoryDigest, string.Empty);
                result.Add(line with { LineDigest = ComputeLineDigest(line) });
                cost = checked(cost + total);
            }
            catch (OverflowException) { findings.Add(CharacterCreationLoadoutBlockers.InvalidBasket); }
        }
        blockers = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (blockers.Length != 0) { cost = 0m; return false; }
        lines = result.OrderBy(line => line.OptionId, StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool ValidSelections(CharacterCreationLoadoutSelections? selections, int maxLines, int maxQuantity)
        => selections?.Weapons is { } weapons && selections.Armor is { Count: <= 1 } armor
           && weapons.Count <= maxLines && armor.Count <= maxLines - weapons.Count
           && weapons.All(item => item is not null && !string.IsNullOrWhiteSpace(item.OptionId)
               && item.Quantity > 0 && item.Quantity <= maxQuantity)
           && armor.All(item => item is not null && !string.IsNullOrWhiteSpace(item.OptionId) && item.Quantity == 1)
           && Ids(selections).Distinct(StringComparer.Ordinal).Count() == weapons.Count + armor.Count;

    private static IEnumerable<string> Ids(CharacterCreationLoadoutSelections selections)
        => selections.Weapons.Select(item => item.OptionId).Concat(selections.Armor.Select(item => item.OptionId));
    private static CharacterCreationLoadoutSelections CopySelections(CharacterCreationLoadoutSelections source)
        => new(source.Weapons.ToArray(), source.Armor.ToArray());
    private static bool ValidOptions(IReadOnlyList<CharacterCreationLoadoutCatalogOption> options, int maxAvailability)
        => options.All(option => IsValidOption(option) && (!option.IsSelectable || option.Availability <= maxAvailability))
           && options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() == options.Count;
    private static bool ValidBinding(string ruleset, string profile, int availability, int maxLines, int maxQuantity,
        IReadOnlyList<string>? anchors, params string[] digests)
        => ruleset == "sr5" && !string.IsNullOrWhiteSpace(profile) && availability >= 0
           && maxLines is > 0 and <= MaximumBasketLines && maxQuantity is > 0 and <= MaximumQuantityPerLine
           && anchors is { Count: > 0 } && anchors.All(anchor => !string.IsNullOrWhiteSpace(anchor))
           && digests.All(Digest);
    private static bool Digest(string? value) => CharacterCreationFinalizationDigest.IsCanonical(value);
    private static bool Equal(string? left, string? right) => CharacterCreationFinalizationDigest.EqualsFixedTime(left, right);

    /// <summary>Only plain, unloaded firearms and one plain armor are supported in v1.
    /// Every unknown source child, expression, intrinsic child or improvement is rejected.</summary>
    public static bool IsSourceProjectable(string kind, string xml, string categoryXml)
    {
        if (!TryReadNode(xml, kind, out XElement? source) || source!.HasAttributes
            || !TryReadNode(categoryXml, "category", out XElement? category)
            || category!.HasElements || category.Value != Scalar(source, "category")
            || category.Attributes().Any(attribute => attribute.Name.NamespaceName.Length != 0
                || attribute.Name.LocalName is not ("type" or "blackmarket" or "gunneryspec" or "translate"))
            || !Guid.TryParse(Scalar(source, "id"), out Guid id) || id == Guid.Empty
            || s_RequiredTextFields.Any(field => string.IsNullOrWhiteSpace(Scalar(source, field)))
            || !TryReadFixedCost(Scalar(source, "cost"), out _)
            || !TryReadAvailability(Scalar(source, "avail"), out _, out _)
            || source.Elements().GroupBy(element => element.Name).Any(group => group.Count() != 1)
            || source.Elements().Any(element => element.Name.NamespaceName.Length != 0
                || !s_CommonFields.Contains(element.Name.LocalName)
                    && !(kind == CharacterCreationLoadoutKinds.Weapon ? s_WeaponFields : s_ArmorFields).Contains(element.Name.LocalName))
            || source.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || source.Elements().Any(element => element.Name.LocalName != "accessorymounts"
                && (element.HasElements || element.HasAttributes && element.Name.LocalName != "range"))
            || Scalar(source, "rating") is not ("" or "0")
            || source.Element("notesColor") is { } notesColor
                && !Regex.IsMatch(notesColor.Value, "^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)
            || !OptionalBoolean(source, "stolen")) return false;
        if (kind == CharacterCreationLoadoutKinds.Armor)
            return NonnegativeInteger(Scalar(source, "armor")) && NonnegativeInteger(Scalar(source, "armorcapacity"))
                   && (Scalar(source, "armoroverride") == "" || NonnegativeInteger(Scalar(source, "armoroverride")))
                   && OptionalBoolean(source, "encumbrance");
        if (kind != CharacterCreationLoadoutKinds.Weapon || Scalar(source, "type") != "Ranged"
            || (Scalar(source, "weapontype") is { Length: > 0 } weaponType ? weaponType : (string?)category.Attribute("type")) != "gun"
            || !NonnegativeInteger(Scalar(source, "accuracy")) || !NonnegativeInteger(Scalar(source, "rc"))
            || Scalar(source, "reach") != "0" || !Integer(Scalar(source, "conceal"))
            || !Regex.IsMatch(Scalar(source, "damage"), "^[0-9]+[PS]$", RegexOptions.CultureInvariant)
            || !NonnegativeInteger(Scalar(source, "damage")[..^1])
            || !(Scalar(source, "ap") == "-" || Integer(Scalar(source, "ap")))
            || !Regex.IsMatch(Scalar(source, "ammo"), "^[1-9][0-9]*\\((c|cy|b|m|d)\\)$", RegexOptions.CultureInvariant)
            || !NonnegativeInteger(Scalar(source, "ammo").Split('(')[0])
            || !Scalar(source, "mode").Split('/').All(mode => mode is "SS" or "SA" or "BF" or "FA")
            || !OptionalBoolean(source, "allowaccessory") || !OptionalBoolean(source, "requireammo")
            || source.Element("requireammo") is { } requireAmmo && !bool.Parse(requireAmmo.Value)) return false;
        XElement? range = source.Element("range");
        if (range is not null && (range.Attributes().Any(attribute => attribute.Name != "multiply")
                || range.Attribute("multiply") is { } multiply
                && (!decimal.TryParse(multiply.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal factor)
                    || factor <= 0m))) return false;
        XElement? mounts = source.Element("accessorymounts");
        return mounts is null || !mounts.HasAttributes
            && !mounts.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            && mounts.Elements().All(mount => mount.Name == "mount" && !mount.HasAttributes && !mount.HasElements
                && !string.IsNullOrWhiteSpace(mount.Value) && !mount.Value.Contains('/'));
    }

    public static bool TryReadFixedCost(string text, out decimal cost)
        => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out cost) && cost >= 0m;
    public static bool TryReadAvailability(string text, out int availability, out string legality)
    {
        availability = 0; legality = CharacterCreationGearLegality.Legal;
        if (string.IsNullOrEmpty(text)) return false;
        char suffix = text[^1];
        if (suffix is 'R' or 'F')
        {
            legality = suffix == 'R' ? CharacterCreationGearLegality.Restricted : CharacterCreationGearLegality.Forbidden;
            text = text[..^1];
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out availability) && availability >= 0;
    }

    private static bool TryReadNode(string? xml, string kind, [NotNullWhen(true)] out XElement? source)
    {
        source = null;
        if (xml is not { Length: > 0 and <= MaximumSourceNodeLength }) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumSourceNodeLength });
            var document = XDocument.Load(reader);
            source = document.Root;
            return source is not null && source.Name == kind
                && !source.DescendantsAndSelf().Any(node => node.Name.NamespaceName.Length != 0);
        }
        catch (XmlException) { return false; }
    }

    private static string Scalar(XElement source, string name)
        => source.Elements(name).ToArray() is [var value] && !value.HasElements ? value.Value : string.Empty;
    private static bool Integer(string value) => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    private static bool NonnegativeInteger(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 0;
    private static bool OptionalBoolean(XElement source, string field)
        => source.Element(field) is null || bool.TryParse(Scalar(source, field), out _);
}
