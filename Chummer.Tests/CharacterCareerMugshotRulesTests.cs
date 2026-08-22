using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerMugshotRulesTests
{
    [TestMethod]
    public void Career_state_uses_one_based_default_and_exact_wraparound()
    {
        CharacterMugshotIdentity[] identities = Identities();
        Assert.IsTrue(CharacterCareerMugshotRules.TryCreateState(
            created: true, identities, mainMugshotIndex: 1, out CharacterCareerMugshotState state));

        Assert.AreEqual(2, state.DefaultSelectedOneBasedIndex);
        Assert.AreEqual(2, CharacterCareerMugshotRules.WrapSelection(state, 0));
        Assert.AreEqual(1, CharacterCareerMugshotRules.WrapSelection(state, 3));
        Assert.IsTrue(CharacterCareerMugshotRules.IsSelectedMain(state, 2));
        Assert.IsFalse(CharacterCareerMugshotRules.IsSelectedMain(state, 1));
    }

    [TestMethod]
    public void Empty_collection_is_zero_and_creation_or_invalid_main_fails_closed()
    {
        Assert.IsTrue(CharacterCareerMugshotRules.TryCreateState(
            created: true,
            Array.Empty<CharacterMugshotIdentity>(),
            mainMugshotIndex: -1,
            out CharacterCareerMugshotState empty));
        Assert.AreEqual(0, CharacterCareerMugshotRules.WrapSelection(empty, 99));
        Assert.IsNull(CharacterCareerMugshotRules.ResolveSelection(empty, 0));

        Assert.IsFalse(CharacterCareerMugshotRules.TryCreateState(
            created: false, Identities(), 0, out _));
        Assert.IsFalse(CharacterCareerMugshotRules.TryCreateState(
            created: true, Identities(), 2, out _));
    }

    [TestMethod]
    public void Main_mutation_is_identity_and_revision_bound_and_clears_to_minus_one()
    {
        CharacterMugshotIdentity[] identities = Identities();
        Assert.IsTrue(CharacterCareerMugshotRules.TryCreateState(
            created: true, identities, mainMugshotIndex: 0, out CharacterCareerMugshotState state));

        Assert.AreEqual(1, CharacterCareerMugshotRules.ApplyMainMutation(
            state, identities[1], state.Revision, isMain: true));
        Assert.AreEqual(-1, CharacterCareerMugshotRules.ApplyMainMutation(
            state, identities[0], state.Revision, isMain: false));
        Assert.IsFalse(CharacterCareerMugshotRules.TryValidateMainMutation(
            state, identities[1], new string('0', 64), isMain: true));
        Assert.IsFalse(CharacterCareerMugshotRules.TryValidateMainMutation(
            state, identities[0], state.Revision, isMain: true));
    }

    private static CharacterMugshotIdentity[] Identities()
    {
        Assert.IsTrue(CharacterCareerMugshotRules.TryCreateIdentity(0, [1, 2, 3], out CharacterMugshotIdentity first));
        Assert.IsTrue(CharacterCareerMugshotRules.TryCreateIdentity(1, [4, 5, 6], out CharacterMugshotIdentity second));
        return [first, second];
    }
}
