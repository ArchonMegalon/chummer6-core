using System;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationLifestyleDeleteRulesTests
{
    private static readonly CharacterCreationLifestyleDeleteIdentity Identity =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [TestMethod]
    public void CreationStateCarriesExactZeroRefundAndRemovedCostAuthority()
    {
        Assert.IsTrue(CharacterCreationLifestyleDeleteRules.TryCreateState(
            Identity,
            created: false,
            "Safehouse",
            lifestyleQualityCount: 2,
            linkedImprovementCount: 1,
            "<lifestyle />",
            "<improvement />",
            out CharacterCreationLifestyleDeleteState state));

        Assert.IsTrue(state.CanDelete);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.AreEqual(0, state.Economics.ExpenseRecordDelta);
        Assert.IsTrue(state.Economics.RemovesLifestyleCost);
        Assert.AreEqual(64, state.Revision.Length);
        Assert.IsTrue(CharacterCreationLifestyleDeleteRules.CanDelete(
            state,
            Identity,
            state.Revision,
            confirmed: true));
    }

    [TestMethod]
    public void CareerOrCancelledOrStaleDeletionFailsClosed()
    {
        CharacterCreationLifestyleDeleteState career = State(created: true);
        CharacterCreationLifestyleDeleteState creation = State(created: false);

        Assert.IsFalse(CharacterCreationLifestyleDeleteRules.CanDelete(
            career,
            Identity,
            career.Revision,
            confirmed: true));
        Assert.IsFalse(CharacterCreationLifestyleDeleteRules.CanDelete(
            creation,
            Identity,
            creation.Revision,
            confirmed: false));
        Assert.IsFalse(CharacterCreationLifestyleDeleteRules.CanDelete(
            creation,
            Identity,
            new string('0', CharacterCreationLifestyleDeleteRules.RevisionHexLength),
            confirmed: true));
    }

    [TestMethod]
    public void MissingIdentityCostOrAmbiguousCountsFailClosed()
    {
        Assert.IsFalse(CharacterCreationLifestyleDeleteRules.TryCreateState(
            new CharacterCreationLifestyleDeleteIdentity(Guid.Empty), false, "Home", 0, 0,
            "<lifestyle />", string.Empty, out _));
        Assert.IsFalse(CharacterCreationLifestyleDeleteRules.TryCreateState(
            Identity, false, "Home", -1, 0,
            "<lifestyle />", string.Empty, out _));
    }

    [TestMethod]
    public void TargetOrImprovementDriftChangesLocalRevision()
    {
        CharacterCreationLifestyleDeleteState original = State(created: false);
        Assert.IsTrue(CharacterCreationLifestyleDeleteRules.TryCreateState(
            Identity, false, "Home", 1, 1,
            "<lifestyle><months>2</months></lifestyle>",
            "<improvement><enabled>False</enabled></improvement>",
            out CharacterCreationLifestyleDeleteState changed));

        Assert.AreNotEqual(original.Revision, changed.Revision);
    }

    private static CharacterCreationLifestyleDeleteState State(bool created)
    {
        Assert.IsTrue(CharacterCreationLifestyleDeleteRules.TryCreateState(
            Identity,
            created,
            "Home",
            1,
            1,
            "<lifestyle><months>1</months></lifestyle>",
            "<improvement><enabled>True</enabled></improvement>",
            out CharacterCreationLifestyleDeleteState state));
        return state;
    }
}
