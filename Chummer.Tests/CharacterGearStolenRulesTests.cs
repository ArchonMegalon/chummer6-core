using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearStolenRulesTests
{
    private static readonly Guid ParentId = Guid.Parse("a7111111-7111-7111-7111-711111111111");
    private static readonly Guid ChildId = Guid.Parse("b7111111-7111-7111-7111-711111111111");

    [TestMethod]
    public void Creation_eligible_recursive_gear_projects_exact_stable_identity()
    {
        var identity = new CharacterGearStolenIdentity([ParentId, ChildId]);

        Assert.IsTrue(CharacterGearStolenRules.TryCreateState(
            identity,
            created: false,
            hasStolenNuyenImprovement: true,
            displayPath: "Parent > Child",
            stolen: false,
            out CharacterGearStolenState state));
        Assert.IsTrue(CharacterGearStolenRules.IdentityEquals(identity, state.Identity));
        Assert.AreEqual(CharacterGearStolenRules.RevisionHexLength, state.Revision.Length);
    }

    [TestMethod]
    public void Career_missing_eligibility_and_invalid_hierarchy_fail_closed()
    {
        var valid = new CharacterGearStolenIdentity([ParentId]);
        Assert.IsFalse(CharacterGearStolenRules.TryCreateState(
            valid, true, true, "Parent", false, out _));
        Assert.IsFalse(CharacterGearStolenRules.TryCreateState(
            valid, false, false, "Parent", false, out _));
        Assert.IsFalse(CharacterGearStolenRules.IsValidIdentity(
            new CharacterGearStolenIdentity([])));
        Assert.IsFalse(CharacterGearStolenRules.IsValidIdentity(
            new CharacterGearStolenIdentity([ParentId, ParentId])));
        Assert.IsFalse(CharacterGearStolenRules.IsValidIdentity(
            new CharacterGearStolenIdentity([Guid.Empty])));
    }

    [TestMethod]
    public void Revision_binds_full_path_and_stolen_value_and_rejects_stale_or_noop()
    {
        var identity = new CharacterGearStolenIdentity([ParentId, ChildId]);
        Assert.IsTrue(CharacterGearStolenRules.TryCreateState(
            identity, false, true, "Parent > Child", false, out CharacterGearStolenState original));
        Assert.IsTrue(CharacterGearStolenRules.TryCreateState(
            new CharacterGearStolenIdentity([ChildId]),
            false,
            true,
            "Child",
            false,
            out CharacterGearStolenState moved));
        Assert.IsTrue(CharacterGearStolenRules.TryCreateState(
            identity, false, true, "Parent > Child", true, out CharacterGearStolenState changed));

        Assert.AreNotEqual(original.Revision, moved.Revision);
        Assert.AreNotEqual(original.Revision, changed.Revision);
        Assert.IsFalse(CharacterGearStolenRules.TryValidateMutation(
            original, new string('0', 64), true));
        Assert.IsFalse(CharacterGearStolenRules.TryValidateMutation(
            original, original.Revision, false));
        Assert.IsTrue(CharacterGearStolenRules.TryValidateMutation(
            original, original.Revision, true));
    }
}
