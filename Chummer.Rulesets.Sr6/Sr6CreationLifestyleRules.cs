using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>SR6 basic prepaid lifestyle and shared cash projection. No SR5 starting-money dice.</summary>
public static class Sr6CreationLifestyleRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p59,70";

    public static IReadOnlyList<Sr6CreationLifestyleOption> Catalog() =>
    [
        new("street", 0m, "sr6_core_de_2024:p59"),
        new("squatter", 500m, "sr6_core_de_2024:p59"),
        new("low", 2000m, "sr6_core_de_2024:p59"),
        new("middle", 5000m, "sr6_core_de_2024:p59"),
        new("high", 10000m, "sr6_core_de_2024:p59"),
        new("luxury", 100000m, "sr6_core_de_2024:p59")
    ];

    public static CharacterCreationFoundationResult<Sr6CreationLifestylePreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationLifestyleSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeLifestyle(selection, out var frozen))
            return Fail(Sr6CreationLifestyleBlockers.InvalidSelection);
        var catalog = Catalog();
        var option = catalog.SingleOrDefault(row => row.Id == frozen!.LifestyleId);
        if (option is null) return Fail(Sr6CreationLifestyleBlockers.Unavailable);
        decimal resources = foundation.Karma?.ResourcesNuyen ?? foundation.Budget.ResourcesNuyen;
        decimal gear = foundation.Gear?.SpentNuyen ?? 0m;
        decimal spent = option.MonthlyNuyen * frozen!.Months;
        decimal remaining = resources - gear - spent;
        if (remaining < 0m) return Fail(Sr6CreationLifestyleBlockers.BudgetExceeded);
        const decimal maximumCarryOver = Sr6CreationGearRules.MaximumCarryOver;
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-lifestyle.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, Catalog = catalog, Selection = frozen,
            KarmaAuthorityDigest = foundation.Karma?.AuthorityDigest, GearAuthorityDigest = foundation.Gear?.AuthorityDigest,
            resources, gear, spent, maximumCarryOver
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(frozen, option, spent, gear, resources, remaining, Math.Min(remaining, maximumCarryOver),
                maximumCarryOver, Math.Max(0m, remaining - maximumCarryOver), authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationLifestylePreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
