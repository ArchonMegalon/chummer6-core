using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterVehicleEquipmentInstalledRulesTests
{
    private static readonly Guid VehicleId = Guid.Parse("61111111-6111-4111-8111-611111111111");
    private static readonly Guid MountId = Guid.Parse("62222222-6222-4222-8222-622222222222");
    private static readonly Guid ModId = Guid.Parse("63333333-6333-4333-8333-633333333333");
    private static readonly Guid WeaponId = Guid.Parse("64444444-6444-4444-8444-644444444444");
    private static readonly Guid AccessoryId = Guid.Parse("65555555-6555-4555-8555-655555555555");

    [TestMethod]
    public void Create_and_career_preserve_zero_economic_union_semantics()
    {
        CharacterVehicleEquipmentInstalledIdentity identity = Identity(
            new(CharacterVehicleEquipmentNodeKind.WeaponMount, MountId),
            new(CharacterVehicleEquipmentNodeKind.VehicleMod, ModId));
        var provenance = new CharacterVehicleEquipmentInstalledProvenance(false, string.Empty, null, true);

        Assert.IsTrue(CharacterVehicleEquipmentInstalledRules.TryCreateState(
            identity, false, "Van > Mount > Mod", false, provenance, out var creation));
        Assert.IsTrue(CharacterVehicleEquipmentInstalledRules.TryCreateState(
            identity, true, "Van > Mount > Mod", false, provenance, out var career));

        Assert.AreEqual(CharacterVehicleEquipmentInstalledPhase.Creation, creation.Phase);
        Assert.AreEqual(CharacterVehicleEquipmentInstalledPhase.Career, career.Phase);
        Assert.IsTrue(creation.LegacyEnabled);
        Assert.IsTrue(creation.CanChangeInstalled);
        Assert.AreEqual(0m, creation.Economics.NuyenDelta);
        Assert.AreEqual(0, career.Economics.KarmaDelta);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void Legacy_enable_rules_are_exact_for_all_four_kinds()
    {
        AssertState(Identity(new(CharacterVehicleEquipmentNodeKind.WeaponMount, MountId)),
            new(true, string.Empty, null, true), legacyEnabled: false, mutationExact: true);
        AssertState(Identity(new(CharacterVehicleEquipmentNodeKind.VehicleMod, ModId)),
            new(false, string.Empty, null, false), legacyEnabled: true, mutationExact: false);
        AssertState(Identity(new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId)),
            new(null, VehicleId.ToString("D"), null, true), legacyEnabled: false, mutationExact: true);
        AssertState(Identity(new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId)),
            new(null, VehicleId.ToString("D").ToUpperInvariant(), null, true), legacyEnabled: true, mutationExact: true);
        AssertState(Identity(new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId)),
            new(null, string.Empty, null, true), legacyEnabled: true, mutationExact: true);
        AssertState(Identity(
                new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId),
                new(CharacterVehicleEquipmentNodeKind.Weapon, MountId)),
            new(null, WeaponId.ToString("D"), WeaponId, true), legacyEnabled: false, mutationExact: true);
        AssertState(Identity(
                new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId),
                new(CharacterVehicleEquipmentNodeKind.WeaponAccessory, AccessoryId)),
            new(null, string.Empty, null, true), legacyEnabled: true, mutationExact: true);
    }

    [TestMethod]
    public void Revision_and_identity_fail_closed_on_move_ambiguity_and_stale_state()
    {
        CharacterVehicleEquipmentInstalledIdentity identity = Identity(
            new(CharacterVehicleEquipmentNodeKind.WeaponMount, MountId),
            new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId),
            new(CharacterVehicleEquipmentNodeKind.WeaponAccessory, AccessoryId));
        var provenance = new CharacterVehicleEquipmentInstalledProvenance(null, string.Empty, null, true);
        Assert.IsTrue(CharacterVehicleEquipmentInstalledRules.TryCreateState(
            identity, false, "Van > Mount > Weapon > Accessory", true, provenance, out var current));

        Assert.IsTrue(CharacterVehicleEquipmentInstalledRules.TryValidateMutation(
            current, current.Revision, false));
        Assert.IsFalse(CharacterVehicleEquipmentInstalledRules.TryValidateMutation(
            current, new string('0', 64), false));
        Assert.IsFalse(CharacterVehicleEquipmentInstalledRules.TryValidateMutation(
            current, current.Revision, true));
        Assert.IsFalse(CharacterVehicleEquipmentInstalledRules.IsValidIdentity(
            Identity(
                new(CharacterVehicleEquipmentNodeKind.WeaponAccessory, AccessoryId))));
        Assert.IsFalse(CharacterVehicleEquipmentInstalledRules.IsValidIdentity(
            Identity(
                new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId),
                new(CharacterVehicleEquipmentNodeKind.VehicleMod, ModId))));
        Assert.IsFalse(CharacterVehicleEquipmentInstalledRules.IsValidIdentity(
            Identity(
                new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId),
                new(CharacterVehicleEquipmentNodeKind.Weapon, WeaponId))));
    }

    private static CharacterVehicleEquipmentInstalledIdentity Identity(
        params CharacterVehicleEquipmentPathSegment[] path)
        => new(VehicleId, path);

    private static void AssertState(
        CharacterVehicleEquipmentInstalledIdentity identity,
        CharacterVehicleEquipmentInstalledProvenance provenance,
        bool legacyEnabled,
        bool mutationExact)
    {
        Assert.IsTrue(CharacterVehicleEquipmentInstalledRules.TryCreateState(
            identity, false, "Target", true, provenance, out var state));
        Assert.AreEqual(legacyEnabled, state.LegacyEnabled);
        Assert.AreEqual(mutationExact && legacyEnabled, state.CanChangeInstalled);
    }
}
