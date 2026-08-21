using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterSpiritFetteringRulesTests
{
    private static readonly Guid TargetId = Guid.Parse("71111111-7111-7111-7111-711111111111");
    private static readonly Guid OtherId = Guid.Parse("72222222-7222-7222-7222-722222222222");

    [TestMethod]
    public void Creation_spirit_can_fetter_without_a_profile_cost()
    {
        bool projected = CharacterSpiritFetteringRules.TryProject(
            TargetId,
            created: false,
            availableKarma: 0,
            karmaSpiritFettering: null,
            allowSpriteFettering: false,
            spiritFetteringImprovementCount: 0,
            [new CharacterSpiritFetteringBasis(TargetId, "Spirit", 4, 2, true, false)],
            out CharacterSpiritFetteringState? state);

        Assert.IsTrue(projected);
        Assert.IsNotNull(state);
        Assert.IsTrue(state.CanFetter);
        Assert.AreEqual(0, state.ActivationKarmaCost);
        Assert.IsTrue(state.ActivationCostExact);
    }

    [TestMethod]
    public void Career_activation_uses_exact_entity_cost_and_one_fettered_limit()
    {
        CharacterSpiritFetteringBasis[] basis =
        [
            new(TargetId, "Spirit", 4, 1, true, false),
            new(OtherId, "Sprite", 3, 0, true, false)
        ];
        Assert.IsTrue(CharacterSpiritFetteringRules.TryProject(
            TargetId,
            created: true,
            availableKarma: 20,
            karmaSpiritFettering: 3,
            allowSpriteFettering: true,
            spiritFetteringImprovementCount: 0,
            basis,
            out CharacterSpiritFetteringState? spirit));
        Assert.IsNotNull(spirit);
        Assert.AreEqual(12, spirit.ActivationKarmaCost);
        Assert.IsTrue(spirit.CanFetter);

        CharacterSpiritFetteringBasis[] alreadyFettered =
        [
            basis[0],
            basis[1] with { Fettered = true }
        ];
        Assert.IsTrue(CharacterSpiritFetteringRules.TryProject(
            TargetId,
            true,
            20,
            3,
            true,
            0,
            alreadyFettered,
            out spirit));
        Assert.IsNotNull(spirit);
        Assert.IsFalse(spirit.CanFetter);
    }

    [TestMethod]
    public void Career_unfetter_rejects_a_second_serviced_unbound_entity_of_the_same_type()
    {
        CharacterSpiritFetteringBasis[] basis =
        [
            new(TargetId, "Spirit", 4, 2, false, true),
            new(OtherId, "Spirit", 3, 1, false, false)
        ];
        Assert.IsTrue(CharacterSpiritFetteringRules.TryProject(
            TargetId,
            true,
            0,
            null,
            false,
            1,
            basis,
            out CharacterSpiritFetteringState? state));
        Assert.IsNotNull(state);
        Assert.IsFalse(state.CanUnfetter);
        Assert.IsFalse(CharacterSpiritFetteringRules.CanSet(state, false));
    }

    [TestMethod]
    public void Career_confirmation_may_spend_more_karma_than_is_available_like_legacy()
    {
        Assert.IsTrue(CharacterSpiritFetteringRules.TryProject(
            TargetId,
            created: true,
            availableKarma: 2,
            karmaSpiritFettering: 3,
            allowSpriteFettering: false,
            spiritFetteringImprovementCount: 0,
            [new CharacterSpiritFetteringBasis(TargetId, "Spirit", 4, 1, true, false)],
            out CharacterSpiritFetteringState? state));

        Assert.IsNotNull(state);
        Assert.AreEqual(12, state.ActivationKarmaCost);
        Assert.IsTrue(state.CanFetter);
    }

    [TestMethod]
    public void Parser_projects_sprite_pet_only_from_an_enabled_saved_improvement()
    {
        const string xml = """
<character>
  <created>True</created><karma>8</karma>
  <improvements><improvement><improvementttype>AllowSpriteFettering</improvementttype><enabled>1</enabled></improvement></improvements>
  <spirits><spirit><guid>71111111-7111-7111-7111-711111111111</guid><name>Machine Sprite</name><type>Sprite</type><force>4</force><services>1</services><bound>True</bound><fettered>False</fettered></spirit></spirits>
</character>
""";

        CharacterSpiritFetteringState? state = new CharacterSectionService()
            .ParseSpirits(xml)
            .Spirits.Single()
            .FetteringSemantics;

        Assert.IsNotNull(state);
        Assert.IsTrue(state.SpriteFetteringAllowed);
        Assert.AreEqual(4, state.ActivationKarmaCost);
        Assert.IsTrue(state.CanFetter);
    }
}
