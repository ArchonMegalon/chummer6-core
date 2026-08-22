using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleMatrixSwapRulesTests
{
    private static readonly CharacterVehicleMatrixSwapIdentity Identity =
        new(Guid.Parse("91111111-1111-4111-8111-111111111111"));

    [TestMethod]
    public void Create_and_career_bind_raw_values_provenance_and_zero_economics()
    {
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, false, "Creation Van", "7", "{Pilot}", "5", "4", "7,6,5,4", true,
            out var creation));
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, true, "Career Van", "8", "7", "{Pilot}", "5", "8,7,6,5", true,
            out var career));
        Assert.AreEqual(CharacterVehicleMatrixSwapPhase.Creation, creation.Phase);
        Assert.AreEqual(CharacterVehicleMatrixSwapPhase.Career, career.Phase);
        Assert.AreEqual(0m, creation.Economics.NuyenDelta);
        Assert.AreEqual(0, career.Economics.KarmaDelta);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void Data_processing_and_firewall_permute_distinct_raw_values_only()
    {
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, false, "Van", "7", "6", "5", "4", "7,6,5,4", true, out var state));
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryValidateMutation(
            state, state.Revision, CharacterVehicleMatrixStat.DataProcessing, CharacterVehicleMatrixStat.Attack));
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryValidateMutation(
            state, state.Revision, CharacterVehicleMatrixStat.Firewall, CharacterVehicleMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.TryValidateMutation(
            state, state.Revision, CharacterVehicleMatrixStat.Attack, CharacterVehicleMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.TryValidateMutation(
            state, new string('0', 64), CharacterVehicleMatrixStat.Firewall, CharacterVehicleMatrixStat.Attack));
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterVehicleMatrixStat.Firewall, CharacterVehicleMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterVehicleMatrixStat.Firewall, CharacterVehicleMatrixStat.Attack));
    }

    [TestMethod]
    public void Missing_enable_or_xml_provenance_and_equal_values_fail_closed()
    {
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, false, "Van", "7", "6", "5", "4", "7,6,5,4", false, out _));
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, false, "Van", "7", "6", "5", "4", "", true, out _));
        Assert.IsTrue(CharacterVehicleMatrixSwapRules.TryCreateState(
            Identity, false, "Van", "7", "6", "5", "5", "7,6,5,4", true, out var equal));
        Assert.IsFalse(CharacterVehicleMatrixSwapRules.TryValidateMutation(
            equal, equal.Revision, CharacterVehicleMatrixStat.DataProcessing, CharacterVehicleMatrixStat.Firewall));
    }
}
