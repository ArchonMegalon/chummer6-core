using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Free creation contacts, derived from final natural Charisma. No SR5 contact/Karma rules.</summary>
public static class Sr6CreationContactRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p52-53,70";

    public static Sr6CreationContactOptions? Options(Sr6CreationFoundationPreview foundation)
    {
        if (foundation.Attributes is null) return null;
        int charisma = Sr6CreationKarmaRules.AttributeRating(foundation, "Charisma");
        int budget = charisma * 6;
        return new(charisma, 6, budget, 1, Math.Min(12, charisma), Math.Min(64, budget / 2));
    }

    public static CharacterCreationFoundationResult<Sr6CreationContactPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationContactSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeContacts(selection, out var frozen))
            return Fail(Sr6CreationContactBlockers.InvalidSelection);
        if (Options(foundation) is not { } options) return Fail(Sr6CreationContactBlockers.AttributesRequired);
        if (frozen!.Contacts.Any(row => row.Connection > options.MaximumRating || row.Loyalty > options.MaximumRating))
            return Fail(Sr6CreationContactBlockers.RatingExceeded);
        var values = frozen.Contacts.Select(row => new Sr6CreationContactValue(row, row.Connection + row.Loyalty)).ToArray();
        int spent = values.Sum(row => row.PointCost);
        if (spent > options.PointBudget) return Fail(Sr6CreationContactBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-contacts.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, AttributeAuthorityDigest = foundation.Attributes!.AuthorityDigest,
            KarmaAuthorityDigest = foundation.Karma?.AuthorityDigest, Options = options,
            Cost = "Connection+Loyalty", SeparateFromKarma = true
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values, options, spent, options.PointBudget - spent, spent == options.PointBudget,
                values.Length > 0, authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationContactPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
