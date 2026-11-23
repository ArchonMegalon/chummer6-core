using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>Explicit Karma v2 grammar. A saved name or Extra never supplies authority.</summary>
public static class CharacterCreationKarmaQualityPrompts
{
    public const string SourceSchema = "chummer.karma-quality-source-text.v1";
    public const string PromptRequired = "creation-karma-quality-prompt-required";

    // Call only through a newly admitted source profile. Existing profiles retain v1.
    public static CharacterCreationKarmaQualitiesCatalog? AdmitCatalog(CharacterCreationKarmaQualitiesCatalog source)
    {
        if (!CharacterCreationKarmaQualitiesRules.IsValidCatalog(source)
            || source.Schema != CharacterCreationKarmaQualitiesCatalog.SchemaV1) return null;
        var options = source.Options.Select(option =>
        {
            if (option.IsSelectable || option.DisableReasonKey is not
                ("creation-qualities-followup-projection-pending" or "creation-qualities-requirement-projection-pending")
                || !TryCompile(option, out _, out _, out var prompt)) return option;
            var admitted = option with { IsSelectable = true, EligibilityIsExact = true, DisableReasonKey = null,
                SourceExecutionSchema = SourceSchema, FollowUpChoiceId = prompt.PromptId, FollowUpChoiceLabel = null };
            return admitted with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(admitted) };
        }).ToArray();
        var catalog = source with { Schema = CharacterCreationKarmaQualitiesCatalog.SchemaV2, Options = options };
        return catalog with { CatalogDigest = CharacterCreationKarmaQualitiesRules.CatalogDigest(catalog) };
    }

    public static LifeModuleFollowUpPromptDto? Prompt(CharacterCreationQualityCatalogOption option)
        => option.SourceExecutionSchema == SourceSchema && TryCompile(option, out _, out _, out var prompt)
            && option.FollowUpChoiceId == prompt.PromptId ? prompt : null;

    public static bool TryFreeze(IReadOnlyDictionary<string, string>? values,
        out IReadOnlyDictionary<string, string>? frozen)
    {
        frozen = null;
        if (values is null) return true;
        if (!LifeModuleDecisionInputIntegrity.TryNormalize(values, out var normalized)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(values, normalized)
            || normalized.Values.Any(value => !CharacterCreationFoundationQualityInstanceResolver.IsLiteral(value))) return false;
        frozen = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            normalized.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        return true;
    }

    public static bool TryNormalizeAnswer(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!LifeModuleDecisionInputIntegrity.TryNormalize(new Dictionary<string, string> { ["answer"] = value }, out var values)
            || !CharacterCreationFoundationQualityInstanceResolver.IsLiteral(values["answer"])) return false;
        normalized = values["answer"];
        return true;
    }

    internal static IReadOnlyDictionary<string, string>? RetainedValues(CharacterCreationKarmaQualitiesQuote? quote)
    {
        if (quote?.Selections is null) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in quote.Selections)
        {
            if (option is null) return null;
            if (option.SourceExecutionSchema != SourceSchema) continue;
            if (option.FollowUpChoiceId is null || option.FollowUpChoiceLabel is null
                || !values.TryAdd(option.FollowUpChoiceId, option.FollowUpChoiceLabel)) return null;
        }
        return values.Count == 0 ? null : values;
    }

    internal static bool IsExactOption(CharacterCreationQualityCatalogOption option)
        => option.SourceExecutionSchema == SourceSchema
            && TryCompile(option, out _, out _, out var prompt)
            && option.FollowUpChoiceId == prompt.PromptId
            && (option.FollowUpChoiceLabel is null || TryNormalizeAnswer(option.FollowUpChoiceLabel, out var answer)
                && answer == option.FollowUpChoiceLabel);

    internal static bool TryBind(CharacterCreationQualityCatalogOption option,
        IReadOnlyDictionary<string, string>? values, ISet<string> consumed,
        out CharacterCreationQualityCatalogOption bound)
    {
        bound = option;
        if (option.SourceExecutionSchema != SourceSchema) return true;
        if (Prompt(option) is not { } prompt || option.FollowUpChoiceLabel is not null) return false;
        if (values is null || !values.TryGetValue(prompt.PromptId, out var value))
            return true;
        if (!consumed.Add(prompt.PromptId) || !TryNormalizeAnswer(value, out var answer) || answer != value) return false;
        bound = option with { FollowUpChoiceLabel = answer };
        bound = bound with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(bound) };
        return true;
    }

    internal static bool Conflicts(CharacterCreationQualityCatalogOption option, IReadOnlySet<string> selectedNames)
        => option.SourceExecutionSchema == SourceSchema
            && (!TryCompile(option, out _, out var forbidden, out _) || forbidden.Any(selectedNames.Contains));

    internal static CharacterCreationQualitySelection Selection(CharacterCreationQualityCatalogOption option, bool retainSourceExecution = true)
        => new(option.OptionId, option.SourceId, option.SelectionKey, option.Name, option.Type, option.Rating,
            option.KarmaCost, option.IsMetagenic, option.CountsAgainstQualityLimit, option.CountsAgainstKarma,
            option.IsFreeOrGranted, option.FollowUpChoiceId, option.FollowUpChoiceLabel, option.SourceAnchorIds,
            option.SourceNodeXml, option.SourceNodeDigest, option.OptionDigest)
        { ResolvedArmCount = option.SourceExecutionSchema == SourceSchema ? option.ResolvedArmCount : null,
            SourceExecutionSchema = retainSourceExecution || option.SourceExecutionSchema == SourceSchema ? option.SourceExecutionSchema : null };

    internal static bool TryPrepare(CharacterCreationQualitySelection selection,
        out CharacterCreationQualitySelection plain, out string answer, bool requireAnswer = true)
    {
        plain = selection; answer = string.Empty;
        var option = new CharacterCreationQualityCatalogOption(selection.OptionId, selection.SourceId, selection.SelectionKey,
            selection.Name, selection.Type, selection.Rating, selection.KarmaCost, 1, selection.IsMetagenic,
            selection.CountsAgainstQualityLimit, selection.CountsAgainstKarma, selection.IsFreeOrGranted, true, true, null,
            selection.FollowUpChoiceId, selection.FollowUpChoiceLabel, selection.SourceAnchorIds, selection.SourceNodeXml,
            selection.SourceNodeDigest, selection.OptionDigest)
        { ResolvedArmCount = selection.ResolvedArmCount, SourceExecutionSchema = selection.SourceExecutionSchema };
        if (selection.SourceExecutionSchema != SourceSchema || !IsExactOption(option)
            || option.OptionDigest != CharacterCreationQualitiesRules.ComputeOptionDigest(option)
            || requireAnswer && selection.FollowUpChoiceLabel is null
            || !TryCompile(option, out var remainder, out _, out _)) return false;
        answer = selection.FollowUpChoiceLabel ?? string.Empty;
        plain = PlainSelection(option, remainder);
        return true;
    }

    private static CharacterCreationQualitySelection PlainSelection(CharacterCreationQualityCatalogOption option, XElement remainder)
    {
        string xml = remainder.ToString(SaveOptions.DisableFormatting);
        return Selection(option) with { SourceNodeXml = xml,
            SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml),
            SourceExecutionSchema = null, FollowUpChoiceId = null, FollowUpChoiceLabel = null };
    }

    private static bool TryCompile(CharacterCreationQualityCatalogOption option, out XElement remainder,
        out string[] forbiddenNames, out LifeModuleFollowUpPromptDto prompt)
    {
        remainder = null!; forbiddenNames = []; prompt = null!;
        if (option.SourceNodeXml is not { Length: > 0 and <= 32 * 1024 }
            || option.SourceNodeDigest != CharacterCreationQualitiesRules.ComputeSourceNodeDigest(option.SourceNodeXml)
            || option.SourceId == Guid.Empty || option.SelectionKey != option.SourceId.ToString("D")
            || option.OptionId != $"quality:{option.SourceId:D}:rating:{option.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            || option.Rating is < 1 or > 100 || option.MaximumSelections != 1 || option.IsFreeOrGranted
            || option.SourceAnchorIds is not { Count: > 0 }) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(option.SourceNodeXml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 });
            var row = XElement.Load(reader);
            if (row.Element("onlyprioritygiven") is not null || row.Element("required") is not null
                || row.Element("careeronly") is { } career && (!bool.TryParse(career.Value, out bool onlyCareer) || onlyCareer)
                || row.Element("implemented") is { } impl && (!bool.TryParse(impl.Value, out bool implemented) || !implemented)
                || row.Elements("bonus").Count() != 1 || row.Elements("forbidden").Count() > 1
                || row.Element("bonus")!.Elements("selecttext").ToArray() is not [var text]
                || text.HasElements || !string.IsNullOrWhiteSpace(text.Value)) return false;
            if (text.HasAttributes)
            {
                // Editable suggestions permit a literal answer. Only the established absolute
                // child-path union grammar is admitted; arbitrary XPath is never evaluated here.
                if (text.Attributes().Count() != 3 || text.Attribute("allowedit")?.Value is not { } allow
                    || !bool.TryParse(allow, out bool editable) || !editable
                    || text.Attribute("xml")?.Value is not { } xml
                    || !Regex.IsMatch(xml, @"\A[A-Za-z0-9_-]+\.xml\z", RegexOptions.CultureInvariant)
                    || text.Attribute("xpath")?.Value is not { Length: > 0 and <= 2048 } xpath
                    || !Regex.IsMatch(xpath, @"\A/[A-Za-z_][A-Za-z0-9_-]*(?:/(?:[A-Za-z_][A-Za-z0-9_-]*|\*))*(?:\s*\|\s*/[A-Za-z_][A-Za-z0-9_-]*(?:/(?:[A-Za-z_][A-Za-z0-9_-]*|\*))*)*\z", RegexOptions.CultureInvariant)) return false;
            }
            if (!CharacterCreationFoundationQualityInstanceResolver.TryInspectSelection(row, out bool needsText) || !needsText)
                return false;
            if (row.Element("forbidden") is { } forbidden)
            {
                if (forbidden.HasAttributes || forbidden.Elements().ToArray() is not [var oneof]
                    || oneof.Name != "oneof" || oneof.HasAttributes || !oneof.HasElements
                    || forbidden.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                    || oneof.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                    || oneof.Elements().Any(q => q.Name != "quality" || q.HasAttributes || q.HasElements
                        || !CharacterCreationFoundationQualityInstanceResolver.IsLiteral(q.Value) || q.Value != q.Value.Trim())) return false;
                forbiddenNames = oneof.Elements().Select(q => q.Value).ToArray();
                if (forbiddenNames.Length > 64 || forbiddenNames.Distinct(StringComparer.Ordinal).Count() != forbiddenNames.Length) return false;
                forbidden.Remove();
            }
            text.Remove();
            remainder = row;
            // The original source stays digest-bound. The typed prompt/forbidden nodes above are
            // consumed explicitly; the existing compiler must accept every remaining effect/cost.
            if (!CharacterCreationLegacySourceProjector.TryBuildQualityGraph(PlainSelection(option, row),
                    option.SourceNodeDigest, out _, out _)) return false;
            string id = "karma-quality:" + CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
            { Schema = SourceSchema, option.OptionId, option.SourceNodeDigest })[7..];
            prompt = new(id, option.Name, "text", true, [], option.SourceAnchorIds.ToArray(), option.OptionId, "quality/bonus/selecttext");
            return true;
        }
        catch (XmlException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
