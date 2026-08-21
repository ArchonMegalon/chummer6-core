using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGroupMembershipRulesTests
{
    [TestMethod]
    public void Creation_toggle_is_direct_and_does_not_spend_current_karma()
    {
        Assert.IsTrue(CharacterGroupMembershipRules.TryProject(
            false, false, true, false, 0, null, null,
            out CharacterGroupMembershipState? state));
        Assert.IsNotNull(state);
        Assert.IsTrue(state.CanChange);
        Assert.IsFalse(state.RequiresConfirmation);
        Assert.AreEqual(0, state.TransitionKarmaCost);
    }

    [TestMethod]
    public void Career_magician_uses_exact_profile_cost_and_available_karma()
    {
        Assert.IsTrue(CharacterGroupMembershipRules.TryProject(
            false, true, true, false, 4, 5, 1,
            out CharacterGroupMembershipState? blocked));
        Assert.IsNotNull(blocked);
        Assert.IsFalse(blocked.CanChange);
        Assert.IsTrue(blocked.RequiresConfirmation);

        Assert.IsTrue(CharacterGroupMembershipRules.TryProject(
            false, true, true, false, 5, 5, 1,
            out CharacterGroupMembershipState? allowed));
        Assert.IsNotNull(allowed);
        Assert.IsTrue(allowed.CanChange);
        Assert.AreEqual(5, allowed.TransitionKarmaCost);
    }

    [TestMethod]
    public void Career_non_magician_membership_is_cost_free_like_legacy_network_flow()
    {
        Assert.IsTrue(CharacterGroupMembershipRules.TryProject(
            false, true, false, true, -3, null, null,
            out CharacterGroupMembershipState? state));
        Assert.IsNotNull(state);
        Assert.IsTrue(state.CanChange);
        Assert.IsFalse(state.RequiresConfirmation);
        Assert.AreEqual(0, state.TransitionKarmaCost);
    }
}
