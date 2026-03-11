using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr4;

internal static class Sr4AppCommandCatalog
{
    public static readonly IReadOnlyList<AppCommandDefinition> All =
    [
        Sr4("file", "command.file", "menu", false, true),
        Sr4("tools", "command.tools", "menu", false, true),
        Sr4("help", "command.help", "menu", false, true),
        Sr4("new_character", "command.new_character", "file", false, true),
        Sr4("open_character", "command.open_character", "file", false, true),
        Sr4("save_character", "command.save_character", "file", true, true),
        Sr4("save_character_as", "command.save_character_as", "file", true, true),
        Sr4("print_character", "command.print_character", "file", true, true),
        Sr4("export_character", "command.export_character", "file", true, true),
        Sr4("switch_ruleset", "command.switch_ruleset", "tools", false, true),
        Sr4(AppCommandIds.RuntimeInspector, "command.runtime_inspector", "tools", false, true),
        Sr4("character_settings", "command.character_settings", "tools", true, true),
        Sr4("dice_roller", "command.dice_roller", "tools", false, true),
        Sr4("about", "command.about", "help", false, true)
    ];

    private static AppCommandDefinition Sr4(
        string id,
        string labelKey,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, labelKey, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr4);
}

internal static class Sr4NavigationTabCatalog
{
    public static readonly IReadOnlyList<NavigationTabDefinition> All =
    [
        Sr4("tab-info", "Info", "profile", "character", true, true),
        Sr4("tab-attributes", "Attributes", "attributes", "character", true, true),
        Sr4("tab-skills", "Skills", "skills", "character", true, true),
        Sr4("tab-gear", "Gear", "inventory", "character", true, true),
        Sr4("tab-rules", "Rules", "rules", "character", true, true),
        Sr4("tab-notes", "Notes", "profile", "character", true, true)
    ];

    private static NavigationTabDefinition Sr4(
        string id,
        string label,
        string sectionId,
        string group,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, sectionId, group, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr4);
}

internal static class Sr4WorkspaceSurfaceActionCatalog
{
    public static readonly IReadOnlyList<WorkspaceSurfaceActionDefinition> All =
    [
        Sr4("tab-info.summary", "Summary", "tab-info", WorkspaceSurfaceActionKind.Summary, "summary", true, true),
        Sr4("tab-info.validate", "Validate", "tab-info", WorkspaceSurfaceActionKind.Validate, "validate", true, true),
        Sr4("tab-info.metadata", "Apply Metadata", "tab-info", WorkspaceSurfaceActionKind.Metadata, "metadata", true, true),
        Sr4("tab-info.profile", "Profile", "tab-info", WorkspaceSurfaceActionKind.Section, "profile", true, true),
        Sr4("tab-info.progress", "Progress", "tab-info", WorkspaceSurfaceActionKind.Section, "progress", true, true),
        Sr4("tab-attributes.attributes", "Attributes", "tab-attributes", WorkspaceSurfaceActionKind.Section, "attributes", true, true),
        Sr4("tab-skills.skills", "Skills", "tab-skills", WorkspaceSurfaceActionKind.Section, "skills", true, true),
        Sr4("tab-gear.inventory", "Inventory", "tab-gear", WorkspaceSurfaceActionKind.Section, "inventory", true, true),
        Sr4("tab-rules.rules", "Rules", "tab-rules", WorkspaceSurfaceActionKind.Section, "rules", true, true),
        Sr4("tab-notes.metadata", "Save Notes", "tab-notes", WorkspaceSurfaceActionKind.Metadata, "metadata", true, true)
    ];

    private static WorkspaceSurfaceActionDefinition Sr4(
        string id,
        string label,
        string tabId,
        WorkspaceSurfaceActionKind kind,
        string targetId,
        bool requiresOpenCharacter,
        bool enabledByDefault)
        => new(id, label, tabId, kind, targetId, requiresOpenCharacter, enabledByDefault, RulesetDefaults.Sr4);
}
