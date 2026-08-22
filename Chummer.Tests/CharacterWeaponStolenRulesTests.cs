using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterWeaponStolenRulesTests
{
    private static readonly Guid WeaponId = Guid.Parse("aa131111-1311-4311-8311-131111111111");
    private static readonly Guid AccessoryId = Guid.Parse("bb131111-1311-4311-8311-131111111111");
    private static readonly Guid GearId = Guid.Parse("cc131111-1311-4311-8311-131111111111");

    [TestMethod]
    public void Creation_weapon_accessory_gear_path_has_zero_transaction_economics()
    {
        var identity = new CharacterWeaponStolenIdentity([
            new(CharacterWeaponStolenNodeKind.Weapon, WeaponId),
            new(CharacterWeaponStolenNodeKind.WeaponAccessory, AccessoryId),
            new(CharacterWeaponStolenNodeKind.Gear, GearId)
        ]);

        Assert.IsTrue(CharacterWeaponStolenRules.TryCreateState(
            identity, false, true, "Weapon > Accessory > Gear", false,
            out CharacterWeaponStolenState state));
        Assert.AreEqual(CharacterWeaponStolenPhase.Creation, state.Phase);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(CharacterWeaponStolenRules.RevisionHexLength, state.Revision.Length);
    }

    [TestMethod]
    public void Career_missing_eligibility_and_invalid_topology_fail_closed()
    {
        var root = new CharacterWeaponStolenIdentity([
            new(CharacterWeaponStolenNodeKind.Weapon, WeaponId)
        ]);
        Assert.IsFalse(CharacterWeaponStolenRules.TryCreateState(
            root, true, true, "Weapon", false, out _));
        Assert.IsFalse(CharacterWeaponStolenRules.TryCreateState(
            root, false, false, "Weapon", false, out _));
        Assert.IsFalse(CharacterWeaponStolenRules.IsValidIdentity(
            new CharacterWeaponStolenIdentity([
                new(CharacterWeaponStolenNodeKind.WeaponAccessory, AccessoryId)
            ])));
        Assert.IsFalse(CharacterWeaponStolenRules.IsValidIdentity(
            new CharacterWeaponStolenIdentity([
                new(CharacterWeaponStolenNodeKind.Weapon, WeaponId),
                new(CharacterWeaponStolenNodeKind.Gear, GearId)
            ])));
    }

    [TestMethod]
    public void Revision_binds_typed_path_and_value_and_rejects_stale_or_noop()
    {
        var root = new CharacterWeaponStolenIdentity([
            new(CharacterWeaponStolenNodeKind.Weapon, WeaponId)
        ]);
        var underbarrel = new CharacterWeaponStolenIdentity([
            new(CharacterWeaponStolenNodeKind.Weapon, WeaponId),
            new(CharacterWeaponStolenNodeKind.Weapon, AccessoryId)
        ]);
        Assert.IsTrue(CharacterWeaponStolenRules.TryCreateState(
            root, false, true, "Weapon", false, out CharacterWeaponStolenState current));
        Assert.IsTrue(CharacterWeaponStolenRules.TryCreateState(
            underbarrel, false, true, "Weapon > Underbarrel", false,
            out CharacterWeaponStolenState moved));

        Assert.AreNotEqual(current.Revision, moved.Revision);
        Assert.IsFalse(CharacterWeaponStolenRules.TryValidateMutation(
            current, new string('0', 64), true));
        Assert.IsFalse(CharacterWeaponStolenRules.TryValidateMutation(
            current, current.Revision, false));
        Assert.IsTrue(CharacterWeaponStolenRules.TryValidateMutation(
            current, current.Revision, true));
    }
}
