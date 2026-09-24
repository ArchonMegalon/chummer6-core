using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Companion pool purchases. Spell/power/form purchases and Karma are separate later steps.</summary>
public static class Sr6CreationPointBuyRules
{
    public const string SourceAnchor = "sr6_schattenkompendium_2022:p29-31";
    public const string SourceSha256 = "fe4e5b69c6ea721bc59c26ceec084f40d3f29f84671e2ea3ad17482bf9e236fa";
    public static Sr6CreationPointBuyLimits Limits() => new(100, 4, 12, 1, 20, 20, 12, 30, 2, 2, 4, 1, 15000, 10, 50);

    public static string AuthorityDigest(CharacterCreationBootstrapBinding bootstrap)
        => Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.point-buy-pools-authority.v1", bootstrap.BindingDigest,
            SourceAnchor, SourceSha256, Sr6CreationFoundationRules.CoreSourceSha256, Limits = Limits(),
            Metatypes = new[] { "human", "elf", "dwarf", "ork", "troll" },
            Talents = new[] { "mundane", "magician", "aspected-magician", "adept", "mystic-adept", "technomancer" },
            BaseAwakenedRating = 1, BaseAspectedMagic = 2, FreeSpells = 0, FreeComplexForms = 0, FreePowerPoints = 0,
            AdjustmentOnReducedMetatypeMaxima = false
        });

    internal static CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Evaluate(
        CharacterCreationBootstrapBinding bootstrap, Sr6CreationFoundationBinding binding, Sr6CreationFoundationSelection selection)
    {
        if (bootstrap.BuildMethod != Sr6CharacterCreationBuildMethods.PointBuy || selection.Assignments.Count != 0)
            return Fail(Sr6CreationPointBuyBlockers.MethodMismatch);
        if (bootstrap.SettingsProfileId != Sr6CharacterCreationBootstrapProfiles.PointBuy
            || !bootstrap.SourceAnchorIds.Contains(SourceAnchor, StringComparer.Ordinal))
            return Fail(Sr6CreationPointBuyBlockers.SourceRequired);
        var limits = Limits();
        var purchase = selection.PointBuy;
        if (!Sr6CreationFoundationIntegrity.ValidPointBuyShape(purchase)
            || purchase!.AdditionalAttributePoints > limits.MaximumAdditionalAttributePoints
            || purchase.AdditionalSkillPoints > limits.MaximumAdditionalSkillPoints
            || purchase.AdditionalAdjustmentPoints > limits.MaximumAdditionalAdjustmentPoints
            || purchase.ResourceUnits > limits.MaximumResourceUnits)
            return Fail(Sr6CreationPointBuyBlockers.InvalidSelection);
        int talent = selection.TalentId == "mundane" ? 0 : limits.AwakenedOrResonanceCost;
        int attributes = purchase.AdditionalAttributePoints * limits.AttributePointCost;
        int skills = purchase.AdditionalSkillPoints * limits.SkillPointCost;
        int adjustment = purchase.AdditionalAdjustmentPoints * limits.AdjustmentPointCost;
        int resources = purchase.ResourceUnits * limits.ResourceUnitCost;
        int spent = talent + attributes + skills + adjustment + resources;
        if (spent > limits.CharacterPoints) return Fail(Sr6CreationPointBuyBlockers.BudgetExceeded);
        int magic = selection.TalentId is "mundane" or "technomancer" ? 0 : selection.TalentId == "aspected-magician" ? 2 : 1;
        int resonance = selection.TalentId == "technomancer" ? 1 : 0;
        var budget = new Sr6CreationPriorityBudget(limits.FreeAttributePoints + purchase.AdditionalAttributePoints,
            limits.FreeSkillPoints + purchase.AdditionalSkillPoints, purchase.ResourceUnits * limits.NuyenPerResourceUnit,
            limits.FreeAdjustmentPoints + purchase.AdditionalAdjustmentPoints, null);
        var pointBuy = new Sr6CreationPointBuyPreview(limits.CharacterPoints, spent, limits.CharacterPoints - spent,
            talent, attributes, skills, adjustment, resources, limits.CustomizationKarma,
            spent == limits.CharacterPoints, 0, 0, 0, AuthorityDigest(bootstrap));
        return new(CharacterCreationFoundationOutcomes.Success,
            new(binding, selection, budget, magic, resonance, [SourceAnchor], string.Empty) { PointBuy = pointBuy }, []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
