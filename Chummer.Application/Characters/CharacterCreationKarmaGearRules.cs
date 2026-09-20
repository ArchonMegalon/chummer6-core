using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationKarmaGearRules
{
    public static bool TryFreeze(IReadOnlyList<CharacterCreationGearSelection>? selections,
        out CharacterCreationGearSelection[] frozen)
    {
        frozen = [];
        if (selections is null || selections.Count > 4096) return false;
        var copy = selections.Take(4097).ToArray();
        if (copy.Length != selections.Count || copy.Any(item => item is null
                || item.Quantity is < 1 or > 1_000_000 || !IsOptionId(item.OptionId))
            || copy.Select(item => item.OptionId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            return false;
        frozen = copy;
        return true;
    }

    public static CharacterCreationKarmaGearQuote? Evaluate(CharacterCreationGearAuthority authority,
        CharacterCreationKarmaResourcesQuote resources, IReadOnlyList<CharacterCreationGearSelection> selections)
    {
        if (!TryFreeze(selections, out var frozen) || authority is null
            || !ValidResources(resources, allowBlocked: true) || resources.Policy.SettingsProfileId != authority.SettingsProfileId
            || resources.Policy.RawProfileInputsDigest != authority.ProfileDigest) return null;
        // TryProjectBasket admits the complete catalog itself. Do not hash all
        // 1,000+ rows twice in this one evaluation; a rejected authority still
        // returns no quote, never a fabricated empty basket.
        CharacterCreationGearRules.TryProjectBasket(frozen, authority, resources.NuyenFromKarma,
            out var lines, out var budget, out var blockers);
        if (blockers.Contains(CharacterCreationGearBlockers.AuthorityUnavailable, StringComparer.Ordinal)) return null;
        blockers = blockers.Concat(resources.Blockers).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        budget = budget with { IsExact = blockers.Length == 0, Blockers = blockers };
        var basis = new CharacterCreationKarmaGearBasis(authority.SettingsProfileId, authority.ProfileDigest,
            authority.SourceDigest, authority.RulesDigest, authority.RuntimeDigest, authority.AuthorityDigest,
            authority.MaximumAvailability, authority.MaximumBasketLines, authority.MaximumQuantityPerLine);
        var result = new CharacterCreationKarmaGearQuote(CharacterCreationKarmaGearQuote.SchemaV1, basis,
            resources.QuoteDigest, lines, budget, blockers, string.Empty);
        return result with { QuoteDigest = Hash(result) };
    }

    /// <summary>Intrinsic history validation only. Commit/load also re-admit the current full source catalog.</summary>
    public static bool IsValid(CharacterCreationKarmaGearQuote? quote, CharacterCreationKarmaResourcesQuote? resources,
        IReadOnlyList<CharacterCreationGearSelection>? selections)
    {
        if (quote is null) return selections is null;
        if (!TryFreeze(selections, out var frozen) || !ValidResources(resources)
            || quote is not { Schema: CharacterCreationKarmaGearQuote.SchemaV1, Blockers.Count: 0,
                Basis: { MaximumAvailability: >= 0, MaximumBasketLines: >= 1 and <= 4096,
                    MaximumQuantityPerLine: >= 1 and <= 1_000_000 } basis, Lines: not null }
            || basis.SettingsProfileId != resources!.Policy.SettingsProfileId
            || basis.ProfileDigest != resources.Policy.RawProfileInputsDigest
            || !Digest(basis.ProfileDigest) || !Digest(basis.SourceDigest) || !Digest(basis.RulesDigest)
            || !Digest(basis.RuntimeDigest) || !Digest(basis.AuthorityDigest)
            || quote.ResourcesQuoteDigest != resources.QuoteDigest
            || quote.Lines.Count != frozen.Length || quote.Lines.Count > basis.MaximumBasketLines
            || quote.QuoteDigest != Hash(quote with { QuoteDigest = string.Empty })) return false;
        var sorted = frozen.OrderBy(item => item.OptionId, StringComparer.Ordinal).ToArray();
        decimal cost = 0m;
        try
        {
            for (int i = 0; i < sorted.Length; i++)
            {
                var line = quote.Lines[i];
                if (line is null || line.OptionId != sorted[i].OptionId || line.Quantity != sorted[i].Quantity
                    || line.OptionId != $"gear:{line.SourceId:D}" || line.Quantity > basis.MaximumQuantityPerLine
                    || line.Availability > basis.MaximumAvailability || line.SourceAnchorIds is not { Count: > 0 }
                    || line.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
                    || line.LineDigest != CharacterCreationGearRules.ComputeLineDigest(line)
                    || !CharacterCreationLegacySourceProjector.TryBuildGear(line, quote.QuoteDigest, out _)) return false;
                cost = checked(cost + line.TotalCost);
            }
            if (cost > resources.NuyenFromKarma) return false;
            var expected = new CharacterCreationGearBudget(resources.NuyenFromKarma, cost,
                resources.NuyenFromKarma - cost, 0m, true, []);
            return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, quote.Budget);
        }
        catch (Exception error) when (error is OverflowException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidResources(CharacterCreationKarmaResourcesQuote? resources, bool allowBlocked = false)
        => resources is { Schema: CharacterCreationKarmaResourcesQuote.SchemaV1, Blockers: not null,
                NuyenFromKarma: >= 0, Policy: not null }
            && (allowBlocked || resources.Blockers.Count == 0)
            && CharacterCreationKarmaResourcesRules.IsValidPolicy(resources.Policy)
            && resources.QuoteDigest == Hash(resources with { QuoteDigest = string.Empty });

    private static bool IsOptionId(string? id) => id is { Length: 41 }
        && id.StartsWith("gear:", StringComparison.Ordinal) && Guid.TryParseExact(id.AsSpan(5), "D", out var value)
        && value != Guid.Empty && id == $"gear:{value:D}";
    private static bool Digest(string? value) => CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(value);
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
