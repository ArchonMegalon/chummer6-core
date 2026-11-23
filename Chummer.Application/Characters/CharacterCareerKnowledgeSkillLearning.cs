using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>A retained first-purchase intent, distinct from advancing a saved zero-rated row.</summary>
public sealed record CharacterCareerKnowledgeSkillLearningIntent(Guid SkillId, Guid SourceSkillId)
{
    // Null/null preserves the original catalog-picker contract. Typed selections
    // retain both values even when their exact name resolves to a real source row.
    public string? Name { get; init; }
    public string? Category { get; init; }
    public bool MatchesQuote(CharacterCareerKnowledgeSkillAdvanceQuote quote)
        => SkillId != Guid.Empty && quote.Identity.SkillId == SkillId
            && quote.Identity.SourceSkillId == (SourceSkillId == Guid.Empty ? (Guid?)null : SourceSkillId)
            && (Name is null && Category is null ? SourceSkillId != Guid.Empty
                : !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Category)
                    && Name == quote.Name && Category == quote.SkillType && Category == quote.SkillCategory);
}

public sealed record CharacterCareerKnowledgeSkillLearningChoice(
    CharacterCareerKnowledgeSkillLearningIntent Intent,
    CharacterCareerKnowledgeSkillAdvanceQuote Quote);

/// <summary>
/// Source-backed and named ordinary Career Knowledge/language learning. Resolves its own
/// enabled catalog, saved graph and settings; callers cannot supply admission flags
/// or a price. Does not persist, spend Creation points or grant native languages.
/// </summary>
public static class CharacterCareerKnowledgeSkillLearning
{
    private const string ReceiptContainer = "careerknowledgeskilladvancementreceipts";

    public static IReadOnlyList<CharacterCareerKnowledgeSkillLearningChoice> GetChoices(
        string xml, ICharacterSourceDataResolver? resolver)
    {
        try
        {
            if (!TryContext(xml, resolver, out var context, out var root, out var catalog)) return [];
            string stateDigest = Digest(CanonicalCharacterState(root));
            var choices = new List<CharacterCareerKnowledgeSkillLearningChoice>();
            foreach (var entry in catalog.KnowledgeSkills)
            {
                if (!Guid.TryParseExact(entry.SourceSkillId, "D", out Guid sourceId)) continue;
                // Stable for one saved graph, including a cold restart before mutation.
                // The retained intent, not a later catalog projection, owns the purchase ID.
                Guid id = CharacterCreationFinalizationProjector.StableGuid(
                    $"career-knowledge-learning/v1:{stateDigest}:{sourceId:D}");
                var intent = new CharacterCareerKnowledgeSkillLearningIntent(id, sourceId);
                if (TryQuote(context, root, catalog, intent, out var quote, out _))
                    choices.Add(new(intent, quote));
            }
            return choices.OrderBy(x => x.Quote.SkillType, StringComparer.Ordinal)
                .ThenBy(x => x.Quote.Name, StringComparer.Ordinal).ThenBy(x => x.Intent.SourceSkillId).ToArray();
        }
        catch (Exception error) when (IsInvalid(error)) { return []; }
    }

    /// <summary>Supported categories from the profile's canonical ordered Knowledge map.</summary>
    public static IReadOnlyList<string> GetCustomCategories(string xml, ICharacterSourceDataResolver? resolver)
    {
        try
        {
            return TryContext(xml, resolver, out var context, out _, out var catalog)
                && TryResolveCategories(context, catalog, out var categories, out _)
                ? categories.Keys.Order(StringComparer.Ordinal).ToArray() : [];
        }
        catch (Exception error) when (IsInvalid(error)) { return []; }
    }

    /// <summary>Resolves an exact entered name/category using the actual saved profile.</summary>
    public static bool TryCreateNamedChoice(string xml, ICharacterSourceDataResolver? resolver,
        string name, string category, out CharacterCareerKnowledgeSkillLearningChoice choice)
    {
        choice = null!;
        try
        {
            if (!IsValidName(name) || !TryContext(xml, resolver, out var context, out var root, out var catalog)
                || !TryResolveNamedSelection(context, catalog, name, category, out Guid sourceId, out _)) return false;
            // Length-prefixed strings prevent delimiter collisions. Stable only for this saved graph/selection.
            Guid skillId = CharacterCreationFinalizationProjector.StableGuid(
                $"career-knowledge-named/v1:{Digest(CanonicalCharacterState(root))}:{sourceId:D}:{name.Length}:{name}:{category.Length}:{category}");
            var intent = new CharacterCareerKnowledgeSkillLearningIntent(skillId, sourceId) { Name = name, Category = category };
            if (!TryQuote(context, root, catalog, intent, out var quote, out _)) return false;
            choice = new(intent, quote);
            return true;
        }
        catch (Exception error) when (IsInvalid(error)) { return false; }
    }

    public static bool TryQuote(string xml, ICharacterSourceDataResolver? resolver,
        CharacterCareerKnowledgeSkillLearningIntent intent,
        out CharacterCareerKnowledgeSkillAdvanceQuote quote)
    {
        quote = null!;
        try
        {
            return TryContext(xml, resolver, out var context, out var root, out var catalog)
                && TryQuote(context, root, catalog, intent, out quote, out _);
        }
        catch (Exception error) when (IsInvalid(error)) { return false; }
    }

    /// <summary>Re-resolves and re-quotes immediately before returning one detached canonical row.</summary>
    public static bool TryProject(string xml, ICharacterSourceDataResolver? resolver,
        CharacterCareerKnowledgeSkillLearningIntent intent,
        CharacterCareerKnowledgeSkillAdvanceQuote expected,
        CharacterCareerKnowledgeSkillAdvancePlan plan, out XElement? row)
    {
        row = null;
        if (intent is null || expected is null || plan is null) return false;
        try
        {
            if (!TryContext(xml, resolver, out var context, out var root, out var catalog)
                || !TryQuote(context, root, catalog, intent, out var current, out var entry)
                || !CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(expected)
                || current.Identity != expected.Identity || current.LogicalRevision != expected.LogicalRevision
                || !CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(current,
                    expected.CharacterRevision, expected.LogicalRevision, expected.SourceRevision, expected.RuleDigest,
                    true, plan.ExpenseId, plan.ExpenseDateLocal, out var actual)
                || actual != plan || plan.KarmaUndoType != "AddSkill" || plan.SavedSkillKarmaPoints != 1)
                return false;
            row = entry is null
                ? CharacterCreationPurchasedSkillsLegacyProjector.BuildCustomKnowledgeSkill(
                    intent.SkillId, current.Name, current.SkillType, plan.SavedSkillKarmaPoints)
                : CharacterCreationPurchasedSkillsLegacyProjector.BuildSkill(entry,
                    new(entry.SourceSkillId, CharacterCreationSkillKinds.Knowledge, plan.SavedSkillKarmaPoints),
                    intent.SkillId, Guid.Empty);
            _ = row.ToString(SaveOptions.DisableFormatting);
            return true;
        }
        catch (Exception error) when (IsInvalid(error)) { row = null; return false; }
    }

    private static bool TryContext(string xml, ICharacterSourceDataResolver? resolver,
        out ICharacterSourceDataContext context, out XElement root, out CharacterCreationSkillsCatalog catalog)
    {
        context = null!; root = null!; catalog = null!;
        if (string.IsNullOrWhiteSpace(xml) || resolver?.TryCreateContext(xml) is not { } resolved) return false;
        root = XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!;
        if (root is null || root.Name != "character" || !ReadRequiredBool(root, "created")
            || !string.Equals(ReadText(root, "gameedition"), "SR5", StringComparison.OrdinalIgnoreCase)
            || !resolved.TryResolveCreationSkillsCatalog(out var sources) || sources is null
            || !CharacterCreationSkillsCatalogAuthority.IsValid(sources)
            || ReadText(root, "settings") != sources.SettingsProfileId) return false;
        context = resolved; catalog = sources;
        return true;
    }

    private static bool TryQuote(ICharacterSourceDataContext context, XElement root,
        CharacterCreationSkillsCatalog catalog, CharacterCareerKnowledgeSkillLearningIntent intent,
        out CharacterCareerKnowledgeSkillAdvanceQuote quote, out CharacterCreationSkillCatalogEntry entry)
    {
        quote = null!; entry = null!;
        if (intent is null || intent.SkillId == Guid.Empty) return false;
        string name, category, rawSource;
        string namedAuthority = string.Empty;
        if ((intent.SourceSkillId == Guid.Empty || intent.Name is not null || intent.Category is not null)
            && (!IsValidName(intent.Name) || intent.Category is null
                || !TryResolveNamedSelection(context, catalog, intent.Name!, intent.Category, out var namedSource, out namedAuthority)
                || namedSource != intent.SourceSkillId)) return false;
        if (intent.SourceSkillId == Guid.Empty)
        {
            name = intent.Name!; category = intent.Category!;
            rawSource = CharacterCreationPurchasedSkillsLegacyProjector.BuildCustomKnowledgeSkill(
                intent.SkillId, name, category, 0).ToString(SaveOptions.DisableFormatting);
        }
        else
        {
            var entries = catalog.KnowledgeSkills.Where(x => x.SourceSkillId == intent.SourceSkillId.ToString("D"))
                .Take(2).ToArray();
            if (entries.Length != 1 || !TryResolveEntry(context, entries[0], out var source)) return false;
            entry = entries[0]; name = source.Name; category = source.SkillCategory; rawSource = source.RawSourceXml;
            if ((intent.Name is not null || intent.Category is not null)
                && (intent.Name != name || intent.Category != category)) return false;
        }

        var graph = Single(root, "newskills");
        var saved = Single(graph, "knoskills").Elements("skill").ToArray();
        var ids = new HashSet<Guid>();
        foreach (var skill in graph.Elements().SelectMany(x => x.Elements("skill")))
        {
            if (!Guid.TryParse(ReadText(skill, "guid"), out Guid id) || id == Guid.Empty
                || !ids.Add(id) || id == intent.SkillId) return false;
        }
        foreach (var skill in saved)
        {
            if (!ReadRequiredBool(skill, "isknowledge")
                || !Guid.TryParse(ReadText(skill, "suid"), out Guid savedSource)) return false;
            // Same canonical (Kind, SourceId) policy as CreationSkillsService's
            // allocation map. Do not invent name/case/category uniqueness for custom rows.
            if (intent.SourceSkillId != Guid.Empty && savedSource == intent.SourceSkillId) return false;
        }
        if (!context.TryResolveCareerSettingsProfile(out string settingsXml, out string settingsAuthority)
            || string.IsNullOrWhiteSpace(settingsAuthority)) return false;
        XElement settings = XElement.Parse(settingsXml, LoadOptions.PreserveWhitespace);
        if (settings.Name != "settings" || ReadText(settings, "id") != ReadText(root, "settings")) return false;
        var costs = Single(settings, "karmacost");
        var rules = new CharacterCareerKnowledgeSkillAdvanceSettings(
            ReadInt(costs, "karmanewknowledgeskill"), ReadInt(costs, "karmaimproveknowledgeskill"));
        var improvementContainers = root.Elements("improvements").Take(2).ToArray();
        if (improvementContainers.Length > 1) return false;
        XElement? improvements = improvementContainers.SingleOrDefault();
        if (HasUnsupportedRatingAuthority(improvements, name)
            || !TryResolveCostModifiers(improvements, name, category, out var modifiers)) return false;
        var input = new CharacterCareerKnowledgeSkillAdvanceInput(
            new(intent.SkillId, intent.SourceSkillId == Guid.Empty ? null : intent.SourceSkillId), true, CharacterCareerKnowledgeSkillAdvanceRules.RulesetId,
            true, true, false, name, category, category, name,
            0, 0, 0, ReadOptionalNonNegativeInt(settings, "maxknowledgeskillrating", 12), ReadInt(root, "karma"),
            rules, modifiers, CanonicalCharacterState(root), rawSource,
            settings.ToString(SaveOptions.DisableFormatting) + "\n" + settingsAuthority + "\n"
                + (improvements?.ToString(SaveOptions.DisableFormatting) ?? "<improvements />")
                + (intent.Name is null ? "" : "\n" + namedAuthority));
        return CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(input, out quote);
    }

    private static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && name == name.Trim()
            && name.Length <= CharacterCareerKnowledgeSkillAdvanceRules.MaximumNameLength
            && !name.Any(char.IsControl) && XmlConvert.VerifyXmlChars(name) == name;

    private static bool TryResolveCategories(ICharacterSourceDataContext context,
        CharacterCreationSkillsCatalog catalog, out IReadOnlyDictionary<string, string> categories, out string authority)
    {
        if (!context.TryResolveCareerKnowledgeSkillCategories(out categories, out authority)
            || categories is null || string.IsNullOrWhiteSpace(authority) || categories.Count == 0) return false;
        return categories.All(x => !string.IsNullOrWhiteSpace(x.Key)
            && CharacterCreationStandardPrioritySkillsRules.IsSupportedAttribute(x.Value)
            && catalog.KnowledgeSkills.Any(entry => entry.Category == x.Key));
    }

    private static bool TryResolveNamedSelection(ICharacterSourceDataContext context,
        CharacterCreationSkillsCatalog catalog, string name, string category, out Guid sourceId, out string authority)
    {
        sourceId = Guid.Empty; authority = string.Empty;
        if (!TryResolveCategories(context, catalog, out var categories, out var categoryAuthority)
            || !categories.ContainsKey(category)
            || !context.TryResolveCareerKnowledgeSkillName(name, out var source, out var nameAuthority)
            || string.IsNullOrWhiteSpace(nameAuthority)) return false;
        var matches = catalog.KnowledgeSkills.Where(x => x.Name == name
            || Guid.TryParse(name, out var enteredId) && Guid.TryParse(x.SourceSkillId, out var id) && id == enteredId)
            .Take(2).ToArray();
        if (source is null)
        {
            // Null is admitted only by the live full-document resolver, never by
            // absence in this finite enabled catalog or a caller-supplied hash.
            if (matches.Length != 0) return false;
        }
        else
        {
            if (matches.Length != 1 || matches[0].Name != name || matches[0].Category != category
                || !TryResolveEntry(context, matches[0], out var actual) || actual != source
                || !Guid.TryParseExact(source.SourceSkillId, "D", out sourceId) || sourceId == Guid.Empty) return false;
        }
        authority = catalog.CatalogDigest + "\n" + categoryAuthority + "\n" + nameAuthority;
        return true;
    }

    private static bool TryResolveEntry(ICharacterSourceDataContext context,
        CharacterCreationSkillCatalogEntry entry, out CharacterKnowledgeSkillSource source)
    {
        source = null!;
        if (entry.Kind != CharacterCreationSkillKinds.Knowledge || entry.IsExotic
            || !Guid.TryParseExact(entry.SourceSkillId, "D", out var id) || id == Guid.Empty
            || !context.TryResolveKnowledgeSkillSource(entry.SourceSkillId, out source)
            || !Guid.TryParse(source.SourceSkillId, out var sourceId) || sourceId != id
            || entry.Name != source.Name || entry.Category != source.SkillCategory
            || entry.DefaultAttribute != source.DefaultAttribute) return false;
        var raw = XElement.Parse(source.RawSourceXml, LoadOptions.PreserveWhitespace);
        return raw.Name == "skill" && !raw.Elements("hide").Any()
            && Guid.TryParse(ReadText(raw, "id"), out var rawId) && rawId == id
            && ReadText(raw, "name") == source.Name && ReadText(raw, "category") == source.SkillCategory
            && ReadText(raw, "attribute") == source.DefaultAttribute;
    }

    private static string CanonicalCharacterState(XElement root)
    {
        var copy = new XElement(root);
        copy.Elements(ReceiptContainer).Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Single(XElement parent, string name)
    {
        var rows = parent.Elements(name).Take(2).ToArray();
        return rows.Length == 1 ? rows[0] : throw new InvalidOperationException("Missing or ambiguous " + name);
    }

    private static string ReadText(XElement parent, string name)
    {
        var node = Single(parent, name);
        return !node.HasElements && !node.HasAttributes && !string.IsNullOrWhiteSpace(node.Value)
            ? node.Value.Trim() : throw new InvalidOperationException("Invalid " + name);
    }

    private static int ReadInt(XElement parent, string name)
        => int.TryParse(ReadText(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= 0 ? value : throw new InvalidOperationException("Invalid " + name);

    private static bool ReadRequiredBool(XElement parent, string name)
        => TryParseBoolean(ReadText(parent, name), out bool value) ? value
            : throw new InvalidOperationException("Invalid " + name);

    private static bool TryParseBoolean(string text, out bool value)
    {
        if (text == "1") { value = true; return true; }
        if (text == "0") { value = false; return true; }
        return bool.TryParse(text, out value);
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsInvalid(Exception error) => error is ArgumentException or InvalidOperationException
        or OverflowException or XmlException;
    private static bool HasUnsupportedRatingAuthority(
        XElement? improvements,
        string dictionaryKey)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ReadOptionalText(improvement, "improvementttype", string.Empty) is
                    "Skill" or "SkillBase" or "SkillLevel" or "KnowledgeSkillLevel"
                && TargetMatches(
                    ReadOptionalText(improvement, "improvedname", string.Empty),
                    dictionaryKey));

    private static bool TryResolveCostModifiers(
        XElement? improvements,
        string dictionaryKey,
        string skillCategory,
        out IReadOnlyList<CharacterCareerKnowledgeSkillKarmaModifier> modifiers)
    {
        List<CharacterCareerKnowledgeSkillKarmaModifier> result = [];
        int ordinal = 0;
        foreach (XElement improvement in improvements?.Elements("improvement") ?? [])
        {
            string type = ReadOptionalText(improvement, "improvementttype", string.Empty);
            if (!TryMapModifierKind(
                    type,
                    out CharacterCareerKnowledgeSkillKarmaModifierKind kind)
                || !IsEnabledCareerCostImprovement(improvement))
            {
                ordinal++;
                continue;
            }

            string target = ReadOptionalText(improvement, "improvedname", string.Empty);
            bool targetMatches = kind switch
            {
                CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum =>
                    TargetMatches(target, dictionaryKey)
                    || TargetMatches(target, skillCategory),
                CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost
                    or CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier =>
                    TargetMatches(target, dictionaryKey),
                _ => TargetMatches(target, skillCategory)
            };
            if (!targetMatches)
            {
                ordinal++;
                continue;
            }
            if (!string.IsNullOrEmpty(ReadOptionalText(improvement, "unique", string.Empty))
                || !TryReadNonNegativeInt(improvement, "min", 0, out int minimum)
                || !TryReadNonNegativeInt(improvement, "max", 0, out int maximum)
                || maximum != 0 && maximum < minimum
                || !TryReadDecimal(improvement, "val", out decimal value))
            {
                modifiers = [];
                return false;
            }

            string raw = ordinal.ToString(CultureInfo.InvariantCulture)
                + "\0"
                + improvement.ToString(SaveOptions.DisableFormatting);
            string identity = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
                .ToLowerInvariant();
            result.Add(new CharacterCareerKnowledgeSkillKarmaModifier(
                identity,
                kind,
                target,
                minimum,
                maximum,
                value));
            ordinal++;
        }
        modifiers = result;
        return true;
    }

    private static bool TryMapModifierKind(
        string type,
        out CharacterCareerKnowledgeSkillKarmaModifierKind kind)
    {
        switch (type)
        {
            case "KnowledgeSkillKarmaCostMinimum":
                kind = CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum;
                return true;
            case "KnowledgeSkillKarmaCost":
                kind = CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost;
                return true;
            case "KnowledgeSkillKarmaCostMultiplier":
                kind = CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier;
                return true;
            case "SkillCategoryKarmaCost":
                kind = CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCost;
                return true;
            case "SkillCategoryKarmaCostMultiplier":
                kind = CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCostMultiplier;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsEnabledCareerCostImprovement(XElement improvement)
        => IsEnabledInCareer(improvement)
            && !ReadOptionalBool(improvement, "addtorating", false);

    private static bool IsEnabledInCareer(XElement improvement)
    {
        string condition = ReadOptionalText(improvement, "condition", string.Empty);
        return ReadOptionalBool(improvement, "enabled", true)
            && (condition.Length == 0
                || string.Equals(condition, "career", StringComparison.Ordinal));
    }

    private static bool TargetMatches(string target, string expected)
        => target.Length == 0 || string.Equals(target, expected, StringComparison.Ordinal);

    private static int ReadOptionalNonNegativeInt(XElement parent, string name, int fallback)
        => TryReadNonNegativeInt(parent, name, fallback, out int value)
            ? value
            : throw new InvalidOperationException(
                $"The active settings profile has an invalid or duplicate <{name}> value.");

    private static bool TryReadNonNegativeInt(
        XElement parent,
        string name,
        int fallback,
        out int value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = fallback;
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback >= 0;
        }
        return values.Length == 1
            && int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
            && value >= 0;
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = 0m;
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool ReadOptionalBool(XElement parent, string name, bool fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback;
        }
        if (values.Length != 1 || !TryParseBoolean(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException(
                $"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    private static string ReadOptionalText(XElement parent, string name, string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException(
                $"The saved runner has duplicate <{name}> values.")
        };
    }
}
