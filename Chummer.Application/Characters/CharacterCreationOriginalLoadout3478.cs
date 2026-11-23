using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// This adapter accepts the private live original-inventory proof, or V9's
// reconciled current sheet owner compiler. A saved result/digest, proposal or
// public projection cannot manufacture either authority.
internal static class CharacterCreationOriginalLoadout3478
{
    internal const string Blocker = "original-stock-loadout-unresolved";

    internal static bool TryEvaluate(XElement graph, CharacterCreationLoadoutCompletionProjection? proposal,
        CharacterCreationOriginalInventory3475 proof, out CharacterCreationStockLoadoutProjection3478? result,
        string? instanceSeed3497 = null)
    {
        result = null;
        if (!proof.MatchesCombinedLoadout3497(graph, proposal, instanceSeed3497)) return false;

        if (!TryCalculateStock5696(graph, proof.AuthorityDigest, proof.IsCapacityFreeOrdinaryAmmunition3847, out var calculated)
            || !proof.MatchesCombinedLoadout3497(graph, proposal, instanceSeed3497)) return false;
        result = calculated;
        return true;
    }

    // The imported branch receives the private reconciled owner compiler itself.
    // A DTO, boolean or caller digest cannot supply stock authority. Reuse the
    // same calculation as Creation; no purchase/grant history is manufactured.
    internal static bool TryEvaluateImported5696(XElement graph, CharacterSheetOwnerGraph owners,
        out CharacterCreationStockLoadoutProjection3478? result)
    {
        result = null;
        if (!owners.MatchesCurrentStockLoadout5696(graph)) return false;
        if (!TryCalculateStock5696(graph, owners.AuthorityDigest, owners.IsCapacityFreeAmmunition5701, out var calculated, owners)
            || !owners.MatchesCurrentStockLoadout5696(graph)) return false;
        result = calculated;
        return true;
    }

    private static bool TryCalculateStock5696(XElement graph, string authorityDigest,
        Func<XElement, bool> capacityFreeAmmunition, out CharacterCreationStockLoadoutProjection3478? result,
        CharacterSheetOwnerGraph? sourceOwners5925 = null)
    {
        result = null;

        // Every original owner/effect survives unchanged; every added owner must
        // match the current source-priced basket and actual compiler instance ID.
        // This remains the stock subset, not attachments/ware/all archetypes.
        var armor = graph.Element("armors")?.Elements().ToArray() ?? [];
        var weapons = graph.Element("weapons")?.Elements().ToArray() ?? [];
        if (armor.Length + weapons.Length == 0 && sourceOwners5925 is null) return false;
        var capacities = new List<ArmorCapacity>();
        int highest = -1;
        int? selectedStackingEncumbrance = null;
        foreach (var item in armor)
        {
            if (item.Name != "armor" || !Guid.TryParse(Value(item, "guid"), out var id)
                || !Natural(Value(item, "armor"), out int rating)
                || !Natural(Value(item, "armorcapacity"), out int capacity)
                || Value(item, "armoroverride") is { Length: > 0 } alternative && !Natural(alternative, out _)
                || !bool.TryParse(Value(item, "equipped"), out bool worn)
                || !bool.TryParse(Value(item, "encumbrance"), out _)
                || !CharacterCreationOwnedGearGraph1233.Empty(item, "armormods")
                || !CharacterCreationOwnedGearGraph1233.Empty(item, "gears")) return false;
            // Armor.TotalArmorCapacity -> CalculatedCapacity -> CapacityRemaining:
            // the proven numeric source capacity survives unchanged because the
            // actual mod/gear child collections are empty. Do not replace it by 0.
            capacities.Add(new(id, capacity));
            if (!worn) continue;
            // The native stacking dictionary is initialized for each equipped
            // base armor. Only signed armor/override rows can add to it; those
            // cannot pass the numeric source projection above. Preserve the
            // real highest-base selection, including zero armor and ties.
            int stacking = 0;
            if (rating > highest || rating == highest && stacking < (selectedStackingEncumbrance ?? int.MaxValue))
            {
                highest = rating;
                selectedStackingEncumbrance = stacking;
            }
        }

        // Character.ArmorEncumbrance: unsigned base/override values have no
        // stacking contribution. Armor.GetTotalArmor(true) returns zero for
        // these exact sources. No arbitrary armor or signed accessory enters.
        // Highest base selection (including ties/zero armor) therefore selects
        // a zero stacking term; no strength penalty can arise in this subset.
        int armorEncumbrance = selectedStackingEncumbrance ?? 0; // native no-equipped-armor early return

        bool noUnarmedTargets = true;
        foreach (var weapon in weapons)
        {
            if (weapon.Name != "weapon" || Value(weapon, "type") != "Ranged"
                || Value(weapon, "weapontype") != "gun") return false;
            string skill = Value(weapon, "useskill");
            string category = Value(weapon, "category");
            if (category == "Special Weapons" && Value(weapon, "range") is { Length: > 0 } range) category = range;
            // Weapon.Skill/GetSkillDictionaryKey: the only category mapping to
            // Unarmed Combat is Unarmed; an explicit UseSkill overrides it.
            noUnarmedTargets &= Value(weapon, "name") != "Unarmed Attack"
                && (skill.Length == 0 ? category != "Unarmed" : skill != "Unarmed Combat");
        }

        // Visit all native capacity target kinds in the actual graph. Stock
        // weapon cyberware=False is a scalar flag, not an installed owner.
        // Reconciled standalone gear is evaluated below. Installed gear,
        // vehicles, accessories, ware and mounts remain unsupported here.
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "armor", "gear", "cyberware", "bioware", "vehicle", "accessory", "armormod", "weaponmount" };
        var gearCapacities = new List<CharacterSheetOwnerGraph.StandaloneGearCapacity6604>();
        bool StandaloneGear(XElement node)
        {
            if (sourceOwners5925 is null
                || !sourceOwners5925.TryEvaluateStandaloneGearCapacity6604(graph, node, out var capacity)) return false;
            gearCapacities.Add(capacity!);
            return true;
        }
        bool capacityComplete = graph.Descendants().Where(node => targets.Contains(node.Name.LocalName)).All(node =>
            armor.Contains(node)
                // Armor.Save also emits an <armor> rating leaf. Only the leaf
                // of an owner already replayed from the live stock proof is a
                // scalar; another armor node remains a capacity target.
                || node.Name == "armor" && armor.Contains(node.Parent!)
                    && !node.HasAttributes && !node.HasElements && Natural(node.Value, out _)
                || node.Name == "cyberware" && weapons.Contains(node.Parent!)
                    && !node.HasAttributes && !node.HasElements && bool.TryParse(node.Value, out bool installed) && !installed
                || capacityFreeAmmunition(node)
                || sourceOwners5925?.IsCapacityFreeRetainedFocus6511(graph, node) == true
                || StandaloneGear(node)
                || sourceOwners5925?.IsCurrentCritterArmorBonus5925(graph, node) == true);

        // Gear-only Career stock needs a real reconciled standalone owner and
        // complete capacity traversal. Neither an empty inventory nor a partly
        // supported graph can manufacture the stock proof. Creation's original
        // inventory and existing weapon/armor branches keep their prior result.
        if (armor.Length + weapons.Length == 0 && (gearCapacities.Count == 0 || !capacityComplete)) return false;

        // The caller rechecks freshness after traversal, including every effect.
        // The digest is output evidence, never authority.
        result = new(armorEncumbrance, armor.Length, weapons.Length, capacities.Count + gearCapacities.Count,
            capacities.Sum(row => (decimal)row.Remaining),
            gearCapacities.Count == 0
                ? CharacterCreationFinalizationDigest.Compute(capacities.OrderBy(row => row.InstanceId).ToArray())
                : CharacterCreationFinalizationDigest.Compute(new
                {
                    Armor = capacities.OrderBy(row => row.InstanceId).ToArray(),
                    StandaloneGear = gearCapacities.OrderBy(row => row.InstanceId).ToArray()
                }),
            capacityComplete, noUnarmedTargets, authorityDigest);
        return true;
    }

    private sealed record ArmorCapacity(Guid InstanceId, int Remaining);
    private static bool Natural(string value, out int number)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0;
    private static string Value(XElement parent, string name)
        => parent.Elements(name).ToArray() is [var value] && !value.HasAttributes && !value.HasElements
            ? value.Value : string.Empty;
}
