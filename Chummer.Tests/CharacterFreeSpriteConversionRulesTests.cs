using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterFreeSpriteConversionRulesTests
{
    private static readonly Guid ExistingId =
        Guid.Parse("81111111-8111-8111-8111-811111111111");
    private static readonly Guid NewId =
        Guid.Parse("82222222-8222-8222-8222-822222222222");

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Creation_and_career_sprite_use_identical_zero_cost_conversion(bool created)
    {
        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateState(
            created,
            "Registered Sprites",
            [ExistingId],
            out CharacterFreeSpriteConversionState state));
        Assert.IsTrue(state.CanConvert);
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateIdentity(
            state,
            NewId,
            out CharacterFreeSpriteConversionIdentity identity));
        Assert.AreEqual(CharacterFreeSpriteConversionRules.DenialSourceId, identity.SourceId);
        Assert.AreEqual(1, identity.ExpectedAppendIndex);
        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryValidateMutation(
            state,
            identity,
            state.Revision));
    }

    [TestMethod]
    public void Non_sprite_free_sprite_duplicate_and_stale_requests_fail_closed()
    {
        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateState(
            false, "Metahuman", [ExistingId], out CharacterFreeSpriteConversionState nonSprite));
        Assert.IsFalse(nonSprite.CanConvert);
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryCreateIdentity(nonSprite, NewId, out _));

        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateState(
            true, "Free Sprite", [ExistingId], out CharacterFreeSpriteConversionState freeSprite));
        Assert.IsFalse(freeSprite.CanConvert);

        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateState(
            true, "Machine Sprites", [ExistingId], out CharacterFreeSpriteConversionState sprite));
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryCreateIdentity(sprite, ExistingId, out _));
        Assert.IsTrue(CharacterFreeSpriteConversionRules.TryCreateIdentity(sprite, NewId, out CharacterFreeSpriteConversionIdentity identity));
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryValidateMutation(
            sprite, identity, new string('0', CharacterFreeSpriteConversionRules.RevisionHexLength)));
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryValidateMutation(
            sprite, identity with { SourceId = Guid.NewGuid() }, sprite.Revision));
    }

    [TestMethod]
    public void Invalid_or_duplicate_saved_identity_fails_closed()
    {
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryCreateState(
            false, "Machine Sprites", [Guid.Empty], out _));
        Assert.IsFalse(CharacterFreeSpriteConversionRules.TryCreateState(
            false, "Machine Sprites", [ExistingId, ExistingId], out _));
    }
}
