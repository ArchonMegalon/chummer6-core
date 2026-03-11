using System;
using System.IO;
using System.Linq;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CharacterSectionServiceTests
{
    [TestMethod]
    public void ParseAttributes_reads_core_attribute_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterAttributesSection section = service.ParseAttributes(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "BOD"));
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "AGI"));
        Assert.IsTrue(section.Attributes.All(attribute => attribute.TotalValue >= 0));
    }

    [TestMethod]
    public void ParseAttributeDetails_reads_attribute_bounds_and_values()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterAttributeDetailsSection section = service.ParseAttributeDetails(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "BOD"));
        Assert.IsTrue(section.Attributes.All(attribute => attribute.MetatypeMax >= attribute.MetatypeMin));
    }

    [TestMethod]
    public void ParseInventory_extracts_item_counts_and_names()
    {
        string xml = File.ReadAllText(FindTestFilePath("Barrett.chum5"));
        var service = new CharacterSectionService();

        CharacterInventorySection section = service.ParseInventory(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.GearCount);
        Assert.IsGreaterThanOrEqualTo(0, section.WeaponCount);
        Assert.IsGreaterThanOrEqualTo(0, section.ArmorCount);
        Assert.IsGreaterThanOrEqualTo(0, section.CyberwareCount);
        Assert.IsGreaterThanOrEqualTo(0, section.VehicleCount);
        Assert.HasCount(section.GearCount, section.GearNames);
        Assert.HasCount(section.WeaponCount, section.WeaponNames);
        Assert.HasCount(section.ArmorCount, section.ArmorNames);
        Assert.HasCount(section.CyberwareCount, section.CyberwareNames);
        Assert.HasCount(section.VehicleCount, section.VehicleNames);
    }

    [TestMethod]
    public void ParseProfile_extracts_character_identity_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterProfileSection section = service.ParseProfile(xml);

        Assert.AreEqual("Troy Simmons", section.Name);
        Assert.AreEqual("BLUE", section.Alias);
        Assert.AreEqual("Ork", section.Metatype);
        Assert.AreEqual("SumtoTen", section.BuildMethod);
    }

    [TestMethod]
    public void ParseProgress_extracts_character_progress_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterProgressSection section = service.ParseProgress(xml);

        Assert.IsGreaterThan(0m, section.Nuyen);
        Assert.IsGreaterThanOrEqualTo(0m, section.Karma);
        Assert.IsGreaterThan(0m, section.TotalEssence);
    }

    [TestMethod]
    public void ParseRules_extracts_character_rules_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterRulesSection section = service.ParseRules(xml);

        Assert.AreEqual("SR5", section.GameEdition);
        Assert.AreEqual("default.xml", section.Settings);
        Assert.IsGreaterThan(0, section.BannedWareGrades.Count);
    }

    [TestMethod]
    public void ParseBuild_extracts_character_build_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterBuildSection section = service.ParseBuild(xml);

        Assert.AreEqual("SumtoTen", section.BuildMethod);
        Assert.AreEqual("C,2", section.PriorityMetatype);
        Assert.IsGreaterThan(0, section.TotalAttributes);
    }

    [TestMethod]
    public void ParseMovement_extracts_character_movement_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterMovementSection section = service.ParseMovement(xml);

        Assert.AreEqual("2/1/0", section.Walk);
        Assert.AreEqual("4/0/0", section.Run);
        Assert.IsGreaterThanOrEqualTo(0, section.PhysicalCmFilled);
    }

    [TestMethod]
    public void ParseAwakening_extracts_magic_resonance_and_limits()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterAwakeningSection section = service.ParseAwakening(xml);

        Assert.IsFalse(section.MagEnabled);
        Assert.IsFalse(section.ResEnabled);
        Assert.IsFalse(section.DepEnabled);
        Assert.AreEqual("RES + WIL", section.StreamDrain);
    }

    [TestMethod]
    public void ParseGear_extracts_gear_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterGearSection section = service.ParseGear(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Gear.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseWeapons_extracts_weapon_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterWeaponsSection section = service.ParseWeapons(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Weapons.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
        Assert.IsTrue(section.Weapons.Any(item => !string.IsNullOrWhiteSpace(item.Damage)));
    }

    [TestMethod]
    public void ParseWeaponAccessories_extracts_weapon_accessory_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterWeaponAccessoriesSection section = service.ParseWeaponAccessories(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Accessories.Any(item => !string.IsNullOrWhiteSpace(item.WeaponName)));
    }

    [TestMethod]
    public void ParseArmors_extracts_armor_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterArmorsSection section = service.ParseArmors(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Armors.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseArmorMods_extracts_armor_mod_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterArmorModsSection section = service.ParseArmorMods(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.ArmorMods.Any(item => !string.IsNullOrWhiteSpace(item.ArmorName)));
    }

    [TestMethod]
    public void ParseCyberwares_extracts_cyberware_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterCyberwaresSection section = service.ParseCyberwares(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Cyberwares.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
        Assert.IsTrue(section.Cyberwares.Any(item => !string.IsNullOrWhiteSpace(item.Essence)));
    }

    [TestMethod]
    public void ParseVehicles_extracts_vehicle_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterVehiclesSection section = service.ParseVehicles(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Vehicles.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseVehicleMods_extracts_vehicle_mod_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterVehicleModsSection section = service.ParseVehicleMods(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.VehicleMods.Any(item => !string.IsNullOrWhiteSpace(item.VehicleName)));
    }

    [TestMethod]
    public void ParseSkills_extracts_skill_entries_and_specializations()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterSkillsSection section = service.ParseSkills(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.KnowledgeCount);
        Assert.IsTrue(section.Skills.Any(skill => !string.IsNullOrWhiteSpace(skill.Suid) || !string.IsNullOrWhiteSpace(skill.Guid)));
        Assert.IsTrue(section.Skills.Any(skill => skill.Specializations.Count >= 0));
    }

    [TestMethod]
    public void ParseQualities_extracts_quality_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterQualitiesSection section = service.ParseQualities(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Qualities.Any(quality => !string.IsNullOrWhiteSpace(quality.Name)));
        Assert.HasCount(section.Count, section.Qualities);
    }

    [TestMethod]
    public void ParseContacts_extracts_contact_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterContactsSection section = service.ParseContacts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Contacts.Any(contact => !string.IsNullOrWhiteSpace(contact.Name)));
    }

    [TestMethod]
    public void ParseSpells_extracts_spell_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin.chum5"));
        var service = new CharacterSectionService();

        CharacterSpellsSection section = service.ParseSpells(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Spells.Any(spell => !string.IsNullOrWhiteSpace(spell.Name)));
    }

    [TestMethod]
    public void ParsePowers_extracts_power_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterPowersSection section = service.ParsePowers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Powers.Any(power => !string.IsNullOrWhiteSpace(power.Name)));
    }

    [TestMethod]
    public void ParseComplexForms_extracts_complexform_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Rez0luti0n2.0.chum5"));
        var service = new CharacterSectionService();

        CharacterComplexFormsSection section = service.ParseComplexForms(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.ComplexForms.Any(form => !string.IsNullOrWhiteSpace(form.Name)));
    }

    [TestMethod]
    public void ParseSpirits_extracts_spirit_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Glessner.chum5"));
        var service = new CharacterSectionService();

        CharacterSpiritsSection section = service.ParseSpirits(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Spirits.Any(spirit => !string.IsNullOrWhiteSpace(spirit.Name)));
    }

    [TestMethod]
    public void ParseFoci_extracts_focus_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Gangerbean.chum5"));
        var service = new CharacterSectionService();

        CharacterFociSection section = service.ParseFoci(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Foci.Any(focus => !string.IsNullOrWhiteSpace(focus.Guid)));
    }

    [TestMethod]
    public void ParseAiPrograms_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterAiProgramsSection section = service.ParseAiPrograms(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.AiPrograms);
    }

    [TestMethod]
    public void ParseMartialArts_extracts_martial_art_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterMartialArtsSection section = service.ParseMartialArts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.MartialArts.Any(art => !string.IsNullOrWhiteSpace(art.Name)));
    }

    [TestMethod]
    public void ParseLimitModifiers_extracts_modifier_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterLimitModifiersSection section = service.ParseLimitModifiers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.LimitModifiers.Any(modifier => !string.IsNullOrWhiteSpace(modifier.Name)));
    }

    [TestMethod]
    public void ParseLifestyles_extracts_lifestyle_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterLifestylesSection section = service.ParseLifestyles(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Lifestyles.Any(lifestyle => !string.IsNullOrWhiteSpace(lifestyle.Name)));
    }

    [TestMethod]
    public void ParseMetamagics_extracts_metamagic_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterMetamagicsSection section = service.ParseMetamagics(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Metamagics.Any(metamagic => !string.IsNullOrWhiteSpace(metamagic.Name)));
    }

    [TestMethod]
    public void ParseArts_extracts_art_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterArtsSection section = service.ParseArts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Arts.Any(art => !string.IsNullOrWhiteSpace(art.Name)));
    }

    [TestMethod]
    public void ParseInitiationGrades_extracts_grade_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterInitiationGradesSection section = service.ParseInitiationGrades(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.InitiationGrades.Any(grade => grade.Grade >= 0));
    }

    [TestMethod]
    public void ParseCritterPowers_extracts_critter_power_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCritterPowersSection section = service.ParseCritterPowers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.CritterPowers.Any(power => !string.IsNullOrWhiteSpace(power.Name)));
    }

    [TestMethod]
    public void ParseMentorSpirits_extracts_mentor_spirit_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterMentorSpiritsSection section = service.ParseMentorSpirits(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.MentorSpirits.Any(spirit => !string.IsNullOrWhiteSpace(spirit.Name)));
    }

    [TestMethod]
    public void ParseExpenses_extracts_expense_entries_and_totals()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterExpensesSection section = service.ParseExpenses(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.TotalKarma);
        Assert.IsGreaterThanOrEqualTo(0, section.TotalNuyen);
        Assert.HasCount(section.Count, section.Expenses);
    }

    [TestMethod]
    public void ParseSources_extracts_distinct_source_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterSourcesSection section = service.ParseSources(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Sources.Any(source => !string.IsNullOrWhiteSpace(source)));
    }

    [TestMethod]
    public void ParseGearLocations_extracts_location_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseGearLocations(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Locations.Any(location => !string.IsNullOrWhiteSpace(location.Name)));
    }

    [TestMethod]
    public void ParseArmorLocations_extracts_location_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseArmorLocations(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Locations.Any(location => !string.IsNullOrWhiteSpace(location.Name)));
    }

    [TestMethod]
    public void ParseWeaponLocations_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseWeaponLocations(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Locations);
    }

    [TestMethod]
    public void ParseVehicleLocations_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseVehicleLocations(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Locations);
    }

    [TestMethod]
    public void ParseCalendar_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCalendarSection section = service.ParseCalendar(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Entries);
    }

    [TestMethod]
    public void ParseImprovements_extracts_improvement_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterImprovementsSection section = service.ParseImprovements(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.EnabledCount);
        Assert.HasCount(section.Count, section.Improvements);
    }

    [TestMethod]
    public void ParseCustomDataDirectoryNames_extracts_directory_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCustomDataDirectoryNamesSection section = service.ParseCustomDataDirectoryNames(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.DirectoryNames.Any(name => !string.IsNullOrWhiteSpace(name)));
    }

    [TestMethod]
    public void ParseDrugs_extracts_drug_entries_from_xml_payload()
    {
        const string xml = "<character><drugs><drug><name>Jazz</name><category>Combat Drugs</category><source>SR5</source><rating>2</rating><qty>3</qty></drug></drugs></character>";
        var service = new CharacterSectionService();

        CharacterDrugsSection section = service.ParseDrugs(xml);

        Assert.HasCount(1, section.Drugs);
        Assert.AreEqual("Jazz", section.Drugs[0].Name);
        Assert.AreEqual(3m, section.Drugs[0].Quantity);
    }

    private static string FindTestFilePath(string fileName)
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer.Tests", "TestFiles", fileName);
            if (File.Exists(candidate))
                return candidate;

            if (current.Parent == null)
                break;

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate test character file.", fileName);
    }
}
