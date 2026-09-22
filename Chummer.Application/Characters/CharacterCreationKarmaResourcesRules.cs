using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationKarmaResourcesRules
{
    public const string InvestmentLimitExceeded = "creation-karma-resources-investment-limit-exceeded";

    public static string ComputePolicyDigest(CharacterCreationKarmaResourcesPolicy policy)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(policy with { AuthorityDigest = string.Empty });

    public static bool IsValidPolicy(CharacterCreationKarmaResourcesPolicy? policy)
        => IsValidSpendingPolicy(policy, CharacterCreationKarmaResourcesPolicy.SchemaV1);

    public static bool IsValidSpendingPolicy(CharacterCreationKarmaResourcesPolicy? policy, string expectedSchema)
        => expectedSchema is CharacterCreationKarmaResourcesPolicy.SchemaV1 or CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1
            && policy is { FundingExpression.Length: > 0 and <= 2048, MaximumKarmaInvestment: >= 0 and <= int.MaxValue,
            SourceAnchorIds.Count: > 0 }
            && policy.Schema == expectedSchema
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
        if (!TryFundingAmount(policy.FundingExpression, investment,
            attributes.Attributes.Select(row => new KeyValuePair<string, int>(row.AttributeId, row.Current)), out decimal nuyen)) return null;
        var blockers = new List<string>();
        if (!attributes.CanSelect) blockers.Add(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
        if (investment > policy.MaximumKarmaInvestment) blockers.Add(InvestmentLimitExceeded);
        if (investment > karmaAvailable) blockers.Add(CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
        var result = new CharacterCreationKarmaResourcesQuote(CharacterCreationKarmaResourcesQuote.SchemaV1,
            policy, attributes.QuoteDigest, karmaAvailable, investment, nuyen,
            blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
        return result with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(result) };
    }

    // Both methods spend Karma rather than Priority resource grants. This is
    // arithmetic only; neither method can use it to authorize the other's quote.
    internal static bool TryFundingAmount(string expression, decimal investment,
        IEnumerable<KeyValuePair<string, int>> attributes, out decimal nuyen)
    {
        nuyen = 0;
        if (investment is < 0 or > int.MaxValue || expression is not { Length: > 0 and <= 2048 }) return false;
        expression = expression.Replace("{Karma}", investment.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{PriorityNuyen}", "0", StringComparison.Ordinal);
        foreach (var attribute in attributes)
            expression = expression.Replace("{" + attribute.Key + "Unaug}", attribute.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{" + attribute.Key + "}", attribute.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        expression = expression.Replace(" div ", " / ", StringComparison.Ordinal);
        int depth = 0;
        foreach (char character in expression)
        {
            if (character == '(' && ++depth > 32 || character == ')' && --depth < 0) return false;
            if (!char.IsWhiteSpace(character) && character is not (>= '0' and <= '9')
                && character is not ('(' or ')' or '+' or '-' or '*' or '/' or '.')) return false;
        }
        return depth == 0 && CharacterGearQuantityRules.TryEvaluateCostExpression(expression, 0, out nuyen) && nuyen >= 0;
    }

    public static bool IsValid(CharacterCreationKarmaResourcesQuote? quote, CharacterCreationKarmaAttributesQuote? attributes,
        decimal? investment, decimal available)
        => quote is null ? investment is null
            : attributes is not null && investment.HasValue && quote.CanSelect && quote.KarmaAvailable == available
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(quote,
                    Evaluate(quote.Policy, attributes, available, investment.Value));
}
