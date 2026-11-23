using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal bool IsRetainedSpellcastingFocus7471(XElement effect)
        => Text(effect, "improvementttype") == "SpellCategory"
            && Text(effect, "improvementsource") == "Gear"
            && Text(effect, "condition") == CharacterCareerFocusBondingTransaction.OrdinarySpellcastingFocusCondition7471
            && Guid.TryParse(Text(effect, "sourcename"), out var id)
            && retainedFocusEffects6511.TryGetValue(id, out var compiled)
            && compiled.Any(row => XNode.DeepEquals(row, effect));

    // Bind the learning calculation to this exact reconciled owner/effect graph.
    // Unlike the stock loadout consumer, learning adds no inventory acquisition
    // or weapon/armor projection requirements. These are identity/source checks.
    internal bool MatchesCurrentSpellLearningEffects7552(XElement calculation)
    {
        if (!global::Chummer.Contracts.Characters.CharacterCreationFinalizationDigest.IsCanonical(AuthorityDigest)
            || !checks.All(check => check())) return false;
        var expected = new XElement(root);
        expected.Elements("improvements").Remove();
        expected.Add(new XElement("improvements", Effects.Select(row => new XElement(row))));
        return CharacterCreationOwnedGearGraph1233.GraphDigest(expected)
                == CharacterCreationOwnedGearGraph1233.GraphDigest(calculation)
            && checks.All(check => check());
    }

    private bool TryOrdinarySpellcastingFocusModifier7471(Owner spell, out decimal modifier)
    {
        modifier = 0;
        // The caller has already admitted an ordinary supported cast. Evaluate this
        // one condition against that spell; do not relax Active or evaluate XPath.
        if (Flag(spell.Saved, "alchemical", false)) return false;
        var applicable = Effects.Where(e => IsRetainedSpellcastingFocus7471(e)
            && Flag(e, "enabled") && Text(e, "improvedname") == Text(spell.Source, "category")).ToArray();
        if (applicable.Length == 0) return true;
        if (applicable.Length != 1) return false;

        // The retained compiler emits this exact row only for an owned, bonded
        // Power Focus. SkillModifier already includes its value in the raw pool.
        var power = retainedFocusEffects6511.Values.SelectMany(rows => rows).Where(e => Active(e)
            && Text(e, "improvementttype") == "SkillAttribute" && Text(e, "improvedname") == "MAG"
            && Text(e, "unique", "") == "precedence0").ToArray();
        // Preserve the existing power-only path; do not guess how to compensate
        // multiple base-pool focus contributions when a category focus applies.
        if (power.Length > 1) return false;
        modifier = Math.Max(0, Dec(applicable[0], "val") - (power.Length == 1 ? Dec(power[0], "val") : 0));
        return true;
    }
}
