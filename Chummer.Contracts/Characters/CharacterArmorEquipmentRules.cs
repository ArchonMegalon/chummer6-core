namespace Chummer.Contracts.Characters;

public enum CharacterArmorEquipmentAction
{
    EquipSelected,
    UnequipSelected,
    EquipAll,
    UnequipAll
}

public sealed record CharacterArmorEquipmentBasis(
    Guid ArmorId,
    bool Equipped,
    bool Exact = true);

public sealed record CharacterArmorEquipmentState(
    Guid ArmorId,
    bool Equipped,
    int ArmorCount,
    int EquippedCount,
    bool CanEquipSelected,
    bool CanUnequipSelected,
    bool CanEquipAll,
    bool CanUnequipAll);

public static class CharacterArmorEquipmentRules
{
    public static bool TryProject(
        Guid selectedArmorId,
        IReadOnlyList<CharacterArmorEquipmentBasis> armors,
        out CharacterArmorEquipmentState? state)
    {
        ArgumentNullException.ThrowIfNull(armors);
        state = null;
        if (selectedArmorId == Guid.Empty || armors.Count == 0)
        {
            return false;
        }

        HashSet<Guid> identities = [];
        CharacterArmorEquipmentBasis? selected = null;
        int equippedCount = 0;
        foreach (CharacterArmorEquipmentBasis armor in armors)
        {
            if (!armor.Exact || armor.ArmorId == Guid.Empty || !identities.Add(armor.ArmorId))
            {
                return false;
            }
            if (armor.Equipped)
            {
                equippedCount++;
            }
            if (armor.ArmorId == selectedArmorId)
            {
                selected = armor;
            }
        }

        if (selected is null)
        {
            return false;
        }

        state = new CharacterArmorEquipmentState(
            selectedArmorId,
            selected.Equipped,
            armors.Count,
            equippedCount,
            CanApply(CharacterArmorEquipmentAction.EquipSelected, selected.Equipped, armors.Count, equippedCount),
            CanApply(CharacterArmorEquipmentAction.UnequipSelected, selected.Equipped, armors.Count, equippedCount),
            CanApply(CharacterArmorEquipmentAction.EquipAll, selected.Equipped, armors.Count, equippedCount),
            CanApply(CharacterArmorEquipmentAction.UnequipAll, selected.Equipped, armors.Count, equippedCount));
        return true;
    }

    public static bool CanApply(
        CharacterArmorEquipmentAction action,
        bool selectedEquipped,
        int armorCount,
        int equippedCount)
        => armorCount > 0
            && equippedCount >= 0
            && equippedCount <= armorCount
            && (action switch
            {
                CharacterArmorEquipmentAction.EquipSelected => !selectedEquipped,
                CharacterArmorEquipmentAction.UnequipSelected => selectedEquipped,
                CharacterArmorEquipmentAction.EquipAll => equippedCount < armorCount,
                CharacterArmorEquipmentAction.UnequipAll => equippedCount > 0,
                _ => false
            });

    public static bool ResolveEquipped(
        CharacterArmorEquipmentAction action,
        Guid selectedArmorId,
        Guid armorId,
        bool currentEquipped)
        => action switch
        {
            CharacterArmorEquipmentAction.EquipSelected when armorId == selectedArmorId => true,
            CharacterArmorEquipmentAction.UnequipSelected when armorId == selectedArmorId => false,
            CharacterArmorEquipmentAction.EquipAll => true,
            CharacterArmorEquipmentAction.UnequipAll => false,
            _ => currentEquipped
        };
}
