using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>German 2024 core pp67–68/158–160 and Companion p30.
/// Entitlements are deliberately separate from a later catalog of learned abilities.</summary>
public static class Sr6CreationTalentRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p67-68,158-160";

    public static Sr6CreationTalentOptions? Options(Sr6CreationFoundationPreview foundation)
    {
        if (foundation.Attributes is null) return null;
        string talent = foundation.Selection.TalentId;
        int magic = Rating(foundation, "Magic");
        int poolMagic = foundation.Attributes.Values.Single(row => row.AttributeId == "Magic").Value;
        int karmaPowerPoints = foundation.Karma?.AdditionalPowerPoints ?? 0;
        bool pointBuy = foundation.PointBuy is not null;
        bool adept = talent == "adept", mystic = talent == "mystic-adept";
        return new(pointBuy && (adept || mystic) ? poolMagic : mystic ? foundation.BaseMagic : 0,
            !pointBuy && adept ? magic : karmaPowerPoints, pointBuy,
            pointBuy ? adept ? 4 : mystic ? 8 : 0 : 0, pointBuy ? 2 : 0,
            talent == "aspected-magician");
    }

    public static CharacterCreationFoundationResult<Sr6CreationTalentPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationTalentSelection selection)
    {
        if (selection is not { SelectedPowerPoints: >= 0 and <= 6 })
            return Fail(Sr6CreationTalentBlockers.InvalidSelection);
        if (Options(foundation) is not { } options)
            return Fail(Sr6CreationTalentBlockers.AttributesRequired);
        if (selection.SelectedPowerPoints > options.MaximumSelectedPowerPoints)
            return Fail(Sr6CreationTalentBlockers.PowerPointLimit);
        string? aspect = foundation.Selection.Skills?.AspectedSkillId;
        if (options.RequiresAspect && aspect is not ("Sorcery" or "Conjuring" or "Enchanting"))
            return Fail(Sr6CreationTalentBlockers.AspectRequired);
        int magic = Rating(foundation, "Magic"), resonance = Rating(foundation, "Resonance");
        // CP purchases precede customization. Karma raises final ratings, but does
        // not retroactively increase free grants or the pre-Karma CP purchase caps.
        int spellBasis = options.UsesCharacterPoints
            ? foundation.Attributes!.Values.Single(row => row.AttributeId == "Magic").Value : foundation.BaseMagic;
        int formBasis = options.UsesCharacterPoints
            ? foundation.Attributes!.Values.Single(row => row.AttributeId == "Resonance").Value : foundation.BaseResonance;
        string talent = foundation.Selection.TalentId;
        if (talent == "mystic-adept") spellBasis -= selection.SelectedPowerPoints;
        int spells = talent is "magician" or "mystic-adept" || (options.RequiresAspect && aspect == "Sorcery")
            ? spellBasis * 2 : 0;
        int alchemy = options.RequiresAspect && aspect == "Enchanting" ? spellBasis * 2 : 0;
        int forms = talent == "technomancer" ? formBasis * 2 : 0;
        int cost = selection.SelectedPowerPoints * options.CharacterPointsPerPowerPoint;
        if (foundation.PointBuy is { } points && cost > points.PointsRemaining)
            return Fail(Sr6CreationPointBuyBlockers.BudgetExceeded);
        string[] anchors = options.UsesCharacterPoints ? [SourceAnchor, Sr6CreationPointBuyRules.SourceAnchor] : [SourceAnchor];
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-talent-budget.v1", SourceAnchor,
            Sr6CreationFoundationRules.CoreSourceSha256,
            CompanionSourceSha256 = options.UsesCharacterPoints ? Sr6CreationPointBuyRules.SourceSha256 : null,
            foundation.Binding.AuthorityDigest, AttributeAuthority = foundation.Attributes!.AuthorityDigest,
            talent, aspect, foundation.BaseMagic, foundation.BaseResonance, magic, resonance, options, selection
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(magic, resonance, options.AutomaticPowerPoints + selection.SelectedPowerPoints, cost, spells, alchemy, forms,
                options.UsesCharacterPoints ? 0 : spells, options.UsesCharacterPoints ? 0 : alchemy,
                options.UsesCharacterPoints ? 0 : forms, options.CharacterPointsPerSpellOrForm, authority, anchors), []);
    }

    private static int Rating(Sr6CreationFoundationPreview foundation, string id)
        => Sr6CreationKarmaRules.AttributeRating(foundation, id);

    private static CharacterCreationFoundationResult<Sr6CreationTalentPreview> Fail(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
