using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Natural_values_combine_saved_pools_and_karma_without_double_count_or_writes(string method)
    {
        using var fixture = new Fixture(method);
        var languageId = Guid.NewGuid();
        var newLanguageId = Guid.NewGuid();
        var poolTopic = new Sr6CreationKnowledgeEntry(Guid.NewGuid(), "Seattle");
        var karmaTopic = new Sr6CreationKnowledgeEntry(Guid.NewGuid(), "Street medicine");
        var selection = KarmaSeed(method) with
        {
            Attributes = Spend(Spend(EmptyAttributes(), "Body", 1, 0), "Logic", 1, 0),
            Skills = new([new("Athletics", 1, ["Climbing"])]),
            Knowledge = new("German", [poolTopic], [new(languageId, "English", "basic")]),
            Karma = new([new("Body", 1)], [new("Athletics", 1), new("ExoticWeapons", 1, "Whip")], 0)
            {
                Specializations = [new("ExoticWeapons", "Laser")],
                Knowledge = new([karmaTopic], [new(languageId, "English", "expert"), new(newLanguageId, "Spanish", "basic")])
            }
        };
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        string bytes = Sr6CreationFoundationIntegrity.Digest(before);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        var actual = state.DraftSummary!.NaturalValues!;
        var body = actual.Attributes!.Single(row => row.AttributeId == "Body");
        Assert.AreEqual(new Sr6CreationNaturalAttributeValue("Body", 1, 1, 0, 1, 3, 6), body);
        Assert.AreEqual(0, actual.Attributes.Single(row => row.AttributeId == "Magic").Rating);
        var athletics = actual.Skills!.Single(row => row.SkillId == "Athletics");
        Assert.AreEqual(1, athletics.PoolRating);
        Assert.AreEqual(1, athletics.KarmaIncrease);
        Assert.AreEqual(2, athletics.Rating, "A specialty is not an unconditional rating increase.");
        Assert.AreEqual(new Sr6CreationNaturalSpecialization("Climbing", 2), athletics.Specializations.Single());
        var exotic = actual.Skills.Single(row => row.SkillId == "ExoticWeapons");
        Assert.AreEqual(0, exotic.PoolRating);
        Assert.AreEqual(1, exotic.KarmaIncrease);
        Assert.AreEqual(1, exotic.Rating);
        CollectionAssert.AreEqual(new[] { "Whip", "Laser" }, exotic.Specializations.Select(row => row.Subject).ToArray());
        Assert.IsTrue(exotic.Specializations.All(row => row.DicePoolBonus == 0));
        Assert.IsFalse(actual.Skills.Single(row => row.SkillId == "Sorcery").Available);
        Assert.AreEqual("German", actual.Knowledge!.NativeLanguage);
        CollectionAssert.AreEqual(new[] { poolTopic, karmaTopic }, actual.Knowledge.KnowledgeSkills.ToArray());
        Assert.HasCount(2, actual.Knowledge.Languages);
        Assert.AreEqual(new Sr6CreationNaturalLanguage(languageId, "English", "expert", 3), actual.Knowledge.Languages[0]);
        Assert.AreEqual(new Sr6CreationNaturalLanguage(newLanguageId, "Spanish", "basic", 0), actual.Knowledge.Languages[1]);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
        Assert.AreEqual(request.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.AreEqual(state.Binding, state.DraftSummary.Binding);
        Assert.IsFalse(state.DraftSummary.FinalizationAvailable);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));

        var replacement = selection with { Karma = new([], [], 0) };
        Assert.IsNotNull(cold.Confirm(fixture.Stamp, fixture.Request(replacement)).Value);
        var updated = cold.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.NaturalValues!;
        Assert.AreEqual(2, updated.Attributes!.Single(row => row.AttributeId == "Body").Rating);
        Assert.AreEqual(1, updated.Skills!.Single(row => row.SkillId == "Athletics").Rating);
        Assert.AreEqual(0, updated.Skills.Single(row => row.SkillId == "ExoticWeapons").Rating);
        Assert.HasCount(1, updated.Knowledge!.Languages);
        Assert.AreEqual("basic", updated.Knowledge.Languages[0].Level);
        Assert.HasCount(1, updated.Knowledge.KnowledgeSkills);
    }

    [TestMethod]
    public void Natural_values_do_not_invent_missing_allocations_or_convert_exotic_permissions_to_bonuses()
    {
        using var fixture = new Fixture();
        Assert.IsNull(fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.NaturalValues);
        var selection = Selection();
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.NaturalValues!;
        Assert.IsNull(values.Attributes);
        Assert.IsNull(values.Skills);
        Assert.IsNull(values.Knowledge);
        selection = selection with { Skills = new([new("ExoticWeapons", 1, ["Whip", "Laser"])]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.NaturalValues!;
        Assert.IsNull(values.Attributes);
        var exotic = values.Skills!.Single(row => row.SkillId == "ExoticWeapons");
        Assert.HasCount(2, exotic.Specializations);
        Assert.IsTrue(exotic.Specializations.All(row => row.DicePoolBonus == 0));
        Assert.AreEqual(0, values.Skills.Single(row => row.SkillId == "Athletics").Rating);
    }

    [TestMethod]
    public void Natural_values_include_metatype_adjustment_quality_caps_and_saved_magic_karma()
    {
        using var fixture = new Fixture("PointBuy");
        var selection = PointBuy("ork", "adept") with
        {
            Qualities = new(["exceptional-Body"]),
            Attributes = Spend(EmptyAttributes(), "Body", 0, 1), Skills = new([]),
            Karma = new([new("Body", 1), new("Magic", 1)], [], 0)
        };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.NaturalValues!;
        Assert.AreEqual(new Sr6CreationNaturalAttributeValue("Body", 1, 0, 1, 1, 3, 9), values.Attributes!.Single(row => row.AttributeId == "Body"));
        Assert.AreEqual(new Sr6CreationNaturalAttributeValue("Magic", 1, 0, 0, 1, 2, 6), values.Attributes.Single(row => row.AttributeId == "Magic"));
    }
}
