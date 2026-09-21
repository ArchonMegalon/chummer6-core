using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Standard Priority/Sum-to-Ten allocation before qualities, Karma and augmentation.</summary>
public static class Sr6CreationAttributeRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p65-67";

    public static IReadOnlyList<Sr6CreationAttributeOption> Options(Sr6CreationFoundationPreview foundation)
    {
        var metatypes = new Sr6MetatypeProvider();
        return Sr6CreationAttributeIds.Ordered.Select(id =>
        {
            if (id is "Magic" or "Resonance")
            {
                int rating = id == "Magic" ? foundation.BaseMagic : foundation.BaseResonance;
                return new Sr6CreationAttributeOption(id, rating, rating > 0 ? 6 : 0, false, rating > 0);
            }
            var range = metatypes.GetAttributeRange(foundation.Selection.MetatypeId, id);
            // The German printing explicitly includes reduced ranges (e.g. dwarf Reaction).
            return new Sr6CreationAttributeOption(id, range.Minimum, range.Maximum, id != "Edge",
                id == "Edge" || (foundation.PointBuy is not null ? range.Maximum > 6 : range.Minimum != 1 || range.Maximum != 6));
        }).ToArray();
    }

    public static CharacterCreationFoundationResult<Sr6CreationAttributePreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationAttributeSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeAttributes(selection, out var frozen))
            return Fail(Sr6CreationAttributeBlockers.InvalidAllocation);
        var options = Options(foundation);
        var values = new List<Sr6CreationAttributeValue>();
        int normal = 0, adjustment = 0, maximumCount = 0;
        foreach (var option in options)
        {
            var spend = frozen!.Allocations.Single(row => row.AttributeId == option.AttributeId);
            if ((!option.AllowsAttributePoints && spend.AttributePoints != 0)
                || (!option.AllowsAdjustmentPoints && spend.AdjustmentPoints != 0))
                return Fail(Sr6CreationAttributeBlockers.PointKindUnavailable);
            int value = option.BaseValue + spend.AttributePoints + spend.AdjustmentPoints;
            if (value > option.Maximum) return Fail(Sr6CreationAttributeBlockers.RatingExceeded);
            if (option.AllowsAttributePoints && value == option.Maximum) maximumCount++;
            normal += spend.AttributePoints;
            adjustment += spend.AdjustmentPoints;
            values.Add(new(option.AttributeId, option.BaseValue, option.Maximum, spend.AttributePoints, spend.AdjustmentPoints, value));
        }
        if (maximumCount > 1) return Fail(Sr6CreationAttributeBlockers.MaximumCountExceeded);
        if (normal > foundation.Budget.AttributePoints) return Fail(Sr6CreationAttributeBlockers.AttributeBudgetExceeded);
        if (adjustment > foundation.Budget.MetatypeAdjustmentPoints) return Fail(Sr6CreationAttributeBlockers.AdjustmentBudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-attribute-allocation.v1", SourceAnchor,
            Sr6CreationFoundationRules.CoreSourceSha256, foundation.Binding.AuthorityDigest,
            foundation.Selection.MetatypeId, foundation.Selection.TalentId, foundation.Budget,
            foundation.BaseMagic, foundation.BaseResonance, Options = options, MaximumNormalAtCap = 1
        });
        var preview = new Sr6CreationAttributePreview(values, normal, foundation.Budget.AttributePoints - normal,
            adjustment, foundation.Budget.MetatypeAdjustmentPoints - adjustment,
            normal == foundation.Budget.AttributePoints && adjustment == foundation.Budget.MetatypeAdjustmentPoints,
            authority, foundation.PointBuy is null ? [SourceAnchor] : [SourceAnchor, Sr6CreationPointBuyRules.SourceAnchor]);
        return new(CharacterCreationFoundationOutcomes.Success, preview, []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationAttributePreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
