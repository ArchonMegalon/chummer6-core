using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterSustainedObjectRulesTests
{
    private static readonly Guid SpellId = Guid.Parse("81111111-8111-8111-8111-811111111111");

    [TestMethod]
    public void Projection_preserves_duplicate_casts_with_persisted_occurrence_identity()
    {
        CharacterSustainedObjectBasis[] basis =
        [
            new(new("Spell", SpellId, 0), "Increase Reflexes", 4, 2, true, true),
            new(new("Spell", SpellId, 1), "Increase Reflexes", 6, 3, false, true)
        ];

        Assert.IsTrue(CharacterSustainedObjectRules.TryProjectAll(
            basis,
            out IReadOnlyList<CharacterSustainedObjectState>? states));
        Assert.IsNotNull(states);
        Assert.HasCount(2, states);
        Assert.AreEqual(1, states[1].Identity.Occurrence);
        Assert.AreEqual(6, states[1].Force);
    }

    [TestMethod]
    public void Projection_fails_closed_on_nonsequential_or_unsupported_identity()
    {
        Assert.IsFalse(CharacterSustainedObjectRules.TryProjectAll(
            [new(new("Spell", SpellId, 1), "Increase Reflexes", 4, 2, true, true)],
            out _));
        Assert.IsFalse(CharacterSustainedObjectRules.TryProjectAll(
            [new(new("Gear", SpellId, 0), "Wrong domain", 4, 2, true, true)],
            out _));
    }

    [TestMethod]
    public void Update_enforces_legacy_bounds_and_critter_power_self_sustained_visibility()
    {
        CharacterSustainedObjectState spell = new(
            new("Spell", SpellId, 0), "Increase Reflexes", 4, 2, true, true);
        CharacterSustainedObjectState critterPower = new(
            new("CritterPower", SpellId, 0), "Fear", 4, 2, true, false);

        Assert.IsTrue(CharacterSustainedObjectRules.CanUpdate(spell, 0, 100, false));
        Assert.IsFalse(CharacterSustainedObjectRules.CanUpdate(spell, -1, 2, true));
        Assert.IsFalse(CharacterSustainedObjectRules.CanUpdate(spell, 4, 101, true));
        Assert.IsTrue(CharacterSustainedObjectRules.CanUpdate(critterPower, 5, 3, true));
        Assert.IsFalse(CharacterSustainedObjectRules.CanUpdate(critterPower, 5, 3, false));
    }

    [TestMethod]
    public void Delete_requires_explicit_confirmation()
    {
        Assert.IsFalse(CharacterSustainedObjectRules.CanDelete(false));
        Assert.IsTrue(CharacterSustainedObjectRules.CanDelete(true));
    }

    [TestMethod]
    public void Psyche_active_requires_career_mode_a_visible_legacy_surface_and_a_change()
    {
        CharacterPsycheActiveState state = new(
            CareerMode: true,
            Active: false,
            MagicianControlAvailable: true,
            TechnomancerControlAvailable: false);

        Assert.IsTrue(CharacterSustainedObjectRules.CanSetPsycheActive(
            state,
            CharacterPsycheActiveSurface.Magician,
            value: true));
        Assert.IsFalse(CharacterSustainedObjectRules.CanSetPsycheActive(
            state,
            CharacterPsycheActiveSurface.Technomancer,
            value: true));
        Assert.IsFalse(CharacterSustainedObjectRules.CanSetPsycheActive(
            state,
            CharacterPsycheActiveSurface.Magician,
            value: false));
        Assert.IsFalse(CharacterSustainedObjectRules.CanSetPsycheActive(
            state with { CareerMode = false },
            CharacterPsycheActiveSurface.Magician,
            value: true));
    }
}
