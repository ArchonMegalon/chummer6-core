using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Hosting.Presentation;

public static class NavigationTabCatalog
{
    public static readonly IReadOnlyList<NavigationTabDefinition> All = CreateSr5Catalog();

    public static IReadOnlyList<NavigationTabDefinition> ForRuleset(string? rulesetId)
    {
        string effectiveRulesetId = ResolveCompatibilityRulesetId(rulesetId);
        return All
            .Where(tab => string.Equals(tab.RulesetId, effectiveRulesetId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ResolveCompatibilityRulesetId(string? rulesetId)
    {
        return RulesetDefaults.NormalizeOptional(rulesetId) ?? RulesetDefaults.Sr5;
    }

    private static IReadOnlyList<NavigationTabDefinition> CreateSr5Catalog()
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
