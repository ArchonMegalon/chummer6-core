using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterImprovementGroupAddRulesTests
{
    [TestMethod]
    public void Career_state_uses_ordered_collection_revision_and_zero_economics()
    {
        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateState(
            created: true,
            groups: ["Alpha", "Beta"],
            out CharacterImprovementGroupAddState state));

        Assert.AreEqual(CharacterImprovementGroupAddRules.RevisionHexLength, state.Revision.Length);
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);

        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateState(
            true,
            ["Beta", "Alpha"],
            out CharacterImprovementGroupAddState reordered));
        Assert.AreNotEqual(state.Revision, reordered.Revision);
    }

    [TestMethod]
    public void Exact_nonempty_untrimmed_name_and_expected_append_index_are_required()
    {
        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateState(
            true,
            ["Alpha", "Alpha"],
            out CharacterImprovementGroupAddState state));
        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateIdentity(
            state,
            " Alpha ",
            out CharacterImprovementGroupInsertionIdentity identity));

        Assert.AreEqual(" Alpha ", identity.Name);
        Assert.AreEqual(2, identity.ExpectedAppendIndex);
        Assert.IsTrue(CharacterImprovementGroupAddRules.TryValidateMutation(
            state,
            identity,
            state.Revision));
        Assert.IsFalse(CharacterImprovementGroupAddRules.TryValidateMutation(
            state,
            identity with { ExpectedAppendIndex = 1 },
            state.Revision));
        Assert.IsFalse(CharacterImprovementGroupAddRules.TryCreateIdentity(
            state,
            string.Empty,
            out _));
    }

    [TestMethod]
    public void Creation_null_saved_values_and_stale_revision_fail_closed()
    {
        Assert.IsFalse(CharacterImprovementGroupAddRules.TryCreateState(
            created: false,
            groups: ["Alpha"],
            out _));
        Assert.IsFalse(CharacterImprovementGroupAddRules.TryCreateState(
            created: true,
            groups: new string[] { "Alpha", null! },
            out _));

        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateState(
            true,
            ["Alpha"],
            out CharacterImprovementGroupAddState state));
        Assert.IsTrue(CharacterImprovementGroupAddRules.TryCreateIdentity(
            state,
            "Alpha",
            out CharacterImprovementGroupInsertionIdentity duplicate));
        Assert.IsFalse(CharacterImprovementGroupAddRules.TryValidateMutation(
            state,
            duplicate,
            new string('0', 64)));
    }
}
