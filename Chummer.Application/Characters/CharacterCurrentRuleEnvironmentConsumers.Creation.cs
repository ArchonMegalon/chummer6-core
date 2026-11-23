using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Read results only. Neither result can authorize a purchase, finalization or
// workspace mutation. Missing original choices remain explicit in the quote.
public sealed class CharacterCurrentCreationMetatypeQuote
{
    internal CharacterCurrentCreationMetatypeQuote(string contextDigest, string catalogDigest,
        CharacterCreationMetatypeOptionProjection metatype, string[] qualityXml, string[] improvementXml)
    {
        ContextDigest = contextDigest; CatalogDigest = catalogDigest; Metatype = metatype;
        GrantedQualityXml = Array.AsReadOnly(qualityXml); ImprovementXml = Array.AsReadOnly(improvementXml);
    }
    public string ContextDigest { get; }
    public string CatalogDigest { get; }
    public CharacterCreationMetatypeOptionProjection Metatype { get; }
    public IReadOnlyList<string> GrantedQualityXml { get; }
    public IReadOnlyList<string> ImprovementXml { get; }
}

public sealed class CharacterCurrentCreationSelectionQuote
{
    internal CharacterCurrentCreationSelectionQuote(string contextDigest, string catalogDigest,
        IReadOnlyList<CharacterCreationPriorityAssignment> assignments, CharacterCreationPriorityHeritageSelection? heritage,
        CharacterCreationPriorityTalentSelection? talent, IReadOnlyList<string> missingChoices, int? normal, int? special)
    {
        ContextDigest = contextDigest; CatalogDigest = catalogDigest;
        Assignments = Array.AsReadOnly(assignments.ToArray()); Heritage = heritage; Talent = talent;
        MissingChoices = Array.AsReadOnly(missingChoices.ToArray());
        EffectiveNormalAttributePoints = normal; TotalSpecialAttributePoints = special;
    }
    public string ContextDigest { get; }
    public string CatalogDigest { get; }
    public IReadOnlyList<CharacterCreationPriorityAssignment> Assignments { get; }
    public CharacterCreationPriorityHeritageSelection? Heritage { get; }
    public CharacterCreationPriorityTalentSelection? Talent { get; }
    public IReadOnlyList<string> MissingChoices { get; }
    public int? EffectiveNormalAttributePoints { get; }
    public int? TotalSpecialAttributePoints { get; }
}

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static bool UsesStockLifestyles2678(CharacterCurrentRuleEnvironmentContext current)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV6
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV6
            || current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV7
                && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV7
            || UsesOwnedPriorityCareer9569(current);

    public static bool TryLifestyleScalar2678(string text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;
        try
        {
            // Existing bounded decimal arithmetic; no variables, XPath, I/O or fallback price.
            value = CharacterCreationEssenceExpression1222.Evaluate(text, new Dictionary<string, decimal>());
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or DivideByZeroException)
        { value = 0m; return false; }
    }

    public static bool HasUnrepresentedLifestyleMechanics2678(XElement row)
        => row.Elements().Any(field => field.Name.LocalName is "areaminimum" or "comfortsminimum" or "securityminimum"
            || field.Name.LocalName is "area" or "comforts" or "security"
                && int.TryParse(field.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int adjustment) && adjustment != 0);

    public static bool RequiresStockLifestyles2678(CharacterCurrentRuleCatalogInput input)
        => input.Entries.Where(entry => entry.State != CharacterCurrentRuleEntryState.Tombstone)
            .Select(entry => ParseCreation1949(entry.Xml)).Any(RequiresStockLifestyleShape2678);

    public static bool RequiresStockLifestyleShape2678(XElement node)
        => node.DescendantsAndSelf().Any(field => field.Name.LocalName is "hide" or "areaminimum" or "comfortsminimum" or "securityminimum"
                    || field.Name.LocalName is "categories" or "cities" && field.HasElements
                    || field.Name == "cost" && field.Parent?.Name == "quality"
                        && !string.IsNullOrWhiteSpace(field.Value)
                        && !decimal.TryParse(field.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                        && TryLifestyleScalar2678(field.Value, out _))
                || node.Name == "lifestyle" && Guid.TryParse(ScalarCreation1949(node, "id"), out var id) && id == Guid.Empty;

    private static bool LifestyleQualitySyntax2678(XElement row)
    {
        // Same declared grammar as V5, plus scalar price expressions and recognized
        // minima. Minima have no native quality contract and always block selection.
        // Invalid numeric leaves are faithfully retained as native unavailable rows.
        var shape = new XElement(row);
        foreach (var field in shape.Elements().ToArray())
        {
            if (field.Name.LocalName is "areaminimum" or "comfortsminimum" or "securityminimum")
            {
                if (field.HasAttributes || field.HasElements || shape.Elements(field.Name).Count() != 1
                    || !int.TryParse(field.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum)
                    || minimum < 0) return false;
                field.Remove();
            }
            else if (field.Name.LocalName is "lp" or "cost" or "multiplier" or "multiplierbaseonly"
                or "area" or "comforts" or "security" or "areamaximum" or "comfortsmaximum" or "securitymaximum")
            {
                if (field.HasAttributes || field.HasElements) return false;
                field.Value = "0"; // Detached syntax probe only; original bytes and numeric projection stay intact.
            }
        }
        return LifestyleQualitySyntax2670(shape);
    }

    private const string CreationGrammar1949 = "current-creation-bounded-kernels.v2";
    private static bool UsesLifestyleQualities2661(CharacterCurrentRuleEnvironmentContext current)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV4
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV4
            || UsesLifestyleAvailability2670(current) || UsesStockLifestyles2678(current);

    // V4 admits the existing scalar monetary-quality kernel, not arbitrary bonus,
    // prerequisite, prompt, advanced-aspect or free-grid mechanics. Every source
    // field is checked against the resolver's canonical projection below.
    private static bool LifestyleQualitySyntax2661(XElement row)
    {
        string[] required = ["id", "name", "category", "source", "page"];
        string[] optional = ["lp", "cost", "multiplier", "multiplierbaseonly"];
        if (row.Name != "quality" || row.HasAttributes || !CreationXmlSafe1950(row)
            || row.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || row.Elements().Any(field => field.HasAttributes || field.HasElements
                || !required.Concat(optional).Contains(field.Name.LocalName, StringComparer.Ordinal))
            || row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)
            || required.Any(name => string.IsNullOrWhiteSpace(ScalarCreation1949(row, name)))) return false;
        return Guid.TryParseExact(ScalarCreation1949(row, "id"), "D", out var id) && id != Guid.Empty
            && (!row.Elements("lp").Any() || int.TryParse(ScalarCreation1949(row, "lp"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            && new[] { "cost", "multiplier", "multiplierbaseonly" }.All(name => !row.Elements(name).Any()
                || decimal.TryParse(ScalarCreation1949(row, name), NumberStyles.Number, CultureInfo.InvariantCulture, out _));
    }

    private static bool LifestyleQualityProjection2661(CharacterCurrentRuleCatalogInput input,
        CharacterCreationLifestylesAuthority authority)
    {
        var rows = input.Entries.Where(entry => entry.State != CharacterCurrentRuleEntryState.Tombstone)
            .SelectMany(entry => ParseCreation1949(entry.Xml).Descendants("qualities").Elements("quality")).ToArray();
        if (rows.Length != authority.QualityOptions.Count
            || rows.Select(row => ScalarCreation1949(row, "id")).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            return false;
        foreach (var row in rows)
        {
            if (!LifestyleQualitySyntax2661(row)) return false;
            var id = Guid.Parse(ScalarCreation1949(row, "id"));
            var options = authority.QualityOptions.Where(option => option.SourceId == id).ToArray();
            string category = ScalarCreation1949(row, "category");
            string type = category switch
            {
                "Positive" => CharacterCreationLifestyleQualityTypes.Positive,
                "Negative" => CharacterCreationLifestyleQualityTypes.Negative,
                "Contracts" => CharacterCreationLifestyleQualityTypes.Contracts,
                _ when category.StartsWith("Entertainment", StringComparison.Ordinal) => CharacterCreationLifestyleQualityTypes.Entertainment,
                _ => string.Empty
            };
            decimal Number(string name) => row.Elements(name).Any() ? DecimalCreation1949(row, name) : 0m;
            if (options is not [var option] || !option.IsSelectable || !option.EligibilityIsExact || option.Blockers.Count != 0
                || option.OptionId != $"lifestyle-quality:{id:D}" || option.Name != ScalarCreation1949(row, "name")
                || option.Category != category || option.QualityType != type || type.Length == 0
                || option.SourceBook != ScalarCreation1949(row, "source") || option.Page != ScalarCreation1949(row, "page")
                || option.LifestylePointCost != Number("lp") || option.FlatCost != Number("cost")
                || option.CostMultiplierPercent != Number("multiplier") || option.BaseCostMultiplierPercent != Number("multiplierbaseonly")
                || option.Area != 0 || option.Comforts != 0 || option.Security != 0 || option.AreaMaximumModifier != 0
                || option.ComfortsMaximumModifier != 0 || option.SecurityMaximumModifier != 0
                || option.AllowedFreeLifestyleNames.Count != 0 || option.SelectionSemantics is not { RequiresText: false, AllowMultiple: false }
                || !option.SourceAnchorIds.SequenceEqual(new[] { $"lifestyles.xml#quality:{id:D}" }, StringComparer.Ordinal)
                || option.OptionDigest != CharacterCreationLifestylesRules.ComputeQualityOptionDigest(option)) return false;
        }
        return true;
    }

    private static bool UsesLifestyleAvailability2670(CharacterCurrentRuleEnvironmentContext current)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV5
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV5;

    // V5 retains the native blocked-option contract. This is deliberately not a
    // general XML mechanic interpreter: expression prices and unmodelled fields
    // still refuse the catalog instead of becoming selectable zero-cost options.
    private static bool LifestyleQualitySyntax2670(XElement row)
    {
        string[] required = ["id", "name", "category", "source", "page"];
        string[] integers = ["lp", "area", "comforts", "security", "areamaximum", "comfortsmaximum", "securitymaximum"];
        string[] decimals = ["cost", "multiplier", "multiplierbaseonly"];
        string[] other = ["allowed", "allowedfreelifestyles", "allowmultiple", "bonus", "required", "forbidden", "implemented", "careeronly"];
        if (row.Name != "quality" || row.HasAttributes || !CreationXmlSafe1950(row)
            || row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)
            || row.Elements().Any(field => !required.Concat(integers).Concat(decimals).Concat(other).Contains(field.Name.LocalName, StringComparer.Ordinal))
            || required.Any(name => row.Element(name) is not { HasAttributes: false, HasElements: false } field
                || string.IsNullOrWhiteSpace(field.Value))
            || !Guid.TryParseExact(ScalarCreation1949(row, "id"), "D", out var id) || id == Guid.Empty) return false;
        foreach (string name in integers.Concat(decimals).Concat(["implemented", "careeronly"]))
        {
            if (row.Element(name) is not { } field) continue;
            if (field.HasAttributes || field.HasElements) return false;
            string value = field.Value.Trim();
            if (integers.Contains(name) && value.Length != 0
                && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
            if (decimals.Contains(name) && value.Length != 0
                && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return false;
            if (name is "implemented" or "careeronly" && value.ToLowerInvariant() is not ("true" or "false" or "yes" or "no" or "1" or "0")) return false;
        }
        // Unknown effects do not acquire an eligibility interpretation. Native
        // selecttext metadata may be retained blocked when its prompt is richer
        // than the existing selection contract can execute.
        return row.Element("bonus") is not { } bonus || !bonus.HasAttributes
            && bonus.Elements().All(field => field.Name == "selecttext");
    }

    private static bool LifestyleQualityProjection2670(CharacterCurrentRuleCatalogInput input,
        CharacterCreationLifestylesAuthority authority, ICharacterSourceDataContext source)
        => LifestyleQualityProjection2678(input, authority, source, false);

    private static bool LifestyleQualityProjection2678(CharacterCurrentRuleCatalogInput input,
        CharacterCreationLifestylesAuthority authority, ICharacterSourceDataContext source, bool stock)
    {
        if (!source.TryResolveCreationSourceProfile(out var profile)) return false;
        var mechanicsSource2684 = source as ICurrentLifestyleMechanicsSource2684;
        bool mechanics2684 = stock && mechanicsSource2684?.HasLifestyleMechanics2684 == true;
        if (mechanics2684 != (authority.Mechanics is not null)) return false;
        if (mechanics2684 && (!mechanicsSource2684!.TryReadLifestyleCharacterFacts2684(out var facts2684)
            || CharacterCreationFinalizationDigest.Compute(facts2684) != CharacterCreationFinalizationDigest.Compute(authority.Mechanics))) return false;
        var rows = input.Entries.Where(entry => entry.State != CharacterCurrentRuleEntryState.Tombstone)
            .SelectMany(entry => ParseCreation1949(entry.Xml).Descendants("qualities").Elements("quality")).ToArray();
        if (rows.Length != authority.QualityOptions.Count
            || rows.Select(row => ScalarCreation1949(row, "id")).Distinct(StringComparer.Ordinal).Count() != rows.Length) return false;
        foreach (var row in rows)
        {
            if (!(stock ? LifestyleQualitySyntax2678(row) : LifestyleQualitySyntax2670(row))) return false;
            var id = Guid.Parse(ScalarCreation1949(row, "id"));
            var options = authority.QualityOptions.Where(option => option.SourceId == id).ToArray();
            if (options is not [var option]) return false;
            string category = ScalarCreation1949(row, "category");
            string type = CharacterCreationLifestyleLocationRules.QualityType(category);
            if (type.Length == 0) return false;
            bool Leaf(XElement field) => !field.HasAttributes && !field.HasElements;
            bool Flag(string name) => ScalarCreation1949(row, name).ToLowerInvariant() is "true" or "yes" or "1";
            bool numericExact = true;
            var integers = new Dictionary<string, int>();
            var numbers = new Dictionary<string, decimal>();
            foreach (string name in new[] { "lp", "area", "comforts", "security", "areamaximum", "comfortsmaximum", "securitymaximum" })
            {
                string value = ScalarCreation1949(row, name);
                int parsed = 0;
                numericExact &= string.IsNullOrWhiteSpace(value) || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
                integers[name] = parsed;
            }
            foreach (string name in new[] { "cost", "multiplier", "multiplierbaseonly" })
            {
                string value = ScalarCreation1949(row, name);
                decimal parsed = 0m;
                numericExact &= string.IsNullOrWhiteSpace(value) || (stock && name == "cost"
                    ? TryLifestyleScalar2678(value, out parsed)
                    : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed));
                numbers[name] = parsed;
            }
            int Integer(string name) => numericExact ? integers[name] : 0;
            decimal Number(string name) => numericExact ? numbers[name] : 0m;
            var bonus = row.Element("bonus");
            bool text = bonus?.HasElements == true;
            bool selectionExact = bonus is null || !bonus.HasAttributes && (!text
                || bonus.Elements().ToArray() is [var prompt] && prompt.Name == "selecttext"
                    && Leaf(prompt) && string.IsNullOrWhiteSpace(prompt.Value));
            var multiple = row.Element("allowmultiple");
            selectionExact &= multiple is null || Leaf(multiple) && string.IsNullOrWhiteSpace(multiple.Value);
            CharacterCreationLifestyleQualitySelectionSemantics? selection = selectionExact ? new(text, multiple is not null) : null;
            bool unboundedPrompt = row.Descendants().Any(field => field.Name.LocalName.StartsWith("select", StringComparison.OrdinalIgnoreCase)
                && !(selectionExact && field.Name == "selecttext" && field.Parent == bonus));
            string[] allowed = [];
            var csv = row.Element("allowed");
            var nested = row.Element("allowedfreelifestyles");
            bool allowedExact = csv is null || nested is null;
            if (allowedExact && csv is not null)
            {
                allowedExact = Leaf(csv);
                if (allowedExact)
                {
                    allowed = csv.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
                    allowedExact = !allowed.Any(string.IsNullOrEmpty);
                }
            }
            else if (allowedExact && nested is not null)
            {
                allowedExact = !nested.HasAttributes && nested.Elements().All(field => field.Name == "lifestyle"
                    && Leaf(field) && !string.IsNullOrWhiteSpace(field.Value));
                if (allowedExact) allowed = nested.Elements().Select(field => field.Value.Trim()).ToArray();
            }
            CharacterCreationLifestyleQualityMechanics? qualityMechanics2684 = null;
            if (mechanics2684)
                selectionExact = CharacterCreationLifestyleSourceMechanics2684.TryCompile(row, mechanicsSource2684!,
                    out qualityMechanics2684, out selection);
            if (mechanics2684) unboundedPrompt = !selectionExact;
            // Match native early-return behaviour even for a blocked malformed CSV.
            if (allowedExact) allowed = allowed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            bool exact = numericExact && selectionExact && allowedExact && !unboundedPrompt
                && (mechanics2684 || !row.Elements("required").Any() && !row.Elements("forbidden").Any()
                    && (!stock || !HasUnrepresentedLifestyleMechanics2678(row)));
            string book = ScalarCreation1949(row, "source");
            bool enabled = profile.EnabledSourcebooks.Contains(book, StringComparer.OrdinalIgnoreCase);
            bool selectable = enabled && exact && (!row.Elements("implemented").Any() || Flag("implemented")) && !Flag("careeronly");
            string[] blockers = selectable ? [] : !enabled ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                : [CharacterCreationLifestylesBlockers.UnsupportedSemantics];
            var expected = new CharacterCreationLifestyleQualityCatalogOption($"lifestyle-quality:{id:D}", id,
                ScalarCreation1949(row, "name"), category, book, ScalarCreation1949(row, "page"), type,
                Integer("lp"), Number("cost"), Number("multiplier"), Number("multiplierbaseonly"),
                Integer("area"), Integer("comforts"), Integer("security"), Integer("areamaximum"), Integer("comfortsmaximum"), Integer("securitymaximum"),
                allowed, selectable, exact, blockers, [$"lifestyles.xml#quality:{id:D}"], string.Empty) { SelectionSemantics = selection, Mechanics = qualityMechanics2684 };
            // Compare the whole projection, including exact blockers, availability,
            // optional selection semantics and all canonical mechanics. Rehashing a
            // client-forged option cannot substitute for the retained source row.
            if (option.OptionDigest != CharacterCreationLifestylesRules.ComputeQualityOptionDigest(option)
                || option.OptionDigest != CharacterCreationLifestylesRules.ComputeQualityOptionDigest(expected)) return false;
        }
        return true;
    }

    private static bool TryStockLifestyleCatalogues2678(XElement root,
        out CharacterCreationLifestyleLocationCatalog? locations,
        out IReadOnlyList<CharacterCreationLifestyleCategory>? categories)
    {
        locations = null;
        categories = null;
        if (root.Name != "chummer"
            || !OptionalContainer2678(root, "cities", out var cities)
            || !OptionalContainer2678(root, "categories", out var declaration)) return false;
        if (cities is not null)
        {
            var ids = new HashSet<Guid>();
            int remaining = 65_536;
            if (!TryReadLocationRows2678(cities, "city", 0, "lifestyles.xml#/cities", ids, ref remaining, out var rows)) return false;
            locations = new(rows);
            if (!CharacterCreationLifestyleLocationRules.IsValidCatalog(locations)) return false;
        }
        if (declaration is not null)
        {
            var compiled = new List<CharacterCreationLifestyleCategory>();
            foreach (var field in declaration.Elements())
            {
                if (field.Name != "category" || !Leaf2678(field)) return false;
                string name = field.Value.Trim();
                string type = CharacterCreationLifestyleLocationRules.QualityType(name);
                compiled.Add(new(name, type));
            }
            if (!CharacterCreationLifestyleLocationRules.IsValidCategories(compiled)) return false;
            categories = compiled.ToArray();
        }
        return true;
    }

    private static bool OptionalContainer2678(XElement parent, string name, out XElement? container)
    {
        var matches = parent.Elements().Where(field => field.Name.LocalName == name).ToArray();
        container = null;
        if (matches.Length == 0) return true;
        if (matches is not [var field] || field.Name != name || !ContainerShape2678(field)) return false;
        container = field;
        return true;
    }

    private static bool ContainerShape2678(XElement field) => !field.HasAttributes
        && !HasDirectStockScopeText2678(field) && !field.Nodes().OfType<XProcessingInstruction>().Any();

    private static bool Leaf2678(XElement field) => !field.HasAttributes && !field.HasElements
        && !field.Nodes().OfType<XProcessingInstruction>().Any();

    private static bool TryReadLocationRows2678(XElement parent, string rowName, int depth, string parentPath,
        HashSet<Guid> ids, ref int remaining, out CharacterCreationLifestyleLocationNode[] result)
    {
        result = [];
        var rows = new List<CharacterCreationLifestyleLocationNode>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in parent.Elements().Where(field => field.Name != "name" && field.Name != "id" && field.Name != "secRating"))
        {
            if (--remaining < 0 || row.Name != rowName || !ContainerShape2678(row)) return false;
            string path = parentPath + "/" + rowName + "["
                + (rows.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            string? childName = depth == 0 ? "district" : depth == 1 ? "borough" : null;
            if (row.Elements().Any(field => field.Name != "name" && field.Name != "id"
                && !(depth == 2 && field.Name == "secRating") && (childName is null || field.Name != childName))) return false;
            if (row.Elements("name").ToArray() is not [var name] || !Leaf2678(name)) return false;
            string label = name.Value.Trim();
            if (!names.Add(label)) return false;
            Guid? sourceId = null;
            var keys = row.Elements("id").ToArray();
            if (keys.Length != 0)
            {
                if (keys is not [var key] || !Leaf2678(key) || !Guid.TryParse(key.Value.Trim(), out var id)
                    || id == Guid.Empty || !ids.Add(id)) return false;
                sourceId = id;
            }
            string? rating = null;
            var ratings = row.Elements("secRating").ToArray();
            if (ratings.Length != 0)
            {
                if (ratings is not [var security] || !Leaf2678(security) || string.IsNullOrWhiteSpace(security.Value)) return false;
                rating = security.Value.Trim();
            }
            CharacterCreationLifestyleLocationNode[] children = [];
            if (childName is not null && !TryReadLocationRows2678(row, childName, depth + 1, path, ids, ref remaining, out children)) return false;
            rows.Add(new(sourceId, label, path, rating,
                CharacterCreationFinalizationDigest.Compute(row.ToString(SaveOptions.DisableFormatting)), children));
        }
        // The top-level container admits rows only (not a stray name/id/rating).
        if (depth == 0 && parent.Elements().Any(field => field.Name != rowName)) return false;
        result = rows.ToArray();
        return true;
    }

    private static bool HasDirectStockScopeText2678(XElement row)
        => row.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));

    private static bool StockLifestyleProjection2678(CharacterCurrentRuleCatalogInput input,
        XElement[] rows, CharacterCreationLifestylesAuthority authority, ICharacterSourceDataContext source)
    {
        if (!source.TryResolveCreationSourceProfile(out var profile)
            || source is not ICurrentLifestyleSettingsSource2678 settingsSource
            || !settingsSource.TryReadLifestyleSettings(out var settingsXml)
            || !settingsSource.TryReadLifestyleSource(out var sourceXml, out var sourceDigest)) return false;
        var global = ParseCreation1949(input.Entries.Single(entry => entry.Key == "$global").Xml);
        var original = ParseCreation1949(sourceXml);
        foreach (var entry in input.Entries.Where(entry => entry.Key != "$global" && entry.State != CharacterCurrentRuleEntryState.Tombstone))
        {
            var row = ParseCreation1949(entry.Xml);
            if (row.Name != "lifestyle" || !Guid.TryParseExact(ScalarCreation1949(row, "id"), "D", out var id)) return false;
            string name = ScalarCreation1949(row, "name");
            string key = id == Guid.Empty ? "lifestyles/name:" + name : $"lifestyles/id:{id:D}";
            if (entry.Key != key || entry.Name != name) return false;
        }
        string Canonical(XElement node) => CreationComparisonTree1949(node).ToString(SaveOptions.DisableFormatting);
        if (!rows.Select(Canonical).Order(StringComparer.Ordinal).SequenceEqual(
                original.Elements("lifestyles").Elements("lifestyle").Select(Canonical).Order(StringComparer.Ordinal))) return false;
        original.Elements("lifestyles").Remove();
        if (Canonical(global) != Canonical(original)) return false;
        if (!settingsSource.TryReadLifestyleLocationRoundTrip2678(out bool roundTrip)) return false;
        if (roundTrip)
        {
            // V6 custom locations historically used XElement.Parse(..., None).
            // First bind the raw header to retained source: stripping whitespace
            // must never hide a forged header, even if it rehashes successfully.
            string Raw(XElement node) => node.ToString(SaveOptions.DisableFormatting);
            foreach (string name in new[] { "cities", "categories" })
                if (!global.Elements(name).Select(Raw).SequenceEqual(original.Elements(name).Select(Raw), StringComparer.Ordinal)) return false;
            global = XElement.Parse(global.ToString(SaveOptions.DisableFormatting), LoadOptions.None);
        }
        if (!TryStockLifestyleCatalogues2678(global, out var locations, out var categories)
            || CharacterCreationFinalizationDigest.Compute(locations) != CharacterCreationFinalizationDigest.Compute(authority.LocationCatalog)
            || CharacterCreationFinalizationDigest.Compute(categories) != CharacterCreationFinalizationDigest.Compute(authority.Categories)
            || !LifestyleQualityProjection2678(input, authority, source, true)) return false;
        var settings = ParseCreation1949(settingsXml);
        if (settings.Elements("allowfreegrids").Count() > 1) return false;
        string? rawGrids = settings.Element("allowfreegrids")?.Value.Trim();
        bool settingGrids = rawGrids?.ToLowerInvariant() is "true" or "yes" or "1";
        bool bookGrids = profile.EnabledSourcebooks.Contains("HT", StringComparer.OrdinalIgnoreCase);
        var activation = new CharacterCreationFreeGridsActivation(rawGrids, bookGrids,
            settingGrids ? CharacterCreationFreeGridsActivationReason.Setting
                : bookGrids ? CharacterCreationFreeGridsActivationReason.Sourcebook : CharacterCreationFreeGridsActivationReason.Disabled,
            CharacterCreationFinalizationDigest.Compute(settingsXml), sourceDigest);
        if (authority.FreeGridsEnabled != (settingGrids || bookGrids) || authority.FreeGridsActivation != activation
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(activation.LifestylesSourceDigest)) return false;
        var accounted = new HashSet<Guid>();
        var identities = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!CreationSyntax1949(row, CharacterCurrentRuleDomain.Lifestyles, lifestyleQualities: true, stockLifestyles: true)
                || !Guid.TryParseExact(ScalarCreation1949(row, "id"), "D", out var id) || !identities.Add(id)
                || new[] { "name", "source", "page" }.Any(name => string.IsNullOrWhiteSpace(ScalarCreation1949(row, name)))) return false;
            bool numeric = true;
            var numbers = new Dictionary<string, decimal>();
            foreach (string name in new[] { "cost", "dice", "multiplier", "lp", "costforarea", "costforcomforts", "costforsecurity" })
            {
                string text = ScalarCreation1949(row, name);
                decimal value = 0m;
                bool exact = text.Length == 0 && name is not ("cost" or "dice" or "multiplier")
                    || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0m;
                if (name is "dice" or "lp") exact &= int.TryParse(text.Length == 0 ? "0" : text,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                numeric &= exact;
                numbers[name] = value;
            }
            decimal Number(string name) => numeric ? numbers[name] : 0m;
            int Integer(string name) => checked((int)Number(name));
            bool hidden = row.Element("hide") is not null;
            // Every excluded row still passes the complete known shape and numeric
            // grammar. An empty identity without a real hide marker is not a placeholder.
            if (hidden || id == Guid.Empty)
            {
                if (!hidden || !numeric || authority.LifestyleOptions.Any(option => option.SourceId == id)) return false;
                continue;
            }
            var matches = authority.LifestyleOptions.Where(option => option.SourceId == id).ToArray();
            if (matches is not [var option]) return false;
            accounted.Add(id);
            string label = ScalarCreation1949(row, "name");
            bool Aspect(string container, string member, out int minimum, out int maximum)
            {
                minimum = maximum = 0;
                var found = global.Elements(container).Elements(member).Where(item => ScalarCreation1949(item, "name") == label).ToArray();
                return found is [var aspect] && int.TryParse(ScalarCreation1949(aspect, "minimum"), NumberStyles.Integer, CultureInfo.InvariantCulture, out minimum)
                    && int.TryParse(ScalarCreation1949(aspect, "limit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum)
                    && minimum >= 0 && maximum >= minimum;
            }
            bool aspects = Aspect("comforts", "comfort", out int comfort, out int comfortMax);
            aspects &= Aspect("neighborhoods", "neighborhood", out int area, out int areaMax);
            aspects &= Aspect("securities", "security", out int security, out int securityMax);
            if (!aspects)
            {
                comfort = comfortMax = area = areaMax = security = securityMax = 0;
                aspects = !row.Elements("lp").Any() || ScalarCreation1949(row, "lp") == "0";
            }
            bool gridsExact = true;
            var builtIns = new List<CharacterCreationLifestyleBuiltInQuality>();
            if (authority.FreeGridsEnabled)
            foreach (var grid in row.Elements("freegrids").Elements("freegrid"))
            {
                var qualities = authority.QualityOptions.Where(quality => quality.Name == grid.Value.Trim()).ToArray();
                if (qualities is not [var quality] || !quality.EligibilityIsExact)
                { gridsExact = false; continue; }
                builtIns.Add(new(quality.OptionId, grid.Attribute("select")?.Value.Trim() ?? string.Empty, quality.SourceAnchorIds));
            }
            bool exactOption = numeric && aspects && gridsExact;
            string book = ScalarCreation1949(row, "source");
            bool enabled = profile.EnabledSourcebooks.Contains(book, StringComparer.OrdinalIgnoreCase);
            bool selectable = enabled && exactOption;
            string[] blockers = selectable ? [] : !enabled ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                : [CharacterCreationLifestylesBlockers.UnsupportedSemantics];
            string increment = ScalarCreation1949(row, "increment").ToLowerInvariant() switch
            { "day" => CharacterCreationLifestyleIncrementIds.Day, "week" => CharacterCreationLifestyleIncrementIds.Week, _ => CharacterCreationLifestyleIncrementIds.Month };
            var expected = new CharacterCreationLifestyleCatalogOption($"lifestyle:{id:D}", id, label,
                Number("cost"), Integer("dice"), Number("multiplier"), Integer("lp"), Number("costforarea"), Number("costforcomforts"), Number("costforsecurity"),
                area, areaMax, comfort, comfortMax, security, securityMax,
                ScalarCreation1949(row, "allowbonuslp").ToLowerInvariant() is "true" or "yes" or "1", increment,
                book, ScalarCreation1949(row, "page"), builtIns, selectable, exactOption, blockers, [$"lifestyles.xml#lifestyle:{id:D}"], string.Empty);
            if (option.OptionDigest != CharacterCreationLifestylesRules.ComputeOptionDigest(option)
                || option.OptionDigest != CharacterCreationLifestylesRules.ComputeOptionDigest(expected)) return false;
        }
        return accounted.Count == authority.LifestyleOptions.Count;
    }

    private static string ValidateStockLifestyleAuthority2678(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogInput input, XElement[] rows, ICharacterSourceDataContext source,
        List<string> blockers, CancellationToken cancellationToken)
    {
        string? failure5874 = null;
        if (!TryLifestyleConsumerAuthority2719(current, source, out var authority))
            failure5874 = "current-stock-lifestyle-consumer-authority-unresolved";
        else if (!CharacterCreationLifestylesRules.IsValidAuthority(authority))
            failure5874 = "current-stock-lifestyle-authority-invalid";
        else if (!CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts))
            failure5874 = "current-stock-lifestyle-purchased-quality-costs-invalid";
        else if (!StockLifestyleProjection2678(input, rows, authority, source))
            failure5874 = "current-stock-lifestyle-projection-mismatch";
        if (failure5874 is not null)
        {
            blockers.Add("current-stock-lifestyle-source-projection-not-exact");
            // Fixed first-failure code only; preserve ordinary Creation diagnostics.
            if (current.Stage == CharacterCurrentRuleStage.Career
                && current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV9)
                blockers.Add(failure5874);
            return string.Empty;
        }
        foreach (var option in authority.LifestyleOptions.Where(option => option.IsSelectable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var quote = new CharacterCreationLifestyleConfiguration(option.SourceId, option.OptionId, option.Name,
                CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m,
                0, false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
            bool exact = current.Stage == CharacterCurrentRuleStage.Career
                ? CharacterCareerLifestyleMaintenanceProjection.TryProject(quote, authority, out _, out _)
                : CharacterCreationLifestylesRules.TryProject(quote, authority, out _, out _);
            if (!exact) blockers.Add("current-stock-lifestyle-future-price-unresolved");
        }
        // A synthetically selected quality/base cross product is not source validity:
        // aspects, free-LP eligibility and selections belong to each actual quote.
        return CharacterCreationFinalizationDigest.Compute(authority);
    }

    private const string CreationUnavailable1949 = "current-creation-source-or-mechanic-unavailable";
    private sealed record CreationObservation1949(CharacterCurrentRuleEnvironmentContext Current,
        string Digest, string AuthorityDigest);
    private static readonly ConditionalWeakTable<CharacterCurrentRuleCatalogValidation, CreationObservation1949>
        CreationObservations1949 = new();

    public static CharacterCreationMetatypeOptionProjection? FindCurrentCreationMetatype(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string optionId, CancellationToken cancellationToken = default)
    {
        if (catalog.Domain != CharacterCurrentRuleDomain.Metatypes
            || ExecuteCreation(current, catalog, source, cancellationToken) is not { IsComplete: true }
            || !CurrentAvailableKey1950(catalog, "metatype", optionId)
            || !source.TryResolveCreationMetatypeCatalog(out var authority) || !authority.IsAuthoritative) return null;
        var option = authority.Options.SingleOrDefault(option => option.OptionId == optionId && option.IsEnabled && option.Blockers.Count == 0);
        cancellationToken.ThrowIfCancellationRequested();
        return current.IsUnchanged ? option : null;
    }

    public static CharacterCurrentCreationMetatypeQuote? QuoteCurrentCreationMetatype(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string optionId, CancellationToken cancellationToken = default)
    {
        var metatype = FindCurrentCreationMetatype(current, catalog, source, optionId, cancellationToken);
        if (metatype is null) return null;
        var blockers = new List<string>();
        var effects = CurrentMetatypeEffects1950(current, catalog, source, metatype.OptionId, blockers);
        return blockers.Count == 0 && current.IsUnchanged ? effects : null;
    }

    public static CharacterCurrentCreationSelectionQuote? QuoteCurrentCreationSelection(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, IReadOnlyDictionary<string, string> ranks,
        string? heritageSelectionId, string? talentSelectionId, CancellationToken cancellationToken = default)
    {
        if (ranks is null || catalog.Domain != CharacterCurrentRuleDomain.Priorities
            || ExecuteCreation(current, catalog, source, cancellationToken) is not { IsComplete: true }
            || !source.TryResolveCreationPrerequisiteAuthority(out var authority)
            || !CharacterCreationPrerequisiteService.TryEvaluatePartialCurrentSelection1949(authority, ranks,
                heritageSelectionId, talentSelectionId, out var result, out _) || result is null
            || result.Assignments.Any(row => !CurrentAvailableKey1950(catalog, "priority", row.SourceId))
            || !current.IsUnchanged) return null;
        return new(current.BindingDigest, catalog.ContentDigest, result.Assignments, result.Heritage, result.Talent,
            result.MissingChoices, result.EffectiveNormalAttributePoints, result.TotalSpecialAttributePoints);
    }

    private static bool CurrentAvailableKey1950(CharacterCurrentRuleCatalogValidation catalog, string element, string id)
        => catalog.Entries.Count(entry => entry.State == CharacterCurrentRuleEntryState.AvailableNotSelected
            && ParseCreation1949(entry.Xml).Name == element
            && ScalarCreation1949(ParseCreation1949(entry.Xml), "id") == id) == 1;

    public static CharacterCreationLifestyleProjection? QuoteCurrentCreationLifestyle(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CharacterCreationLifestyleConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (catalog.Domain != CharacterCurrentRuleDomain.Lifestyles || configuration is null
            || ExecuteCreation(current, catalog, source, cancellationToken) is not { IsComplete: true }
            || !TryCurrentLifestyleAuthority1950(current, source, out var authority)) return null;
        var options = authority.LifestyleOptions.Where(option => option.OptionId == configuration.BaseLifestyleOptionId
            && option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0).ToArray();
        if (options is not [var option] || !CurrentAvailableKey1950(catalog, "lifestyle", option.SourceId.ToString("D"))) return null;
        if (current.Stage == CharacterCurrentRuleStage.Career && UsesNativeLifecycleMechanics9569(current)
            && (!CharacterCareerLifestyleMaintenanceProjection.TrySourcePeriods(current.CharacterXml,
                new CreationExactSource1949(current.CharacterXml, source), out var periods)
                || !periods.TryGetValue(option.SourceId, out var period) || period != configuration.IncrementId)) return null;
        CharacterCreationLifestyleProjection? projection;
        bool priced = current.Stage == CharacterCurrentRuleStage.Career
            ? CharacterCareerLifestyleMaintenanceProjection.TryProject(configuration, authority, out projection, out _)
            : CharacterCreationLifestylesRules.TryProject(configuration, authority, out projection, out _);
        cancellationToken.ThrowIfCancellationRequested();
        return priced && current.IsUnchanged ? projection : null;
    }

    private static bool TryLifestyleConsumerAuthority2719(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CharacterCreationLifestylesAuthority authority)
        => current.Stage == CharacterCurrentRuleStage.Career && UsesNativeLifecycleMechanics9569(current)
            ? TryCurrentLifestyleAuthority1950(current, source, out authority)
            : source.TryResolveCreationLifestylesAuthority(out authority);

    private static bool TryCurrentLifestyleAuthority1950(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CharacterCreationLifestylesAuthority authority)
    {
        if (current.Stage == CharacterCurrentRuleStage.Career && UsesNativeLifecycleMechanics9569(current))
            return CharacterCareerLifestyleMaintenanceProjection.TryAuthority(current.CharacterXml,
                new CreationExactSource1949(current.CharacterXml, source), out authority, out _, out _);
        if (!source.TryResolveCreationLifestylesAuthority(out authority) || !CharacterCreationLifestylesRules.IsValidAuthority(authority))
            return false;
        if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation)
        {
            if (!TryCurrentNativeMethod1958(current, source, out var native, []) || native?.Karma?.Quote?.Lifestyles is not { } quote)
                return false; // Native future selection uses QuoteKarmaBuild until its effect basis exists.
            authority = quote.ProjectionAuthority;
            return CharacterCreationLifestylesRules.IsValidAuthority(authority);
        }
        if (current.Stage != CharacterCurrentRuleStage.Career) return true;
        var root = ParseCreation1949(current.CharacterXml);
        // An actually empty Career basket has no saved instance/owner to admit.
        // The retained source still computes its racial costs; future pricing
        // below uses the Career kernel and grants no mutation permission.
        if (!CharacterCareerMartialAcquisitionTransaction.TryProjectCurrentMartialEffects2438(root, source, out var calculation)) return false;
        if (!calculation.Elements("lifestyles").Elements().Any() && !calculation.Elements("improvements").Elements().Any()) return true;
        return CharacterCareerLifestyleMaintenanceProjection.TryHistoricalAuthority2719(current.CharacterXml,
            new CreationExactSource1949(current.CharacterXml, source), out authority, out _, out _);
    }

    private static CharacterCurrentCreationMetatypeQuote? CurrentMetatypeEffects1950(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string id, List<string> blockers)
    {
        if (UsesNativeMethods1958(current))
            return CurrentNativeMetatypeEffects1958(current, catalog, source, id, blockers);
        if (!source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative
            || metatypes.Options.Where(option => option.OptionId == id && option.IsEnabled && option.Blockers.Count == 0)
                .ToArray() is not [var metatype]
            || !source.TryResolveCreationPrerequisiteAuthority(out var authority) || !authority.IsAuthoritative)
        { blockers.Add("current-metatype-effects-authority-unresolved"); return null; }
        XElement[] qualities = [], improvements = [];
        if (metatype.GrantedQualities.Count != 0)
        {
            var candidates = authority.Options.SelectMany(option => option.HeritageOptions)
                .Where(option => option.IsEnabled && option.Blockers.Count == 0 && option.MetatypeSourceId == id
                    && option.MetavariantSourceId is null && option.RacialQualitySources is { Count: > 0 }).ToArray();
            if (candidates.Length == 0 || candidates.Any(option => CharacterCreationFinalizationDigest.Compute(option.RacialQualitySources)
                    != CharacterCreationFinalizationDigest.Compute(candidates[0].RacialQualitySources))
                || candidates[0].RacialQualitySources!.Count != metatype.GrantedQualities.Count
                || !CharacterCreationPriorityRacialQualities.TryProjectCurrent(candidates[0].RacialQualitySources,
                    authority.AuthorityDigest, out qualities, out improvements))
            { blockers.Add("current-metatype-granted-effects-unresolved"); return null; }
        }
        return new(current.BindingDigest, catalog.ContentDigest, metatype,
            qualities.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
            improvements.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray());
    }

    public static CharacterCurrentRuleCatalogValidation? ValidateCreationCatalog(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogInput input,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CurrentCreationBinding1949(current, input) || input.Blockers.Count != 0) return null;
        var blockers = new List<string>();
        try
        {
            var rows = CreationRows1949(input);
            var entries = new List<CharacterCurrentRuleCatalogEntry>();
            XElement? spriteSource = null;
            bool spriteCatalog = input.Domain == CharacterCurrentRuleDomain.Metatypes && input.Query == "critters.xml";
            if (spriteCatalog) ObserveCurrentSpriteOrigin5841(current, source, blockers, out spriteSource);
            foreach (var entry in input.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.State == CharacterCurrentRuleEntryState.Tombstone)
                {
                    if (!input.Operations.Any(operation => operation.IsTombstone && operation.BeforeXml == entry.Xml))
                        blockers.Add("current-creation-tombstone-without-merge-observation");
                    entries.Add(entry);
                    continue;
                }
                XElement? checkedNode = null;
                if (entry.State != CharacterCurrentRuleEntryState.AvailableNotSelected
                    || entry.UnavailableReason is not null
                    || !CreationXmlSafe1950(checkedNode = ParseCreation1949(entry.Xml)))
                    blockers.Add("current-creation-catalog-syntax-unsupported");
                // The safety check only reads this local tree. Keep the original
                // short circuit: rejected entry metadata still parses below.
                var node = checkedNode ?? ParseCreation1949(entry.Xml);
                if (spriteCatalog && entry.Key != "$global")
                {
                    // Only the retained selected owner can admit a critter row.
                    // Preserve every other row and key as explicitly unavailable.
                    entries.Add(spriteSource is not null && SameSpriteOriginSource5841(node, spriteSource)
                        ? entry : entry with { State = CharacterCurrentRuleEntryState.Unavailable,
                            UnavailableReason = "current-sprite-unselected-source-not-evaluated" });
                }
                else if (input.Domain == CharacterCurrentRuleDomain.Profile && entry.Key != "$global")
                {
                    // Only the retained selected profile can issue a policy observation.
                    // Other catalog profiles stay present, explicitly unavailable, and
                    // cannot force unrelated settings into the current character.
                    if (!source.TryResolveCreationAttributePolicy(out var selectedPolicy) || selectedPolicy is null)
                        blockers.Add("current-profile-selected-policy-unresolved");
                    if (selectedPolicy is not null && MatchesSettingsRow5691(source, node, selectedPolicy.SettingsProfileId))
                    {
                        if (!CreationSyntax1949(node, input.Domain, UsesNativeMethods1958(current),
                                karmaQualityPrompts: UsesKarmaQualityPrompts2742(current, source),
                                nativeSheetProfile: UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Career,
                                nativeReputationProfile: UsesNativeMethods1958(current),
                                careerCondition5497: UsesCareerCondition5497(current))
                            && !NativeMethodFullProfile9749(current, source, node)
                            && !PriorityProfile3027(current, source, node))
                            blockers.Add("current-creation-catalog-syntax-unsupported");
                        // These optional settings are one canonical policy. Recognizing their
                        // scalar syntax must not admit malformed or incomplete policy values.
                        if (node.Descendants().Any(field => IsCurrentMysticAdeptProfileSetting1969(
                                field.Parent!.Name.LocalName, field.Name.LocalName))
                            && (selectedPolicy is null || !TryReadSelectedMysticPolicy5691(source,
                                selectedPolicy.SettingsProfileId, selectedPolicy.RawProfileInputsDigest, node)))
                            blockers.Add("current-profile-mystic-adept-policy-unresolved");
                        entries.Add(entry);
                    }
                    else entries.Add(entry with { State = CharacterCurrentRuleEntryState.Unavailable,
                        UnavailableReason = "current-profile-unselected-policy-not-evaluated" });
                }
                else if (entry.Key == "$global" || input.Domain == CharacterCurrentRuleDomain.Lifestyles)
                {
                    if (!CreationSyntax1949(node, input.Domain, lifestyleQualities: UsesLifestyleQualities2661(current), lifestyleAvailability: UsesLifestyleAvailability2670(current), stockLifestyles: UsesStockLifestyles2678(current))) blockers.Add("current-creation-catalog-syntax-unsupported");
                    string? unavailable = null;
                    if (UsesStockLifestyles2678(current) && node.Name == "lifestyle")
                    {
                        if (node.Element("hide") is not null) unavailable = "current-stock-lifestyle-hidden-source-row";
                        else if (source.TryResolveCreationLifestylesAuthority(out var lifestyleAvailabilityAuthority)
                            && lifestyleAvailabilityAuthority.LifestyleOptions.SingleOrDefault(option => option.SourceId.ToString("D") == ScalarCreation1949(node, "id")) is { IsSelectable: false } option)
                            unavailable = string.Join(";", option.Blockers);
                    }
                    entries.Add(unavailable is null ? entry : entry with
                    { State = CharacterCurrentRuleEntryState.Unavailable, UnavailableReason = unavailable });
                }
                else
                {
                    string? reason = CurrentOriginRowUnavailable1950(input.Domain, node, source);
                    entries.Add(reason is null ? entry : entry with
                    { State = CharacterCurrentRuleEntryState.Unavailable, UnavailableReason = reason });
                }
            }
            if (rows.Select(row => row.Element("id")?.Value).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                blockers.Add("current-creation-catalog-identity-ambiguous");
            string authority = ValidateCreationAuthority1949(current, input, rows, source, blockers,
                cancellationToken);
            var report = CharacterCurrentRuleCatalogValidation.CreateSnapshot(input, Array.AsReadOnly(entries.ToArray()),
                Array.AsReadOnly(blockers.Distinct(StringComparer.Ordinal).ToArray()), UsesStockLifestyles2678(current) ? "current-creation-bounded-kernels.v5" : UsesLifestyleAvailability2670(current) ? "current-creation-bounded-kernels.v4" : UsesLifestyleQualities2661(current) ? "current-creation-bounded-kernels.v3" : CreationGrammar1949);
            if (report.IsComplete && current.IsUnchanged)
                CreationObservations1949.Add(report, new(current, report.ContentDigest, authority));
            return report;
        }
        catch (Exception error) when (CreationInputError1949(error))
        {
            return new(input, Array.AsReadOnly(input.Entries.ToArray()),
                ["current-creation-catalog-malformed"], UsesStockLifestyles2678(current) ? "current-creation-bounded-kernels.v5" : UsesLifestyleAvailability2670(current) ? "current-creation-bounded-kernels.v4" : UsesLifestyleQualities2661(current) ? "current-creation-bounded-kernels.v3" : CreationGrammar1949);
        }
    }

    public static CharacterCurrentRuleDomainExecution? ExecuteCreation(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CurrentCreationBinding1949(current, catalog.Input) || !catalog.IsComplete
            || !CreationObservations1949.TryGetValue(catalog, out var observed)
            || !ReferenceEquals(observed.Current, current) || observed.Digest != catalog.ContentDigest) return null;
        var blockers = new List<string>();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var state = CharacterCurrentRuleConsumptionState.Evaluated;
        object? outcome = null;
        try
        {
            var rows = CreationRows1949(catalog.Input);
            string authority = ValidateCreationAuthority1949(current, catalog.Input, rows, source, blockers,
                cancellationToken);
            if (authority != observed.AuthorityDigest) blockers.Add("current-creation-authority-changed");
            var root = ParseCreation1949(current.CharacterXml);
            if (current.Stage == CharacterCurrentRuleStage.Career
                && !CharacterCareerMartialAcquisitionTransaction.TryProjectCurrentMartialEffects2438(root, source, out root)) return null;
            // Windows saves both a rank and a choice container as priorityskills.
            // Only the exact imported Career selection proved by retained sources
            // may cross this shape gate; every other duplicate remains refused.
            if (root.Name != "character" || root.HasAttributes
                || root.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1
                    && (group.Key != "priorityskills" || group.Count() != 2
                        || !UsesImportedPriorityCareer5667(current) || current.Stage != CharacterCurrentRuleStage.Career
                        || !CharacterCreationPriorityGrantAuthority3563.CaptureImported5693(current).IsImported5693))
                || !bool.TryParse(root.Element("created")?.Value, out bool career)
                || career != (current.Stage == CharacterCurrentRuleStage.Career)) return null;
            if (new[] { "improvements", "lifestyles", "attributes" }.Any(name => root.Element(name) is { } container
                && (container.HasAttributes || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                    || container.Nodes().OfType<XProcessingInstruction>().Any()))) return null;
            if (blockers.Count == 0)
            {
                switch (catalog.Domain)
                {
                    case CharacterCurrentRuleDomain.Lifestyles:
                        outcome = ConsumeCurrentLifestyles1949(current, catalog, source, root, selected, blockers, ref state);
                        break;
                    case CharacterCurrentRuleDomain.Profile:
                        outcome = ConsumeCurrentProfile1949(current, catalog, source, root, selected, blockers, cancellationToken);
                        break;
                    default:
                        outcome = ConsumeCurrentOrigins1949(current, catalog, source, root, selected, blockers, ref state);
                        break;
                }
            }
            if (!current.IsUnchanged || observed.Digest != catalog.ContentDigest) return null;
            return IssueRuleEffects2912(current, catalog, new(current, catalog, state, selected.Order(StringComparer.Ordinal).ToArray(),
                outcome is null ? string.Empty : CharacterCreationFinalizationDigest.Compute(new
                { current.BindingDigest, catalog.ContentDigest, Authority = authority, Result = outcome }),
                Array.AsReadOnly(blockers.Distinct(StringComparer.Ordinal).ToArray())), outcome);
        }
        catch (Exception error) when (CreationInputError1949(error))
        { return new(current, catalog, state, [], string.Empty, ["current-creation-state-unresolved"]); }
    }

    private static bool CurrentCreationBinding1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogInput input)
        => current.IsUnchanged && current.Current.Document.RulesetId == "sr5"
            && input.ContextDigest == current.BindingDigest
            && input.Domain is CharacterCurrentRuleDomain.Profile or CharacterCurrentRuleDomain.Metatypes
                or CharacterCurrentRuleDomain.Priorities or CharacterCurrentRuleDomain.Lifestyles
            && (input.Domain == CharacterCurrentRuleDomain.Profile ? !string.IsNullOrWhiteSpace(input.Query)
                : input.Domain == CharacterCurrentRuleDomain.Metatypes && input.Query == "critters.xml"
                    ? UsesImportedPriorityCareer5667(current)
                : input.Query == input.Domain.ToString().ToLowerInvariant() + ".xml")
            && input.Entries.Count is > 0 and <= 65536
            && input.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.Key))
            && input.Entries.Count(entry => entry.Key == "$global") == 1
            && input.Entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() == input.Entries.Count;

    private static string ValidateCreationAuthority1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogInput input, XElement[] rows, ICharacterSourceDataContext source,
        List<string> blockers, CancellationToken cancellationToken)
    {
        var domain = input.Domain;
        if (domain == CharacterCurrentRuleDomain.Metatypes && input.Query == "critters.xml")
        {
            var sprite = ObserveCurrentSpriteOrigin5841(current, source, blockers, out var spriteSource);
            if (sprite is null || spriteSource is null
                || rows.Where(row => ScalarCreation1949(row, "id") == sprite.Metatype.OptionId).ToArray() is not [var selectedSource]
                || !SameSpriteOriginSource5841(selectedSource, spriteSource))
            { blockers.Add("current-sprite-origin-catalog-not-exact"); return string.Empty; }
            return CharacterCreationFinalizationDigest.Compute(new { sprite.Digest, sprite.Metatype,
                Source = spriteSource.ToString(SaveOptions.DisableFormatting) });
        }
        if (UsesNativeMethods1958(current) && domain != CharacterCurrentRuleDomain.Lifestyles)
            return ValidateNativeCreationAuthority1958(current, domain, rows, source, blockers);
        if (domain == CharacterCurrentRuleDomain.Lifestyles)
        {
            if (UsesStockLifestyles2678(current)) return ValidateStockLifestyleAuthority2678(current, input, rows, source, blockers, cancellationToken);
            if (!source.TryResolveCreationLifestylesAuthority(out var authority)
                || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts)
                || (UsesLifestyleAvailability2670(current) ? !LifestyleQualityProjection2670(input, authority, source)
                        : UsesLifestyleQualities2661(current) ? !LifestyleQualityProjection2661(input, authority)
                    : authority.QualityOptions.Count != 0) || authority.FreeGridsEnabled
                || authority.LifestyleOptions.Count != rows.Length)
            { blockers.Add(CreationUnavailable1949); return string.Empty; }
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var options = authority.LifestyleOptions.Where(option => option.SourceId.ToString("D") == ScalarCreation1949(row, "id")).ToArray();
                if (options is not [var option] || !option.IsSelectable || !option.EligibilityIsExact
                    || option.Blockers.Count != 0 || option.BuiltInQualities.Count != 0
                    || option.Name != ScalarCreation1949(row, "name") || option.BaseCost != DecimalCreation1949(row, "cost")
                    || option.StartingNuyenDice != DecimalCreation1949(row, "dice")
                    || option.StartingNuyenMultiplier != DecimalCreation1949(row, "multiplier")
                    || option.SourceBook != ScalarCreation1949(row, "source") || option.Page != ScalarCreation1949(row, "page"))
                { blockers.Add("current-lifestyle-option-not-exact"); continue; }
                // A future quote is evaluated independently of the real selection.
                // Its deterministic probe ID never enters the character or selected keys.
                var quote = new CharacterCreationLifestyleConfiguration(option.SourceId, option.OptionId, option.Name,
                    CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m,
                    0, false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
                if (current.Stage == CharacterCurrentRuleStage.Career)
                {
                    if (!CharacterCareerLifestyleMaintenanceProjection.TryProject(quote, authority, out _, out _))
                        blockers.Add("current-career-lifestyle-future-price-unresolved");
                }
                else if (!CharacterCreationLifestylesRules.TryProject(quote, authority, out _, out var findings))
                    blockers.AddRange(findings);
                if (UsesLifestyleQualities2661(current))
                foreach (var quality in authority.QualityOptions.Where(quality => !UsesLifestyleAvailability2670(current) || quality.IsSelectable))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Existing canonical economics, with one real source quality.
                    // Probe identities never enter the character or selected keys.
                    var qualityQuote = quote with { Qualities = [new(quality.SourceId, quality.OptionId, UsesLifestyleAvailability2670(current) && quality.SelectionSemantics?.RequiresText == true ? "current-source-probe" : string.Empty, false, false, false)] };
                    bool exact = current.Stage == CharacterCurrentRuleStage.Career
                        ? CharacterCareerLifestyleMaintenanceProjection.TryProject(qualityQuote, authority, out _, out _)
                        : CharacterCreationLifestylesRules.TryProject(qualityQuote, authority, out _, out _);
                    if (!exact) blockers.Add("current-lifestyle-quality-future-price-unresolved");
                }
            }
            return CharacterCreationFinalizationDigest.Compute(authority);
        }
        if (domain == CharacterCurrentRuleDomain.Profile)
        {
            if (!source.TryResolveCreationAttributePolicy(out var policy) || policy is null
                || policy.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(policy))
            { blockers.Add(CreationUnavailable1949); return string.Empty; }
            var matching = rows.Where(row => MatchesSettingsRow5691(source, row, policy.SettingsProfileId)).ToArray();
            if (matching is not [var profile]
                || CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(profile) != policy.MaxNumberMaxAttributesCreate
                || policy.KarmaAttribute <= 0 || policy.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            { blockers.Add("current-profile-selected-policy-unresolved"); return string.Empty; }
            // Alternative profiles remain unavailable catalog entries. Their policy
            // is not evidence for this exact retained selection.
            // The same kernel used by ordinary attribute allocation owns both
            // house-rule flags. This checks the bounded future cost grammar.
            if (!CharacterCreationAttributeCostRules.TryCalculateAllocation(1, 1, 1, policy.KarmaAttribute,
                policy.AlternateMetatypeAttributeKarma, policy.ReverseAttributePriorityOrder, out _, out _))
                blockers.Add("current-profile-attribute-cost-unresolved");
            if (!source.TryResolveCreationPrerequisiteAuthority(out var prerequisites) || !prerequisites.IsAuthoritative
                || prerequisites.Blockers.Count != 0 || prerequisites.SettingsProfileId != policy.SettingsProfileId)
                blockers.Add("current-profile-prerequisite-authority-unresolved");
            else if (!CharacterCreationPrerequisiteService.TryEvaluateCurrentSelection1949(prerequisites, null, out _, out var findings))
                blockers.AddRange(findings);
            CharacterCreationResourcesAuthority? resources = null;
            if (current.Stage == CharacterCurrentRuleStage.Creation || HasCreationFundingFields1949(profile))
            {
                if (!source.TryResolveCreationResourcesAuthority(out resources)
                    || !CharacterCreationResourcesRules.IsValidAuthority(resources))
                    blockers.Add("current-profile-resources-authority-unresolved");
                else EvaluateFundingOptions1949(resources, blockers, cancellationToken);
            }
            return CharacterCreationFinalizationDigest.Compute(new { policy, prerequisites, resources });
        }
        if (!source.TryResolveCreationPrerequisiteAuthority(out var prerequisite) || !prerequisite.IsAuthoritative
            || prerequisite.Blockers.Count != 0 || prerequisite.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
        { blockers.Add("current-origin-prerequisite-authority-unresolved"); return string.Empty; }
        if (!CharacterCreationPrerequisiteService.TryEvaluateCurrentSelection1949(prerequisite, null, out _, out var authorityBlockers))
            blockers.AddRange(authorityBlockers);
        if (domain == CharacterCurrentRuleDomain.Priorities)
        {
            // The current profile projects one table. Other table rows remain
            // covered but unavailable; disabled child choices retain their
            // canonical selection guards instead of vetoing enabled siblings.
            if (prerequisite.Options.Any(option => rows.Count(row => option.SourceId == ScalarCreation1949(row, "id")
                && option.Label == ScalarCreation1949(row, "name") && option.Rank == ScalarCreation1949(row, "value")
                && option.CategoryName == ScalarCreation1949(row, "category") && CurrentPriorityRowShape1950(row)) != 1))
                blockers.Add("current-priority-catalog-not-exact");
            return CharacterCreationFinalizationDigest.Compute(prerequisite);
        }
        if (!source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative
            || metatypes.Blockers.Count != 0 || metatypes.Options.Count != rows.Length
            || metatypes.Options.Any(option => option.IsEnabled != (option.Blockers.Count == 0)
                || option.ExcludedMetavariants.Any(variant => variant.Blockers.Count == 0))
            || rows.Any(row => !metatypes.Options.Any(option => option.OptionId == ScalarCreation1949(row, "id")
                && option.Label == ScalarCreation1949(row, "name"))))
            blockers.Add("current-metatype-catalog-not-exact");
        return CharacterCreationFinalizationDigest.Compute(new { prerequisite, metatypes });
    }

    private static bool CreationXmlSafe1950(XElement node)
        => node.DescendantsAndSelf().All(element => element.Name.Namespace == XNamespace.None
            && element.Attributes().All(attribute => attribute.Name.Namespace == XNamespace.None)
            && !element.Nodes().Any(item => item is not XElement && item is not XComment
                && (item is not XText text || text.GetType() != typeof(XText)))
            && (!element.HasElements || !element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))));

    private static bool CurrentPriorityRowShape1950(XElement row)
    {
        string[] fields = ["id", "name", "value", "category", "prioritytable", "attributes", "skills", "skillgroups",
            "resources", "metatypes", "talents"];
        return row.Name == "priority" && !row.HasAttributes && CreationXmlSafe1950(row)
            && !row.Elements().Any(field => !fields.Contains(field.Name.LocalName, StringComparer.Ordinal))
            && !row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)
            && row.Elements().Where(field => field.Name != "metatypes" && field.Name != "talents")
                .All(field => !field.HasAttributes && !field.HasElements);
    }

    private static string? CurrentOriginRowUnavailable1950(CharacterCurrentRuleDomain domain, XElement row,
        ICharacterSourceDataContext source)
    {
        string id = ScalarCreation1949(row, "id");
        if (domain == CharacterCurrentRuleDomain.Metatypes)
        {
            if (row.Name != "metatype" || !source.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative)
                return CreationUnavailable1949;
            var matches = catalog.Options.Where(option => option.OptionId == id && option.Label == ScalarCreation1949(row, "name")).ToArray();
            return matches is [var option] && option.IsEnabled && option.Blockers.Count == 0 ? null
                : matches is [var disabled] && disabled.Blockers.Count != 0 ? string.Join(";", disabled.Blockers)
                : CreationUnavailable1949;
        }
        if (!CurrentPriorityRowShape1950(row) || !source.TryResolveCreationPrerequisiteAuthority(out var authority)
            || !authority.IsAuthoritative) return CreationUnavailable1949;
        return authority.Options.Any(option => option.SourceId == id) ? null : "current-priority-row-outside-selected-table";
    }

    private static object? ConsumeCurrentLifestyles1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, ref CharacterCurrentRuleConsumptionState state)
    {
        if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation
            && source.TryResolveCreationSourceProfile(out var nativeProfile) && nativeProfile.BuildMethod == CharacterCreationBuildMethods.Karma)
            return ConsumeNativeKarmaLifestyles1958(current, catalog, source, selected, blockers, ref state);
        if (!TryLifestyleConsumerAuthority2719(current, source, out var authority)) return null;
        var projected = new List<CharacterCreationLifestyleProjection>();
        var saved = root.Element("lifestyles")?.Elements().ToArray() ?? [];
        if (root.Element("lifestyles") is { HasAttributes: true } || saved.Any(row => row.Name != "lifestyle"))
        { blockers.Add("current-lifestyle-saved-container-unresolved"); return null; }
        var auxiliary = current.Current.Document.AuxiliaryState;
        if (current.Stage == CharacterCurrentRuleStage.Career)
        {
            // This reads the original Career graph. It neither replays Creation
            // nor treats historical wizard receipts as a new purchase request.
            if (!TryCurrentLifestyleAuthority1950(current, source, out authority))
            { blockers.Add("current-career-lifestyle-state-unresolved"); return null; }
            IReadOnlyDictionary<Guid, string>? periods = null;
            if (UsesNativeLifecycleMechanics9569(current)
                && !CharacterCareerLifestyleMaintenanceProjection.TrySourcePeriods(current.CharacterXml,
                    new CreationExactSource1949(current.CharacterXml, source), out periods))
            { blockers.Add("current-career-lifestyle-source-period-unresolved"); return null; }
            foreach (var row in saved)
            {
                CharacterCreationLifestyleProjection? result = null;
                if (!Guid.TryParse(ScalarCreation1949(row, "guid"), out var id)
                    || !(UsesNativeLifecycleMechanics9569(current)
                        ? CharacterCareerLifestyleMaintenanceProjection.TryExistingForMaintenance(row, id, authority, out result, out _)
                        : CharacterCareerLifestyleMaintenanceProjection.TryExisting(row, id, authority, out result, out _))
                    || result is null
                    || periods is not null && (!periods.TryGetValue(result.SourceId, out var period)
                        || period != result.Configuration.IncrementId))
                { blockers.Add("current-career-lifestyle-state-unresolved"); return null; }
                projected.Add(result);
            }
        }
        else
        {
            if (auxiliary.CharacterCreationCompletionDrafts is { Count: > 0 })
            { blockers.Add("current-lifestyle-composite-selection-consumer-required"); return null; }
            if (auxiliary.CharacterCreationLifestylesDraft is { } draft)
            {
                if (saved.Length != 0 || !CharacterCreationLifestylesDraftRules.IsValidShape(current.Current.Id,
                    current.Current.ContentRevision, draft))
                { blockers.Add("current-lifestyle-draft-binding-unresolved"); return null; }
                foreach (var configuration in draft.Lifestyles)
                {
                    if (!CharacterCreationLifestylesRules.TryProject(configuration, authority, out var result, out var findings))
                        blockers.AddRange(findings);
                    else projected.Add(result);
                }
            }
            else foreach (var row in saved)
            {
                if (!CreationSavedLifestyleShape1949(row)
                    || !Guid.TryParse(ScalarCreation1949(row, "guid"), out var id)
                    || !CharacterCreationLifestylesService.TryReadConfiguration(row, id, authority, out var config, out var findings))
                { blockers.Add("current-lifestyle-saved-selection-unresolved"); return null; }
                if (!CharacterCreationLifestylesRules.TryProject(config, authority, out var result, out var projectionFindings))
                    blockers.AddRange(projectionFindings);
                else projected.Add(result);
            }
        }
        if (projected.Select(row => row.Configuration.LifestyleId).Distinct().Count() != projected.Count)
            blockers.Add("current-lifestyle-instance-duplicate");
        foreach (var result in projected)
        {
            var option = authority.LifestyleOptions.Single(option => option.OptionId == result.Configuration.BaseLifestyleOptionId);
            SelectCreationKey1949(catalog, "lifestyle", option.SourceId.ToString("D"), selected, blockers);
        }
        // Cost effects are global state even with no purchased lifestyle.
        if (projected.Count == 0 && authority.TrustFundLevel == 0 && authority.MetatypeCostPercent == 0
            && authority.PurchasedQualityCosts is null && !authority.FreeGridsEnabled)
            state = CharacterCurrentRuleConsumptionState.NotConfigured;
        return TagRuleEffects2912(new { authority.TrustFundLevel, authority.MetatypeCostPercent, authority.PurchasedQualityCosts,
            authority.FreeGridsEnabled, Lifestyles = projected.ToArray() },
            CharacterRuleEffectValues2912.Lifestyles(projected),
            "Lifestyle economics are calculated; source constraints and non-economic lifestyle effects remain separately scoped.");
    }

    private static object? ConsumeCurrentOrigins1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, ref CharacterCurrentRuleConsumptionState state)
    {
        if (UsesNativeMethods1958(current))
            return ConsumeNativeOrigins1958(current, catalog, source, root, selected, blockers, ref state);
        var original = current.Current.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (current.Stage == CharacterCurrentRuleStage.Career)
            return ConsumePartialOrigins1949(current, catalog, source, root, selected, blockers, ref state);
        if (current.Stage == CharacterCurrentRuleStage.Creation
            && current.Current.Document.AuxiliaryState.CharacterCreationCompletionDrafts is { Count: > 0 })
        { blockers.Add("current-origin-composite-selection-consumer-required"); return null; }
        if (original is null)
        {
            if (current.Current.Document.AuxiliaryState.CharacterCreationAttributesDraft is not null)
            { blockers.Add("current-origin-dependent-attributes-without-prerequisite"); return null; }
            return ConsumePartialOrigins1949(current, catalog, source, root, selected, blockers, ref state);
        }
        if (!source.TryResolveCreationPrerequisiteAuthority(out var authority)
            || original.WorkspaceId != current.Current.Id || original.BaseContentRevision <= 0
            || original.BaseContentRevision >= current.Current.ContentRevision
            || current.Stage == CharacterCurrentRuleStage.Creation
                && original.BaseRawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.CharacterXml)
            || !CharacterCreationPrerequisiteService.TryEvaluateCurrentSelection1949(authority, original, out var executed, out _)
            || executed is null)
        { blockers.Add("current-origin-canonical-selection-unresolved"); return null; }
        if (catalog.Domain == CharacterCurrentRuleDomain.Metatypes)
            SelectCreationKey1949(catalog, "metatype", executed.HeritageSelection!.MetatypeSourceId, selected, blockers);
        else foreach (var assignment in executed.Assignments)
            SelectCreationKey1949(catalog, "priority", assignment.SourceId, selected, blockers);
        var effects = CurrentMetatypeEffects1950(current, catalog, source, executed.HeritageSelection!.MetatypeSourceId, blockers);
        return TagRuleEffects2912(new { Historical = current.Stage == CharacterCurrentRuleStage.Career, Selection = executed, MetatypeEffects = effects },
            OriginEffects2912(executed.EffectiveNormalAttributePoints, executed.TotalSpecialAttributePoints, effects?.Metatype));
    }

    private static bool SameSpriteOriginSource5841(XElement row, XElement source)
        => row.Name == "metatype" && XNode.DeepEquals(CreationComparisonTree1949(row), CreationComparisonTree1949(source));

    private static CurrentCareerEffects1960? ObserveCurrentSpriteOrigin5841(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        List<string> blockers, out XElement? selectedSource)
    {
        selectedSource = null;
        if (!UsesImportedPriorityCareer5667(current)) return Fail();
        var effects = CompileCurrentCareerEffects1960(current, source, blockers);
        if (effects?.Imported?.Owners.Sprite5751 is null
            || !CharacterSheetDerivedQuery.TryMetatype(ParseCreation1949(current.CharacterXml), source,
                out string xml, out string sourceState, out _)
            || string.IsNullOrWhiteSpace(sourceState)) return Fail();
        var row = ParseCreation1949(xml);
        if (!CharacterSpriteSource5751.SupportsSource(row)
            || ScalarCreation1949(row, "id") != effects.Metatype.OptionId
            || ScalarCreation1949(row, "name") != effects.Metatype.Label || !current.IsUnchanged) return Fail();
        selectedSource = row;
        return effects;

        CurrentCareerEffects1960? Fail()
        { blockers.Add("current-sprite-origin-owner-unresolved"); return null; }
    }

    private static object? ConsumeCurrentSpriteOrigins5841(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        CurrentCareerEffects1960 imported, HashSet<string> selected, List<string> blockers,
        ref CharacterCurrentRuleConsumptionState state)
    {
        // The owner compiler has already evaluated the retained force expressions,
        // generated grants and saved bounds. Historical Priority fields cannot
        // turn that typed Sprite observation into ordinary Creation grants.
        if (catalog.Domain == CharacterCurrentRuleDomain.Priorities)
        {
            state = CharacterCurrentRuleConsumptionState.NotConfigured;
            return TagRuleEffects2912(new { Historical = true, ImportedOwnerEffects = imported.Digest }, [],
                "Sprite origins are source-owned; no Priority grants are inferred.");
        }
        if (catalog.Domain != CharacterCurrentRuleDomain.Metatypes || catalog.Input.Query != "critters.xml")
        { blockers.Add("current-sprite-origin-catalog-required"); return null; }
        SelectCreationKey1949(catalog, "metatype", imported.Metatype.OptionId, selected, blockers);
        if (root.Element("movement") is { } movement
                && (movement.HasAttributes || movement.HasElements || movement.Value != "Special")
            || new[] { "init", "initiative", "reach", "armor" }.Any(field => root.Element(field) is { } value
                && (value.HasElements || !string.IsNullOrWhiteSpace(value.Value)))
            || !CurrentMetatypeScalars1950(root, imported.Metatype))
        { blockers.Add("current-sprite-origin-source-scalars-unresolved"); return null; }
        if (!source.TryResolveCreationAttributePolicy(out var policy) || policy is null)
        { blockers.Add("current-career-metatype-attribute-policy-unavailable"); return null; }
        var attributes = EvaluateCareerAttributes1960(current, source, policy, blockers);
        return TagRuleEffects2912(new { Historical = true, Metatype = imported.Metatype,
            ImportedOwnerEffects = imported.Digest, CareerAttributes = attributes },
            OriginEffects2912(null, null, imported.Metatype).Concat(CharacterRuleEffectValues2912.CareerAttributes(attributes)),
            "Sprite bounds and grants are evaluated from the retained selected source and reconciled owners.");
    }

    private static object? ConsumePartialOrigins1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, ref CharacterCurrentRuleConsumptionState state)
    {
        if (UsesOwnedPriorityCareer9569(current))
        {
            var imported = CompileCurrentCareerEffects1960(current, source, blockers);
            if (imported is null) return null;
            if (imported.Imported?.Owners.Sprite5751 is not null)
                return ConsumeCurrentSpriteOrigins5841(current, catalog, source, root, imported, selected, blockers, ref state);
        }
        if (!source.TryResolveCreationPrerequisiteAuthority(out var authority)) return null;
        (string Field, string Category)[] fields = [("prioritymetatype", CharacterCreationPriorityCategoryIds.Heritage),
            ("priorityattributes", CharacterCreationPriorityCategoryIds.Attributes), ("priorityspecial", CharacterCreationPriorityCategoryIds.Talent),
            ("priorityskills", CharacterCreationPriorityCategoryIds.Skills), ("priorityresources", CharacterCreationPriorityCategoryIds.Resources)];
        // The outer shape gate already admits the source-proved Windows rank/choices
        // pair. Keep that same proof here, and read its scalar independently of order.
        bool importedSkillsPair = current.Stage == CharacterCurrentRuleStage.Career
            && UsesImportedPriorityCareer5667(current) && root.Elements("priorityskills").Count() == 2
            && CharacterCreationPriorityGrantAuthority3563.CaptureImported5693(current).IsImported5693;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, category) in fields)
        {
            var value = importedSkillsPair && field == "priorityskills"
                ? root.Elements(field).Single(node => !node.HasElements && !string.IsNullOrWhiteSpace(node.Value))
                : root.Element(field);
            if (value is null) continue;
            if (value.HasAttributes || value.HasElements) { blockers.Add("current-origin-rank-malformed"); return null; }
            string rank = value.Value;
            if (current.Stage == CharacterCurrentRuleStage.Career && UsesPriorityCurrent3027(current)
                && rank.Contains(','))
            {
                // Finalization persists rank and source weight together. Validate the
                // complete canonical pair before passing the rank to the existing kernels;
                // the saved weight is never an independent source of authority.
                var matches = authority.Options.Where(option => option.CategoryId == category
                    && string.Create(CultureInfo.InvariantCulture,
                        $"{option.Rank},{option.SumToTenValue}") == rank).ToArray();
                if (matches is not [var matched])
                { blockers.Add("current-origin-rank-malformed"); return null; }
                rank = matched.Rank;
            }
            if (!string.IsNullOrEmpty(rank)) ranks.Add(category, rank);
        }
        if (UsesPriorityCurrent3027(current)
            && fields.Any(field => root.Elements(field.Field).Count() > 1
                && !(field.Field == "priorityskills" && importedSkillsPair)))
        { blockers.Add("current-origin-rank-malformed"); return null; }
        string metatype = ScalarCreation1949(root, "metatypeguid");
        string metatypeName = ScalarCreation1949(root, "metatype");
        string talentName = ScalarCreation1949(root, "prioritytalent");
        if (new[] { "metatypeguid", "metatype", "metavariantguid", "metavariant", "prioritytalent" }
            .Any(field => UsesPriorityCurrent3027(current) && root.Elements(field).Count() > 1
                || root.Element(field) is { } value && (value.HasElements || value.HasAttributes))
            || ScalarCreation1949(root, "metavariant") is not ("" or "None")
            || ScalarCreation1949(root, "metavariantguid") is { Length: > 0 } variant && variant != Guid.Empty.ToString("D"))
        { blockers.Add("current-origin-identity-unsupported"); return null; }
        string? heritageId = null, talentId = null;
        CharacterCreationMetatypeOptionProjection? heritageOnly = null;
        if (!string.IsNullOrEmpty(metatype) || !string.IsNullOrEmpty(metatypeName))
        {
            if (!source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative) return null;
            var matches = metatypes.Options.Where(option => option.IsEnabled && option.Blockers.Count == 0
                && (string.IsNullOrEmpty(metatype) || option.OptionId == metatype)
                && (string.IsNullOrEmpty(metatypeName) || option.Label == metatypeName)).ToArray();
            if (matches is not [var found]) { blockers.Add("current-origin-metatype-unresolved"); return null; }
            heritageOnly = found;
            if (ranks.TryGetValue(CharacterCreationPriorityCategoryIds.Heritage, out string? rank))
            {
                var options = authority.Options.Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                    && option.Rank == rank).SelectMany(option => option.HeritageOptions)
                    .Where(option => option.MetatypeSourceId == found.OptionId && option.MetavariantSourceId is null).ToArray();
                if (options is not [var chosen]) { blockers.Add("current-origin-heritage-selection-unresolved"); return null; }
                heritageId = chosen.SelectionId;
            }
        }
        if (!string.IsNullOrEmpty(talentName))
        {
            if (!ranks.TryGetValue(CharacterCreationPriorityCategoryIds.Talent, out string? rank))
            { blockers.Add("current-origin-talent-rank-required"); return null; }
            var options = authority.Options.Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == rank).SelectMany(option => option.TalentOptions)
                .Where(option => option.SelectionId == talentName || option.Name == talentName || option.Value == talentName).ToArray();
            if (options is not [var chosen]) { blockers.Add("current-origin-talent-selection-unresolved"); return null; }
            talentId = chosen.SelectionId;
        }
        CharacterCreationPrerequisiteService.CurrentPartialSelection1949? result;
        if (UsesNativePriorityCareer9569(current))
        {
            var read = CompileCurrentCareerEffects1960(current, source, blockers)?.Imported?.Read;
            if (read is null || !read.HasPrioritySelection9569 || read.PriorityGrants is null
                || !read.PriorityGrants.TryResolveNativeSelection9569(root, source, out var confirmed))
            { blockers.Add("current-native-priority-history-selection-unresolved"); return null; }
            result = new(confirmed.Assignments.ToArray(), confirmed.HeritageSelection,
                confirmed.TalentSelection, [])
            {
                EffectiveNormalAttributePoints = confirmed.EffectiveNormalAttributePoints,
                TotalSpecialAttributePoints = confirmed.TotalSpecialAttributePoints
            };
        }
        else if (!CharacterCreationPrerequisiteService.TryEvaluatePartialCurrentSelection1949(authority, ranks,
            heritageId, talentId, out result, out var findings) || result is null)
        { blockers.AddRange(findings); return null; }
        if (catalog.Domain == CharacterCurrentRuleDomain.Metatypes)
        {
            if (heritageOnly is null) state = CharacterCurrentRuleConsumptionState.NotConfigured;
            else SelectCreationKey1949(catalog, "metatype", heritageOnly.OptionId, selected, blockers);
        }
        else
        {
            foreach (var assignment in result.Assignments)
                SelectCreationKey1949(catalog, "priority", assignment.SourceId, selected, blockers);
            if (result.Assignments.Count == 0) state = CharacterCurrentRuleConsumptionState.NotConfigured;
        }
        CharacterCareerAttributeAdvanceQuote[]? careerAttributes = null;
        if (current.Stage == CharacterCurrentRuleStage.Career && catalog.Domain == CharacterCurrentRuleDomain.Metatypes)
        {
            if (new[] { "movement", "init", "initiative", "reach", "armor" }
                .Any(field => root.Element(field) is { } value && (value.HasElements || !string.IsNullOrWhiteSpace(value.Value))))
            { blockers.Add("current-career-metatype-derived-mechanics-consumer-required"); return null; }
            if (!CurrentMetatypeScalars1950(root, heritageOnly))
            { blockers.Add("current-career-metatype-source-scalars-unresolved"); return null; }
            if (!source.TryResolveCreationAttributePolicy(out var policy) || policy is null)
            { blockers.Add("current-career-metatype-attribute-policy-unavailable"); return null; }
            careerAttributes = UsesOwnedPriorityCareer9569(current)
                ? EvaluateCareerAttributes1960(current, source, policy, blockers)
                : EvaluateCareerAttributes1949(root, source, policy, blockers, UsesPriorityCurrent3027(current));
        }
        // Imported Career uses the actual reconciled saved owners. A generated
        // Creation preview cannot prove that those owners exist in an import.
        var importedEffects = UsesOwnedPriorityCareer9569(current)
            ? CompileCurrentCareerEffects1960(current, source, blockers) : null;
        if (UsesOwnedPriorityCareer9569(current) && importedEffects is null) return null;
        var effects = importedEffects is not null || heritageOnly is null ? null
            : CurrentMetatypeEffects1950(current, catalog, source, heritageOnly.OptionId, blockers);
        if (UsesPriorityCurrent3027(current))
        {
            // Source rows, not ranks converted with a second table. Null choices
            // stay pending. A Resources rank is a grant, not selected Karma
            // investment or a current cash/spend balance.
            var selectedRows = result.Assignments.Select(assignment => authority.Options.Single(option =>
                option.CategoryId == assignment.CategoryId && option.SourceId == assignment.SourceId)).ToArray();
            var outcome = TagRuleEffects2912(new { Historical = current.Stage == CharacterCurrentRuleStage.Career, Selection = result,
                SelectedRows = selectedRows, authority.PriorityTable, authority.PriorityArray, authority.SumToTenTarget,
                SumToTenUsed = result.Assignments.Sum(assignment => assignment.SumToTenValue), authority.CreationKarmaTotal,
                MetatypeWithoutPriority = heritageId is null ? heritageOnly : null, MetatypeEffects = effects, CareerAttributes = careerAttributes },
                OriginEffects2912(result.EffectiveNormalAttributePoints, result.TotalSpecialAttributePoints, heritageOnly)
                    .Concat(CharacterRuleEffectValues2912.CareerAttributes(careerAttributes ?? [])), result.MissingChoices.ToArray());
            return importedEffects is null ? outcome : ForwardRuleEffects2912(
                new { Operation = outcome, ImportedOwnerEffects = importedEffects.Digest }, outcome);
        }
        return TagRuleEffects2912(new { Historical = current.Stage == CharacterCurrentRuleStage.Career, Selection = result,
            MetatypeWithoutPriority = heritageId is null ? heritageOnly : null, MetatypeEffects = effects, CareerAttributes = careerAttributes },
            OriginEffects2912(result.EffectiveNormalAttributePoints, result.TotalSpecialAttributePoints, heritageOnly)
                .Concat(CharacterRuleEffectValues2912.CareerAttributes(careerAttributes ?? [])), result.MissingChoices.ToArray());
    }

    private static bool CurrentMetatypeScalars1950(XElement root, CharacterCreationMetatypeOptionProjection? metatype)
    {
        foreach (var (field, rate) in new[] { ("walk", metatype?.Movement.Walk),
            ("run", metatype?.Movement.Run), ("sprint", metatype?.Movement.Sprint) })
        {
            if (root.Element(field) is not { } saved) continue;
            if (rate is null || saved.HasAttributes || saved.HasElements) return false;
            string[] parts = saved.Value.Split('/');
            if (parts.Length != 3) return false;
            decimal[] expected = [rate.Ground, rate.Swim, rate.Fly];
            for (int index = 0; index < parts.Length; index++)
                if (!decimal.TryParse(parts[index], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                    || value != expected[index]) return false;
        }
        return root.Element("initiativedice") is not { } dice || metatype is not null
            && !dice.HasAttributes && !dice.HasElements
            && int.TryParse(dice.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valueDice)
            && valueDice == (metatype.Initiative.DirectDice886 ?? metatype.Initiative.MinimumDiceFallback);
    }

    private static object? ConsumeCurrentProfile1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, CancellationToken cancellationToken)
    {
        if (UsesNativeMethods1958(current))
            return ConsumeNativeProfile1958(current, catalog, source, root, selected, blockers, cancellationToken);
        if (!source.TryResolveCreationAttributePolicy(out var policy) || policy is null) return null;
        var profile = CreationRows1949(catalog.Input).Single(row => MatchesSettingsRow5691(source, row, policy.SettingsProfileId));
        SelectCreationKey1949(catalog, "setting", ScalarCreation1949(profile, "id"), selected, blockers);
        if (!TryObserveCurrentMetatypeProfile1951(current, source, profile, out var metatypePolicy))
        { blockers.Add("current-profile-metatype-policy-unresolved"); return null; }
        string? purchasePolicy = null;
        if (profile.Descendants().Any(field => IsCurrentPurchaseProfileSetting1950(field.Parent!.Name.LocalName, field.Name.LocalName))
            && !TryObserveCurrentPurchaseProfileSettings1950(current, source, profile, out purchasePolicy))
        { blockers.Add("current-profile-purchase-policy-unresolved"); return null; }
        string? skillPolicy = null;
        if (profile.Descendants().Any(field => IsCurrentSkillProfileSetting1951(field.Parent!.Name.LocalName, field.Name.LocalName))
            && !TryObserveCurrentSkillProfileSettings1951(current, source, profile, out skillPolicy))
        { blockers.Add("current-profile-skill-policy-unresolved"); return null; }
        int? cap = CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(profile);
        var auxiliary = current.Current.Document.AuxiliaryState;
        CharacterCreationContactsState? contacts = null;
        if (UsesPriorityCurrent3027(current) && current.Stage == CharacterCurrentRuleStage.Creation)
        {
            if (root.Elements("contacts").Count() > 1)
            { blockers.Add("current-priority-contacts-unresolved"); return null; }
            if (auxiliary.CharacterCreationKarmaMetatypeDecisions is { Count: > 0 }
                || auxiliary.CharacterCreationFoundationDraft is not null || auxiliary.LifeModuleDecisionAcceptances is { Count: > 0 })
            { blockers.Add("current-priority-mixed-creation-ledgers"); return null; }
            if (auxiliary.CharacterCreationContactsDraft is not null || auxiliary.CharacterCreationContactReceipts is { Count: > 0 }
                || root.Elements("contacts").Any(node => node.HasAttributes || node.HasElements || !string.IsNullOrWhiteSpace(node.Value)))
            {
                var loaded = new CharacterCreationContactsService(new WorkspaceContinuationReadView(current.Current),
                    new CreationExactSource1949(current.CharacterXml, source), current.Queries).Load(new(current.Current.Id));
                blockers.AddRange(loaded.Blockers.Where(blocker => blocker != CharacterCreationContactsBlockers.PersistenceAuthorityRequired));
                contacts = loaded.Value;
                if (contacts is null) { blockers.Add("current-priority-contacts-unresolved"); return null; }
            }
        }
        if (!UsesOwnedPriorityCareer9569(current) && root.Elements("improvements").Any(node => node.Elements().Any()))
        { blockers.Add("current-profile-character-effects-consumer-required"); return null; }
        if (current.Stage == CharacterCurrentRuleStage.Career)
        {
            SelectedCompletionResourcesPrice[]? configuredFunding = null;
            if (HasCreationFundingFields1949(profile))
            {
                if (!source.TryResolveCreationResourcesAuthority(out var fundingPolicy)
                    || !CharacterCreationResourcesRules.IsValidAuthority(fundingPolicy))
                { blockers.Add("current-profile-resources-authority-unresolved"); return null; }
                configuredFunding = EvaluateFundingOptions1949(fundingPolicy, blockers, cancellationToken);
            }
            var career = UsesOwnedPriorityCareer9569(current)
                ? EvaluateCareerAttributes1960(current, source, policy, blockers)
                : EvaluateCareerAttributes1949(root, source, policy, blockers, UsesPriorityCurrent3027(current));
            if (UsesOwnedPriorityCareer9569(current))
            {
                // Also execute the actual playtime sheet from the same retained
                // sources. This validates complete normal attributes and current
                // initiative/sustaining policy; saved totals are not authority.
                if (!TryObserveNativeSheetProfile5130(current, source, profile, out var importedSheet) || importedSheet is null)
                { blockers.Add("current-imported-priority-sheet-policy-unresolved"); return null; }
                return TagRuleEffects2912(new { Policy = policy, MaximumAttributesAtCap = cap, MetatypePolicy = metatypePolicy,
                    PurchasePolicy = purchasePolicy, SkillPolicy = skillPolicy, SheetPolicy = importedSheet,
                    CareerAttributes = career, ConfiguredFundingPolicyQuotes = configuredFunding },
                    CharacterRuleEffectValues2912.CareerAttributes(career),
                    "Current persisted Career attributes and retained sheet policy are calculated; missing creation choices remain pending.");
            }
            return TagRuleEffects2912(new { Policy = policy, MaximumAttributesAtCap = cap, MetatypePolicy = metatypePolicy,
                PurchasePolicy = purchasePolicy, SkillPolicy = skillPolicy,
                CareerAttributes = career, ConfiguredFundingPolicyQuotes = configuredFunding },
                CharacterRuleEffectValues2912.CareerAttributes(career),
                "Current attribute advancement quotes are calculated. Current nuyen balance and configured future funding are not recalculated cash in this consumer.");
        }
        if (auxiliary.CharacterCreationCompletionDrafts is { Count: > 0 })
        { blockers.Add("current-profile-composite-selection-consumer-required"); return null; }
        CharacterCreationAttributesState? allocationState = null;
        if (auxiliary.CharacterCreationAttributesDraft is { } attributes)
        {
            // Load executes the complete ordinary allocation kernel, including
            // racial/talent minima, budgets and maximum-count checks. A copied
            // saved projection or isolated per-attribute sum is insufficient.
            var service = new CharacterCreationAttributesService(new WorkspaceContinuationReadView(current.Current),
                new CreationExactSource1949(current.CharacterXml, source));
            var loaded = UsesPriorityCurrent3027(current)
                ? service.LoadCurrentObservation3053(new(current.Current.Id))
                : service.Load(new(current.Current.Id));
            allocationState = loaded.Value;
            blockers.AddRange(loaded.Blockers.Where(blocker => blocker != CharacterCreationAttributesBlockers.PersistenceAuthorityRequired));
            if (allocationState?.PendingDraft?.DraftDigest != attributes.DraftDigest)
                blockers.Add("current-profile-attribute-draft-unresolved");
        }
        else if (root.Element("attributes")?.Elements().Any() == true)
        { blockers.Add("current-profile-raw-attributes-consumer-required"); return null; }
        CharacterCreationResourcesState? fundingState = null;
        if (auxiliary.CharacterCreationResourcesDraft is { } funding)
        {
            var service = new CharacterCreationResourcesService(new WorkspaceContinuationReadView(current.Current),
                new CreationExactSource1949(current.CharacterXml, source), current.Queries);
            var loaded = UsesPriorityCurrent3027(current)
                ? service.LoadCurrentObservation3053(new(current.Current.Id))
                : service.Load(new(current.Current.Id));
            fundingState = loaded.Value;
            blockers.AddRange(loaded.Blockers.Where(blocker => blocker != CharacterCreationResourcesBlockers.PersistenceAuthorityRequired));
            if (fundingState?.PendingDraft?.DraftDigest != funding.DraftDigest)
                blockers.Add("current-profile-funding-current-basket-unresolved");
        }
        if (!source.TryResolveCreationResourcesAuthority(out var resources)) return null;
        var futureFunding = EvaluateFundingOptions1949(resources, blockers, cancellationToken);
        // These global settings remain evaluated even for an incomplete wizard.
        // No item, rank, metatype, or lifestyle is chosen to manufacture a result.
        if (UsesPriorityCurrent3027(current))
            return TagRuleEffects2912(new { Policy = policy, MaximumAttributesAtCap = cap, MetatypePolicy = metatypePolicy,
                PurchasePolicy = purchasePolicy, SkillPolicy = skillPolicy,
                Allocations = allocationState?.Attributes, NormalBudget = allocationState?.NormalPointBudget,
                SpecialBudget = allocationState?.SpecialPointBudget, KarmaBudget = allocationState?.CreationKarmaBudget,
                CurrentFunding = fundingState?.Budget, FutureFundingQuotes = futureFunding, Contacts = contacts },
                ProfileEffects2912(allocationState, fundingState),
                allocationState is null ? "Attribute allocations are pending." : "Current allocations were recalculated.",
                fundingState is null ? "Resource investment and spending are pending." : "Current funding was recalculated.",
                contacts is null ? "Contact selections are pending; no contact budget or expenditure is inferred." : "Current contact costs were recalculated.");
        return TagRuleEffects2912(new { Policy = policy, MaximumAttributesAtCap = cap, MetatypePolicy = metatypePolicy,
            PurchasePolicy = purchasePolicy, SkillPolicy = skillPolicy,
            Allocations = allocationState?.Attributes, NormalBudget = allocationState?.NormalPointBudget,
            SpecialBudget = allocationState?.SpecialPointBudget, KarmaBudget = allocationState?.CreationKarmaBudget,
            CurrentFunding = fundingState?.Budget, FutureFundingQuotes = futureFunding },
            ProfileEffects2912(allocationState, fundingState),
            allocationState is null ? "Attribute allocations have not been selected." : "Creation allocations are unaugmented values, not a finalized sheet.",
            fundingState is null ? "Current resource funding has not been selected." : "Funding budget recalculated by the current resources service.");
    }

    private static bool IsCurrentMetatypeProfileSetting1951(string name)
        => name is "metatypecostskarma" or "metatypecostskarmamultiplier" or "mininitiativedice" or "dronemods";

    private static bool TryObserveCurrentMetatypeProfile1951(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out string? canonicalPolicyDigest)
    {
        canonicalPolicyDigest = null;
        if (!profile.Elements().Any(field => IsCurrentMetatypeProfileSetting1951(field.Name.LocalName))) return true;
        if (!current.IsUnchanged || !source.TryResolveCareerSettingsProfile(out var settings, out var settingsState)
            || string.IsNullOrWhiteSpace(settingsState)
            || !XNode.DeepEquals(ParseCreation1949(settings), new XElement("settings", profile.Nodes()))
            || !source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative
            || metatypes.Blockers.Count != 0 || !metatypes.SourceContext.IsAuthoritative
            || metatypes.SourceContext.Blockers.Count != 0
            || !MatchesSettingsRow5691(source, profile, metatypes.SourceContext.SettingsProfileId)) return false;
        var fields = profile.Elements().Where(field => IsCurrentMetatypeProfileSetting1951(field.Name.LocalName)).ToArray();
        if (fields.GroupBy(field => field.Name).Any(group => group.Count() != 1)
            || fields.Any(field => field.HasAttributes || field.HasElements)) return false;
        // The resolver executes the canonical current metatype projector: karma
        // multiplier, initiative fallback and drone policy are represented by its
        // typed source context and computed available options. Disabled catalog
        // siblings are not invented current selections or global policy failures.
        canonicalPolicyDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-metatype-profile-policy.v1", current.BindingDigest,
            Settings = settings, SettingsState = settingsState, metatypes.SourceContext,
            Options = metatypes.Options.Where(option => option.IsEnabled && option.Blockers.Count == 0).ToArray()
        });
        return current.IsUnchanged;
    }

    private static CharacterCareerAttributeAdvanceQuote[] EvaluateCareerAttributes1949(XElement root,
        ICharacterSourceDataContext source, CharacterCreationAttributePolicy policy, List<string> blockers,
        bool canonicalAttributeRows)
    {
        var quotes = new List<CharacterCareerAttributeAdvanceQuote>();
        var attributes = root.Element("attributes")?.Elements().ToArray() ?? [];
        if (attributes.Length == 0) return [];
        // A bounded ordinary, unmodified graph. Other attribute effects need the
        // full existing Career projector, not a guessed cap or ignored modifier.
        if (root.Element("attributes")!.HasAttributes || root.Elements("improvements").Any(node => node.Elements().Any())
            || root.Element("burnededge") is { } burned && (!int.TryParse(burned.Value, out int burn) || burn != 0)
            || !source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative)
        { blockers.Add("current-career-attribute-effects-consumer-required"); return []; }
        string id = ScalarCreation1949(root, "metatypeguid");
        string name = ScalarCreation1949(root, "metatype");
        var selected = metatypes.Options.Where(option => option.OptionId == id && option.Label == name
            && option.IsEnabled && option.Blockers.Count == 0).ToArray();
        if (selected is not [var metatype] || !int.TryParse(ScalarCreation1949(root, "karma"), out int karma)
            || new[] { "magenabled", "resenabled", "depenabled" }.Any(field => root.Element(field) is { } flag
                && (!bool.TryParse(flag.Value, out bool enabled) || enabled)))
        { blockers.Add("current-career-attribute-authority-unresolved"); return []; }
        string[] allowed = ["name", "base", "karma", "metatypemin", "metatypemax", "metatypeaug", "totalvalue", "limits", "notes"];
        // The Priority producer and V9 Career import retain canonical saved rows,
        // including disabled attributes and Essence. They need source validation,
        // not advancement quotes. Keep V2 and all older unmarked grammar unchanged.
        if (canonicalAttributeRows) allowed = [.. allowed, "metatypeaugmax", "metatypecategory"];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in attributes)
        {
            string attribute = ScalarCreation1949(row, "name");
            bool retainedOnly = canonicalAttributeRows && attribute is "MAG" or "RES" or "DEP" or "ESS";
            if (row.Name != "attribute" || row.HasAttributes || !identities.Add(attribute)
                || row.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                || row.Elements().Any(node => node.HasAttributes || node.HasElements || !allowed.Contains(node.Name.ToString(), StringComparer.Ordinal))
                || canonicalAttributeRows && (DirectSkillText1949(row)
                    || row.Element("metatypecategory") is { } category && category.Value != "Standard")
                || !int.TryParse(ScalarCreation1949(row, "base"), out int basePoints)
                || !int.TryParse(ScalarCreation1949(row, "karma"), out int karmaPoints))
            { blockers.Add("current-career-attribute-shape-unresolved"); return []; }
            var ranges = metatype.Attributes.Where(range => range.AttributeId == attribute).ToArray();
            bool disabled = retainedOnly && attribute != "ESS";
            if (ranges is not [var range]
                || !int.TryParse(ScalarCreation1949(row, "metatypemin"), out int minimum) || minimum != (disabled ? 0 : range.Minimum)
                || !int.TryParse(ScalarCreation1949(row, "metatypemax"), out int maximum) || maximum != (disabled ? 0 : range.Maximum)
                || row.Element("metatypeaug") is null && (!canonicalAttributeRows || row.Element("metatypeaugmax") is null)
                || row.Element("metatypeaug") is { } legacyAugmented
                    && (!int.TryParse(legacyAugmented.Value, out int augmented) || augmented != (disabled ? 0 : range.AugmentedMaximum))
                || canonicalAttributeRows && row.Element("metatypeaugmax") is { } canonicalAugmented
                    && (!int.TryParse(canonicalAugmented.Value, out int augmentedMax) || augmentedMax != (disabled ? 0 : range.AugmentedMaximum)))
            { blockers.Add("current-career-attribute-source-range-mismatch"); return []; }
            int effective = retainedOnly && attribute == "ESS" ? maximum : checked(minimum + basePoints + karmaPoints);
            if (retainedOnly && (basePoints != 0 || karmaPoints != 0)
                || row.Element("totalvalue") is { } total && (!int.TryParse(total.Value, out int saved) || saved != effective)
                || row.Element("limits") is { } limits && !string.IsNullOrWhiteSpace(limits.Value))
            { blockers.Add("current-career-attribute-derived-state-unresolved"); return []; }
            if (retainedOnly) continue;
            if (!CharacterCareerAttributeAdvanceRules.TryCreateIdentity(attribute, out var identity)
                || identity.Kind is not (CharacterCareerAttributeKind.Normal or CharacterCareerAttributeKind.Edge))
            { blockers.Add("current-career-attribute-shape-unresolved"); return []; }
            var input = new CharacterCareerAttributeAdvanceInput(identity, true, "sr5", attribute, basePoints, karmaPoints,
                effective, maximum, minimum, karma, false, false, false, false, 0,
                new(policy.KarmaAttribute, policy.AlternateMetatypeAttributeKarma), [],
                row.ToString(SaveOptions.DisableFormatting), CharacterCreationFinalizationDigest.Compute(new { policy, metatype }));
            if (!CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote))
            { blockers.Add("current-career-attribute-canonical-quote-unresolved"); return []; }
            quotes.Add(quote);
        }
        return quotes.ToArray();
    }

    private static SelectedCompletionResourcesPrice[] EvaluateFundingOptions1949(CharacterCreationResourcesAuthority authority,
        List<string> blockers, CancellationToken cancellationToken)
    {
        var results = new List<SelectedCompletionResourcesPrice>();
        foreach (var option in authority.PriorityOptions)
        foreach (int investment in new[] { 0, authority.MaximumKarmaInvestment }.Distinct())
        {
            // Explicit future empty-basket quote, not current selection or a
            // fabricated wallet. The real current basket above has its own Load.
            var quoted = CharacterCreationResourcesService.PriceSelectedCompletion(new(authority, option,
                authority.MaximumKarmaInvestment, investment, true, 0m, []), cancellationToken, includeOptions: false);
            if (quoted.Value is not { } price || quoted.Blockers.Count != 0)
                blockers.AddRange(quoted.Blockers.Count == 0 ? ["current-profile-funding-quote-unresolved"] : quoted.Blockers);
            else results.Add(price);
        }
        return results.ToArray();
    }

    private static bool HasCreationFundingFields1949(XElement profile)
        => new[] { "nuyenperbpwftm", "chargenkarmatonuyenexpression", "nuyenmaxbp", "nuyencarryover", "availability", "unrestrictednuyen" }
            .Any(field => profile.Element(field) is not null);

    private sealed class CreationExactSource1949(string xml, ICharacterSourceDataContext source) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => string.Equals(xml, characterXml, StringComparison.Ordinal) ? source : null;
    }

    private static void SelectCreationKey1949(CharacterCurrentRuleCatalogValidation catalog, string element,
        string id, HashSet<string> selected, List<string> blockers)
    {
        var matches = catalog.Entries.Where(entry => entry.State == CharacterCurrentRuleEntryState.AvailableNotSelected
            && ParseCreation1949(entry.Xml).DescendantsAndSelf(element).Any(row => row.Element("id")?.Value == id)).ToArray();
        if (matches is [var match]) selected.Add(match.Key);
        else blockers.Add("current-creation-selected-entry-unresolved");
    }

    private static XElement[] CreationRows1949(CharacterCurrentRuleCatalogInput input)
    {
        string element = input.Domain switch
        { CharacterCurrentRuleDomain.Profile => "setting", CharacterCurrentRuleDomain.Metatypes => "metatype",
            CharacterCurrentRuleDomain.Priorities => "priority", _ => "lifestyle" };
        return input.Entries.Where(entry => entry.State != CharacterCurrentRuleEntryState.Tombstone)
            .SelectMany(entry => ParseCreation1949(entry.Xml).DescendantsAndSelf(element))
            .Where(row => row.Element("id") is not null).ToArray();
    }

    private static XElement ParseCreation1949(string xml)
    {
        using var reader = CreateCurrentRuleXmlReader(xml, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw new InvalidDataException();
    }

    private static XElement CreationComparisonTree1949(XElement node)
    {
        var copy = new XElement(node);
        copy.DescendantNodes().OfType<XText>().Where(text => text.GetType() == typeof(XText)
            && string.IsNullOrWhiteSpace(text.Value)).Remove();
        return copy;
    }

    private static bool MatchesProfileSettings6254(string settings, XElement profile)
    {
        // Both trees are owned by this invocation: parsing creates the first,
        // and attached profile nodes are copied into the second. Normalize those
        // trees directly instead of cloning each again. No source/caller tree or
        // parsed result escapes, and comparison alone grants no rule authority.
        var retained = ParseCreation1949(settings);
        RemoveWhitespace(retained);
        var candidate = new XElement("settings", profile.Nodes());
        RemoveWhitespace(candidate);
        return XNode.DeepEquals(retained, candidate);

        static void RemoveWhitespace(XElement owned)
            => owned.DescendantNodes().OfType<XText>().Where(text => text.GetType() == typeof(XText)
                && string.IsNullOrWhiteSpace(text.Value)).Remove();
    }

    private static string ScalarCreation1949(XElement row, string name)
        => row.Elements(name).ToArray() is [var node] && !node.HasAttributes && !node.HasElements
            ? node.Value : string.Empty;
    private static decimal DecimalCreation1949(XElement row, string name)
        => decimal.Parse(ScalarCreation1949(row, name), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static bool CreationInputError1949(Exception error) => error is XmlException or InvalidDataException
        or ArgumentException or InvalidOperationException or OverflowException or FormatException;

    private static bool IsCurrentMysticAdeptProfileSetting1969(string parent, string name)
        => parent == "karmacost" ? name == "karmamysadpp"
            : parent == "setting" && name is "priorityspellsasadeptpowers" or "mysadeptsecondmagattribute";

    private static bool IsCurrentProfilePolicySetting1969(string parent, string name)
        => parent == "setting" && IsCurrentMetatypeProfileSetting1951(name)
            || IsCurrentPurchaseProfileSetting1950(parent, name)
            || IsCurrentSkillProfileSetting1951(parent, name)
            || IsCurrentMysticAdeptProfileSetting1969(parent, name);

    private static bool IsCurrentNativeMagicProfileSetting1986(string parent, string name)
        => parent == "karmacost" ? name is "karmaspell" or "karmanewcomplexform"
            : parent == "setting" && name == "ignorecomplexformlimit";

    private static bool CreationLifestyleAspects2001(XElement container, string member)
    {
        // The selected resolver matches trimmed names ordinally and requires
        // nonnegative Int32 bounds. Validate every global row, including names
        // not used by a lifestyle option; ignored XML is not supported syntax.
        if (container.Parent?.Name != "chummer" || container.HasAttributes
            || !CreationXmlSafe1950(container)
            || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in container.Elements())
        {
            if (row.Name != member || row.HasAttributes
                || row.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || row.Elements().Count() != 3
                || row.Elements("name").ToArray() is not [var name]
                || row.Elements("minimum").ToArray() is not [var minimum]
                || row.Elements("limit").ToArray() is not [var limit]
                || row.Elements().Any(field => field.HasAttributes || field.HasElements)
                || string.IsNullOrWhiteSpace(name.Value) || !names.Add(name.Value.Trim())
                || !int.TryParse(minimum.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lower)
                || lower < 0
                || !int.TryParse(limit.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int upper)
                || upper < lower) return false;
        }
        return true;
    }

    private static bool UsesImportedPriorityCareer5667(CharacterCurrentRuleEnvironmentContext current)
    {
        if (!CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(current.SemanticsProfile, current.SemanticsFingerprint)
            || current.Stage != CharacterCurrentRuleStage.Career
            || current.Current.Document.RulesetId != "sr5") return false;
        XElement character = ParseCreation1949(current.CharacterXml);
        return ScalarCreation1949(character, "buildmethod") == CharacterCreationBuildMethods.Priority
            && !character.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).Any();
    }

    private static bool UsesPriorityCurrent3027(CharacterCurrentRuleEnvironmentContext current)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV7
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV7
            && current.Current.Document.RulesetId == "sr5"
            && ScalarCreation1949(ParseCreation1949(current.CharacterXml), "buildmethod")
                is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            || UsesOwnedPriorityCareer9569(current);

    private static bool PriorityProfile3027(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile)
    {
        // Same bounded full-profile grammar as the existing V7 native preset,
        // with Priority-specific policy readers. This is not a preset checksum or
        // permission to ignore an unknown field. Each rules-bearing extension is
        // either parsed by a canonical reader below or restricted to the existing
        // explicitly supported standard semantics by KarmaPromptField2895.
        if (!UsesPriorityCurrent3027(current) || profile.Name != "setting"
            || !CreationSyntax1949(profile, CharacterCurrentRuleDomain.Profile,
                nativeMagicProfile: true, supportedKarmaProfile: true)
            || !source.TryResolveCreationSourceProfile(out var method)
            || method.BuildMethod != ScalarCreation1949(ParseCreation1949(current.CharacterXml), "buildmethod")
            // V9 binds a saved Career selection through TryResolveCareerSettingsProfile
            // and complete effective-row equality below. Native V7 keeps its preset gate.
            || (UsesImportedPriorityCareer5667(current)
                ? !HasImportedSettingsIdentity5691(method)
                : !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method.BuildMethod, method.SettingsProfileId))
            || !MatchesSettingsRow5691(source, profile, method.SettingsProfileId)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(method.RawProfileInputsDigest)
            || !source.TryResolveCareerSettingsProfile(out var settings, out var settingsState)
            || string.IsNullOrWhiteSpace(settingsState)
            || !MatchesProfileSettings6254(settings, profile)) return false;

        // Validate settings before any character choice exists. The normal domain
        // consumers still evaluate actual ranks, selected grants, allocation,
        // purchases, magic and loadout; none is replaced by this policy observation.
        if (!source.TryResolveCreationPrioritySkillKarmaPolicy(out var skills) || skills is null
            || skills.Schema != CharacterCreationKarmaSkillsPolicy.PriorityPurchaseSchemaV1
            || skills.SettingsProfileId != method.SettingsProfileId
            || skills.RawProfileInputsDigest != method.RawProfileInputsDigest
            || skills.AuthorityDigest != CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(skills)
            || !TryCurrentMagicAuthority6242(current, source, out var magic)
            || magic.SettingsProfileId != method.SettingsProfileId
            || CharacterCreationNativeMagic1430.ReadCurrentPolicy(magic.MysticAdeptPowerPointPolicy,
                method.SettingsProfileId, method.BuildMethod, ImportedSettingsFile5691(current, source)) is null
            || magic.MysticAdeptPowerPointPolicy is null
            || CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(magic.MysticAdeptPowerPointPolicy.SettingsInputsDigest,
                method.SettingsProfileId) != method.RawProfileInputsDigest
            || !CharacterCreationNativeLoadout1432.TryReadPolicy(profile, out _)
            || !source.TryResolveCreationContactsPolicy(out var contacts)
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(contacts)
            || contacts!.SettingsProfileId != method.SettingsProfileId
            || contacts.RawProfileInputsDigest != method.RawProfileInputsDigest
            || !source.TryResolveCreationCarryoverPolicy(out var carryover)
            || !CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(carryover)
            || carryover!.SettingsProfileId != method.SettingsProfileId
            || carryover.RawProfileInputsDigest != method.RawProfileInputsDigest
            || !TryObserveCurrentPurchaseProfileSettings1950(current, source, profile, out _)) return false;

        // These lists clear on load. Missing lists in a full profile cannot inherit
        // constructor defaults. The grammar above checks their exact supported rows.
        if (profile.Element("bannedwaregrades") is null || profile.Element("redlinerexclusion") is null) return false;
        return CurrentProfileSelections3027(profile) && current.IsUnchanged;
    }

    private static bool CurrentProfileSelections3027(XElement profile)
    {
        if (profile.Element("books") is { } books
            && (books.Elements().Any(book => !ProfileText2895(book, 128))
                || books.Elements().Select(book => book.Value).Distinct(StringComparer.Ordinal).Count()
                    != books.Elements().Count())) return false;
        if (profile.Element("customdatadirectorynames") is { } directories)
        {
            var rows = directories.Elements().ToArray();
            if (rows.Any(row => row.Elements().Count() != 3
                    || row.Element("directoryname") is not { } name || !ProfileText2895(name, 1024)
                    || row.Element("order") is not { } order
                    || !int.TryParse(order.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
                || rows.Select(row => row.Element("directoryname")!.Value).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                return false;
        }
        return true;
    }

    private static bool UsesLifeModulesFullProfile9749(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV7
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV7
            && ScalarCreation1949(ParseCreation1949(current.CharacterXml), "buildmethod") == CharacterCreationBuildMethods.LifeModules
            && source.TryResolveCreationSourceProfile(out var method)
            && method.BuildMethod == CharacterCreationBuildMethods.LifeModules
            && CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method.BuildMethod, method.SettingsProfileId)
            && MatchesSettingsRow5691(source, profile, method.SettingsProfileId)
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(method.RawProfileInputsDigest);

    private static bool NativeMethodFullProfile9749(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile)
    {
        // Full Life Modules presets use the same bounded field grammar, with
        // their own policy readers. Only the persisted V7 capability opts in;
        // earlier retained profiles keep their original admission behavior.
        bool lifeModules = UsesLifeModulesFullProfile9749(current, source, profile);
        // This is a field grammar for opt-in V7 sources, not a preset checksum.
        // Keep the complete row and its source order in the input/capture digests.
        if ((!lifeModules && (!UsesKarmaQualityPrompts2742(current, source)
                || ScalarCreation1949(profile, "id") != NativeSr5CreationProfiles.KarmaQualityPrompts))
            || profile.Name != "setting"
            || !CreationSyntax1949(profile, CharacterCurrentRuleDomain.Profile, nativeMagicProfile: true,
                karmaQualityPrompts: !lifeModules, supportedKarmaProfile: true)
            || !source.TryResolveCareerSettingsProfile(out var settings, out var settingsState)
            || string.IsNullOrWhiteSpace(settingsState)
            || !MatchesProfileSettings6254(settings, profile)
            || !source.TryResolveCreationAttributePolicy(out var attributes) || attributes is null
            || attributes.SettingsProfileId != ScalarCreation1949(profile, "id")
            // These load-time lists clear before reading; absence is not their
            // constructor default and would silently remove standard restrictions.
            || profile.Element("bannedwaregrades") is null || profile.Element("redlinerexclusion") is null) return false;

        // The ordinary native Profile consumer still owns attribute/metatype/funding
        // execution. These source readers validate configured policies even before a
        // metatype is selected; no selected character or execution report is invented.
        CharacterCreationKarmaSkillsPolicy? skills;
        CharacterCreationKarmaMagicPolicy? magic;
        CharacterCreationKarmaContactsPolicy? contacts;
        CharacterCreationKarmaCarryoverPolicy? carryover;
        if (!TryObserveCurrentPurchaseProfileSettings1950(current, source, profile, out _)
            || !(lifeModules ? source.TryResolveCreationLifeModuleSkillsPolicy(out skills)
                : source.TryResolveCreationKarmaSkillsPolicy(out skills)) || skills is null
            || skills.Schema != (lifeModules ? CharacterCreationKarmaSkillsPolicy.LifeModulesSchemaV1
                : CharacterCreationKarmaSkillsPolicy.SchemaV1)
            || skills.SettingsProfileId != attributes.SettingsProfileId
            || skills.RawProfileInputsDigest != attributes.RawProfileInputsDigest
            || skills.AuthorityDigest != CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(skills)
            || skills.KnowledgePointsExpression != CharacterCreationStandardPrioritySkillsRules.KnowledgePointsExpression
            || !(lifeModules ? CharacterCreationKarmaMagicRules.TryCreateLifeModulePolicy(attributes.SettingsProfileId,
                attributes.RawProfileInputsDigest, profile.ToString(SaveOptions.DisableFormatting), out magic)
                : CharacterCreationKarmaMagicRules.TryCreatePolicy(attributes.SettingsProfileId,
                    attributes.RawProfileInputsDigest, profile.ToString(SaveOptions.DisableFormatting), out magic))
            || CharacterCreationNativeMagic1430.ReadCurrentPolicy(magic, current) is null
            || !CharacterCreationNativeLoadout1432.TryReadPolicy(profile, out _)
            || !(lifeModules ? source.TryResolveCreationContactsPolicy(out contacts)
                : source.TryResolveCreationKarmaContactsPolicy(out contacts))
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(contacts)
            || contacts!.SettingsProfileId != attributes.SettingsProfileId
            || contacts.RawProfileInputsDigest != attributes.RawProfileInputsDigest
            || !(lifeModules ? source.TryResolveCreationCarryoverPolicy(out carryover)
                : source.TryResolveCreationKarmaCarryoverPolicy(out carryover))
            || !CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(carryover)
            || carryover!.SettingsProfileId != attributes.SettingsProfileId
            || carryover.RawProfileInputsDigest != attributes.RawProfileInputsDigest) return false;

        // Books and directories remain ordinary ordered source selections. The
        // resolver enforces the native package identity plus actual selected packs.
        if (profile.Element("books") is { } books
            && (books.Elements().Any(book => !ProfileText2895(book, 128))
                || books.Elements().Select(book => book.Value).Distinct(StringComparer.Ordinal).Count()
                    != books.Elements().Count())) return false;
        if (profile.Element("customdatadirectorynames") is { } directories)
        {
            var rows = directories.Elements().ToArray();
            if (rows.Any(row => row.Elements().Count() != 3
                    || row.Element("directoryname") is not { } name || !ProfileText2895(name, 1024)
                    || row.Element("order") is not { } order
                    || !int.TryParse(order.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
                || rows.Select(row => row.Element("directoryname")!.Value).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                return false;
        }
        return current.IsUnchanged;
    }

    private static bool ProfileText2895(XElement node, int maximum)
        => !node.HasAttributes && node.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
            && !string.IsNullOrWhiteSpace(text.Value) && text.Value.Length <= maximum && !text.Value.Any(char.IsControl);

    private static bool KarmaPromptField2895(XElement node, out bool valid)
    {
        valid = false;
        string? parent = node.Parent?.Name.LocalName;
        string name = node.Name.LocalName;
        if (parent == "setting")
        {
            if (name is "name" or "gameplayoptionname")
            {
                // Labels are source data, not permission to satisfy a gameplay
                // prerequisite. Unsupported source predicates remain unavailable.
                valid = ProfileText2895(node, 256);
                return true;
            }
            if (CharacterCreationNativeMagicDefinitions1430.All.Any(field => field.Element == name)
                || CharacterCreationNativeLoadoutDefinitions1432.All.Any(field => field.Element == name))
            {
                // Value/relationship validation uses the canonical policy readers
                // above; Talents/Loadout still evaluate their actual current graph.
                valid = !node.HasElements && (name is not ("limbcount" or "dronearmorflatnumber")
                    || int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count >= 0);
                return true;
            }
            if (name is "bannedwaregrades" or "redlinerexclusion")
            {
                // No new ware/anatomy policy is implemented here. Retain exactly
                // the standard restrictions, independently of unrelated settings.
                string member = name == "bannedwaregrades" ? "grade" : "limb";
                string[] expected = name == "bannedwaregrades"
                    ? ["Betaware", "Deltaware", "Gammaware"] : ["skull", "torso"];
                var rows = node.Elements().ToArray();
                valid = rows.Length == expected.Length
                    && rows.All(row => row.Name == member && ProfileText2895(row, 256))
                    && rows.Select(row => row.Value).Distinct(StringComparer.Ordinal).Count() == rows.Length
                    && expected.All(value => rows.Any(row => row.Value == value));
                return true;
            }
        }
        if (parent == "karmacost" && name is "karmaimprovecomplexform" or "karmacomplexformoption" or "karmacomplexformskillsoft")
        {
            // RetainedSr5Compatibility1241 proves that both SR5 loaders omit these
            // retired SR4 costs. Preserve bounded integer metadata, not pricing.
            valid = !node.HasElements && node.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                && int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            return true;
        }
        if (parent == "karmacost" && name is "karmacontact" or "karmacarryover")
        {
            // Exact source-owned Contacts and finalization carryover policies.
            valid = !node.HasElements && int.TryParse(node.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int cost) && cost >= 0;
            return true;
        }

        // Explicit standard-only compatibility, not a field-name allowlist.
        // CharacterSettings supplies the ordinary constructor/load defaults;
        // explicitly disabled weight controls also match the supported sheet policy.
        // Their nonstandard mechanics are not admitted by this current Profile
        // extension. Existing canonical consumers still guard selected targets.
        string? standard = (parent, name) switch
        {
            ("setting", "licenserestricted" or "morelethalgameplay" or "cyberlegmovement"
                or "nosinglearmorencumbrance" or "uncappedarmoraccessorybonuses" or "esslossreducesmaximumonly"
                or "allowcyberwareessdiscounts" or "maximumarmormodifications" or "armordegredation"
                or "specialkarmacostbasedonshownvalue" or "mysaddppcareer" or "donotroundessenceinternally"
                or "enableenemytracking" or "allowhigherstackedfoci" or "alloweditpartofbaseweapon"
                or "extendanydetectionspell" or "dontusecyberlimbcalculation" or "allowbiowaresuites"
                or "freemartialartspecialization" or "usecalculatedpublicawareness" or "allowfreegrids"
                or "allowtechnomancerschooling" or "unclampattributeminimum") => "False",
            // Retain the exported controls without admitting enabled weight mechanics.
            ("setting", "doencumbrancepenaltyphysicallimit" or "doencumbrancepenaltymovementspeed"
                or "doencumbrancepenaltyagility" or "doencumbrancepenaltyreaction"
                or "doencumbrancepenaltywoundmodifier") => "False",
            ("setting", "enemykarmaqualitylimit" or "restrictrecoil" or "allowskilldicerolling" or "autobackstory") => "True",
            ("setting", "nuyenperbpwftp") => "2000",
            ("setting", "cyberlimbattributebonuscap" or "minhotsiminitiativedice") => "4",
            ("setting", "minastralinitiativedice" or "mincoldsiminitiativedice") => "3",
            ("setting", "maxinitiativedice" or "maxastralinitiativedice" or "maxcoldsiminitiativedice" or "maxhotsiminitiativedice") => "5",
            ("setting", "excludelimbslot") => "",
            ("setting", "essenceformat") => "#,0.00",
            ("setting", "contactpointsexpression") => "{CHAUnaug} * 3",
            ("karmacost", "karmaenhancement" or "karmabanishingfocus" or "karmabindingfocus"
                or "karmacounterspellingfocus" or "karmaqifocus" or "karmaritualspellcastingfocus"
                or "karmaspellcastingfocus" or "karmasummoningfocus" or "karmasustainingfocus") => "2",
            ("karmacost", "karmaenemy" or "karmaspirit" or "karmaleavegroup") => "1",
            ("karmacost", "karmanewaiprogram" or "karmamaneuver" or "karmajoingroup") => "5",
            ("karmacost", "karmanewaiadvancedprogram") => "8",
            ("karmacost", "karmainitiation" or "karmaalchemicalfocus" or "karmacenteringfocus" or "karmadisenchantingfocus"
                or "karmaflexiblesignaturefocus" or "karmamaskingfocus" or "karmaspellshapingfocus" or "karmaweaponfocus"
                or "karmaspiritfettering") => "3",
            ("karmacost", "karmainitiationflat") => "10",
            ("karmacost", "karmametamagic") => "15",
            ("karmacost", "karmapowerfocus") => "6",
            _ => null
        };
        if (standard is null) return false;
        if (node.HasElements || node.Nodes().Any(child => child.GetType() != typeof(XText))) return true;
        valid = standard is "True" or "False"
            ? bool.TryParse(node.Value, out bool boolean) && boolean == (standard == "True")
            : int.TryParse(standard, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                ? int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed == number
                : node.Value == standard;
        return true;
    }

    private static bool CreationSyntax1949(XElement node, CharacterCurrentRuleDomain domain,
        bool nativeMagicProfile = false, bool lifestyleQualities = false, bool lifestyleAvailability = false, bool stockLifestyles = false,
        bool karmaQualityPrompts = false, bool supportedKarmaProfile = false, bool nativeSheetProfile = false,
        bool nativeReputationProfile = false, bool careerCondition5497 = false)
    {
        if (careerCondition5497 && domain == CharacterCurrentRuleDomain.Profile
            && node.Name == Chummer.Application.Session.SessionPlayVehiclePolicySource5400.OverrideElement)
            return node.Parent?.Name == "setting"
                && Chummer.Application.Session.SessionPlayVehiclePolicySource5400.IsRecognizedOverride5497(node);
        if (karmaQualityPrompts && domain == CharacterCurrentRuleDomain.Profile
            && node.Name == NativeSr5CreationProfiles.KarmaQualityPromptElement)
            return node.Parent?.Name == "setting" && !node.HasAttributes
                && node.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                && text.Value == CharacterCreationKarmaQualityPrompts.SourceSchema;
        if (domain == CharacterCurrentRuleDomain.Lifestyles)
        {
            if (lifestyleQualities && node.Name == "qualities")
                return node.Parent?.Name == "chummer" && !node.HasAttributes && CreationXmlSafe1950(node)
                    && !node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                    && node.Elements().All(row => stockLifestyles ? LifestyleQualitySyntax2678(row) : lifestyleAvailability ? LifestyleQualitySyntax2670(row) : LifestyleQualitySyntax2661(row));
            if (stockLifestyles && node.Name == "hide")
                return node.Parent?.Name == "lifestyle" && !node.HasAttributes && !node.HasElements && string.IsNullOrWhiteSpace(node.Value);
            if (stockLifestyles && node.Name.LocalName is "categories" or "cities")
                return TryStockLifestyleCatalogues2678(new XElement("chummer", new XElement(node)), out _, out _);
            string? aspectMember = node.Name == "comforts" ? "comfort"
                : node.Name == "neighborhoods" ? "neighborhood" : node.Name == "securities" ? "security" : null;
            if (aspectMember is not null) return CreationLifestyleAspects2001(node, aspectMember);
        }
        if (domain == CharacterCurrentRuleDomain.Lifestyles && node.Name == "freegrids")
        {
            // Retain typed stock metadata when the feature is off. The lifestyle
            // authority guard still refuses enabled free-grid mechanics.
            var grids = node.Elements().ToArray();
            return node.Parent?.Name == "lifestyle" && !node.HasAttributes && CreationXmlSafe1950(node)
                && !node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                && grids.All(grid => grid.Name == "freegrid" && !grid.HasElements
                    && !string.IsNullOrWhiteSpace(grid.Value)
                    && grid.Attributes().All(attribute => attribute.Name == "select"))
                && grids.Select(grid => (grid.Value.Trim(), grid.Attribute("select")?.Value.Trim() ?? string.Empty))
                    .Distinct().Count() == grids.Length;
        }
        if (node.Name.Namespace != XNamespace.None || node.HasAttributes
            || node.Nodes().Any(child => child is not XElement && child is not XComment
                && (child is not XText text || text.GetType() != typeof(XText)))
            || node.HasElements && node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        if (supportedKarmaProfile && domain == CharacterCurrentRuleDomain.Profile
            && KarmaPromptField2895(node, out bool supportedField)) return supportedField;
        string name = node.Name.LocalName;
        // Canonical CharacterSettings XML and KarmaMagicRules use the prefixed
        // cost names. Admit this complete, typed group only for native V3 methods;
        // the native magic consumer still resolves its retained policy authority.
        if (nativeMagicProfile && domain == CharacterCurrentRuleDomain.Profile)
        {
            if (node.Parent is { } nativeParent
                && IsCurrentNativeMagicProfileSetting1986(nativeParent.Name.LocalName, name))
                return !node.HasElements && (name == "ignorecomplexformlimit"
                    ? bool.TryParse(node.Value.Trim(), out _)
                    : int.TryParse(node.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cost)
                        && cost >= 0);
            if (name == "setting" && node.Descendants().Any(field => IsCurrentNativeMagicProfileSetting1986(
                    field.Parent!.Name.LocalName, field.Name.LocalName))
                && (node.Elements("ignorecomplexformlimit").Count() != 1
                    || node.Elements("karmacost").ToArray() is not [var costs]
                    || costs.Elements("karmaspell").Count() != 1
                    || costs.Elements("karmanewcomplexform").Count() != 1)) return false;
        }
        // The selected native profile carries this Career policy in Creation too.
        // Its canonical source reader, not a saved reputation total, owns the value.
        if (nativeReputationProfile && domain == CharacterCurrentRuleDomain.Profile
            && node.Parent?.Name == "setting" && name == "usecalculatedpublicawareness")
            return !node.HasElements && node.Value == node.Value.Trim() && bool.TryParse(node.Value, out _);
        // These Career controls have a typed source-bound sheet consumer in
        // ConsumeNativeProfile1958. Syntax alone never issues an execution.
        if (nativeSheetProfile && domain == CharacterCurrentRuleDomain.Profile
            && node.Parent?.Name == "setting" && IsNativeSheetProfileSetting5130(name))
            return !node.HasElements && int.TryParse(node.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int setting) && setting >= 0;
        // Parent-scoped policy extensions must survive recursive leaf validation.
        // Their canonical consumers still own values; XML shape and duplicates stay
        // subject to the same catalog guards as every other selected-profile field.
        if (domain == CharacterCurrentRuleDomain.Profile && node.Parent is { } parent
            && IsCurrentProfilePolicySetting1969(parent.Name.LocalName, name)) return !node.HasElements;
        string[] children = domain switch
        {
            CharacterCurrentRuleDomain.Profile => name switch
            {
                "chummer" => ["settings"], "settings" => ["setting"],
                "setting" => ["id", "name", "buildmethod", "buildpoints", "priorityarray", "prioritytable", "sumtoten",
                    "books", "customdatadirectorynames", "maxnumbermaxattributescreate", "allow2ndmaxattribute",
                    "alternatemetatypeattributekarma", "reverseattributepriorityorder", "karmacost",
                    "metatypecostskarma", "metatypecostskarmamultiplier", "mininitiativedice", "dronemods",
                    "chargenkarmatonuyenexpression", "nuyenperbpwftm", "nuyenmaxbp", "nuyencarryover", "availability", "unrestrictednuyen",
                    .. (karmaQualityPrompts ? new[] { NativeSr5CreationProfiles.KarmaQualityPromptElement } : Array.Empty<string>())],
                "books" => ["book"], "karmacost" => ["karmaattribute"],
                "customdatadirectorynames" => ["customdatadirectoryname"],
                "customdatadirectoryname" => ["directoryname", "order", "enabled"], _ => []
            },
            CharacterCurrentRuleDomain.Metatypes => name switch
            {
                "chummer" => ["categories", "metatypes"], "categories" => ["category"], "metatypes" => ["metatype"],
                "metatype" => ["id", "name", "karma", "category", "source", "page", "inimin", "inimax", "iniaug",
                    "initiativedice", "walk", "run", "sprint", "bonus", "qualities", "metavariants",
                    .. new[] { "bod", "agi", "rea", "str", "cha", "int", "log", "wil", "edg", "mag", "res", "ess", "dep" }
                        .SelectMany(prefix => new[] { prefix + "min", prefix + "max", prefix + "aug" })], _ => []
            },
            CharacterCurrentRuleDomain.Priorities => name switch
            {
                "chummer" => ["categories", "prioritytables", "priortysumtotenvalues", "priorities"],
                "categories" => ["category"], "prioritytables" => ["prioritytable"], "priortysumtotenvalues" => ["A", "B", "C", "D", "E"],
                "priorities" => ["priority"], "priority" => ["id", "name", "value", "category", "prioritytable",
                    "attributes", "skills", "skillgroups", "resources", "metatypes", "talents"],
                "metatypes" => ["metatype"], "metatype" => ["name", "value", "karma"],
                "talents" => ["talent"], "talent" => ["name", "value"], _ => []
            },
            _ => name switch
            {
                "chummer" => ["lifestyles", "qualities", "comforts", "neighborhoods", "securities", "cities", "categories"],
                "lifestyles" => ["lifestyle"], "lifestyle" => ["id", "name", "cost", "dice", "multiplier", "source", "page",
                    "lp", "costforarea", "costforcomforts", "costforsecurity", "allowbonuslp", "increment", "freegrids", .. (stockLifestyles ? new[] { "hide" } : Array.Empty<string>())], _ => []
            }
        };
        if (children.Length == 0)
        {
            string[] empty = ["bonus", "qualities", "metavariants", "comforts", "neighborhoods", "securities", "cities", "categories"];
            if (empty.Contains(name, StringComparer.Ordinal)) return !node.HasElements && string.IsNullOrWhiteSpace(node.Value);
            string[] leaves = ["id", "name", "buildmethod", "buildpoints", "priorityarray", "prioritytable", "sumtoten",
                "maxnumbermaxattributescreate", "allow2ndmaxattribute", "alternatemetatypeattributekarma", "reverseattributepriorityorder",
                "karmaattribute", "book", "directoryname", "order", "enabled", "category", "karma", "source", "page",
                "inimin", "inimax", "iniaug", "initiativedice", "walk", "run", "sprint", "A", "B", "C", "D", "E",
                "value", "attributes", "skills", "skillgroups", "resources", "cost", "dice", "multiplier", "lp",
                "costforarea", "costforcomforts", "costforsecurity", "allowbonuslp", "increment",
                "chargenkarmatonuyenexpression", "nuyenperbpwftm", "nuyenmaxbp", "nuyencarryover", "availability", "unrestrictednuyen",
                .. new[] { "bod", "agi", "rea", "str", "cha", "int", "log", "wil", "edg", "mag", "res", "ess", "dep" }
                    .SelectMany(prefix => new[] { prefix + "min", prefix + "max", prefix + "aug" })];
            if (!leaves.Contains(name, StringComparer.Ordinal) || node.HasElements) return false;
            if (name == "increment" && node.Value is not ("Day" or "Week" or "Month" or "day" or "week" or "month")) return false;
            if (name is "allowbonuslp" or "allow2ndmaxattribute" or "alternatemetatypeattributekarma" or "reverseattributepriorityorder"
                or "enabled") return bool.TryParse(node.Value, out _);
            return true;
        }
        if (node.Elements().Any(child => !children.Contains(child.Name.LocalName, StringComparer.Ordinal)
            && !(domain == CharacterCurrentRuleDomain.Profile
                && (IsCurrentProfilePolicySetting1969(name, child.Name.LocalName)
                    || nativeMagicProfile && IsCurrentNativeMagicProfileSetting1986(name, child.Name.LocalName)
                    || nativeSheetProfile && name == "setting" && IsNativeSheetProfileSetting5130(child.Name.LocalName)
                    || nativeReputationProfile && name == "setting" && child.Name.LocalName == "usecalculatedpublicawareness"
                        || careerCondition5497 && name == "setting" && child.Name == Chummer.Application.Session.SessionPlayVehiclePolicySource5400.OverrideElement
                    || supportedKarmaProfile && KarmaPromptField2895(child, out _))))) return false;
        string[] repeated = ["settings", "metatypes", "priorities", "lifestyles", "categories", "books", "prioritytables",
            "customdatadirectorynames", "talents"];
        if (!repeated.Contains(name, StringComparer.Ordinal) && node.Elements().GroupBy(child => child.Name).Any(group => group.Count() != 1)) return false;
        return node.Elements().All(child => CreationSyntax1949(child, domain, nativeMagicProfile, lifestyleQualities, lifestyleAvailability, stockLifestyles, karmaQualityPrompts, supportedKarmaProfile, nativeSheetProfile, nativeReputationProfile, careerCondition5497));
    }

    private static bool CreationSavedLifestyleShape1949(XElement row)
    {
        string[] fields = ["guid", "sourceid", "name", "baselifestyle", "type", "increment", "months", "percentage",
            "roommates", "splitcostwithroommates", "trustfund", "area", "comforts", "security", "bonuslp", "city",
            "district", "borough", "cost", "totalcost", "totalmonthlycost", "dice", "multiplier", "lp", "source", "page",
            "notes", "notesColor", "sortorder", "print", "lifestylequalities", "allowbonuslp", "costforarea",
            "costforcomforts", "costforsecurity", "basearea", "basecomforts", "basesecurity", "maxarea", "maxcomforts", "maxsecurity"];
        return !row.HasAttributes && row.Elements().GroupBy(child => child.Name).All(group => group.Count() == 1)
            && row.Elements().All(child => fields.Contains(child.Name.ToString(), StringComparer.Ordinal)
                && !child.HasAttributes && !child.HasElements);
    }
}

/// <summary>One bounded compiler shared by the native authority and retained-source verifier.</summary>
public static class CharacterCreationLifestyleSourceMechanics2684
{
    public static bool TryCompile(XElement row, ICurrentLifestyleMechanicsSource2684 source,
        out CharacterCreationLifestyleQualityMechanics? mechanics,
        out CharacterCreationLifestyleQualitySelectionSemantics? selection)
    {
        mechanics = null;
        selection = null;
        if (!source.HasLifestyleMechanics2684) return false;
        // Unconsumed minima remain unsupported. An explicitly selected amendment
        // must use established maximum fields; never reinterpret retained stock bytes.
        if (row.Elements().Any(field => field.Name.LocalName is "areaminimum" or "comfortsminimum" or "securityminimum")) return false;
        if (!ReadPredicates(row, "required", out var required) || !ReadPredicates(row, "forbidden", out var forbidden)) return false;
        var bonuses = row.Elements("bonus").ToArray();
        if (bonuses.Length > 1 || bonuses.Any(field => !Container(field))) return false;
        var prompts = bonuses.SelectMany(field => field.Elements()).ToArray();
        CharacterCreationLifestyleTextPrompt? prompt = null;
        if (prompts.Length != 0)
        {
            if (prompts is not [var field] || field.Name != "selecttext" || field.HasElements
                || !string.IsNullOrWhiteSpace(field.Value)) return false;
            if (field.HasAttributes)
            {
                if (field.Attributes().Any(attribute => attribute.Name.LocalName is not ("xml" or "xpath" or "allowedit")
                        || attribute.Name.NamespaceName.Length != 0)
                    || field.Attribute("xml")?.Value != "skills.xml"
                    || field.Attribute("xpath") is not { } xpath
                    || !bool.TryParse(field.Attribute("allowedit")?.Value ?? "false", out bool allowCustom)) return false;
                var paths = xpath.Value.Split('|').Select(value => value.Trim()).ToArray();
                if (paths.Length is < 1 or > 2 || paths.Distinct(StringComparer.Ordinal).Count() != paths.Length
                    || paths.Any(path => path is not ("/chummer/skills/skill/name" or "/chummer/knowledgeskills/skill/name"))
                    || !source.TryReadLifestylePromptSource2684("skills.xml", out var xml, out var digest)) return false;
                XElement document;
                try { document = XElement.Parse(xml); }
                catch (System.Xml.XmlException) { return false; }
                if (document.Name != "chummer") return false;
                var choices = new List<CharacterCreationLifestyleTextChoice>();
                foreach (var path in paths)
                    foreach (var skill in document.Elements(path.Split('/')[2]).Elements("skill"))
                    {
                        var names = skill.Elements("name").ToArray();
                        if (names is not [var name] || name.HasElements || name.HasAttributes || string.IsNullOrWhiteSpace(name.Value)) return false;
                        // A catalogue selection is bound to this exact source document, path and value.
                        var key = CharacterCreationFinalizationDigest.Compute(new { File = "skills.xml", Digest = digest, Path = path, Text = name.Value });
                        choices.Add(new(key, name.Value));
                    }
                if (choices.Count is < 1 or > 4096) return false;
                prompt = new("skills.xml", xpath.Value, digest, allowCustom,
                    choices.DistinctBy(choice => choice.ChoiceId).OrderBy(choice => choice.ChoiceId, StringComparer.Ordinal).ToArray());
            }
        }
        var multiple = row.Elements("allowmultiple").ToArray();
        if (multiple.Length > 1 || multiple.Any(field => field.HasElements || field.HasAttributes || !string.IsNullOrWhiteSpace(field.Value))) return false;
        selection = new(prompts.Length != 0, multiple.Length != 0);
        mechanics = new(required, forbidden, prompt);
        return true;
    }

    private static bool Container(XElement field) => !field.HasAttributes
        && !field.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
        && !field.Nodes().OfType<XProcessingInstruction>().Any();
    private static bool ReadPredicates(XElement row, string name, out CharacterCreationLifestylePredicate[] predicates)
    {
        predicates = [];
        var fields = row.Elements(name).ToArray();
        if (fields.Length == 0) return true;
        if (fields is not [var container] || !Container(container)) return false;
        var result = new List<CharacterCreationLifestylePredicate>();
        int budget = 256;
        foreach (var field in container.Elements())
        {
            if (name == "forbidden" && field.Name != "oneof") return false;
            if (!ReadPredicate(field, 0, ref budget, out var predicate)) return false;
            result.Add(predicate);
        }
        if (result.Count == 0) return false;
        predicates = result.ToArray();
        return true;
    }
    private static bool ReadPredicate(XElement field, int depth, ref int budget, out CharacterCreationLifestylePredicate predicate)
    {
        predicate = null!;
        if (--budget < 0 || depth > 1 || field.Name.NamespaceName.Length != 0) return false;
        string kind = field.Name.LocalName;
        if (kind is "oneof" or "allof")
        {
            if (depth != 0 || !Container(field) || !field.HasElements) return false;
            var children = new List<CharacterCreationLifestylePredicate>();
            foreach (var child in field.Elements())
            {
                if (!ReadPredicate(child, depth + 1, ref budget, out var nested)) return false;
                children.Add(nested);
            }
            predicate = new(kind, string.Empty, null, children);
            return true;
        }
        if (kind == CharacterCreationLifestyleMechanicsRules.StartingBasePointsPredicate)
        {
            if (field.HasAttributes || field.HasElements
                || !CharacterCreationLifestyleMechanicsRules.TryReadStartingBasePoints(field.Value, out _)) return false;
            predicate = new(kind, field.Value, null, []);
            return true;
        }
        if (kind is not ("lifestyle" or "lifestylequality" or "characterquality") || field.HasElements
            || string.IsNullOrWhiteSpace(field.Value) || field.Value.Length > 32767
            || field.Attributes().Any(attribute => attribute.Name != "extra" || kind == "lifestyle")) return false;
        predicate = new(kind, field.Value, field.Attribute("extra")?.Value, []);
        return true;
    }
}
