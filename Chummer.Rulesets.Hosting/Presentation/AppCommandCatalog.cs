using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Hosting.Presentation;

public static class AppCommandCatalog
{
    public static readonly IReadOnlyList<AppCommandDefinition> All = CreateSr5Catalog();

    public static IReadOnlyList<AppCommandDefinition> ForRuleset(string? rulesetId)
    {
        string effectiveRulesetId = ResolveCompatibilityRulesetId(rulesetId);
        return All
            .Where(command => string.Equals(command.RulesetId, effectiveRulesetId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ResolveCompatibilityRulesetId(string? rulesetId)
    {
        return RulesetDefaults.NormalizeOptional(rulesetId) ?? RulesetDefaults.Sr5;
    }

    private static IReadOnlyList<AppCommandDefinition> CreateSr5Catalog()
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
