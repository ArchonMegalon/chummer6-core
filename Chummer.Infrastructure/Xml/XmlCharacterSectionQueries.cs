using Chummer.Application.BuildLab;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterSectionQueries : ICharacterSectionQueries
{
    private readonly ICharacterOverviewQueries _overviewQueries;
    private readonly ICharacterStatsQueries _statsQueries;
    private readonly ICharacterInventoryQueries _inventoryQueries;
    private readonly ICharacterMagicResonanceQueries _magicResonanceQueries;
    private readonly ICharacterSocialNarrativeQueries _socialNarrativeQueries;
    private readonly IBuildLabService _buildLabService;

    public XmlCharacterSectionQueries(
        ICharacterSectionService characterSectionService,
        IBuildLabService? buildLabService = null)
        : this(
            new XmlCharacterOverviewQueries(characterSectionService),
            new XmlCharacterStatsQueries(characterSectionService),
            new XmlCharacterInventoryQueries(characterSectionService),
            new XmlCharacterMagicResonanceQueries(characterSectionService),
            new XmlCharacterSocialNarrativeQueries(characterSectionService),
            buildLabService)
    {
    }

    public XmlCharacterSectionQueries(
        ICharacterOverviewQueries overviewQueries,
        ICharacterStatsQueries statsQueries,
        ICharacterInventoryQueries inventoryQueries,
        ICharacterMagicResonanceQueries magicResonanceQueries,
        ICharacterSocialNarrativeQueries socialNarrativeQueries,
        IBuildLabService? buildLabService = null)
    {
        _overviewQueries = overviewQueries;
        _statsQueries = statsQueries;
        _inventoryQueries = inventoryQueries;
        _magicResonanceQueries = magicResonanceQueries;
        _socialNarrativeQueries = socialNarrativeQueries;
        _buildLabService = buildLabService ?? new DefaultBuildLabService();
    }

    public object ParseSection(string sectionId, CharacterDocument document)
    {
        string key = (sectionId ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "profile" => _overviewQueries.ParseProfile(document),
            "progress" => _overviewQueries.ParseProgress(document),
            "karmasummary" => _overviewQueries.ParseKarmaSummary(document),
            "conditionmonitor" => _overviewQueries.ParseConditionMonitor(document),
            "rules" => _overviewQueries.ParseRules(document),
            "build" => _overviewQueries.ParseBuild(document),
            "movement" => _overviewQueries.ParseMovement(document),
            "awakening" => _overviewQueries.ParseAwakening(document),
            "spelldefense" => _overviewQueries.ParseSpellDefense(document),
            "build-lab" => BuildLabWorkspaceProjectionFactory.Create(
                profile: _overviewQueries.ParseProfile(document),
                progress: _overviewQueries.ParseProgress(document),
                rules: _overviewQueries.ParseRules(document),
                build: _overviewQueries.ParseBuild(document),
                skills: _overviewQueries.ParseSkills(document),
                awakening: _overviewQueries.ParseAwakening(document),
                buildLabService: _buildLabService),
            "skills" => _overviewQueries.ParseSkills(document),

            "attributes" => _statsQueries.ParseAttributes(document),
            "attributedetails" => _statsQueries.ParseAttributeDetails(document),
            "limitmodifiers" => _statsQueries.ParseLimitModifiers(document),

            "inventory" => _inventoryQueries.ParseInventory(document),
            "gear" => _inventoryQueries.ParseGear(document),
            "weapons" => _inventoryQueries.ParseWeapons(document),
            "weaponaccessories" => _inventoryQueries.ParseWeaponAccessories(document),
            "armors" => _inventoryQueries.ParseArmors(document),
            "armormods" => _inventoryQueries.ParseArmorMods(document),
            "cyberwares" => _inventoryQueries.ParseCyberwares(document),
            "vehicles" => _inventoryQueries.ParseVehicles(document),
            "vehiclemods" => _inventoryQueries.ParseVehicleMods(document),
            "gearlocations" => _inventoryQueries.ParseGearLocations(document),
            "armorlocations" => _inventoryQueries.ParseArmorLocations(document),
            "weaponlocations" => _inventoryQueries.ParseWeaponLocations(document),
            "vehiclelocations" => _inventoryQueries.ParseVehicleLocations(document),
            "drugs" => _inventoryQueries.ParseDrugs(document),

            "spells" => _magicResonanceQueries.ParseSpells(document),
            "powers" => _magicResonanceQueries.ParsePowers(document),
            "complexforms" => _magicResonanceQueries.ParseComplexForms(document),
            "spirits" => _magicResonanceQueries.ParseSpirits(document),
            "sprites" => _magicResonanceQueries.ParseSpirits(document),
            "foci" => _magicResonanceQueries.ParseFoci(document),
            "aiprograms" => _magicResonanceQueries.ParseAiPrograms(document),
            "martialarts" => _magicResonanceQueries.ParseMartialArts(document),
            "metamagics" => _magicResonanceQueries.ParseMetamagics(document),
            "arts" => _magicResonanceQueries.ParseArts(document),
            "initiationgrades" => _magicResonanceQueries.ParseInitiationGrades(document),
            "critterpowers" => _magicResonanceQueries.ParseCritterPowers(document),
            "mentorspirits" => _magicResonanceQueries.ParseMentorSpirits(document),

            "qualities" => _socialNarrativeQueries.ParseQualities(document),
            "contacts" => _socialNarrativeQueries.ParseContacts(document),
            "relationships" => _socialNarrativeQueries.ParseRelationships(document),
            "enemies" => _socialNarrativeQueries.ParseEnemies(document),
            "pets" => _socialNarrativeQueries.ParsePets(document),
            "lifestyles" => _socialNarrativeQueries.ParseLifestyles(document),
            "sources" => _socialNarrativeQueries.ParseSources(document),
            "expenses" => _socialNarrativeQueries.ParseExpenses(document),
            "calendar" => _socialNarrativeQueries.ParseCalendar(document),
            "improvements" => _socialNarrativeQueries.ParseImprovements(document),
            "customdatadirectorynames" => _socialNarrativeQueries.ParseCustomDataDirectoryNames(document),

            _ => throw new InvalidOperationException($"Unsupported section '{sectionId}'.")
        };
    }
}
