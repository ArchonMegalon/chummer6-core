using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationKarmaResourcesRules
{
    public const string InvestmentLimitExceeded = "creation-karma-resources-investment-limit-exceeded";

    public static string ComputePolicyDigest(CharacterCreationKarmaResourcesPolicy policy)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(policy with { AuthorityDigest = string.Empty });

    public static bool IsValidPolicy(CharacterCreationKarmaResourcesPolicy? policy)
        => policy is { Schema: CharacterCreationKarmaResourcesPolicy.SchemaV1,
            FundingExpression.Length: > 0 and <= 2048, MaximumKarmaInvestment: >= 0 and <= int.MaxValue,
            SourceAnchorIds.Count: > 0 }
            && !string.IsNullOrWhiteSpace(policy.SettingsProfileId)
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(policy.RawProfileInputsDigest)
            && policy.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
            && policy.AuthorityDigest == ComputePolicyDigest(policy);

    public static CharacterCreationKarmaResourcesQuote? Evaluate(
        CharacterCreationKarmaResourcesPolicy policy, CharacterCreationKarmaAttributesQuote attributes,
        decimal karmaAvailable, decimal investment)
    {
        if (!IsValidPolicy(policy) || investment is < 0 or > int.MaxValue
            || attributes is not { Attributes: not null }
            || attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
            || attributes.Policy.SettingsProfileId != policy.SettingsProfileId
            || attributes.QuoteDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                attributes with { QuoteDigest = string.Empty })) return null;
        string expression = policy.FundingExpression
            .Replace("{Karma}", investment.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PriorityNuyen}", "0", StringComparison.Ordinal);
        foreach (var attribute in attributes.Attributes)
            expression = expression.Replace("{" + attribute.AttributeId + "Unaug}",
                    attribute.Current.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{" + attribute.AttributeId + "}",
                    attribute.Current.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        expression = expression.Replace(" div ", " / ", StringComparison.Ordinal);
        int depth = 0;
        foreach (char character in expression)
        {
            if (character == '(' && ++depth > 32 || character == ')' && --depth < 0) return null;
            if (!char.IsWhiteSpace(character) && character is not (>= '0' and <= '9')
                && character is not ('(' or ')' or '+' or '-' or '*' or '/' or '.')) return null;
        }
        if (depth != 0 || !CharacterGearQuantityRules.TryEvaluateCostExpression(expression, 0, out decimal nuyen)
            || nuyen < 0) return null;
        var blockers = new List<string>();
        if (!attributes.CanSelect) blockers.Add(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
        if (investment > policy.MaximumKarmaInvestment) blockers.Add(InvestmentLimitExceeded);
        if (investment > karmaAvailable) blockers.Add(CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
        var result = new CharacterCreationKarmaResourcesQuote(CharacterCreationKarmaResourcesQuote.SchemaV1,
            policy, attributes.QuoteDigest, karmaAvailable, investment, nuyen,
            blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
        return result with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(result) };
    }

    public static bool IsValid(CharacterCreationKarmaResourcesQuote? quote, CharacterCreationKarmaAttributesQuote? attributes,
        decimal? investment, decimal available)
        => quote is null ? investment is null
            : attributes is not null && investment.HasValue && quote.CanSelect && quote.KarmaAvailable == available
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote,
                    Evaluate(quote.Policy, attributes, available, investment.Value));
}
