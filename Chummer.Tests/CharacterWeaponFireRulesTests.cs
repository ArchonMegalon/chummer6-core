using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterWeaponFireRulesTests
{
    private static readonly Guid WeaponId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid AmmoId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [TestMethod]
    public void Career_fire_projects_legacy_modes_counts_and_default_action()
    {
        Assert.IsTrue(CharacterWeaponFireRules.TryCreateState(
            new CharacterWeaponFireIdentity(WeaponId, 1, AmmoId),
            created: true,
            "Ares Alpha",
            ammoRemaining: 30,
            ammoGearQuantity: 30m,
            Source("SA/BF/FA", accessories:
            [
                new(true, "", "", 2, 4, 7, 11, 21)
            ]),
            hasUnsupportedModeSemantics: false,
            out CharacterWeaponFireState state));

        CollectionAssert.AreEqual(
            new[]
            {
                CharacterWeaponFireMode.SingleShot,
                CharacterWeaponFireMode.ShortBurst,
                CharacterWeaponFireMode.LongBurst,
                CharacterWeaponFireMode.FullBurst,
                CharacterWeaponFireMode.SuppressiveFire
            },
            state.Modes.Select(value => value.Mode).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 4, 7, 11, 21 }, state.Modes.Select(value => value.Rounds).ToArray());
        Assert.AreEqual(CharacterWeaponFireMode.SingleShot, state.DefaultMode);
        Assert.HasCount(CharacterWeaponFireRules.RevisionHexLength, state.Revision);
    }

    [TestMethod]
    public void Short_and_long_bursts_require_confirmation_to_consume_partial_magazine()
    {
        CharacterWeaponFireState state = State(ammoRemaining: 2, ammoGearQuantity: 5m);

        Assert.IsTrue(CharacterWeaponFireRules.TryCreatePlan(
            state, state.Revision, CharacterWeaponFireMode.ShortBurst, out CharacterWeaponFirePlan plan));
        Assert.IsTrue(plan.RequiresPartialConfirmation);
        Assert.AreEqual(2, plan.RoundsConsumed);
        Assert.AreEqual(0, plan.NewAmmoRemaining);
        Assert.AreEqual(3m, plan.NewAmmoGearQuantity);
        Assert.IsFalse(plan.DeleteAmmoGear);
        Assert.IsFalse(CharacterWeaponFireRules.TryValidateMutation(
            state, state.Revision, CharacterWeaponFireMode.ShortBurst, confirmedPartial: false, out _));
        Assert.IsTrue(CharacterWeaponFireRules.TryValidateMutation(
            state, state.Revision, CharacterWeaponFireMode.ShortBurst, confirmedPartial: true, out _));
    }

    [TestMethod]
    public void Full_and_suppressive_fire_never_consume_partial_magazine()
    {
        CharacterWeaponFireState state = State(ammoRemaining: 9, ammoGearQuantity: 9m);

        Assert.IsFalse(CharacterWeaponFireRules.TryCreatePlan(
            state, state.Revision, CharacterWeaponFireMode.FullBurst, out _));
        Assert.IsFalse(CharacterWeaponFireRules.TryCreatePlan(
            state, state.Revision, CharacterWeaponFireMode.SuppressiveFire, out _));
    }

    [TestMethod]
    public void Firing_decrements_linked_ammo_quantity_and_marks_exhausted_stack_for_deletion()
    {
        CharacterWeaponFireState state = State(ammoRemaining: 3, ammoGearQuantity: 3m);

        Assert.IsTrue(CharacterWeaponFireRules.TryValidateMutation(
            state, state.Revision, CharacterWeaponFireMode.ShortBurst, confirmedPartial: false,
            out CharacterWeaponFirePlan plan));
        Assert.AreEqual(0, plan.NewAmmoRemaining);
        Assert.AreEqual(0m, plan.NewAmmoGearQuantity);
        Assert.IsTrue(plan.DeleteAmmoGear);
    }

    [TestMethod]
    public void Fire_rules_fail_closed_for_creation_stale_and_mode_bonus_states()
    {
        Assert.IsFalse(CharacterWeaponFireRules.TryCreateState(
            new CharacterWeaponFireIdentity(WeaponId, 1, AmmoId),
            created: false,
            "Ares Alpha",
            30,
            30m,
            Source("SA/BF/FA"),
            false,
            out _));
        Assert.IsFalse(CharacterWeaponFireRules.TryCreateState(
            new CharacterWeaponFireIdentity(WeaponId, 1, AmmoId),
            created: true,
            "Ares Alpha",
            30,
            30m,
            Source("SA/BF/FA"),
            true,
            out _));

        CharacterWeaponFireState state = State(30, 30m);
        Assert.IsFalse(CharacterWeaponFireRules.TryValidateMutation(
            state, new string('0', 64), CharacterWeaponFireMode.SingleShot, false, out _));
    }

    private static CharacterWeaponFireState State(int ammoRemaining, decimal ammoGearQuantity)
    {
        Assert.IsTrue(CharacterWeaponFireRules.TryCreateState(
            new CharacterWeaponFireIdentity(WeaponId, 1, AmmoId),
            true,
            "Ares Alpha",
            ammoRemaining,
            ammoGearQuantity,
            Source("SA/BF/FA"),
            false,
            out CharacterWeaponFireState state));
        return state;
    }

    private static CharacterWeaponFireSource Source(
        string modes,
        IReadOnlyList<CharacterWeaponFireAccessorySource>? accessories = null)
        => new(
            "Ranged", "42(c)", modes,
            true, true, true, true, true,
            1, 3, 6, 10, 20,
            accessories ?? Array.Empty<CharacterWeaponFireAccessorySource>());
}
