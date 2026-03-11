using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public interface ICharacterSectionService
{
    CharacterAttributesSection ParseAttributes(string xml);
    CharacterAttributeDetailsSection ParseAttributeDetails(string xml);

    CharacterInventorySection ParseInventory(string xml);
    CharacterProfileSection ParseProfile(string xml);
    CharacterProgressSection ParseProgress(string xml);
    CharacterRulesSection ParseRules(string xml);
    CharacterBuildSection ParseBuild(string xml);
    CharacterMovementSection ParseMovement(string xml);
    CharacterAwakeningSection ParseAwakening(string xml);
    CharacterGearSection ParseGear(string xml);
    CharacterWeaponsSection ParseWeapons(string xml);
    CharacterWeaponAccessoriesSection ParseWeaponAccessories(string xml);
    CharacterArmorsSection ParseArmors(string xml);
    CharacterArmorModsSection ParseArmorMods(string xml);
    CharacterCyberwaresSection ParseCyberwares(string xml);
    CharacterVehiclesSection ParseVehicles(string xml);
    CharacterVehicleModsSection ParseVehicleMods(string xml);

    CharacterSkillsSection ParseSkills(string xml);

    CharacterQualitiesSection ParseQualities(string xml);

    CharacterContactsSection ParseContacts(string xml);

    CharacterSpellsSection ParseSpells(string xml);

    CharacterPowersSection ParsePowers(string xml);

    CharacterComplexFormsSection ParseComplexForms(string xml);

    CharacterSpiritsSection ParseSpirits(string xml);

    CharacterFociSection ParseFoci(string xml);

    CharacterAiProgramsSection ParseAiPrograms(string xml);

    CharacterMartialArtsSection ParseMartialArts(string xml);

    CharacterLimitModifiersSection ParseLimitModifiers(string xml);

    CharacterLifestylesSection ParseLifestyles(string xml);

    CharacterMetamagicsSection ParseMetamagics(string xml);

    CharacterArtsSection ParseArts(string xml);

    CharacterInitiationGradesSection ParseInitiationGrades(string xml);

    CharacterCritterPowersSection ParseCritterPowers(string xml);

    CharacterMentorSpiritsSection ParseMentorSpirits(string xml);

    CharacterExpensesSection ParseExpenses(string xml);

    CharacterSourcesSection ParseSources(string xml);

    CharacterLocationsSection ParseGearLocations(string xml);

    CharacterLocationsSection ParseArmorLocations(string xml);

    CharacterLocationsSection ParseWeaponLocations(string xml);

    CharacterLocationsSection ParseVehicleLocations(string xml);

    CharacterCalendarSection ParseCalendar(string xml);

    CharacterImprovementsSection ParseImprovements(string xml);

    CharacterCustomDataDirectoryNamesSection ParseCustomDataDirectoryNames(string xml);

    CharacterDrugsSection ParseDrugs(string xml);
}
