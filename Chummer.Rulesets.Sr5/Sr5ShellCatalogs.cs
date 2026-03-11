using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr5;

internal static class Sr5AppCommandCatalog
{
    public static readonly IReadOnlyList<AppCommandDefinition> All = CreateCatalog();

    private static IReadOnlyList<AppCommandDefinition> CreateCatalog()
    {
        return
        [
            Sr5("file", "command.file", "menu", false, true),
            Sr5("edit", "command.edit", "menu", false, true),
            Sr5("special", "command.special", "menu", false, true),
            Sr5("tools", "command.tools", "menu", false, true),
            Sr5("windows", "command.windows", "menu", false, true),
            Sr5("help", "command.help", "menu", false, true),
            Sr5("new_character", "command.new_character", "file", false, true),
            Sr5("new_critter", "command.new_critter", "file", false, true),
            Sr5("open_character", "command.open_character", "file", false, true),
            Sr5("open_for_printing", "command.open_for_printing", "file", false, true),
            Sr5("open_for_export", "command.open_for_export", "file", false, true),
            Sr5("save_character", "command.save_character", "file", true, true),
            Sr5("save_character_as", "command.save_character_as", "file", true, true),
            Sr5("print_character", "command.print_character", "file", true, true),
            Sr5("print_multiple", "command.print_multiple", "file", false, true),
            Sr5("print_setup", "command.print_setup", "file", false, true),
            Sr5("export_character", "command.export_character", "file", true, true),
            Sr5("copy", "command.copy", "edit", true, true),
            Sr5("paste", "command.paste", "edit", true, true),
            Sr5("dice_roller", "command.dice_roller", "tools", false, true),
            Sr5("global_settings", "command.global_settings", "tools", false, true),
            Sr5("switch_ruleset", "command.switch_ruleset", "tools", false, true),
            Sr5(AppCommandIds.RuntimeInspector, "command.runtime_inspector", "tools", false, true),
            Sr5("character_settings", "command.character_settings", "tools", true, true),
            Sr5("translator", "command.translator", "tools", false, true),
            Sr5("hero_lab_importer", "command.hero_lab_importer", "tools", false, true),
            Sr5("xml_editor", "command.xml_editor", "tools", true, true),
            Sr5("master_index", "command.master_index", "tools", false, true),
            Sr5("character_roster", "command.character_roster", "tools", false, true),
            Sr5("data_exporter", "command.data_exporter", "tools", true, true),
            Sr5("report_bug", "command.report_bug", "help", false, true),
            Sr5("new_window", "command.new_window", "windows", false, true),
            Sr5("close_window", "command.close_window", "windows", false, true),
            Sr5("close_all", "command.close_all", "windows", false, true),
            Sr5("wiki", "command.wiki", "help", false, true),
            Sr5("discord", "command.discord", "help", false, true),
            Sr5("revision_history", "command.revision_history", "help", false, true),
            Sr5("dumpshock", "command.dumpshock", "help", false, true),
            Sr5("about", "command.about", "help", false, true),
            Sr5("update", "command.update", "help", false, true),
            Sr5("restart", "command.restart", "help", false, true)
        ];
    }

    private static AppCommandDefinition Sr5(
        string id,
        string labelKey,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, labelKey, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr5);
}

internal static class Sr5NavigationTabCatalog
{
    public static readonly IReadOnlyList<NavigationTabDefinition> All = CreateCatalog();

    private static IReadOnlyList<NavigationTabDefinition> CreateCatalog()
    {
        return
        [
            Sr5("tab-info", "Info", "profile", "character", true, true),
            Sr5("tab-attributes", "Attributes", "attributes", "character", true, true),
            Sr5("tab-skills", "Skills", "skills", "character", true, true),
            Sr5("tab-qualities", "Qualities", "qualities", "character", true, true),
            Sr5("tab-magician", "Magician", "spells", "character", true, true),
            Sr5("tab-adept", "Adept", "powers", "character", true, true),
            Sr5("tab-technomancer", "Technomancer", "complexforms", "character", true, true),
            Sr5("tab-combat", "Combat", "weapons", "character", true, true),
            Sr5("tab-gear", "Gear", "gear", "character", true, true),
            Sr5("tab-armor", "Armor", "armors", "character", true, true),
            Sr5("tab-cyberware", "Cyberware/Bioware", "cyberwares", "character", true, true),
            Sr5("tab-vehicles", "Vehicles", "vehicles", "character", true, true),
            Sr5("tab-lifestyle", "Lifestyle", "lifestyles", "character", true, true),
            Sr5("tab-contacts", "Contacts", "contacts", "character", true, true),
            Sr5("tab-rules", "Rules", "rules", "character", true, true),
            Sr5("tab-notes", "Notes", "profile", "character", true, true),
            Sr5("tab-calendar", "Calendar", "calendar", "character", true, true),
            Sr5("tab-improvements", "Improvements", "improvements", "character", true, true)
        ];
    }

    private static NavigationTabDefinition Sr5(
        string id,
        string label,
        string sectionId,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, sectionId, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr5);
}

internal static class Sr5WorkspaceSurfaceActionCatalog
{
    public static readonly IReadOnlyList<WorkspaceSurfaceActionDefinition> All = CreateCatalog();

    private static IReadOnlyList<WorkspaceSurfaceActionDefinition> CreateCatalog()
    {
        return
        [
            Sr5("tab-info.summary", "Summary", "tab-info", WorkspaceSurfaceActionKind.Summary, "summary", true, true),
            Sr5("tab-info.validate", "Validate", "tab-info", WorkspaceSurfaceActionKind.Validate, "validate", true, true),
            Sr5("tab-info.metadata", "Apply Metadata", "tab-info", WorkspaceSurfaceActionKind.Metadata, "metadata", true, true),
            Sr5("tab-info.profile", "Profile", "tab-info", WorkspaceSurfaceActionKind.Section, "profile", true, true),
            Sr5("tab-info.progress", "Progress", "tab-info", WorkspaceSurfaceActionKind.Section, "progress", true, true),
            Sr5("tab-info.rules", "Rules", "tab-info", WorkspaceSurfaceActionKind.Section, "rules", true, true),
            Sr5("tab-info.build", "Build", "tab-info", WorkspaceSurfaceActionKind.Section, "build", true, true),
            Sr5("tab-info.movement", "Movement", "tab-info", WorkspaceSurfaceActionKind.Section, "movement", true, true),
            Sr5("tab-info.awakening", "Awakening", "tab-info", WorkspaceSurfaceActionKind.Section, "awakening", true, true),
            Sr5("tab-info.attributes", "Attributes", "tab-info", WorkspaceSurfaceActionKind.Section, "attributes", true, true),
            Sr5("tab-info.attributedetails", "Attribute Details", "tab-info", WorkspaceSurfaceActionKind.Section, "attributedetails", true, true),
            Sr5("tab-info.skills", "Skills", "tab-info", WorkspaceSurfaceActionKind.Section, "skills", true, true),
            Sr5("tab-info.qualities", "Qualities", "tab-info", WorkspaceSurfaceActionKind.Section, "qualities", true, true),
            Sr5("tab-info.contacts", "Contacts", "tab-info", WorkspaceSurfaceActionKind.Section, "contacts", true, true),
            Sr5("tab-info.spells", "Spells", "tab-info", WorkspaceSurfaceActionKind.Section, "spells", true, true),
            Sr5("tab-info.powers", "Powers", "tab-info", WorkspaceSurfaceActionKind.Section, "powers", true, true),
            Sr5("tab-info.complexforms", "Complex Forms", "tab-info", WorkspaceSurfaceActionKind.Section, "complexforms", true, true),
            Sr5("tab-info.martialarts", "Martial Arts", "tab-info", WorkspaceSurfaceActionKind.Section, "martialarts", true, true),

            Sr5("tab-gear.inventory", "Inventory", "tab-gear", WorkspaceSurfaceActionKind.Section, "inventory", true, true),
            Sr5("tab-gear.gear", "Gear", "tab-gear", WorkspaceSurfaceActionKind.Section, "gear", true, true),
            Sr5("tab-gear.gearlocations", "Gear Locations", "tab-gear", WorkspaceSurfaceActionKind.Section, "gearlocations", true, true),
            Sr5("tab-gear.weapons", "Weapons", "tab-gear", WorkspaceSurfaceActionKind.Section, "weapons", true, true),
            Sr5("tab-gear.weaponaccessories", "Weapon Accessories", "tab-gear", WorkspaceSurfaceActionKind.Section, "weaponaccessories", true, true),
            Sr5("tab-gear.weaponlocations", "Weapon Locations", "tab-gear", WorkspaceSurfaceActionKind.Section, "weaponlocations", true, true),
            Sr5("tab-gear.armors", "Armors", "tab-gear", WorkspaceSurfaceActionKind.Section, "armors", true, true),
            Sr5("tab-gear.armormods", "Armor Mods", "tab-gear", WorkspaceSurfaceActionKind.Section, "armormods", true, true),
            Sr5("tab-gear.armorlocations", "Armor Locations", "tab-gear", WorkspaceSurfaceActionKind.Section, "armorlocations", true, true),
            Sr5("tab-gear.cyberwares", "Cyberwares", "tab-gear", WorkspaceSurfaceActionKind.Section, "cyberwares", true, true),
            Sr5("tab-gear.drugs", "Drugs", "tab-gear", WorkspaceSurfaceActionKind.Section, "drugs", true, true),
            Sr5("tab-gear.lifestyles", "Lifestyles", "tab-gear", WorkspaceSurfaceActionKind.Section, "lifestyles", true, true),
            Sr5("tab-gear.vehicles", "Vehicles", "tab-gear", WorkspaceSurfaceActionKind.Section, "vehicles", true, true),
            Sr5("tab-gear.vehiclemods", "Vehicle Mods", "tab-gear", WorkspaceSurfaceActionKind.Section, "vehiclemods", true, true),
            Sr5("tab-gear.vehiclelocations", "Vehicle Locations", "tab-gear", WorkspaceSurfaceActionKind.Section, "vehiclelocations", true, true),
            Sr5("tab-gear.sources", "Sources", "tab-gear", WorkspaceSurfaceActionKind.Section, "sources", true, true),
            Sr5("tab-gear.customdatadirectorynames", "Custom Data Dirs", "tab-gear", WorkspaceSurfaceActionKind.Section, "customdatadirectorynames", true, true),

            Sr5("tab-magician.spirits", "Spirits", "tab-magician", WorkspaceSurfaceActionKind.Section, "spirits", true, true),
            Sr5("tab-magician.foci", "Foci", "tab-magician", WorkspaceSurfaceActionKind.Section, "foci", true, true),
            Sr5("tab-magician.aiprograms", "AI Programs", "tab-magician", WorkspaceSurfaceActionKind.Section, "aiprograms", true, true),
            Sr5("tab-magician.limitmodifiers", "Limit Modifiers", "tab-magician", WorkspaceSurfaceActionKind.Section, "limitmodifiers", true, true),
            Sr5("tab-magician.metamagics", "Metamagics", "tab-magician", WorkspaceSurfaceActionKind.Section, "metamagics", true, true),
            Sr5("tab-magician.arts", "Arts", "tab-magician", WorkspaceSurfaceActionKind.Section, "arts", true, true),
            Sr5("tab-magician.initiationgrades", "Initiation Grades", "tab-magician", WorkspaceSurfaceActionKind.Section, "initiationgrades", true, true),
            Sr5("tab-magician.critterpowers", "Critter Powers", "tab-magician", WorkspaceSurfaceActionKind.Section, "critterpowers", true, true),
            Sr5("tab-magician.mentorspirits", "Mentor Spirits", "tab-magician", WorkspaceSurfaceActionKind.Section, "mentorspirits", true, true),
            Sr5("tab-magician.expenses", "Expenses", "tab-magician", WorkspaceSurfaceActionKind.Section, "expenses", true, true),
            Sr5("tab-magician.calendar", "Calendar", "tab-magician", WorkspaceSurfaceActionKind.Section, "calendar", true, true),
            Sr5("tab-magician.improvements", "Improvements", "tab-magician", WorkspaceSurfaceActionKind.Section, "improvements", true, true),

            Sr5("tab-attributes.attributes", "Attributes Summary", "tab-attributes", WorkspaceSurfaceActionKind.Section, "attributes", true, true),
            Sr5("tab-attributes.attributedetails", "Attribute Details", "tab-attributes", WorkspaceSurfaceActionKind.Section, "attributedetails", true, true),
            Sr5("tab-attributes.limitmodifiers", "Limit Modifiers", "tab-attributes", WorkspaceSurfaceActionKind.Section, "limitmodifiers", true, true),

            Sr5("tab-skills.skills", "Skills", "tab-skills", WorkspaceSurfaceActionKind.Section, "skills", true, true),
            Sr5("tab-skills.martialarts", "Martial Arts", "tab-skills", WorkspaceSurfaceActionKind.Section, "martialarts", true, true),

            Sr5("tab-qualities.qualities", "Qualities", "tab-qualities", WorkspaceSurfaceActionKind.Section, "qualities", true, true),
            Sr5("tab-qualities.improvements", "Improvements", "tab-qualities", WorkspaceSurfaceActionKind.Section, "improvements", true, true),

            Sr5("tab-adept.powers", "Adept Powers", "tab-adept", WorkspaceSurfaceActionKind.Section, "powers", true, true),
            Sr5("tab-adept.metamagics", "Metamagics", "tab-adept", WorkspaceSurfaceActionKind.Section, "metamagics", true, true),
            Sr5("tab-adept.initiationgrades", "Initiation/Submersion", "tab-adept", WorkspaceSurfaceActionKind.Section, "initiationgrades", true, true),

            Sr5("tab-technomancer.complexforms", "Complex Forms", "tab-technomancer", WorkspaceSurfaceActionKind.Section, "complexforms", true, true),
            Sr5("tab-technomancer.aiprograms", "Advanced Programs", "tab-technomancer", WorkspaceSurfaceActionKind.Section, "aiprograms", true, true),

            Sr5("tab-combat.weapons", "Weapons", "tab-combat", WorkspaceSurfaceActionKind.Section, "weapons", true, true),
            Sr5("tab-combat.armors", "Armor", "tab-combat", WorkspaceSurfaceActionKind.Section, "armors", true, true),
            Sr5("tab-combat.drugs", "Drugs", "tab-combat", WorkspaceSurfaceActionKind.Section, "drugs", true, true),
            Sr5("tab-combat.movement", "Movement", "tab-combat", WorkspaceSurfaceActionKind.Section, "movement", true, true),

            Sr5("tab-armor.armors", "Armor Items", "tab-armor", WorkspaceSurfaceActionKind.Section, "armors", true, true),
            Sr5("tab-armor.armormods", "Armor Mods", "tab-armor", WorkspaceSurfaceActionKind.Section, "armormods", true, true),
            Sr5("tab-armor.armorlocations", "Armor Locations", "tab-armor", WorkspaceSurfaceActionKind.Section, "armorlocations", true, true),

            Sr5("tab-cyberware.cyberwares", "Cyberware/Bioware", "tab-cyberware", WorkspaceSurfaceActionKind.Section, "cyberwares", true, true),
            Sr5("tab-cyberware.foci", "Foci", "tab-cyberware", WorkspaceSurfaceActionKind.Section, "foci", true, true),

            Sr5("tab-vehicles.vehicles", "Vehicles", "tab-vehicles", WorkspaceSurfaceActionKind.Section, "vehicles", true, true),
            Sr5("tab-vehicles.vehiclemods", "Vehicle Mods", "tab-vehicles", WorkspaceSurfaceActionKind.Section, "vehiclemods", true, true),
            Sr5("tab-vehicles.vehiclelocations", "Vehicle Locations", "tab-vehicles", WorkspaceSurfaceActionKind.Section, "vehiclelocations", true, true),

            Sr5("tab-lifestyle.lifestyles", "Lifestyles", "tab-lifestyle", WorkspaceSurfaceActionKind.Section, "lifestyles", true, true),
            Sr5("tab-lifestyle.expenses", "Expenses", "tab-lifestyle", WorkspaceSurfaceActionKind.Section, "expenses", true, true),
            Sr5("tab-lifestyle.sources", "Sources", "tab-lifestyle", WorkspaceSurfaceActionKind.Section, "sources", true, true),

            Sr5("tab-contacts.contacts", "Contacts", "tab-contacts", WorkspaceSurfaceActionKind.Section, "contacts", true, true),
            Sr5("tab-contacts.mentorspirits", "Mentors/Spirits", "tab-contacts", WorkspaceSurfaceActionKind.Section, "mentorspirits", true, true),

            Sr5("tab-notes.metadata", "Save Notes", "tab-notes", WorkspaceSurfaceActionKind.Metadata, "metadata", true, true),
            Sr5("tab-notes.data_exporter", "Export Notes Snapshot", "tab-notes", WorkspaceSurfaceActionKind.Command, "data_exporter", true, true),

            Sr5("tab-calendar.calendar", "Calendar Entries", "tab-calendar", WorkspaceSurfaceActionKind.Section, "calendar", true, true),
            Sr5("tab-calendar.expenses", "Expense Timeline", "tab-calendar", WorkspaceSurfaceActionKind.Section, "expenses", true, true),

            Sr5("tab-improvements.improvements", "Improvements", "tab-improvements", WorkspaceSurfaceActionKind.Section, "improvements", true, true),
            Sr5("tab-improvements.build", "Build Snapshot", "tab-improvements", WorkspaceSurfaceActionKind.Section, "build", true, true),
            Sr5("tab-improvements.progress", "Career Progress", "tab-improvements", WorkspaceSurfaceActionKind.Section, "progress", true, true)
        ];
    }

    private static WorkspaceSurfaceActionDefinition Sr5(
        string id,
        string label,
        string tabId,
        WorkspaceSurfaceActionKind kind,
        string targetId,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, tabId, kind, targetId, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr5);
}
