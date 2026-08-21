using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearEquipmentRulesTests
{
    private static readonly Guid ParentId = Guid.Parse("a9121111-9111-4111-8111-911111111111");
    private static readonly Guid ChildId = Guid.Parse("b9121111-9111-4111-8111-911111111111");

    [TestMethod]
    public void Create_and_career_project_exact_zero_economic_state()
    {
        var identity = new CharacterGearEquipmentIdentity([ParentId, ChildId]);

        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            identity, false, false, false, "Parent > Child", true,
            out CharacterGearEquipmentState creation));
        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            identity, true, false, false, "Parent > Child", true,
            out CharacterGearEquipmentState career));

        Assert.AreEqual(CharacterGearEquipmentPhase.Creation, creation.Phase);
        Assert.AreEqual(CharacterGearEquipmentPhase.Career, career.Phase);
        Assert.AreEqual(0m, creation.Economics.NuyenDelta);
        Assert.AreEqual(0, career.Economics.KarmaDelta);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void Included_or_clip_loaded_gear_is_projected_but_cannot_mutate()
    {
        var identity = new CharacterGearEquipmentIdentity([ParentId]);
        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            identity, false, true, false, "Included", true,
            out CharacterGearEquipmentState included));
        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            identity, true, false, true, "Ammo", true,
            out CharacterGearEquipmentState loaded));

        Assert.IsFalse(included.CanChangeEquip);
        Assert.IsFalse(loaded.CanChangeEquip);
        Assert.IsFalse(CharacterGearEquipmentRules.TryValidateMutation(
            included, included.Revision, false));
        Assert.IsFalse(CharacterGearEquipmentRules.TryValidateMutation(
            loaded, loaded.Revision, false));
    }

    [TestMethod]
    public void Revision_binds_path_phase_value_and_eligibility()
    {
        var identity = new CharacterGearEquipmentIdentity([ParentId, ChildId]);
        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            identity, true, false, false, "Parent > Child", false,
            out CharacterGearEquipmentState current));
        Assert.IsTrue(CharacterGearEquipmentRules.TryCreateState(
            new CharacterGearEquipmentIdentity([ChildId]), true, false, false, "Child", false,
            out CharacterGearEquipmentState moved));

        Assert.AreNotEqual(current.Revision, moved.Revision);
        Assert.IsFalse(CharacterGearEquipmentRules.TryValidateMutation(
            current, new string('0', 64), true));
        Assert.IsFalse(CharacterGearEquipmentRules.TryValidateMutation(
            current, current.Revision, false));
        Assert.IsTrue(CharacterGearEquipmentRules.TryValidateMutation(
            current, current.Revision, true));
        Assert.IsFalse(CharacterGearEquipmentRules.IsValidIdentity(
            new CharacterGearEquipmentIdentity([ParentId, ParentId])));
    }
}
