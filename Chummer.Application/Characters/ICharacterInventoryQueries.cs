using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterInventoryQueries
{
    CharacterInventorySection ParseInventory(CharacterDocument document);

    CharacterGearSection ParseGear(CharacterDocument document);

    CharacterWeaponsSection ParseWeapons(CharacterDocument document);

    CharacterWeaponAccessoriesSection ParseWeaponAccessories(CharacterDocument document);

    CharacterArmorsSection ParseArmors(CharacterDocument document);

    CharacterArmorModsSection ParseArmorMods(CharacterDocument document);

    CharacterCyberwaresSection ParseCyberwares(CharacterDocument document);

    CharacterVehiclesSection ParseVehicles(CharacterDocument document);

    CharacterVehicleModsSection ParseVehicleMods(CharacterDocument document);

    CharacterLocationsSection ParseGearLocations(CharacterDocument document);

    CharacterLocationsSection ParseArmorLocations(CharacterDocument document);

    CharacterLocationsSection ParseWeaponLocations(CharacterDocument document);

    CharacterLocationsSection ParseVehicleLocations(CharacterDocument document);

    CharacterDrugsSection ParseDrugs(CharacterDocument document);
}
