using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr6;

internal static class Sr6AppCommandCatalog
{
    public static readonly IReadOnlyList<AppCommandDefinition> All =
    [
        Sr6("file", "command.file", "menu", false, true),
        Sr6("tools", "command.tools", "menu", false, true),
        Sr6("help", "command.help", "menu", false, true),
        Sr6("new_character", "command.new_character", "file", false, true),
        Sr6("open_character", "command.open_character", "file", false, true),
        Sr6("save_character", "command.save_character", "file", true, true),
        Sr6("save_character_as", "command.save_character_as", "file", true, true),
        Sr6("print_character", "command.print_character", "file", true, true),
        Sr6("export_character", "command.export_character", "file", true, true),
        Sr6("switch_ruleset", "command.switch_ruleset", "tools", false, true),
        Sr6(AppCommandIds.RuntimeInspector, "command.runtime_inspector", "tools", false, true),
        Sr6("character_settings", "command.character_settings", "tools", true, true),
        Sr6("about", "command.about", "help", false, true)
    ];

    private static AppCommandDefinition Sr6(
        string id,
        string labelKey,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, labelKey, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr6);
}

internal static class Sr6NavigationTabCatalog
{
    public static readonly IReadOnlyList<NavigationTabDefinition> All =
    [
        Sr6("tab-info", "Info", "profile", "character", true, true),
        Sr6("tab-attributes", "Attributes", "attributes", "character", true, true),
        Sr6("tab-skills", "Skills", "skills", "character", true, true),
        Sr6("tab-gear", "Gear", "inventory", "character", true, true),
        Sr6("tab-rules", "Rules", "rules", "character", true, true)
    ];

    private static NavigationTabDefinition Sr6(
        string id,
        string label,
        string sectionId,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, sectionId, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr6);
}

internal static class Sr6WorkspaceSurfaceActionCatalog
{
    public static readonly IReadOnlyList<WorkspaceSurfaceActionDefinition> All =
    [
        Sr6("tab-info.summary", "Summary", "tab-info", WorkspaceSurfaceActionKind.Summary, "summary", true, true),
        Sr6("tab-info.validate", "Validate", "tab-info", WorkspaceSurfaceActionKind.Validate, "validate", true, true),
        Sr6("tab-info.metadata", "Apply Metadata", "tab-info", WorkspaceSurfaceActionKind.Metadata, "metadata", true, true),
        Sr6("tab-info.profile", "Profile", "tab-info", WorkspaceSurfaceActionKind.Section, "profile", true, true),
        Sr6("tab-attributes.attributes", "Attributes", "tab-attributes", WorkspaceSurfaceActionKind.Section, "attributes", true, true),
        Sr6("tab-skills.skills", "Skills", "tab-skills", WorkspaceSurfaceActionKind.Section, "skills", true, true),
        Sr6("tab-gear.inventory", "Inventory", "tab-gear", WorkspaceSurfaceActionKind.Section, "inventory", true, true),
        Sr6("tab-rules.rules", "Rules", "tab-rules", WorkspaceSurfaceActionKind.Section, "rules", true, true)
    ];

    private static WorkspaceSurfaceActionDefinition Sr6(
        string id,
        string label,
        string tabId,
        WorkspaceSurfaceActionKind kind,
        string targetId,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, tabId, kind, targetId, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr6);
}
