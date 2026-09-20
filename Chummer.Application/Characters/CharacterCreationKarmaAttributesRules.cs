using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Attribute allocation on a Karma foundation with no other attribute-affecting
/// qualities, improvements or Priority allocations. Later domains must extend this
/// authority, not silently ignore their modifiers when recomputing this budget.
/// </summary>
public static class CharacterCreationKarmaAttributesRules
{
    private static readonly string[] Normal = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL"];
    private static readonly string[] Special = ["EDG", "MAG", "RES", "ESS", "DEP"];

    public static bool IsAllocationShape(IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? allocations)
        => allocations is { Count: <= 13 }
            && allocations.All(item => item is not null && item.KarmaLevels >= 0
                && (Normal.Contains(item.AttributeId, StringComparer.Ordinal)
                    || Special.Contains(item.AttributeId, StringComparer.Ordinal)))
            && allocations.Select(item => item.AttributeId).Distinct(StringComparer.Ordinal).Count() == allocations.Count;

    public static CharacterCreationKarmaAttributesQuote? Evaluate(
        CharacterCreationMetatypeOptionProjection metatype, CharacterCreationKarmaTalentOption talent,
        CharacterCreationAttributePolicy policy, IReadOnlyList<CharacterCreationKarmaAttributeAllocation> allocations)
    {
        try
        {
            if (allocations is null || allocations.Count > 13) return null;
            allocations = allocations.Take(14).ToArray();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return null;
        }
        if (policy is null || policy.Schema != CharacterCreationAttributePolicy.SchemaV1
            || policy.BuildMethod != CharacterCreationBuildMethods.Karma || policy.KarmaAttribute <= 0
            || policy.MaxNumberMaxAttributesCreate < 0 || policy.SourceAnchorIds is not { Count: > 0 }
            || policy.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(policy)
            || metatype is not { IsEnabled: true, Attributes.Count: 13 }
            || metatype.Attributes.Any(item => item is null)
            || metatype.SourceAnchorIds is not { Count: > 0 }
            || !metatype.Attributes.Select(item => item.AttributeId).Order(StringComparer.Ordinal)
                .SequenceEqual(Normal.Concat(Special).Order(StringComparer.Ordinal))
            || metatype.Attributes.Any(item => item.Minimum < 0 || item.Maximum < item.Minimum
                || item.AugmentedMaximum < item.Maximum)
            || talent is not { IsEnabled: true, Blockers.Count: 0 }
            || talent.SourceAnchorIds is not { Count: > 0 }
            || !CharacterCreationKarmaTalentAuthority.IsCompatible(talent, metatype)
            || !IsAllocationShape(allocations))
            return null;

        var blockers = new HashSet<string>(StringComparer.Ordinal);
        var projected = new List<CharacterCreationAttributeProjection>();
        decimal used = 0;
        foreach (string id in Normal.Concat(Special))
        {
            var range = metatype.Attributes.Single(item => item.AttributeId == id);
            bool normal = Normal.Contains(id, StringComparer.Ordinal);
            bool essence = id == "ESS";
            bool enabled = normal || id == "EDG" || id == talent.EnabledAttribute;
            int levels = allocations.SingleOrDefault(item => item.AttributeId == id)?.KarmaLevels ?? 0;
            int minimum = enabled || essence ? range.Minimum : 0;
            int maximum = enabled || essence ? range.Maximum : 0;
            int augmentedMaximum = enabled || essence ? range.AugmentedMaximum : 0;
            int current = essence ? maximum : minimum;
            int cost = 0;
            if (!enabled && levels != 0)
                blockers.Add(CharacterCreationAttributesBlockers.AttributeDisabled);
            if (enabled)
            {
                long requestedRating = (long)minimum + levels;
                if (requestedRating > maximum)
                    blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                current = (int)Math.Min(requestedRating, int.MaxValue);
                // Chummer5 TotalKarmaCost uses FreeBase + 1 + MinimumModifiers
                // for alternate metatype costs. None exist in this foundation;
                // no Priority points means reverse-order cannot change the base.
                int costBase = policy.AlternateMetatypeAttributeKarma ? 1 : minimum;
                if (!CharacterCreationAttributeCostRules.TryCalculate(costBase, levels, policy.KarmaAttribute, out cost))
                    blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                used += cost;
            }
            string[] reasons = enabled ? [] : essence
                ? [CharacterCreationAttributesBlockers.EssenceNotSpendable]
                : [CharacterCreationAttributesBlockers.SpecialAttributeNotEnabled];
            projected.Add(new(id, normal ? CharacterCreationAttributeCategories.Normal : CharacterCreationAttributeCategories.Special,
                minimum, maximum, augmentedMaximum, current, 0, levels, 0, cost, enabled, reasons,
                metatype.SourceAnchorIds.Concat(policy.SourceAnchorIds)
                    .Concat(id == talent.EnabledAttribute ? talent.SourceAnchorIds : [])
                    .Append($"metatypes.xml#metatype:{metatype.OptionId}:attribute:{id}")
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }
        if (projected.Count(item => item.Category == CharacterCreationAttributeCategories.Normal
                && item.Current == item.Maximum) > policy.MaxNumberMaxAttributesCreate)
            blockers.Add(CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
        var quote = new CharacterCreationKarmaAttributesQuote(CharacterCreationKarmaAttributesQuote.SchemaV1,
            policy, allocations.OrderBy(item => item.AttributeId, StringComparer.Ordinal).ToArray(), projected.ToArray(),
            used, blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
        return quote with { QuoteDigest = Digest(quote) };
    }

    public static bool IsValid(CharacterCreationKarmaAttributesQuote? quote,
        CharacterCreationMetatypeOptionProjection metatype, CharacterCreationKarmaTalentOption? talent,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? allocations)
        => quote is null ? allocations is null : talent is not null && allocations is not null
            && quote is { Schema: CharacterCreationKarmaAttributesQuote.SchemaV1, Blockers.Count: 0 }
            && Evaluate(metatype, talent, quote.Policy, allocations) is { } expected
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, quote);

    private static string Digest(CharacterCreationKarmaAttributesQuote quote)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty });
}
