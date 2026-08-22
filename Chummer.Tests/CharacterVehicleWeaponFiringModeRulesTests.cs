using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleWeaponFiringModeRulesTests
{
    private static readonly CharacterVehicleWeaponFiringModeIdentity Identity = new(
        Guid.Parse("93333333-3333-4333-8333-333333333333"),
        Guid.Parse("94444444-4444-4444-8444-444444444444"));

    [TestMethod]
    public void All_five_legacy_modes_are_typed_for_creation_and_career_with_zero_economics()
    {
        foreach (CharacterVehicleWeaponFiringMode mode in Enum.GetValues<CharacterVehicleWeaponFiringMode>())
        {
            Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.TryCreateState(
                Identity, false, "Creation Turret", mode.ToString(), "Ranged", "30(c)", out var creation));
            Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.TryCreateState(
                Identity, true, "Career Turret", mode.ToString(), "Ranged", "30(c)", out var career));
            Assert.AreEqual(CharacterVehicleWeaponFiringModePhase.Creation, creation.Phase);
            Assert.AreEqual(CharacterVehicleWeaponFiringModePhase.Career, career.Phase);
            Assert.AreEqual(mode, creation.FiringMode);
            Assert.AreEqual(0m, career.Economics.NuyenDelta);
            Assert.AreEqual(0, career.Economics.KarmaDelta);
            Assert.AreNotEqual(creation.Revision, career.Revision);
        }
    }

    [TestMethod]
    public void Legacy_visibility_accepts_ranged_and_ammo_bearing_melee_only()
    {
        Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.IsLegacyEditorVisible("Ranged", "0"));
        Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.IsLegacyEditorVisible("Melee", "1"));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.IsLegacyEditorVisible("Melee", "0"));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.IsLegacyEditorVisible("Thrown", "1"));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.IsLegacyEditorVisible("melee", "1"));
    }

    [TestMethod]
    public void Mutation_is_revision_bound_distinct_and_limited_to_the_five_legacy_modes()
    {
        Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.TryCreateState(
            Identity, false, "Turret", "dogbrain", "Ranged", "30(c)", out var state));
        Assert.AreEqual(CharacterVehicleWeaponFiringMode.DogBrain, state.FiringMode);
        Assert.IsTrue(CharacterVehicleWeaponFiringModeRules.TryValidateMutation(
            state, state.Revision, CharacterVehicleWeaponFiringMode.RemoteOperated));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryValidateMutation(
            state, state.Revision, CharacterVehicleWeaponFiringMode.DogBrain));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryValidateMutation(
            state, new string('0', 64), CharacterVehicleWeaponFiringMode.RemoteOperated));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryValidateMutation(
            state, state.Revision, (CharacterVehicleWeaponFiringMode)999));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryParseSavedValue("NumFiringModes", out _));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryParseSavedValue("burst", out _));
    }

    [TestMethod]
    public void Missing_or_ambiguous_typed_identity_and_hidden_weapons_fail_closed()
    {
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryCreateState(
            new(Guid.Empty, Identity.WeaponId), false, "Turret", "Skill", "Ranged", "30(c)", out _));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryCreateState(
            new(Identity.VehicleId, Identity.VehicleId), false, "Turret", "Skill", "Ranged", "30(c)", out _));
        Assert.IsFalse(CharacterVehicleWeaponFiringModeRules.TryCreateState(
            Identity, false, "Turret", "Skill", "Melee", "0", out _));
    }
}
