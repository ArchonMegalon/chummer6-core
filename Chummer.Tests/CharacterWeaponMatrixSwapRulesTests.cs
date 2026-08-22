using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterWeaponMatrixSwapRulesTests
{
    private static readonly CharacterWeaponMatrixSwapIdentity Identity =
        new(Guid.Parse("d7111111-1711-4711-8711-171111111111"));

    [TestMethod]
    public void Career_state_binds_typed_root_legacy_surface_raw_provenance_and_zero_economics()
    {
        Assert.IsTrue(CharacterWeaponMatrixSwapRules.TryCreateState(
            Identity, true, "Career Deck Weapon", "8", "7", "6", "5", "8,7,6,5", true,
            out CharacterWeaponMatrixSwapState state));

        Assert.AreEqual(CharacterWeaponMatrixSwapPhase.Career, state.Phase);
        Assert.AreEqual(Identity, state.Identity);
        Assert.AreEqual(CharacterWeaponMatrixSwapRules.LegacySurface, state.Provenance.LegacySurface);
        Assert.AreEqual("8,7,6,5", state.Provenance.AttributeArray);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(CharacterWeaponMatrixSwapRules.RevisionHexLength, state.Revision.Length);
    }

    [TestMethod]
    public void All_four_career_handlers_share_one_revision_and_source_bound_permutation_authority()
    {
        Assert.IsTrue(CharacterWeaponMatrixSwapRules.TryCreateState(
            Identity, true, "Deck Weapon", "8", "7", "6", "5", "8,7,6,5", true,
            out CharacterWeaponMatrixSwapState state));

        foreach (CharacterWeaponMatrixStat changed in Enum.GetValues<CharacterWeaponMatrixStat>())
        {
            CharacterWeaponMatrixStat target = changed == CharacterWeaponMatrixStat.Attack
                ? CharacterWeaponMatrixStat.Firewall
                : CharacterWeaponMatrixStat.Attack;
            Assert.IsTrue(CharacterWeaponMatrixSwapRules.TryValidateMutation(
                state, state.Revision, changed, target), changed.ToString());
        }

        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryValidateMutation(
            state, new string('0', 64), CharacterWeaponMatrixStat.Attack,
            CharacterWeaponMatrixStat.Firewall));
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryValidateMutation(
            state, state.Revision, CharacterWeaponMatrixStat.Attack,
            CharacterWeaponMatrixStat.Attack));
        Assert.IsTrue(CharacterWeaponMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterWeaponMatrixStat.Firewall, CharacterWeaponMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterWeaponMatrixStat.Firewall, CharacterWeaponMatrixStat.Attack));
    }

    [TestMethod]
    public void Creation_missing_identity_disabled_or_equal_values_fail_closed()
    {
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryCreateState(
            Identity, false, "Creation weapon", "8", "7", "6", "5", "8,7,6,5", true, out _));
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryCreateState(
            new CharacterWeaponMatrixSwapIdentity(Guid.Empty), true, "Deck", "8", "7", "6", "5",
            "8,7,6,5", true, out _));
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryCreateState(
            Identity, true, "Deck", "8", "7", "6", "5", "8,7,6,5", false, out _));
        Assert.IsTrue(CharacterWeaponMatrixSwapRules.TryCreateState(
            Identity, true, "Deck", "8", "7", "5", "5", "8,7,6,5", true, out var equal));
        Assert.IsFalse(CharacterWeaponMatrixSwapRules.TryValidateMutation(
            equal, equal.Revision, CharacterWeaponMatrixStat.DataProcessing,
            CharacterWeaponMatrixStat.Firewall));
    }
}
