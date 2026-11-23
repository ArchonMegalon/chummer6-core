using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationKarmaQualitiesRules
{
    public const int MaximumSelections = 64;

    public static string PolicyDigest(CharacterCreationKarmaQualitiesPolicy policy)
        => Hash(policy with { AuthorityDigest = string.Empty });

    public static string CatalogDigest(CharacterCreationKarmaQualitiesCatalog catalog)
        => CharacterCreationKarmaQualitiesCatalogAuthority.ComputeDigest(catalog);

    // Karma skills consume the exact selected-quality cost graph. Ordinary
    // quality admission has no such consumer and deliberately disables it.
    // Reuse the strict compiler; source, requirement and choice blockers remain.
    public static CharacterCreationQualitiesAuthority AdmitSkillCostPurchases(CharacterCreationQualitiesAuthority source)
        => source.IsAuthoritative
            ? CharacterCreationLegacySourceProjector.AdmitSkillKarmaQualityAuthority(source)
            : source;

    public static bool IsValidPolicy(CharacterCreationKarmaQualitiesPolicy? policy)
        => IsValidCostPolicy(policy, CharacterCreationKarmaQualitiesPolicy.SchemaV1);

    internal static bool IsValidCostPolicy(CharacterCreationKarmaQualitiesPolicy? policy, string schema)
        => policy is {
                QualityKarmaLimit: >= 0, MetagenicLimit: >= 0, Costs.KarmaMultiplier: >= 0,
                SourceAnchorIds.Count: > 0 }
            && policy.Schema == schema
            && !string.IsNullOrWhiteSpace(policy.SettingsProfileId)
            && Digest(policy.RawProfileInputsDigest) && Digest(policy.SourceInputsDigest)
            && policy.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
            && policy.AuthorityDigest == PolicyDigest(policy);

    public static bool IsValidCatalog(CharacterCreationKarmaQualitiesCatalog? catalog)
        => catalog is { Options.Count: > 0 and <= 65_536 }
        && catalog.Schema is CharacterCreationKarmaQualitiesCatalog.SchemaV1 or CharacterCreationKarmaQualitiesCatalog.SchemaV2
            && IsValidPolicy(catalog.Policy)
            && catalog.Options.All(option => option is not null && !string.IsNullOrWhiteSpace(option.OptionId)
            && (option.SourceExecutionSchema != CharacterCreationKarmaQualityPrompts.SourceSchema
                || catalog.Schema == CharacterCreationKarmaQualitiesCatalog.SchemaV2
                    && option.FollowUpChoiceLabel is null && CharacterCreationKarmaQualityPrompts.IsExactOption(option))
                && option.OptionDigest == CharacterCreationQualitiesRules.ComputeOptionDigest(option))
            && catalog.Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() == catalog.Options.Count
            && catalog.CatalogDigest == CatalogDigest(catalog);

    public static bool TryFreeze(IReadOnlyList<string>? ids, out string[] frozen)
    {
        frozen = [];
        if (ids is null || ids.Count > MaximumSelections) return false;
        frozen = ids.Take(MaximumSelections + 1).ToArray();
        return frozen.Length <= MaximumSelections && frozen.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 120)
            && frozen.Distinct(StringComparer.Ordinal).Count() == frozen.Length;
    }

    public static CharacterCreationKarmaQualitiesQuote? Evaluate(
        CharacterCreationKarmaQualitiesCatalog catalog, CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationKarmaTalentOption talent, IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, string>? promptValues = null)
        => AdmittedCatalog.TryCreate(catalog)?.Evaluate(metatype, talent, ids, promptValues);

    // Operation-local authority, never a caller-provided "already validated"
    // flag or a cache across Load/Preview/Confirm. Copy before validating so
    // neither the resolver nor a returned DTO can change admitted collections.
    internal sealed class AdmittedCatalog
    {
        private AdmittedCatalog(CharacterCreationKarmaQualitiesCatalog catalog) => Catalog = catalog;

        internal CharacterCreationKarmaQualitiesCatalog Catalog { get; }

        internal static AdmittedCatalog? TryCreate(CharacterCreationKarmaQualitiesCatalog? source)
        {
            if (source is not { Options.Count: > 0 and <= 65_536, Policy: not null }) return null;
            try
            {
                var options = source.Options.Take(65_537).ToArray();
                if (options.Length is 0 or > 65_536 || options.Any(option => option is null)) return null;
                var frozen = source with
                {
                    Policy = source.Policy with
                    {
                        SourceAnchorIds = FreezeAnchors(source.Policy.SourceAnchorIds)
                    },
                    Options = Array.AsReadOnly(options.Select(option => option with
                    {
                        SourceAnchorIds = FreezeAnchors(option.SourceAnchorIds)
                    }).ToArray())
                };
                return IsValidCatalog(frozen) ? new AdmittedCatalog(frozen) : null;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return null; }

            static IReadOnlyList<string> FreezeAnchors(IReadOnlyList<string>? anchors)
                => anchors is null ? null! : Array.AsReadOnly(anchors.ToArray());
        }

        internal CharacterCreationKarmaQualitiesQuote? Evaluate(CharacterCreationMetatypeOptionProjection metatype,
            CharacterCreationKarmaTalentOption talent, IReadOnlyList<string> ids,
            IReadOnlyDictionary<string, string>? promptValues = null)
        {
            if (!TryFreeze(ids, out var frozen)
                || !CharacterCreationKarmaQualityPrompts.TryFreeze(promptValues, out var answers)) return null;
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var selected = new List<CharacterCreationQualityCatalogOption>();
            foreach (string id in frozen)
            {
                var option = Catalog.Options.SingleOrDefault(item => item.OptionId == id);
                if (option is null) return null;
                if (!CharacterCreationKarmaQualityPrompts.TryBind(option, answers, consumed, out var bound)) return null;
                selected.Add(bound);
            }
            if (answers is not null && answers.Keys.Any(key => !consumed.Contains(key))) return null;
            // Per-selection source effects, eligibility, policy, budget and
            // quote hashes still run for each evaluation, including saved state.
            return EvaluateSelected(Catalog.Policy, Catalog.CatalogDigest, metatype, talent,
                selected.OrderBy(option => option.OptionId, StringComparer.Ordinal).ToArray());
        }
    }

    public static bool IsValid(CharacterCreationKarmaQualitiesQuote? quote,
        CharacterCreationMetatypeOptionProjection metatype, CharacterCreationKarmaTalentOption? talent,
        IReadOnlyList<string>? ids, IReadOnlyDictionary<string, string>? promptValues = null)
    {
        if (quote is null) return ids is null && promptValues is null;
        if (talent is null || !TryFreeze(ids, out var frozen) || quote.Selections is not { Count: <= MaximumSelections }
            || quote.Selections.Any(item => item is null)
            || quote.Schema is not (CharacterCreationKarmaQualitiesQuote.SchemaV1 or CharacterCreationKarmaQualitiesQuote.SchemaV2)
            || !CharacterCreationKarmaQualityPrompts.TryFreeze(promptValues, out var answers)
            || quote.Blockers is not { Count: 0 }
            || !quote.Selections.Select(item => item.OptionId).SequenceEqual(frozen.Order(StringComparer.Ordinal))) return false;
        if (quote.Selections.Any(item => !IsExactPurchase(item)
                || item.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema
                    && (item.FollowUpChoiceId is null || item.FollowUpChoiceLabel is null))
            || quote.Selections.Where(item => item.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)
                .GroupBy(item => item.FollowUpChoiceId, StringComparer.Ordinal).Any(group => group.Count() != 1)) return false;
        var selectedAnswers = quote.Selections.Where(item => item.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)
            .ToDictionary(item => item.FollowUpChoiceId!, item => item.FollowUpChoiceLabel!, StringComparer.Ordinal);
        if (selectedAnswers.Count != (answers?.Count ?? 0)
            || selectedAnswers.Any(pair => answers is null || !answers.TryGetValue(pair.Key, out var value) || value != pair.Value)) return false;
        var expected = EvaluateSelected(quote.Policy, quote.CatalogDigest, metatype, talent, quote.Selections);
        return expected is { CanSelect: true }
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote, expected);
    }

    private static CharacterCreationKarmaQualitiesQuote? EvaluateSelected(
        CharacterCreationKarmaQualitiesPolicy policy, string catalogDigest,
        CharacterCreationMetatypeOptionProjection metatype, CharacterCreationKarmaTalentOption talent,
        IReadOnlyList<CharacterCreationQualityCatalogOption> selections)
    {
        if (!IsValidPolicy(policy) || !Digest(catalogDigest) || !metatype.IsEnabled || !talent.IsEnabled
            || selections.Count > MaximumSelections || metatype.GrantedQualities is null) return null;
        var blockers = new List<string>();
        var names = selections.Select(item => item.Name).Concat(metatype.GrantedQualities.Select(item => item.Name))
            .Append(talent.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var option in selections)
        {
            if (!IsExactPurchase(option)) return null;
            if (option.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema && option.FollowUpChoiceLabel is null)
                blockers.Add(CharacterCreationKarmaQualityPrompts.PromptRequired);
            if (CharacterCreationKarmaQualityPrompts.Conflicts(option, names))
                blockers.Add(CharacterCreationQualitiesBlockers.EligibilityUnresolved);
            if (metatype.GrantedQualities.Any(grant => grant.Name == option.Name)
                || talent.OptionId == option.SourceId.ToString("D") || talent.Name == option.Name)
                blockers.Add(CharacterCreationQualitiesBlockers.DuplicateSelection);
        }
        if (selections.GroupBy(option => option.SelectionKey, StringComparer.Ordinal).Any(group => group.Count() != 1))
            blockers.Add(CharacterCreationQualitiesBlockers.DuplicateSelection);
        if (!CharacterCreationKarmaTalentAuthority.IsCompatible(talent, metatype, selections.Select(item => item.Name).ToArray()))
            blockers.Add(CharacterCreationQualitiesBlockers.EligibilityUnresolved);
        if (!CharacterCreationQualityCostRules.TryCalculate(policy.Costs, policy.QualityKarmaLimit,
            selections.Select(item => new CharacterCreationQualityCostItem(item.KarmaCost,
                item.CountsAgainstQualityLimit, item.CountsAgainstKarma, item.IsMetagenic)).ToArray(), out var costs)) return null;
        if (costs.PositiveLimitKarma > policy.QualityKarmaLimit && !policy.MayExceedPositiveLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
        if (costs.NegativeLimitKarma > policy.QualityKarmaLimit && !policy.MayExceedNegativeLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.NegativeLimitExceeded);
        if (costs.MetagenicPositiveKarma > policy.MetagenicLimit || costs.MetagenicNegativeKarma > policy.MetagenicLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.MetagenicLimitExceeded);
        if ((costs.MetagenicPositiveKarma != 0 || costs.MetagenicNegativeKarma != 0)
            && costs.MetagenicNegativeKarma != costs.MetagenicPositiveKarma
            && costs.MetagenicNegativeKarma != costs.MetagenicPositiveKarma - 1)
            blockers.Add(CharacterCreationQualitiesBlockers.MetagenicImbalanced);
        var result = new CharacterCreationKarmaQualitiesQuote(
            selections.Any(item => item.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)
                ? CharacterCreationKarmaQualitiesQuote.SchemaV2 : CharacterCreationKarmaQualitiesQuote.SchemaV1,
            policy, catalogDigest, Hash(metatype), Hash(talent), selections.ToArray(), costs,
            blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
        return result with { QuoteDigest = Hash(result) };
    }

    public static bool IsExactPurchase(CharacterCreationQualityCatalogOption? option)
    {
        if (option is not { IsSelectable: true, EligibilityIsExact: true, IsFreeOrGranted: false,
            Rating: > 0 and <= 100, MaximumSelections: 1, SourceAnchorIds.Count: > 0,
            SourceNodeXml.Length: > 0 and <= 32 * 1024 }
            || option.SourceId == Guid.Empty || option.SelectionKey != option.SourceId.ToString("D")
            || option.OptionId != $"quality:{option.SourceId:D}:rating:{option.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            || option.OptionDigest != CharacterCreationQualitiesRules.ComputeOptionDigest(option)) return false;
        if (option.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)
            return CharacterCreationKarmaQualityPrompts.IsExactOption(option);
        if (option.FollowUpChoiceId is not null || option.FollowUpChoiceLabel is not null) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(option.SourceNodeXml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 });
            var row = XElement.Load(reader);
            // Grant-only/talent choices use their own domain, never an ordinary purchase.
            if (row.Element("onlyprioritygiven") is not null
                || row.Element("careeronly") is { } career && (!bool.TryParse(career.Value, out bool onlyCareer) || onlyCareer)
                || row.Element("implemented") is { } implemented && (!bool.TryParse(implemented.Value, out bool enabled) || !enabled)) return false;
            var selection = new CharacterCreationQualitySelection(option.OptionId, option.SourceId, option.SelectionKey,
                option.Name, option.Type, option.Rating, option.KarmaCost, option.IsMetagenic, option.CountsAgainstQualityLimit,
                option.CountsAgainstKarma, false, null, null, option.SourceAnchorIds, option.SourceNodeXml,
                option.SourceNodeDigest, option.OptionDigest);
            return CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection, option.OptionDigest, out _, out _);
        }
        catch (XmlException) { return false; }
    }

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static bool Digest(string? value) => CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(value);
}
