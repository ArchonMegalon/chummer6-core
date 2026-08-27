using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public readonly record struct CharacterCustomDrugComponentId(Guid Value);

public readonly record struct CharacterCustomDrugGradeId(Guid Value);

public readonly record struct CharacterCustomDrugInstanceId(Guid Value);

public enum CharacterCustomDrugContext
{
    Creation = 0,
    Career = 1
}

public enum CharacterCustomDrugQuotePurpose
{
    RecipeDefinition = 0,
    QuantityPurchase = 1
}

public enum CharacterCustomDrugComponentCategory
{
    Foundation = 0,
    Block = 1,
    Enhancer = 2
}

public enum CharacterCustomDrugLegality
{
    Legal = 0,
    Restricted = 1,
    Forbidden = 2
}

/// <summary>
/// Selects the exact arithmetic profile used for one recipe. The pinned
/// Chummer5 designer currently stores a grade but does not apply the grade's
/// cost or addiction-threshold modifiers to its computed Drug totals. Those
/// legacy semantics remain explicit instead of being hidden in UI code; a
/// corrected rules profile can enable either modifier without changing the
/// recipe contract.
/// </summary>
public sealed record CharacterCustomDrugCalculationPolicy(
    bool MultiplyComponentCostByLevel,
    bool ApplyGradeCostMultiplier,
    bool ApplyGradeAddictionThresholdModifier,
    int MaximumComponents,
    decimal MaximumQuantity,
    int QuantityDecimalPlaces);

public sealed record CharacterCustomDrugAttributeEffect(string Attribute, decimal Value);

public sealed record CharacterCustomDrugLimitEffect(string Limit, int Value);

public sealed record CharacterCustomDrugQualityEffect(string Name, int Rating);

public sealed record CharacterCustomDrugEffectLevel(
    int Level,
    IReadOnlyList<CharacterCustomDrugAttributeEffect> Attributes,
    IReadOnlyList<CharacterCustomDrugLimitEffect> Limits,
    IReadOnlyList<CharacterCustomDrugQualityEffect> Qualities,
    IReadOnlyList<string> Information,
    int Initiative,
    int InitiativeDice,
    int CrashDamage,
    int Speed,
    int Duration);

public sealed record CharacterCustomDrugComponentSource(
    CharacterCustomDrugComponentId Id,
    string Name,
    CharacterCustomDrugComponentCategory Category,
    int Limit,
    int AvailabilityModifier,
    CharacterCustomDrugLegality Legality,
    decimal CostPerLevel,
    int AddictionRating,
    int AddictionThreshold,
    string SourceBook,
    string Page,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<CharacterCustomDrugEffectLevel> Effects);

public sealed record CharacterCustomDrugGrade(
    CharacterCustomDrugGradeId Id,
    string Name,
    decimal CostMultiplier,
    int AddictionThresholdModifier,
    string SourceBook,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCustomDrugCatalogAuthority(
    bool Exact,
    IReadOnlyList<string> Blockers,
    string RulesetId,
    string SettingsProfileId,
    string CatalogDigest,
    string RulesDigest,
    CharacterCustomDrugCalculationPolicy Policy,
    IReadOnlyList<CharacterCustomDrugGrade> Grades,
    IReadOnlyList<CharacterCustomDrugComponentSource> Components)
{
    public static CharacterCustomDrugCatalogAuthority Unavailable { get; } = new(
        Exact: false,
        Blockers: [CharacterCustomDrugBlockers.AuthorityUnavailable],
        RulesetId: string.Empty,
        SettingsProfileId: string.Empty,
        CatalogDigest: string.Empty,
        RulesDigest: string.Empty,
        Policy: new CharacterCustomDrugCalculationPolicy(false, false, false, 0, 0m, 0),
        Grades: [],
        Components: []);
}

public sealed record CharacterCustomDrugPreparation(
    bool Exact,
    IReadOnlyList<string> Blockers,
    CharacterCustomDrugContext Context,
    CharacterCustomDrugQuotePurpose Purpose,
    long ContentRevision,
    string CharacterDigest,
    string CatalogDigest,
    string RulesDigest,
    string SettingsProfileId,
    decimal AvailableNuyen,
    CharacterCustomDrugCalculationPolicy Policy,
    IReadOnlyList<CharacterCustomDrugGrade> Grades,
    IReadOnlyList<CharacterCustomDrugComponentSource> Components);

public sealed record CharacterCustomDrugComponentSelection(
    CharacterCustomDrugComponentId ComponentId,
    int Level);

public sealed record CharacterCustomDrugSelection(
    string Name,
    CharacterCustomDrugGradeId GradeId,
    decimal Quantity,
    bool Stolen,
    bool FreeCost,
    decimal MarkupPercent,
    IReadOnlyList<CharacterCustomDrugComponentSelection> Components);

public sealed record CharacterCustomDrugSelectedComponent(
    CharacterCustomDrugComponentId ComponentId,
    string Name,
    CharacterCustomDrugComponentCategory Category,
    int Level,
    decimal CostContribution,
    int AvailabilityContribution,
    CharacterCustomDrugLegality Legality,
    string SourceBook,
    string Page,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCustomDrugAggregateEffects(
    IReadOnlyList<CharacterCustomDrugAttributeEffect> Attributes,
    IReadOnlyList<CharacterCustomDrugLimitEffect> Limits,
    IReadOnlyList<CharacterCustomDrugQualityEffect> Qualities,
    IReadOnlyList<string> Information,
    int Initiative,
    int InitiativeDice,
    int CrashDamage,
    int Speed,
    int Duration);

public sealed record CharacterCustomDrugQuote(
    bool Exact,
    string BlockReason,
    string Name,
    CharacterCustomDrugGradeId GradeId,
    string GradeName,
    decimal Quantity,
    decimal ComponentCost,
    decimal UnitCost,
    decimal ChargedCost,
    decimal NuyenDelta,
    int Availability,
    CharacterCustomDrugLegality Legality,
    int AddictionRating,
    int AddictionThreshold,
    CharacterCustomDrugAggregateEffects Effects,
    IReadOnlyList<CharacterCustomDrugSelectedComponent> Components,
    string QuoteDigest);

public sealed record CharacterCustomDrugCommitCommand(
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedRulesDigest,
    string ExpectedQuoteDigest,
    string IdempotencyKey,
    CharacterCustomDrugSelection Selection,
    CharacterCustomDrugInstanceId NewDrugInstanceId,
    IReadOnlyList<Guid> NewComponentInstanceIds);

public sealed record CharacterCustomDrugCommitReceipt(
    long PreviousContentRevision,
    long ContentRevision,
    string PreviousCharacterDigest,
    string CharacterDigest,
    string CatalogDigest,
    string RulesDigest,
    string QuoteDigest,
    string CommandDigest,
    string IdempotencyKeyDigest,
    CharacterCustomDrugInstanceId DrugInstanceId,
    IReadOnlyList<Guid> ComponentInstanceIds,
    string DrugXmlDigest,
    string ReceiptDigest);

public sealed record CharacterCustomDrugUndoCommand(
    CharacterCustomDrugCommitReceipt? Receipt);

public sealed record CharacterCustomDrugCommitResult(
    bool Committed,
    bool AlreadyCommitted,
    string BlockReason,
    long PreviousContentRevision,
    long NewContentRevision,
    string PreviousCharacterDigest,
    string NewCharacterDigest,
    string CharacterXml,
    CharacterCustomDrugCommitReceipt? Receipt);

/// <summary>
/// Exact legacy payload prepared for inclusion by the atomic Creation finalizer.
/// This is a projection result, never a mutation receipt.
/// </summary>
public sealed record CharacterCustomDrugCreationProjection(
    bool Exact,
    string BlockReason,
    string DrugXml,
    string DrugXmlDigest,
    string QuoteDigest);

public static class CharacterCustomDrugBlockers
{
    public const string AuthorityUnavailable = "The exact custom-drug authority is unavailable.";
    public const string InvalidIdentity = "The custom-drug grade, component, recipe, component-instance, and expense identities are invalid or collide.";
    public const string InvalidName = "The custom drug requires a bounded non-empty name.";
    public const string InvalidQuantity = "The custom-drug quantity is outside the exact calculation profile.";
    public const string InvalidMarkup = "The custom-drug markup must be an exact value from -99.00 through 1000.00 with at most two decimal places.";
    public const string MissingFoundation = "A custom drug requires exactly one Foundation component.";
    public const string DuplicateFoundation = "A custom drug cannot contain more than one Foundation component.";
    public const string ComponentLimit = "A custom-drug component exceeds its source-defined recipe limit.";
    public const string ComponentUnavailable = "A selected custom-drug component or effect level is absent or ambiguous.";
    public const string FoundationConflict = "A level-three Block cannot raise an attribute reduced by the selected Foundation.";
    public const string InsufficientFunds = "The custom-drug purchase exceeds the available Nuyen bound to the quote.";
    public const string RecipeDefinitionOptions = "A custom-drug recipe definition creates exactly one unstolen initial dose without markup or purchase expense.";
    public const string NotCreation = "Custom-drug creation authority requires a saved creation-mode character with created=false.";
    public const string NotCareer = "Custom-drug Career authority requires a saved Career character with created=true.";
    public const string CreationMutationRequiresFinalizer = "Creation-mode custom-drug recipes must be committed by the atomic whole-character finalizer.";
    public const string InvalidIdempotencyKey = "The custom-drug idempotency key is missing or outside the bounded authority.";
    public const string ArithmeticOverflow = "Custom-drug arithmetic exceeded the exact supported range.";
    public const string StaleRevision = "The character content revision changed after the custom drug was prepared.";
    public const string StaleCharacter = "The character bytes changed after the custom drug was prepared.";
    public const string StaleCatalog = "The custom-drug catalog changed after the custom drug was prepared.";
    public const string StaleRules = "The custom-drug calculation policy changed after the custom drug was prepared.";
    public const string StaleQuote = "The custom-drug quote changed after confirmation.";
    public const string StaleReceipt = "The custom-drug commit receipt is stale, altered, or no longer matches the saved recipe.";
}

public static class CharacterCustomDrugLegacyAuthority
{
    public const string DrugComponentsXmlSha256 = "6c13869d5419ae65b402ee3d91b36bc1cf71c792c2e2cdacf914dbf5e3af1425";
    public const string CreateCustomDrugSha256 = "9492046b4549cdaf6b6bdc8ab2956fe4c05701bac36009207dc756046cc6d482";
    public const string DrugsSha256 = "f39f855ffdd90c5e42f61b542166c771468b4ee4a355377d18da294316ae951b";
    public const string CharacterCreateSha256 = "78816501a56194ecc9d757c58c2a4e4e0b7916f55944d58b7d9a3b16761563db";
    public const string CharacterCareerSha256 = "5594076cfe19ac8bfe832025040437ba079f46ef75473cfca0315bd6692da859";
}

/// <summary>
/// Pure SR5 custom-drug recipe authority. Source projection and persistence are
/// separate ports; callers cannot turn labels or raw XML into executable recipe
/// choices. Every accepted quote is bound to the exact character, catalog,
/// rules profile, component levels, aggregate effects, and price.
/// </summary>
public static class CharacterCustomDrugRules
{
    public const int MaximumNameLength = 128;
    public const decimal MinimumMarkupPercent = -99m;
    public const decimal MaximumMarkupPercent = 1_000m;
    public const int MarkupDecimalPlaces = 2;

    private static readonly CharacterCustomDrugAggregateEffects s_EmptyEffects = new(
        [], [], [], [], 0, 0, 0, 0, 0);

    public static CharacterCustomDrugPreparation BindPreparation(
        CharacterCustomDrugCatalogAuthority? authority,
        CharacterCustomDrugContext context,
        CharacterCustomDrugQuotePurpose purpose,
        long contentRevision,
        string characterDigest,
        decimal availableNuyen)
    {
        if (!IsValidCatalogAuthority(authority)
            || contentRevision < 0
            || !IsCanonicalDigest(characterDigest)
            || availableNuyen < 0m)
        {
            return new CharacterCustomDrugPreparation(
                Exact: false,
                Blockers: authority?.Blockers is { Count: > 0 }
                    ? authority.Blockers
                    : [CharacterCustomDrugBlockers.AuthorityUnavailable],
                context,
                purpose,
                Math.Max(0, contentRevision),
                IsCanonicalDigest(characterDigest) ? characterDigest : string.Empty,
                authority?.CatalogDigest ?? string.Empty,
                authority?.RulesDigest ?? string.Empty,
                authority?.SettingsProfileId ?? string.Empty,
                Math.Max(0m, availableNuyen),
                authority?.Policy ?? CharacterCustomDrugCatalogAuthority.Unavailable.Policy,
                authority?.Grades ?? [],
                authority?.Components ?? []);
        }
        return new CharacterCustomDrugPreparation(
            Exact: true,
            Blockers: [],
            context,
            purpose,
            contentRevision,
            characterDigest,
            authority!.CatalogDigest,
            authority.RulesDigest,
            authority.SettingsProfileId,
            availableNuyen,
            authority.Policy,
            authority.Grades,
            authority.Components);
    }

    public static CharacterCustomDrugQuote Quote(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);
        if (!IsValidPreparation(preparation))
            return Blocked(
                selection,
                preparation.Blockers.Count == 0
                    ? CharacterCustomDrugBlockers.AuthorityUnavailable
                    : preparation.Blockers[0]);
        if (!IsValidName(selection.Name))
            return Blocked(selection, CharacterCustomDrugBlockers.InvalidName);
        if (selection.GradeId.Value == Guid.Empty || selection.Components is null)
            return Blocked(selection, CharacterCustomDrugBlockers.InvalidIdentity);
        if (!IsValidQuantity(selection.Quantity, preparation.Policy))
            return Blocked(selection, CharacterCustomDrugBlockers.InvalidQuantity);
        if (!IsValidMarkup(selection.MarkupPercent))
            return Blocked(selection, CharacterCustomDrugBlockers.InvalidMarkup);
        if (preparation.Purpose == CharacterCustomDrugQuotePurpose.RecipeDefinition
            && (selection.Quantity != 1m
                || selection.Stolen
                || selection.FreeCost
                || selection.MarkupPercent != 0m))
        {
            return Blocked(selection, CharacterCustomDrugBlockers.RecipeDefinitionOptions);
        }
        if (selection.Components.Count == 0
            || selection.Components.Count > preparation.Policy.MaximumComponents)
            return Blocked(selection, CharacterCustomDrugBlockers.ComponentLimit);

        CharacterCustomDrugGrade[] gradeMatches = preparation.Grades
            .Where(candidate => candidate.Id == selection.GradeId)
            .Take(2)
            .ToArray();
        if (gradeMatches.Length != 1)
            return Blocked(selection, CharacterCustomDrugBlockers.InvalidIdentity);
        CharacterCustomDrugGrade grade = gradeMatches[0];

        Dictionary<CharacterCustomDrugComponentId, CharacterCustomDrugComponentSource[]> catalog = preparation.Components
            .GroupBy(component => component.Id)
            .ToDictionary(group => group.Key, group => group.Take(2).ToArray());
        var projected = new List<(CharacterCustomDrugComponentSource Source, CharacterCustomDrugEffectLevel Effect, int Level)>();
        foreach (CharacterCustomDrugComponentSelection? requested in selection.Components)
        {
            if (requested is null
                || requested.ComponentId.Value == Guid.Empty
                || !catalog.TryGetValue(requested.ComponentId, out CharacterCustomDrugComponentSource[]? sourceMatches)
                || sourceMatches.Length != 1)
            {
                return Blocked(selection, CharacterCustomDrugBlockers.ComponentUnavailable);
            }
            CharacterCustomDrugComponentSource source = sourceMatches[0];
            CharacterCustomDrugEffectLevel[] effects = source.Effects
                .Where(effect => effect.Level == requested.Level)
                .Take(2)
                .ToArray();
            if (effects.Length != 1)
                return Blocked(selection, CharacterCustomDrugBlockers.ComponentUnavailable);
            projected.Add((source, effects[0], requested.Level));
        }

        int foundations = projected.Count(item => item.Source.Category == CharacterCustomDrugComponentCategory.Foundation);
        if (foundations == 0)
            return Blocked(selection, CharacterCustomDrugBlockers.MissingFoundation);
        if (foundations > 1)
            return Blocked(selection, CharacterCustomDrugBlockers.DuplicateFoundation);
        foreach (IGrouping<CharacterCustomDrugComponentId, (CharacterCustomDrugComponentSource Source, CharacterCustomDrugEffectLevel Effect, int Level)> group
                 in projected.GroupBy(item => item.Source.Id))
        {
            int limit = group.First().Source.Limit;
            if (limit > 0 && group.Count() > limit)
                return Blocked(selection, CharacterCustomDrugBlockers.ComponentLimit);
        }

        CharacterCustomDrugEffectLevel foundationEffect = projected.Single(item =>
            item.Source.Category == CharacterCustomDrugComponentCategory.Foundation).Effect;
        Dictionary<string, decimal> foundationAttributes = foundationEffect.Attributes
            .GroupBy(effect => effect.Attribute, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal);
        foreach ((CharacterCustomDrugComponentSource source, CharacterCustomDrugEffectLevel effect, int level) in projected)
        {
            if (source.Category != CharacterCustomDrugComponentCategory.Block || level < 2)
                continue;
            if (effect.Attributes.Any(block => block.Value > 0m
                                               && foundationAttributes.TryGetValue(block.Attribute, out decimal foundation)
                                               && foundation < 0m))
            {
                return Blocked(selection, CharacterCustomDrugBlockers.FoundationConflict);
            }
        }

        try
        {
            decimal componentCost = 0m;
            int availability = 0;
            CharacterCustomDrugLegality legality = CharacterCustomDrugLegality.Legal;
            int addictionRating = 0;
            int addictionThreshold = 0;
            var selected = new List<CharacterCustomDrugSelectedComponent>(projected.Count);
            foreach ((CharacterCustomDrugComponentSource source, _, int level) in projected)
            {
                decimal levelFactor = preparation.Policy.MultiplyComponentCostByLevel
                    ? checked(level + 1m)
                    : 1m;
                decimal contribution = checked(source.CostPerLevel * levelFactor);
                componentCost = checked(componentCost + contribution);
                availability = checked(availability + source.AvailabilityModifier);
                legality = MostRestrictive(legality, source.Legality);
                addictionRating = checked(addictionRating + source.AddictionRating);
                addictionThreshold = checked(addictionThreshold + source.AddictionThreshold);
                selected.Add(new CharacterCustomDrugSelectedComponent(
                    source.Id,
                    source.Name,
                    source.Category,
                    level,
                    contribution,
                    source.AvailabilityModifier,
                    source.Legality,
                    source.SourceBook,
                    source.Page,
                    source.SourceAnchorIds));
            }
            availability = Math.Max(0, availability);
            if (preparation.Policy.ApplyGradeAddictionThresholdModifier)
                addictionThreshold = checked(addictionThreshold + grade.AddictionThresholdModifier);
            addictionThreshold = Math.Max(0, addictionThreshold);
            addictionRating = Math.Max(0, addictionRating);

            decimal unitCost = preparation.Policy.ApplyGradeCostMultiplier
                ? checked(componentCost * grade.CostMultiplier)
                : componentCost;
            decimal chargedCost = preparation.Purpose == CharacterCustomDrugQuotePurpose.RecipeDefinition
                ? 0m
                : checked(unitCost * selection.Quantity);
            if (preparation.Purpose == CharacterCustomDrugQuotePurpose.QuantityPurchase)
            {
                if (selection.MarkupPercent != 0m)
                    chargedCost = checked(chargedCost * (1m + selection.MarkupPercent / 100m));
                if (selection.FreeCost)
                    chargedCost = 0m;
            }
            if (chargedCost < 0m || chargedCost > preparation.AvailableNuyen)
                return Blocked(selection, CharacterCustomDrugBlockers.InsufficientFunds);

            CharacterCustomDrugAggregateEffects effects = Aggregate(projected.Select(item => item.Effect));
            CharacterCustomDrugSelectedComponent[] ordered = selected
                .OrderBy(item => item.ComponentId.Value)
                .ThenBy(item => item.Level)
                .ToArray();
            var unsigned = new CharacterCustomDrugQuote(
                Exact: true,
                BlockReason: string.Empty,
                selection.Name.Trim(),
                selection.GradeId,
                grade.Name,
                selection.Quantity,
                componentCost,
                unitCost,
                chargedCost,
                NuyenDelta: -chargedCost,
                availability,
                legality,
                addictionRating,
                addictionThreshold,
                effects,
                ordered,
                QuoteDigest: string.Empty);
            return unsigned with { QuoteDigest = ComputeQuoteDigest(preparation, selection, unsigned) };
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            return Blocked(selection, CharacterCustomDrugBlockers.ArithmeticOverflow);
        }
    }

    public static bool IsValidPreparation(CharacterCustomDrugPreparation? preparation)
    {
        if (preparation is null
            || !preparation.Exact
            || preparation.Blockers.Count != 0
            || preparation.ContentRevision < 0
            || !Enum.IsDefined(preparation.Context)
            || !Enum.IsDefined(preparation.Purpose)
            || preparation.AvailableNuyen < 0m
            || string.IsNullOrWhiteSpace(preparation.SettingsProfileId)
            || !IsCanonicalDigest(preparation.CharacterDigest)
            || !IsCanonicalDigest(preparation.CatalogDigest)
            || !IsCanonicalDigest(preparation.RulesDigest)
            || preparation.Policy is null
            || preparation.Policy.MaximumComponents is < 1 or > 256
            || preparation.Policy.MaximumQuantity <= 0m
            || preparation.Policy.QuantityDecimalPlaces is < 0 or > 6
            || preparation.Components is not { Count: > 0 and <= 4096 }
            || preparation.Grades is not { Count: > 0 and <= 128 })
            return false;
        if (preparation.Components.Select(item => item.Id).Distinct().Count() != preparation.Components.Count
            || preparation.Grades.Select(item => item.Id).Distinct().Count() != preparation.Grades.Count)
            return false;
        return preparation.Components.All(IsValidComponent) && preparation.Grades.All(IsValidGrade);
    }

    public static bool IsValidCatalogAuthority(CharacterCustomDrugCatalogAuthority? authority)
    {
        if (authority is null
            || !authority.Exact
            || authority.Blockers.Count != 0
            || !string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || !IsCanonicalDigest(authority.CatalogDigest)
            || !IsCanonicalDigest(authority.RulesDigest)
            || authority.Policy is null
            || authority.Policy.MaximumComponents is < 1 or > 256
            || authority.Policy.MaximumQuantity <= 0m
            || authority.Policy.QuantityDecimalPlaces is < 0 or > 6
            || authority.Components is not { Count: > 0 and <= 4096 }
            || authority.Grades is not { Count: > 0 and <= 128 }
            || authority.Components.Select(item => item.Id).Distinct().Count() != authority.Components.Count
            || authority.Grades.Select(item => item.Id).Distinct().Count() != authority.Grades.Count
            || authority.Components.Any(item => !IsValidComponent(item))
            || authority.Grades.Any(item => !IsValidGrade(item)))
            return false;
        return FixedEquals(authority.RulesDigest, ComputeRulesDigest(authority.Policy))
               && FixedEquals(authority.CatalogDigest, ComputeCatalogDigest(
                   authority.RulesetId,
                   authority.SettingsProfileId,
                   authority.RulesDigest,
                   authority.Grades,
                   authority.Components));
    }

    public static string ComputeRulesDigest(CharacterCustomDrugCalculationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        string canonical = string.Join(
            "\n",
            "sr5-custom-drug-rules-v1",
            policy.MultiplyComponentCostByLevel.ToString(CultureInfo.InvariantCulture),
            policy.ApplyGradeCostMultiplier.ToString(CultureInfo.InvariantCulture),
            policy.ApplyGradeAddictionThresholdModifier.ToString(CultureInfo.InvariantCulture),
            policy.MaximumComponents.ToString(CultureInfo.InvariantCulture),
            policy.MaximumQuantity.ToString(CultureInfo.InvariantCulture),
            policy.QuantityDecimalPlaces.ToString(CultureInfo.InvariantCulture),
            CharacterCustomDrugLegacyAuthority.DrugComponentsXmlSha256,
            CharacterCustomDrugLegacyAuthority.CreateCustomDrugSha256,
            CharacterCustomDrugLegacyAuthority.DrugsSha256,
            CharacterCustomDrugLegacyAuthority.CharacterCreateSha256,
            CharacterCustomDrugLegacyAuthority.CharacterCareerSha256);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string ComputeCatalogDigest(
        string rulesetId,
        string settingsProfileId,
        string rulesDigest,
        IReadOnlyList<CharacterCustomDrugGrade> grades,
        IReadOnlyList<CharacterCustomDrugComponentSource> components)
    {
        var canonical = new StringBuilder("sr5-custom-drug-catalog-v1\n")
            .Append(rulesetId).Append('\n')
            .Append(settingsProfileId).Append('\n')
            .Append(rulesDigest).Append('\n');
        foreach (CharacterCustomDrugGrade grade in grades.OrderBy(item => item.Id.Value))
        {
            canonical
                .Append("grade|").Append(grade.Id.Value.ToString("D")).Append('|')
                .Append(grade.Name).Append('|')
                .Append(grade.CostMultiplier.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(grade.AddictionThresholdModifier.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(grade.SourceBook).Append('|')
                .Append(grade.SourceNodeDigest).Append('\n');
            foreach (string anchor in grade.SourceAnchorIds.OrderBy(value => value, StringComparer.Ordinal))
                canonical.Append("anchor|").Append(anchor).Append('\n');
        }
        foreach (CharacterCustomDrugComponentSource component in components.OrderBy(item => item.Id.Value))
        {
            canonical
                .Append("component|").Append(component.Id.Value.ToString("D")).Append('|')
                .Append(component.Name).Append('|')
                .Append(component.Category).Append('|')
                .Append(component.Limit.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.AvailabilityModifier.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.Legality).Append('|')
                .Append(component.CostPerLevel.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.AddictionRating.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.AddictionThreshold.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.SourceBook).Append('|')
                .Append(component.Page).Append('|')
                .Append(component.SourceNodeDigest).Append('\n');
            foreach (string anchor in component.SourceAnchorIds.OrderBy(value => value, StringComparer.Ordinal))
                canonical.Append("anchor|").Append(anchor).Append('\n');
            foreach (CharacterCustomDrugEffectLevel effect in component.Effects.OrderBy(item => item.Level))
            {
                canonical
                    .Append("effect|").Append(effect.Level.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(effect.Initiative.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(effect.InitiativeDice.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(effect.CrashDamage.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(effect.Speed.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(effect.Duration.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (CharacterCustomDrugAttributeEffect value in effect.Attributes.OrderBy(item => item.Attribute, StringComparer.Ordinal))
                    canonical.Append("attribute|").Append(value.Attribute).Append('|').Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (CharacterCustomDrugLimitEffect value in effect.Limits.OrderBy(item => item.Limit, StringComparer.Ordinal))
                    canonical.Append("limit|").Append(value.Limit).Append('|').Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (CharacterCustomDrugQualityEffect value in effect.Qualities.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Rating))
                    canonical.Append("quality|").Append(value.Name).Append('|').Append(value.Rating.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (string value in effect.Information.OrderBy(item => item, StringComparer.Ordinal))
                    canonical.Append("info|").Append(value).Append('\n');
            }
        }
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static bool IsCanonicalDigest(string? digest)
        => digest is { Length: 64 } && digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string ComputeCharacterDigest(string characterXml)
        => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(characterXml ?? string.Empty)));

    public static string ComputeCommandDigest(CharacterCustomDrugCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var canonical = new StringBuilder("custom-drug-recipe-command-v2\n")
            .Append(command.ExpectedContentRevision.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(command.ExpectedCharacterDigest).Append('\n')
            .Append(command.ExpectedCatalogDigest).Append('\n')
            .Append(command.ExpectedRulesDigest).Append('\n')
            .Append(command.ExpectedQuoteDigest).Append('\n')
            .Append(command.IdempotencyKey).Append('\n')
            .Append(command.NewDrugInstanceId.Value.ToString("D")).Append('\n');
        AppendSelection(canonical, command.Selection);
        foreach (Guid value in command.NewComponentInstanceIds)
            canonical.Append(value.ToString("D")).Append('\n');
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static string ComputeReceiptDigest(CharacterCustomDrugCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var canonical = new StringBuilder("custom-drug-recipe-receipt-v2\n")
            .Append(receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(receipt.PreviousCharacterDigest).Append('\n')
            .Append(receipt.CharacterDigest).Append('\n')
            .Append(receipt.CatalogDigest).Append('\n')
            .Append(receipt.RulesDigest).Append('\n')
            .Append(receipt.QuoteDigest).Append('\n')
            .Append(receipt.CommandDigest).Append('\n')
            .Append(receipt.IdempotencyKeyDigest).Append('\n')
            .Append(receipt.DrugInstanceId.Value.ToString("D")).Append('\n')
            .Append(receipt.DrugXmlDigest).Append('\n');
        foreach (Guid value in receipt.ComponentInstanceIds)
            canonical.Append(value.ToString("D")).Append('\n');
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static string ComputeIdempotencyKeyDigest(string value)
        => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static bool IsValidComponent(CharacterCustomDrugComponentSource? source)
    {
        if (source is null
            || source.Id.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(source.Name)
            || source.Name.Length > MaximumNameLength
            || source.Limit < 0
            || source.AvailabilityModifier < 0
            || source.CostPerLevel < 0m
            || source.AddictionRating < 0
            || source.AddictionThreshold < 0
            || string.IsNullOrWhiteSpace(source.SourceBook)
            || string.IsNullOrWhiteSpace(source.Page)
            || !IsCanonicalDigest(source.SourceNodeDigest)
            || source.SourceAnchorIds is not { Count: > 0 }
            || source.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || source.Effects is not { Count: > 0 and <= 64 })
            return false;
        return source.Effects.Select(effect => effect.Level).Distinct().Count() == source.Effects.Count
               && source.Effects.All(effect => effect.Level is >= 0 and <= 63
                                               && effect.Attributes.All(item => IsKey(item.Attribute))
                                               && effect.Limits.All(item => IsKey(item.Limit))
                                               && effect.Qualities.All(item => IsKey(item.Name) && item.Rating >= 0)
                                               && effect.Information.All(IsBoundedText));
    }

    private static bool IsValidGrade(CharacterCustomDrugGrade? grade)
        => grade is not null
           && grade.Id.Value != Guid.Empty
           && IsKey(grade.Name)
           && grade.CostMultiplier >= 0m
           && !string.IsNullOrWhiteSpace(grade.SourceBook)
           && IsCanonicalDigest(grade.SourceNodeDigest)
           && grade.SourceAnchorIds is { Count: > 0 }
           && grade.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor));

    private static bool IsValidName(string? value)
        => value is not null && IsBoundedText(value.Trim());

    private static bool IsKey(string? value)
        => value is { Length: > 0 and <= MaximumNameLength }
           && value.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static bool IsBoundedText(string? value)
        => value is { Length: > 0 and <= 512 }
           && value.IndexOf('\0') < 0;

    private static bool IsValidQuantity(decimal quantity, CharacterCustomDrugCalculationPolicy policy)
        => quantity > 0m
           && quantity <= policy.MaximumQuantity
           && decimal.Round(quantity, policy.QuantityDecimalPlaces, MidpointRounding.AwayFromZero) == quantity;

    private static bool IsValidMarkup(decimal markup)
        => markup >= MinimumMarkupPercent
           && markup <= MaximumMarkupPercent
           && decimal.Round(markup, MarkupDecimalPlaces, MidpointRounding.AwayFromZero) == markup;

    private static CharacterCustomDrugLegality MostRestrictive(
        CharacterCustomDrugLegality left,
        CharacterCustomDrugLegality right)
        => (CharacterCustomDrugLegality)Math.Max((int)left, (int)right);

    private static CharacterCustomDrugAggregateEffects Aggregate(
        IEnumerable<CharacterCustomDrugEffectLevel> selected)
    {
        CharacterCustomDrugEffectLevel[] effects = selected.ToArray();
        CharacterCustomDrugAttributeEffect[] attributes = effects
            .SelectMany(effect => effect.Attributes)
            .GroupBy(effect => effect.Attribute, StringComparer.Ordinal)
            .Select(group => new CharacterCustomDrugAttributeEffect(group.Key, group.Sum(item => item.Value)))
            .Where(effect => effect.Value != 0m)
            .OrderBy(effect => effect.Attribute, StringComparer.Ordinal)
            .ToArray();
        CharacterCustomDrugLimitEffect[] limits = effects
            .SelectMany(effect => effect.Limits)
            .GroupBy(effect => effect.Limit, StringComparer.Ordinal)
            .Select(group => new CharacterCustomDrugLimitEffect(group.Key, group.Sum(item => item.Value)))
            .Where(effect => effect.Value != 0)
            .OrderBy(effect => effect.Limit, StringComparer.Ordinal)
            .ToArray();
        CharacterCustomDrugQualityEffect[] qualities = effects
            .SelectMany(effect => effect.Qualities)
            .OrderBy(effect => effect.Name, StringComparer.Ordinal)
            .ThenBy(effect => effect.Rating)
            .ToArray();
        string[] information = effects
            .SelectMany(effect => effect.Information)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new CharacterCustomDrugAggregateEffects(
            attributes,
            limits,
            qualities,
            information,
            effects.Sum(effect => effect.Initiative),
            effects.Sum(effect => effect.InitiativeDice),
            effects.Sum(effect => effect.CrashDamage),
            effects.Sum(effect => effect.Speed),
            effects.Sum(effect => effect.Duration));
    }

    private static CharacterCustomDrugQuote Blocked(
        CharacterCustomDrugSelection selection,
        string reason)
        => new(
            Exact: false,
            BlockReason: string.IsNullOrWhiteSpace(reason) ? CharacterCustomDrugBlockers.AuthorityUnavailable : reason,
            Name: selection.Name?.Trim() ?? string.Empty,
            selection.GradeId,
            GradeName: string.Empty,
            selection.Quantity,
            ComponentCost: 0m,
            UnitCost: 0m,
            ChargedCost: 0m,
            NuyenDelta: 0m,
            Availability: 0,
            CharacterCustomDrugLegality.Legal,
            AddictionRating: 0,
            AddictionThreshold: 0,
            s_EmptyEffects,
            Components: [],
            QuoteDigest: string.Empty);

    private static string ComputeQuoteDigest(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection,
        CharacterCustomDrugQuote quote)
    {
        var canonical = new StringBuilder("custom-drug-quote-v2\n")
            .Append(preparation.Context).Append('\n')
            .Append(preparation.Purpose).Append('\n')
            .Append(preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(preparation.CharacterDigest).Append('\n')
            .Append(preparation.CatalogDigest).Append('\n')
            .Append(preparation.RulesDigest).Append('\n')
            .Append(preparation.SettingsProfileId).Append('\n');
        AppendSelection(canonical, selection);
        canonical
            .Append(quote.Name).Append('\n')
            .Append(quote.GradeName).Append('\n')
            .Append(quote.ComponentCost.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.UnitCost.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.ChargedCost.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.NuyenDelta.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Availability.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Legality).Append('\n')
            .Append(quote.AddictionRating.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.AddictionThreshold.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Effects.Initiative.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Effects.InitiativeDice.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Effects.CrashDamage.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Effects.Speed.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(quote.Effects.Duration.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (CharacterCustomDrugAttributeEffect effect in quote.Effects.Attributes)
            canonical.Append("attribute|").Append(effect.Attribute).Append('|').Append(effect.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (CharacterCustomDrugLimitEffect effect in quote.Effects.Limits)
            canonical.Append("limit|").Append(effect.Limit).Append('|').Append(effect.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (CharacterCustomDrugQualityEffect effect in quote.Effects.Qualities)
            canonical.Append("quality|").Append(effect.Name).Append('|').Append(effect.Rating.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string value in quote.Effects.Information)
            canonical.Append("info|").Append(value).Append('\n');
        foreach (CharacterCustomDrugSelectedComponent component in quote.Components)
        {
            canonical
                .Append(component.ComponentId.Value.ToString("D")).Append('|')
                .Append(component.Level.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.CostContribution.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.AvailabilityContribution.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.Legality).Append('\n');
        }
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendSelection(StringBuilder canonical, CharacterCustomDrugSelection selection)
    {
        canonical
            .Append(selection.Name?.Trim() ?? string.Empty).Append('\n')
            .Append(selection.GradeId.Value.ToString("D")).Append('\n')
            .Append(selection.Quantity.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(selection.Stolen).Append('\n')
            .Append(selection.FreeCost).Append('\n')
            .Append(selection.MarkupPercent.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (CharacterCustomDrugComponentSelection component in selection.Components
                     .OrderBy(item => item.ComponentId.Value)
                     .ThenBy(item => item.Level))
        {
            canonical.Append(component.ComponentId.Value.ToString("D")).Append('|')
                .Append(component.Level.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private static bool FixedEquals(string left, string right)
    {
        if (left.Length != right.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }
}
