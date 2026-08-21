using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerEdgeUseRulesTests
{
    [TestMethod]
    public void Career_spend_and_regain_follow_exact_legacy_bounds()
    {
        Assert.IsTrue(CharacterCareerEdgeUseRules.TryProject(
            created: true,
            edgeUsed: 1,
            totalEdge: 4,
            out CharacterCareerEdgeUseState? state));
        Assert.IsNotNull(state);
        Assert.AreEqual(3, state.AvailableEdge);
        Assert.IsTrue(state.CanSpend);
        Assert.IsTrue(state.CanRegain);
        Assert.AreEqual(2, CharacterCareerEdgeUseRules.Apply(state, CharacterCareerEdgeUseAction.Spend));
        Assert.AreEqual(0, CharacterCareerEdgeUseRules.Apply(state, CharacterCareerEdgeUseAction.Regain));
    }

    [TestMethod]
    public void Creation_and_out_of_bounds_actions_fail_closed()
    {
        Assert.IsFalse(CharacterCareerEdgeUseRules.TryProject(false, 0, 4, out _));
        Assert.IsTrue(CharacterCareerEdgeUseRules.TryProject(true, 4, 4, out CharacterCareerEdgeUseState? spent));
        Assert.IsNotNull(spent);
        Assert.IsFalse(spent.CanSpend);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CharacterCareerEdgeUseRules.Apply(spent, CharacterCareerEdgeUseAction.Spend));

        Assert.IsTrue(CharacterCareerEdgeUseRules.TryProject(true, 0, 4, out CharacterCareerEdgeUseState? full));
        Assert.IsNotNull(full);
        Assert.IsFalse(full.CanRegain);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CharacterCareerEdgeUseRules.Apply(full, CharacterCareerEdgeUseAction.Regain));
    }

    [TestMethod]
    public void Legacy_overused_state_can_regain_but_cannot_spend()
    {
        Assert.IsTrue(CharacterCareerEdgeUseRules.TryProject(true, 5, 4, out CharacterCareerEdgeUseState? state));
        Assert.IsNotNull(state);
        Assert.AreEqual(0, state.AvailableEdge);
        Assert.IsFalse(state.CanSpend);
        Assert.IsTrue(state.CanRegain);
    }
}
