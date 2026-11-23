using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    /// <summary>Called only after this graph's source/owner/effect reconciliation and sheet calculation.
    /// Individual source armor is not a stacking total. No capacity arithmetic is invented here.</summary>
    internal IReadOnlyList<CharacterRuleEffectValue> RuleEffectOwners2912()
    {
        var result = new List<CharacterRuleEffectValue>();
        foreach (var owner in owners.Where(o => o.Kind is "Gear" or "Armor" or "Weapon" or "WeaponAccessory" or "Cyberware" or "Bioware")
            .OrderBy(o => o.Id))
        {
            string id = owner.Id.ToString("D"), name = Text(owner.Source, "name");
            result.Add(CharacterRuleEffectValues2912.Text(id, name, CharacterRuleEffectMetric.Selected, owner.SourceId.ToString("D"), owner.Kind));
            result.Add(CharacterRuleEffectValues2912.Flag(id, name, CharacterRuleEffectMetric.Enabled, owner.Enabled, "equipped-owner"));
            result.Add(CharacterRuleEffectValues2912.Number(id, name, CharacterRuleEffectMetric.Rating, owner.Rating));
            result.Add(CharacterRuleEffectValues2912.Text(id, name, CharacterRuleEffectMetric.Selection, owner.Extra));
            if (owner.Kind == "Armor")
                result.Add(CharacterRuleEffectValues2912.Number(id, name, CharacterRuleEffectMetric.ArmorRating,
                    Int(owner.Source, "armor"), "individual-source-armor"));
            if (owner.Kind is "Gear" or "Armor" or "Cyberware" or "Bioware")
                result.Add(CharacterRuleEffectValues2912.Number(id, name, CharacterRuleEffectMetric.CapacityUsed, null,
                    reason: "The equipped sheet consumer validates this owner but does not calculate capacity usage."));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
