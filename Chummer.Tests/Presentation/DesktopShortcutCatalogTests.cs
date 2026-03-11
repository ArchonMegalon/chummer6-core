using Chummer.Presentation.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class DesktopShortcutCatalogTests
{
    [TestMethod]
    public void TryResolveCommandId_maps_known_command_modifier_shortcuts()
    {
        AssertShortcut("s", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "save_character");
        AssertShortcut("S", commandModifier: true, shiftModifier: true, altModifier: false, expectedCommandId: "save_character_as");
        AssertShortcut("w", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "close_window");
        AssertShortcut("g", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "global_settings");
        AssertShortcut("o", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "open_character");
        AssertShortcut("n", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "new_character");
        AssertShortcut("N", commandModifier: true, shiftModifier: true, altModifier: false, expectedCommandId: "new_critter");
        AssertShortcut("p", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "print_character");
        AssertShortcut("r", commandModifier: true, shiftModifier: false, altModifier: false, expectedCommandId: "refresh_character");
    }

    [TestMethod]
    public void TryResolveCommandId_maps_f5_without_command_modifier()
    {
        AssertShortcut("F5", commandModifier: false, shiftModifier: false, altModifier: false, expectedCommandId: "refresh_character");
    }

    [TestMethod]
    public void TryResolveCommandId_rejects_unknown_or_alt_shortcuts()
    {
        AssertNoShortcut("x", commandModifier: true, shiftModifier: false, altModifier: false);
        AssertNoShortcut("s", commandModifier: true, shiftModifier: false, altModifier: true);
        AssertNoShortcut(string.Empty, commandModifier: true, shiftModifier: false, altModifier: false);
    }

    private static void AssertShortcut(
        string key,
        bool commandModifier,
        bool shiftModifier,
        bool altModifier,
        string expectedCommandId)
    {
        bool resolved = DesktopShortcutCatalog.TryResolveCommandId(
            key,
            commandModifier,
            shiftModifier,
            altModifier,
            out string commandId);

        Assert.IsTrue(resolved);
        Assert.AreEqual(expectedCommandId, commandId);
    }

    private static void AssertNoShortcut(
        string key,
        bool commandModifier,
        bool shiftModifier,
        bool altModifier)
    {
        bool resolved = DesktopShortcutCatalog.TryResolveCommandId(
            key,
            commandModifier,
            shiftModifier,
            altModifier,
            out string commandId);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, commandId);
    }
}
