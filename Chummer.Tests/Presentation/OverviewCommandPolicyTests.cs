using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Chummer.Tests.Presentation;

[TestClass]
public class OverviewCommandPolicyTests
{
    [TestMethod]
    public void Known_shared_command_policy_covers_all_app_catalog_commands()
    {
        string[] missing = AppCommandCatalog.All
            .Select(command => command.Id)
            .Where(commandId => !OverviewCommandPolicy.IsKnownSharedCommand(commandId))
            .OrderBy(commandId => commandId)
            .ToArray();

        Assert.IsEmpty(
            missing,
            "App commands missing shared presenter command policy coverage: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void Refresh_character_is_treated_as_known_shared_command()
    {
        Assert.IsTrue(OverviewCommandPolicy.IsKnownSharedCommand("refresh_character"));
    }

    [TestMethod]
    public void Switch_ruleset_is_treated_as_known_shared_command()
    {
        Assert.IsTrue(OverviewCommandPolicy.IsKnownSharedCommand("switch_ruleset"));
    }

    [TestMethod]
    public void Runtime_inspector_is_treated_as_known_shared_command()
    {
        Assert.IsTrue(OverviewCommandPolicy.IsKnownSharedCommand(OverviewCommandPolicy.RuntimeInspectorCommandId));
        Assert.IsTrue(OverviewCommandPolicy.IsRuntimeInspectorCommand(OverviewCommandPolicy.RuntimeInspectorCommandId));
    }

    [TestMethod]
    public void Unknown_command_is_not_marked_as_known()
    {
        Assert.IsFalse(OverviewCommandPolicy.IsKnownSharedCommand("totally_unknown_command"));
    }
}
