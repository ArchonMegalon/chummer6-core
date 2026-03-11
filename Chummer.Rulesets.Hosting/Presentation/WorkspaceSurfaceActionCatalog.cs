using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Hosting.Presentation;

public static class WorkspaceSurfaceActionCatalog
{
    public static readonly IReadOnlyList<WorkspaceSurfaceActionDefinition> All = CreateSr5Catalog();

    public static IReadOnlyList<WorkspaceSurfaceActionDefinition> ForRuleset(string? rulesetId)
    {
        string effectiveRulesetId = ResolveCompatibilityRulesetId(rulesetId);
        return All
            .Where(action => string.Equals(action.RulesetId, effectiveRulesetId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<WorkspaceSurfaceActionDefinition> ForTab(string? tabId)
        => ForTab(tabId, rulesetId: null);

    public static IReadOnlyList<WorkspaceSurfaceActionDefinition> ForTab(string? tabId, string? rulesetId)
    {
        string effectiveTabId = string.IsNullOrWhiteSpace(tabId) ? "tab-info" : tabId;
        WorkspaceSurfaceActionDefinition[] rulesetScopedActions = ForRuleset(rulesetId).ToArray();

        WorkspaceSurfaceActionDefinition[] actions = rulesetScopedActions
            .Where(action => string.Equals(action.TabId, effectiveTabId, StringComparison.Ordinal))
            .ToArray();
        if (actions.Length > 0)
            return actions;

        return rulesetScopedActions
            .Where(action => string.Equals(action.TabId, "tab-info", StringComparison.Ordinal))
            .ToArray();
    }

    private static string ResolveCompatibilityRulesetId(string? rulesetId)
    {
        return RulesetDefaults.NormalizeOptional(rulesetId) ?? RulesetDefaults.Sr5;
    }

    private static IReadOnlyList<WorkspaceSurfaceActionDefinition> CreateSr5Catalog()
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
