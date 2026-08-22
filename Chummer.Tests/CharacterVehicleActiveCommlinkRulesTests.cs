using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleActiveCommlinkRulesTests
{
    private static readonly Guid VehicleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public void TryProject_ProvesTopLevelPersonaVehicleWithZeroEconomics()
    {
        foreach ((bool created, CharacterVehicleActiveCommlinkPhase expectedPhase) in new[]
        {
            (false, CharacterVehicleActiveCommlinkPhase.Creation),
            (true, CharacterVehicleActiveCommlinkPhase.Career)
        })
        {
            XElement character = Character(created);
            XElement vehicle = character.Element("vehicles")!.Element("vehicle")!;

            Assert.IsTrue(CharacterVehicleActiveCommlinkRules.TryProject(
                character, vehicle, created, out CharacterVehicleActiveCommlinkSemantics state));
            Assert.AreEqual(VehicleId, state.VehicleId);
            Assert.AreEqual(expectedPhase, state.Phase);
            Assert.IsFalse(state.ActiveCommlink);
            Assert.IsTrue(state.IsCommlink);
            Assert.IsTrue(state.Visible);
            Assert.IsTrue(state.Enabled);
            Assert.AreEqual(new CharacterVehicleActiveCommlinkEconomics(0m, 0), state.Economics);
        }
    }

    [TestMethod]
    public void TryProject_FailsClosedForDescendantAndDuplicateIdentity()
    {
        XElement character = Character(created: false);
        XElement vehicle = character.Element("vehicles")!.Element("vehicle")!;
        XElement descendant = new("vehicle",
            new XElement("guid", "99999999-8888-7777-6666-555555555555"),
            new XElement("pilot", "3"),
            new XElement("active", "False"),
            new XElement("gears", PersonaGear()));
        vehicle.Add(new XElement("children", descendant));

        Assert.IsFalse(CharacterVehicleActiveCommlinkRules.TryProject(
            character, descendant, created: false, out _));

        vehicle.AddAfterSelf(new XElement(vehicle));
        Assert.IsFalse(CharacterVehicleActiveCommlinkRules.TryProject(
            character, vehicle, created: false, out _));
    }

    [TestMethod]
    public void TryProject_FailsClosedForAmbiguousCharacterWideActiveState()
    {
        foreach (string secondActive in new[] { "not-a-bool", "True" })
        {
            XElement character = Character(created: false);
            character.Add(new XElement("gears",
                new XElement("gear",
                    new XElement("guid", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    new XElement("active", secondActive))));

            Assert.IsFalse(CharacterVehicleActiveCommlinkRules.TryProject(
                character,
                character.Element("vehicles")!.Element("vehicle")!,
                created: false,
                out _));
        }
    }

    [TestMethod]
    public void TryProject_FailsClosedWhenVehiclePersonaAuthorityNeedsUnsavedModSource()
    {
        XElement character = Character(created: false);
        XElement vehicle = character.Element("vehicles")!.Element("vehicle")!;
        vehicle.Add(new XElement("mods", new XElement("mod", new XElement("name", "Unknown"))));

        Assert.IsFalse(CharacterVehicleActiveCommlinkRules.TryProject(
            character, vehicle, created: false, out _));
    }

    [TestMethod]
    public void TryProject_ProjectsHiddenDisabledStateForNonPersonaVehicle()
    {
        XElement character = Character(created: true);
        XElement vehicle = character.Element("vehicles")!.Element("vehicle")!;
        vehicle.Element("gears")!.Remove();

        Assert.IsTrue(CharacterVehicleActiveCommlinkRules.TryProject(
            character, vehicle, created: true, out CharacterVehicleActiveCommlinkSemantics state));
        Assert.IsFalse(state.IsCommlink);
        Assert.IsFalse(state.Visible);
        Assert.IsFalse(state.Enabled);
    }

    private static XElement Character(bool created) => new("character",
        new XElement("created", created),
        new XElement("vehicles",
            new XElement("vehicle",
                new XElement("guid", VehicleId.ToString("D")),
                new XElement("name", "Roadmaster"),
                new XElement("pilot", "3"),
                new XElement("active", "False"),
                new XElement("gears", PersonaGear()))));

    private static XElement PersonaGear() => new("gear",
        new XElement("guid", "01234567-89ab-cdef-0123-456789abcdef"),
        new XElement("canformpersona", "Parent"),
        new XElement("equipped", "True"));
}
