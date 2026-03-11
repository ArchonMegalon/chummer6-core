using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterInventoryQueries : ICharacterInventoryQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterInventoryQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterInventorySection ParseInventory(CharacterDocument document) => _characterSectionService.ParseInventory(document.Content);

    public CharacterGearSection ParseGear(CharacterDocument document) => _characterSectionService.ParseGear(document.Content);

    public CharacterWeaponsSection ParseWeapons(CharacterDocument document) => _characterSectionService.ParseWeapons(document.Content);

    public CharacterWeaponAccessoriesSection ParseWeaponAccessories(CharacterDocument document) => _characterSectionService.ParseWeaponAccessories(document.Content);

    public CharacterArmorsSection ParseArmors(CharacterDocument document) => _characterSectionService.ParseArmors(document.Content);

    public CharacterArmorModsSection ParseArmorMods(CharacterDocument document) => _characterSectionService.ParseArmorMods(document.Content);

    public CharacterCyberwaresSection ParseCyberwares(CharacterDocument document) => _characterSectionService.ParseCyberwares(document.Content);

    public CharacterVehiclesSection ParseVehicles(CharacterDocument document) => _characterSectionService.ParseVehicles(document.Content);

    public CharacterVehicleModsSection ParseVehicleMods(CharacterDocument document) => _characterSectionService.ParseVehicleMods(document.Content);

    public CharacterLocationsSection ParseGearLocations(CharacterDocument document) => _characterSectionService.ParseGearLocations(document.Content);

    public CharacterLocationsSection ParseArmorLocations(CharacterDocument document) => _characterSectionService.ParseArmorLocations(document.Content);

    public CharacterLocationsSection ParseWeaponLocations(CharacterDocument document) => _characterSectionService.ParseWeaponLocations(document.Content);

    public CharacterLocationsSection ParseVehicleLocations(CharacterDocument document) => _characterSectionService.ParseVehicleLocations(document.Content);

    public CharacterDrugsSection ParseDrugs(CharacterDocument document) => _characterSectionService.ParseDrugs(document.Content);
}
