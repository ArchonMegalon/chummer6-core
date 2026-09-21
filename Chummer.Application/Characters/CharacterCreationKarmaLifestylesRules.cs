using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Composes the existing source-owned lifestyle projection with the exact Karma
/// resource and gear quotes. Starting cash is deliberately not purchasing power.
/// Service admission must additionally re-resolve the current source authority.
/// </summary>
public static class CharacterCreationKarmaLifestylesRules
{
    public const int MaximumSelections = 4096;
    public const string StartingLifestyleRequired = "creation-karma-starting-lifestyle-required";

    public static CharacterCreationKarmaMetatypeQuote WithoutLifestyles(CharacterCreationKarmaMetatypeQuote foundation)
    {
        if (foundation.Lifestyles is null) return foundation;
        var result = foundation with { Lifestyles = null, QuoteDigest = string.Empty };
        return result with { QuoteDigest = Digest(result) };
    }

    public static bool TryEffectiveAuthority(CharacterCreationLifestylesAuthority authority,
        CharacterCreationKarmaMetatypeQuote foundation, CharacterCreationKarmaLifestyleEffects effects,
        out CharacterCreationLifestylesAuthority effective)
    {
        effective = CharacterCreationLifestylesAuthority.Unavailable;
        try
        {
            if (!CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || foundation.Attributes?.QuoteDigest != effects.AttributesQuoteDigest
                || foundation.Qualities?.QuoteDigest != effects.QualitiesQuoteDigest
                || !CharacterCreationKarmaEffectsProjector.TryProject(WithoutLifestyles(foundation),
                    effects.RacialSources, effects.TalentSource, out var improvements)
                || !CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts(improvements.Elements("improvement"),
                    authority.TrustFundLevel, out int level, out decimal metatypeCostPercent, out _)) return false;
            var result = authority with
            {
                TrustFundLevel = level,
                MetatypeCostPercent = checked(authority.MetatypeCostPercent + metatypeCostPercent),
                GmPolicyDigest = Digest(new { authority.GmPolicyDigest, Effects = effects, TrustFundLevel = level }),
                AuthorityDigest = string.Empty
            };
            effective = result with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(result) };
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Xml.XmlException or OverflowException)
        { return false; }
    }

    public static CharacterCreationKarmaLifestylesQuote? EvaluateForFoundation(
        CharacterCreationLifestylesAuthority authority, CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationTalentQualitySource> racialSources, CharacterCreationTalentQualitySource? talentSource,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> selections, Guid? startingLifestyleId)
    {
        if (foundation is not { Lifestyles: null, Attributes: not null, Qualities: not null,
                Resources: not null, Gear: not null } || racialSources is null) return null;
        var effects = new CharacterCreationKarmaLifestyleEffects(foundation.Attributes.QuoteDigest,
            foundation.Qualities.QuoteDigest, racialSources.ToArray(), talentSource);
        if (!TryEffectiveAuthority(authority, foundation, effects, out var effective)) return null;
        var quote = Evaluate(effective, foundation.Resources, foundation.Gear, selections, startingLifestyleId);
        if (quote is null) return null;
        quote = quote with { SourceAuthorityDigest = authority.AuthorityDigest,
            ProjectionAuthority = ProjectionAuthority(authority, selections), Effects = effects, QuoteDigest = string.Empty };
        return quote with { QuoteDigest = Digest(quote) };
    }

    public static bool IsValidForFoundation(CharacterCreationKarmaMetatypeQuote foundation,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? selections, Guid? startingLifestyleId)
    {
        if (foundation.Lifestyles is not { } quote) return selections is null && startingLifestyleId is null;
        return quote is { Schema: CharacterCreationKarmaLifestylesQuote.SchemaV1, CanSelect: true, Effects: { } effects }
            && selections is not null && foundation.Binding is not null && quote.ProjectionAuthority is not null
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(quote.SourceAuthorityDigest)
            && foundation.Binding.LifestylesAuthorityDigest == quote.SourceAuthorityDigest
            && foundation.Binding.SourceProfileDigest == quote.ProjectionAuthority.ProfileDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote,
                WithSourceAuthority(EvaluateForFoundation(quote.ProjectionAuthority, WithoutLifestyles(foundation), effects.RacialSources,
                    effects.TalentSource, selections, startingLifestyleId), quote.SourceAuthorityDigest));
    }

    // A draft retains the source authority hash, all base tiers, and only the
    // qualities needed for its selections/built-ins. Unrelated quality rows
    // must not consume the bounded decision ledger on every save. This is a
    // projection basis, never a replacement for full source admission at load.
    public static CharacterCreationLifestylesAuthority ProjectionAuthority(CharacterCreationLifestylesAuthority authority,
        IReadOnlyList<CharacterCreationLifestyleConfiguration> selections)
    {
        var needed = selections.SelectMany(row => row.Qualities).Where(row => !row.IsBuiltIn).Select(row => row.OptionId)
            .Concat(authority.LifestyleOptions.SelectMany(row => row.BuiltInQualities).Select(row => row.QualityOptionId))
            .ToHashSet(StringComparer.Ordinal);
        var projection = authority with
        {
            QualityOptions = authority.QualityOptions.Where(row => needed.Contains(row.OptionId)).ToArray(),
            AuthorityDigest = string.Empty
        };
        return projection with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(projection) };
    }

    private static CharacterCreationKarmaLifestylesQuote? WithSourceAuthority(CharacterCreationKarmaLifestylesQuote? quote,
        string digest)
    {
        if (quote is null) return null;
        var result = quote with { SourceAuthorityDigest = digest, QuoteDigest = string.Empty };
        return result with { QuoteDigest = Digest(result) };
    }

    public static bool TryFreeze(IReadOnlyList<CharacterCreationLifestyleConfiguration>? selections,
        out CharacterCreationLifestyleConfiguration[] frozen)
    {
        frozen = [];
        if (selections is null || selections.Count > MaximumSelections) return false;
        var copy = selections.Take(MaximumSelections + 1).ToArray();
        if (copy.Length != selections.Count || copy.Length > MaximumSelections) return false;
        var identities = new HashSet<Guid>();
        for (int i = 0; i < copy.Length; i++)
        {
            var item = copy[i];
            if (item is null || item.LifestyleId == Guid.Empty || !identities.Add(item.LifestyleId)
                || item.Qualities is null || item.Qualities.Count > MaximumSelections) return false;
            var qualities = item.Qualities.Take(MaximumSelections + 1).ToArray();
            if (qualities.Length != item.Qualities.Count || qualities.Length > MaximumSelections
                || qualities.Any(quality => quality is null || quality.InstanceId == Guid.Empty
                    || !identities.Add(quality.InstanceId))) return false;
            copy[i] = item with { Qualities = qualities.OrderBy(quality => quality.InstanceId).ToArray() };
        }
        frozen = copy.OrderBy(item => item.LifestyleId).ToArray();
        return true;
    }

    public static CharacterCreationKarmaLifestylesQuote? Evaluate(
        CharacterCreationLifestylesAuthority authority, CharacterCreationKarmaResourcesQuote resources,
        CharacterCreationKarmaGearQuote gear, IReadOnlyList<CharacterCreationLifestyleConfiguration> selections,
        Guid? startingLifestyleId)
    {
        try
        {
            if (!TryFreeze(selections, out var frozen)
                || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
                || resources?.Policy is null || gear?.Lines is null || gear.Lines.Any(line => line is null)
                || authority.SettingsProfileId != resources.Policy.SettingsProfileId
                || authority.ProfileDigest != resources.Policy.RawProfileInputsDigest
                || !CharacterCreationKarmaGearRules.IsValid(gear, resources,
                    gear.Lines.Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray()))
                return null;

            var projections = new List<CharacterCreationLifestyleProjection>(frozen.Length);
            var blockers = new List<string>();
            decimal lifestyleCost = 0;
            foreach (var configuration in frozen)
            {
                if (!CharacterCreationLifestylesRules.TryProject(configuration, authority, out var projected, out _))
                    return null;
                projections.Add(projected);
                lifestyleCost = checked(lifestyleCost + projected.Economics.TotalCost);
            }
            // Selecting another source row must not manufacture a lifestyle or
            // fund creation from its future starting-cash roll.
            if (frozen.Length == 0 ? startingLifestyleId is not null
                : startingLifestyleId is null || frozen.All(item => item.LifestyleId != startingLifestyleId))
                blockers.Add(StartingLifestyleRequired);

            decimal used = checked(gear.Budget.BasketCost + lifestyleCost);
            decimal remaining = checked(resources.NuyenFromKarma - used);
            if (remaining < 0) blockers.Add(CharacterCreationLifestylesBlockers.InsufficientFunds);
            string[] normalized = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            string[] anchors = authority.SourceAnchorIds.Concat(projections.SelectMany(line => line.SourceAnchorIds))
                .Concat(gear.Lines.SelectMany(line => line.SourceAnchorIds))
                .Concat(resources.Policy.SourceAnchorIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var budget = new CharacterCreationLifestyleBudget(resources.NuyenFromKarma, used, remaining,
                Math.Max(0, -remaining), normalized.Length == 0, normalized, anchors);
            var quote = new CharacterCreationKarmaLifestylesQuote(CharacterCreationKarmaLifestylesQuote.SchemaV1,
                authority.AuthorityDigest, ProjectionAuthority(authority, frozen), resources.QuoteDigest, gear.QuoteDigest,
                projections.ToArray(), startingLifestyleId,
                lifestyleCost, budget, normalized, string.Empty);
            return quote with { QuoteDigest = Digest(quote) };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Historical validation; current source re-admission remains mandatory at commit.</summary>
    public static bool IsValid(CharacterCreationKarmaLifestylesQuote? quote,
        CharacterCreationKarmaResourcesQuote? resources, CharacterCreationKarmaGearQuote? gear,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? selections, Guid? startingLifestyleId)
        => quote is null ? selections is null && startingLifestyleId is null
            : quote is { Schema: CharacterCreationKarmaLifestylesQuote.SchemaV1, CanSelect: true, Effects: null }
                && resources is not null && gear is not null && selections is not null
                && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(quote.SourceAuthorityDigest)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote,
                    WithSourceAuthority(Evaluate(quote.ProjectionAuthority, resources, gear, selections, startingLifestyleId),
                        quote.SourceAuthorityDigest));

    private static string Digest<T>(T value)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
