#nullable enable annotations

using System;
using System.Linq;
using Chummer.Contracts.Content;
using Chummer.Application.Seeds;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Chummer.Rulesets.Hosting.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class ShellCatalogAndRulesetDetectionTests
{
    [TestMethod]
    public void Default_aesthetic_digest_service_projects_ruleset_specific_defaults()
    {
        DefaultAestheticDigestService service = new();

        var sr4 = service.BuildDigest(" char-4 ", " SR4 ");
        var sr5 = service.BuildDigest("char-5", "");
        var sr6 = service.BuildDigest("char-6", "sr6");

        Assert.AreEqual("char-4", sr4.CharacterId);
        CollectionAssert.AreEqual(new[] { "street" }, sr4.RoleTags.ToArray());
        CollectionAssert.AreEqual(new[] { "classic" }, sr4.BuildTags.ToArray());
        CollectionAssert.AreEqual(new[] { "retro-street" }, sr4.OutfitArchetypeTags.ToArray());
        CollectionAssert.AreEqual(new[] { "chrome" }, sr4.MotifTags.ToArray());

        CollectionAssert.AreEqual(new[] { "generalist" }, sr5.RoleTags.ToArray());
        CollectionAssert.AreEqual(new[] { "balanced" }, sr5.BuildTags.ToArray());
        CollectionAssert.AreEqual(new[] { "street" }, sr5.OutfitArchetypeTags.ToArray());
        CollectionAssert.AreEqual(new[] { "neon" }, sr5.MotifTags.ToArray());

        CollectionAssert.AreEqual(new[] { "hybrid" }, sr6.RoleTags.ToArray());
        CollectionAssert.AreEqual(new[] { "agile" }, sr6.BuildTags.ToArray());
        CollectionAssert.AreEqual(new[] { "sleek" }, sr6.OutfitArchetypeTags.ToArray());
        CollectionAssert.AreEqual(new[] { "neon" }, sr6.MotifTags.ToArray());
    }

    [TestMethod]
    public void Workspace_ruleset_detection_prefers_payload_kind_when_present()
    {
        Assert.AreEqual(RulesetDefaults.Sr4, WorkspaceRulesetDetection.Detect("sr4", "{}"));
        Assert.AreEqual(RulesetDefaults.Sr5, WorkspaceRulesetDetection.Detect("sr5/character", "{}"));
        Assert.AreEqual(RulesetDefaults.Sr6, WorkspaceRulesetDetection.Detect("sr6/import", "{}"));
    }

    [TestMethod]
    public void Workspace_ruleset_detection_reads_nested_json_ruleset_id_and_normalizes_case()
    {
        const string payload = """
            {
              "workspace": {
                "metadata": {
                  "RulesetId": " SR6 "
                }
              }
            }
            """;

        Assert.AreEqual(RulesetDefaults.Sr6, WorkspaceRulesetDetection.Detect(null, payload));
    }

    [TestMethod]
    public void Workspace_ruleset_detection_falls_back_to_hero_lab_online_json_and_xml_markers()
    {
        const string heroLabJson = """
            {
              "metadata": {
                "gameCode": "Shadowrun 6"
              }
            }
            """;
        const string xmlPayload = "<character><edition>Shadowrun 5th Edition</edition></character>";

        Assert.AreEqual(RulesetDefaults.Sr6, WorkspaceRulesetDetection.Detect(null, heroLabJson));
        Assert.AreEqual(RulesetDefaults.Sr5, WorkspaceRulesetDetection.Detect(null, xmlPayload));
    }

    [TestMethod]
    public void Workspace_ruleset_detection_returns_null_for_blank_or_invalid_payloads()
    {
        Assert.IsNull(WorkspaceRulesetDetection.Detect(null, null));
        Assert.IsNull(WorkspaceRulesetDetection.Detect(" ", " "));
        Assert.IsNull(WorkspaceRulesetDetection.Detect(null, "{ invalid json"));
        Assert.IsNull(WorkspaceRulesetDetection.Detect(null, "<character><edition>Unknown</edition></character>"));
    }

    [TestMethod]
    public void App_command_catalog_contains_expected_groups_and_runtime_entries()
    {
        var commands = AppCommandCatalog.All;

        Assert.IsTrue(commands.Count > 35);
        Assert.IsTrue(commands.All(command => command.RulesetId == RulesetDefaults.Sr5));
        Assert.IsTrue(commands.Any(command => command.Id == "new_character" && command.Group == "file" && !command.RequiresOpenCharacter));
        Assert.IsTrue(commands.Any(command => command.Id == "print_character" && command.Group == "file" && command.RequiresOpenCharacter));
        Assert.IsTrue(commands.Any(command => command.Id == AppCommandIds.RuntimeInspector && command.Group == "tools"));
        Assert.IsTrue(commands.Any(command => command.Id == "restart" && command.Group == "help"));
    }

    [TestMethod]
    public void Navigation_tab_catalog_keeps_expected_sections_and_groups()
    {
        var tabs = NavigationTabCatalog.All;

        Assert.IsTrue(tabs.Count >= 19);
        Assert.IsTrue(tabs.All(tab => tab.RulesetId == RulesetDefaults.Sr5));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-create" && tab.SectionId == "build-lab" && tab.Group == "character"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-magician" && tab.SectionId == "spells"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-streetgear" && tab.SectionId == "gear"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-gear" && tab.SectionId == "gear"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-relationships" && tab.SectionId == "relationships"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-karma" && tab.SectionId == "karmasummary"));
        Assert.IsTrue(tabs.Any(tab => tab.Id == "tab-calendar" && tab.SectionId == "calendar"));
    }

    [TestMethod]
    public void Workspace_surface_action_catalog_keeps_summary_validate_metadata_and_command_routes()
    {
        var actions = WorkspaceSurfaceActionCatalog.All;

        Assert.IsTrue(actions.Count > 70);
        Assert.IsTrue(actions.All(action => action.RulesetId == RulesetDefaults.Sr5));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-info.summary" && action.Kind == WorkspaceSurfaceActionKind.Summary));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-info.validate" && action.Kind == WorkspaceSurfaceActionKind.Validate));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-info.metadata" && action.Kind == WorkspaceSurfaceActionKind.Metadata));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-notes.data_exporter" && action.Kind == WorkspaceSurfaceActionKind.Command && action.TargetId == "data_exporter"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-streetgear" && action.TargetId == "gear"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-gear" && action.TargetId == "gear"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-relationships" && action.TargetId == "relationships"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-karma" && action.TargetId == "karmasummary"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-magician" && action.TargetId == "spirits"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-technomancer" && action.TargetId == "sprites"));
        Assert.IsTrue(actions.Any(action => action.TabId == "tab-combat" && action.TargetId == "conditionmonitor"));
    }

    [TestMethod]
    public void Catalogs_filter_and_fallback_to_sr5_compatibility_defaults()
    {
        var sr5Commands = AppCommandCatalog.ForRuleset(" SR5 ");
        var defaultCommands = AppCommandCatalog.ForRuleset(null);
        var sr5Tabs = NavigationTabCatalog.ForRuleset("shadowrun 5");
        var defaultActions = WorkspaceSurfaceActionCatalog.ForRuleset(string.Empty);
        var requestedGearActions = WorkspaceSurfaceActionCatalog.ForTab("tab-gear", "sr5");
        var fallbackActions = WorkspaceSurfaceActionCatalog.ForTab("tab-unknown", "sr5");

        Assert.AreEqual(AppCommandCatalog.All.Count, sr5Commands.Count);
        Assert.AreEqual(defaultCommands.Count, sr5Commands.Count);
        Assert.AreEqual(NavigationTabCatalog.All.Count, sr5Tabs.Count);
        Assert.AreEqual(WorkspaceSurfaceActionCatalog.All.Count, defaultActions.Count);
        Assert.IsTrue(requestedGearActions.All(action => action.TabId == "tab-gear"));
        Assert.IsTrue(requestedGearActions.Any(action => action.TargetId == "inventory"));
        Assert.IsTrue(fallbackActions.All(action => action.TabId == "tab-info"));
        Assert.IsTrue(fallbackActions.Any(action => action.Kind == WorkspaceSurfaceActionKind.Summary));
    }

    [TestMethod]
    public void Sr5_ruleset_catalog_provider_exposes_expected_workflows_and_surfaces()
    {
        Sr5RulesetCatalogProvider provider = new();

        var workflows = provider.GetWorkflowDefinitions();
        var surfaces = provider.GetWorkflowSurfaces();

        Assert.HasCount(5, workflows);
        Assert.HasCount(6, surfaces);
        Assert.IsTrue(workflows.Any(workflow =>
            workflow.WorkflowId == WorkflowDefinitionIds.LibraryShell
            && !workflow.RequiresOpenWorkspace
            && workflow.SurfaceIds.Contains("sr5.shell.menu")));
        Assert.IsTrue(workflows.Any(workflow =>
            workflow.WorkflowId == WorkflowDefinitionIds.CareerWorkbench
            && workflow.RequiresOpenWorkspace
            && workflow.SurfaceIds.Contains("sr5.career.section")));
        Assert.IsTrue(workflows.Any(workflow =>
            workflow.WorkflowId == WorkflowDefinitionIds.SessionDashboard
            && workflow.RequiresOpenWorkspace
            && workflow.MobileOptimized));
        Assert.IsTrue(surfaces.Any(surface =>
            surface.SurfaceId == "sr5.shell.menu"
            && surface.WorkflowId == WorkflowDefinitionIds.LibraryShell
            && surface.Kind == WorkflowSurfaceKinds.ShellRegion
            && surface.RegionId == ShellRegionIds.MenuBar
            && surface.LayoutToken == WorkflowLayoutTokens.ShellFrame
            && surface.ActionIds.Contains("file")));
        Assert.IsTrue(surfaces.Any(surface =>
            surface.SurfaceId == "sr5.career.section"
            && surface.WorkflowId == WorkflowDefinitionIds.CareerWorkbench
            && surface.Kind == WorkflowSurfaceKinds.Workbench
            && surface.RegionId == ShellRegionIds.SectionPane
            && surface.ActionIds.Contains("tab-create.intake")));
        Assert.IsTrue(surfaces.Any(surface =>
            surface.SurfaceId == "sr5.session.summary"
            && surface.WorkflowId == WorkflowDefinitionIds.SessionDashboard
            && surface.Kind == WorkflowSurfaceKinds.Dashboard
            && surface.RegionId == ShellRegionIds.SummaryHeader
            && surface.ActionIds.Contains("tab-info.validate")));
    }

    [TestMethod]
    public void Sr5_shell_catalogs_keep_unique_ids_and_cross_linked_actions()
    {
        var commands = AppCommandCatalog.All;
        var tabs = NavigationTabCatalog.All;
        var actions = WorkspaceSurfaceActionCatalog.All;

        Assert.AreEqual(commands.Count, commands.Select(command => command.Id).Distinct().Count());
        Assert.AreEqual(tabs.Count, tabs.Select(tab => tab.Id).Distinct().Count());
        Assert.AreEqual(actions.Count, actions.Select(action => action.Id).Distinct().Count());
        Assert.IsTrue(actions.All(action => tabs.Any(tab => tab.Id == action.TabId)));
        Assert.IsTrue(actions.Any(action => action.TargetId == "build-lab"));
        Assert.IsTrue(actions.Any(action => action.TargetId == "cyberwares"));
        Assert.IsTrue(actions.Any(action => action.TargetId == "vehiclemods"));
        Assert.IsTrue(actions.Any(action => action.TargetId == "customdatadirectorynames"));
        Assert.IsTrue(actions.Any(action => action.TargetId == "data_exporter" && action.Kind == WorkspaceSurfaceActionKind.Command));
        Assert.IsTrue(commands.Any(command => command.Id == "hero_lab_importer" && !command.RequiresOpenCharacter));
        Assert.IsTrue(commands.Any(command => command.Id == "data_exporter" && command.RequiresOpenCharacter));
    }

    [TestMethod]
    public void Sr5_ruleset_plugin_exposes_expected_serializer_shell_catalog_and_descriptor_surfaces()
    {
        Sr5RulesetPlugin plugin = new();

        WorkspacePayloadEnvelope envelope = plugin.Serializer.Wrap(" character ", "<character />");
        var commands = plugin.ShellDefinitions.GetCommands();
        var tabs = plugin.ShellDefinitions.GetNavigationTabs();
        var actions = plugin.Catalogs.GetWorkspaceActions();
        var descriptors = plugin.CapabilityDescriptors.GetCapabilityDescriptors();

        Assert.AreEqual(RulesetDefaults.Sr5, plugin.Id.Value);
        Assert.AreEqual("Shadowrun 5", plugin.DisplayName);
        Assert.AreEqual(RulesetDefaults.Sr5, envelope.RulesetId);
        Assert.AreEqual(1, envelope.SchemaVersion);
        Assert.AreEqual("character", envelope.PayloadKind);
        Assert.AreEqual("<character />", envelope.Payload);
        Assert.AreEqual(AppCommandCatalog.All.Count, commands.Count);
        Assert.AreEqual(NavigationTabCatalog.All.Count, tabs.Count);
        Assert.AreEqual(WorkspaceSurfaceActionCatalog.All.Count, actions.Count);
        Assert.HasCount(3, descriptors);
        Assert.IsTrue(descriptors.Any(descriptor =>
            descriptor.CapabilityId == RulePackCapabilityIds.DeriveStat
            && descriptor.InvocationKind == RulesetCapabilityInvocationKinds.Rule
            && descriptor.TitleKey == "ruleset.capability.derive.stat.title"
            && descriptor.DefaultGasBudget.ProviderInstructionLimit == 1_000
            && descriptor.MaximumGasBudget.ProviderInstructionLimit == 5_000));
        Assert.IsTrue(descriptors.Any(descriptor =>
            descriptor.CapabilityId == RulePackCapabilityIds.SessionQuickActions
            && descriptor.InvocationKind == RulesetCapabilityInvocationKinds.Script
            && descriptor.SessionSafe
            && descriptor.TitleKey == "ruleset.capability.session.quick-actions.title"));
    }

    [TestMethod]
    public void Sr6_ruleset_plugin_matches_sr5_shell_breadth_and_restores_matrix_support_tabs()
    {
        Sr5RulesetPlugin sr5 = new();
        Sr6RulesetPlugin sr6 = new();

        string[] sr5CommandIds = sr5.ShellDefinitions.GetCommands().Select(static command => command.Id).ToArray();
        string[] sr6CommandIds = sr6.ShellDefinitions.GetCommands().Select(static command => command.Id).ToArray();
        string[] sr5TabIds = sr5.ShellDefinitions.GetNavigationTabs().Select(static tab => tab.Id).ToArray();
        string[] sr6TabIds = sr6.ShellDefinitions.GetNavigationTabs().Select(static tab => tab.Id).ToArray();
        string[] sr5ActionIds = sr5.Catalogs.GetWorkspaceActions().Select(static action => action.Id).ToArray();
        string[] sr6ActionIds = sr6.Catalogs.GetWorkspaceActions().Select(static action => action.Id).ToArray();

        CollectionAssert.AreEqual(sr5CommandIds, sr6CommandIds);
        CollectionAssert.AreEqual(sr5TabIds, sr6TabIds);
        CollectionAssert.AreEqual(sr5ActionIds, sr6ActionIds);
        Assert.IsTrue(sr6.ShellDefinitions.GetCommands().All(command => command.RulesetId == RulesetDefaults.Sr6));
        Assert.IsTrue(sr6.ShellDefinitions.GetNavigationTabs().All(tab => tab.RulesetId == RulesetDefaults.Sr6));
        Assert.IsTrue(sr6.Catalogs.GetWorkspaceActions().All(action => action.RulesetId == RulesetDefaults.Sr6));
        Assert.IsTrue(sr6TabIds.Contains("tab-technomancer", StringComparer.Ordinal));
        Assert.IsTrue(sr6TabIds.Contains("tab-streetgear", StringComparer.Ordinal));
        Assert.IsTrue(sr6TabIds.Contains("tab-relationships", StringComparer.Ordinal));
        Assert.IsTrue(sr6TabIds.Contains("tab-karma", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-technomancer.complexforms", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-technomancer.sprites", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-technomancer.aiprograms", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-streetgear.gear", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-relationships.relationships", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-relationships.enemies", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-relationships.pets", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-karma.summary", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-combat.conditionmonitor", StringComparer.Ordinal));
        Assert.IsTrue(sr6ActionIds.Contains("tab-info.spelldefense", StringComparer.Ordinal));
        Assert.IsFalse(sr6ActionIds.Contains("tab-adept.complexforms", StringComparer.Ordinal));
        Assert.IsFalse(sr6ActionIds.Contains("tab-adept.aiprograms", StringComparer.Ordinal));
    }

    [TestMethod]
    public void Sr5_workspace_surface_action_catalog_materializes_broad_tab_routes()
    {
        var actions = WorkspaceSurfaceActionCatalog.All;

        Assert.IsTrue(actions.Any(action => action.Id == "tab-create.intake" && action.TargetId == "build-lab"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-calendar.calendar" && action.TargetId == "calendar"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-gear.customdatadirectorynames" && action.TargetId == "customdatadirectorynames"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-magician.spirits" && action.TargetId == "spirits"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-technomancer.sprites" && action.TargetId == "sprites"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-magician.arts" && action.TargetId == "arts"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-attributes.limitmodifiers" && action.TargetId == "limitmodifiers"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-combat.drugs" && action.TargetId == "drugs"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-streetgear.gear" && action.TargetId == "gear"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-armor.armormods" && action.TargetId == "armormods"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-relationships.relationships" && action.TargetId == "relationships"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-relationships.contacts" && action.TargetId == "contacts"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-relationships.enemies" && action.TargetId == "enemies"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-relationships.pets" && action.TargetId == "pets"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-vehicles.vehiclelocations" && action.TargetId == "vehiclelocations"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-lifestyle.sources" && action.TargetId == "sources"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-karma.summary" && action.TargetId == "karmasummary"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-combat.conditionmonitor" && action.TargetId == "conditionmonitor"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-info.spelldefense" && action.TargetId == "spelldefense"));
        Assert.IsTrue(actions.Any(action => action.Id == "tab-improvements.progress" && action.TargetId == "progress"));
    }
}
