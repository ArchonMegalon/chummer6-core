using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterArmorTreeFlagRulesTests
{
    private static readonly Guid ArmorId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid ModId = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid GearId = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid ChildGearId = Guid.Parse("d1111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void Armor_mod_and_recursive_gear_identities_are_exact()
    {
        CharacterArmorTreeNodeIdentity[] identities =
        [
            new(CharacterArmorTreeNodeKind.Armor, ArmorId, null, []),
            new(CharacterArmorTreeNodeKind.ArmorMod, ArmorId, ModId, []),
            new(CharacterArmorTreeNodeKind.Gear, ArmorId, null, [GearId, ChildGearId]),
            new(CharacterArmorTreeNodeKind.Gear, ArmorId, ModId, [GearId, ChildGearId])
        ];

        foreach (CharacterArmorTreeNodeIdentity identity in identities)
        {
            Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
                identity,
                created: false,
                displayPath: "Armor > selected node",
                stolen: false,
                discountedCost: true,
                out CharacterArmorTreeFlagState state));
            Assert.AreEqual(CharacterArmorTreeFlagRules.RevisionHexLength, state.Revision.Length);
            Assert.IsTrue(CharacterArmorTreeFlagRules.IdentityEquals(identity, state.Identity));
        }
    }

    [TestMethod]
    public void Invalid_hierarchy_duplicate_ids_stale_revision_and_noop_fail_closed()
    {
        Assert.IsFalse(CharacterArmorTreeFlagRules.IsValidIdentity(
            new(CharacterArmorTreeNodeKind.Armor, ArmorId, ModId, [])));
        Assert.IsFalse(CharacterArmorTreeFlagRules.IsValidIdentity(
            new(CharacterArmorTreeNodeKind.ArmorMod, ArmorId, null, [])));
        Assert.IsFalse(CharacterArmorTreeFlagRules.IsValidIdentity(
            new(CharacterArmorTreeNodeKind.Gear, ArmorId, null, [])));
        Assert.IsFalse(CharacterArmorTreeFlagRules.IsValidIdentity(
            new(CharacterArmorTreeNodeKind.Gear, ArmorId, null, [GearId, GearId])));

        Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
            new(CharacterArmorTreeNodeKind.Gear, ArmorId, ModId, [GearId, ChildGearId]),
            created: false,
            displayPath: "Armor > Mod > Gear > Child",
            stolen: false,
            discountedCost: false,
            out CharacterArmorTreeFlagState state));
        Assert.IsFalse(CharacterArmorTreeFlagRules.TryValidateMutation(
            state, new string('0', 64), stolen: true, discountedCost: false));
        Assert.IsFalse(CharacterArmorTreeFlagRules.TryValidateMutation(
            state, state.Revision, stolen: false, discountedCost: false));
        Assert.IsTrue(CharacterArmorTreeFlagRules.TryValidateMutation(
            state, state.Revision, stolen: true, discountedCost: false));
    }

    [TestMethod]
    public void Revision_binds_hierarchy_and_both_exact_saved_flags()
    {
        CharacterArmorTreeNodeIdentity armorGear = new(
            CharacterArmorTreeNodeKind.Gear,
            ArmorId,
            null,
            [GearId, ChildGearId]);
        CharacterArmorTreeNodeIdentity modGear = armorGear with { ArmorModId = ModId };
        Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
            armorGear, false, "same label", false, false, out CharacterArmorTreeFlagState original));
        Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
            modGear, false, "same label", false, false, out CharacterArmorTreeFlagState moved));
        Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
            armorGear, false, "same label", true, false, out CharacterArmorTreeFlagState stolen));
        Assert.IsTrue(CharacterArmorTreeFlagRules.TryCreateState(
            armorGear, false, "same label", false, true, out CharacterArmorTreeFlagState discounted));

        Assert.IsFalse(CharacterArmorTreeFlagRules.TryCreateState(
            armorGear, true, "same label", false, false, out _));

        Assert.AreNotEqual(original.Revision, moved.Revision);
        Assert.AreNotEqual(original.Revision, stolen.Revision);
        Assert.AreNotEqual(original.Revision, discounted.Revision);
    }
}
