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
    public void Skills_save_with_attributes_and_cold_reopen_without_replaying_write(string method)
    {
        using var fixture = new Fixture(method);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request()).Value);
        var selection = Selection(method) with { Attributes = EmptyAttributes(), Skills = new([
            new("Firearms", 6, ["Pistols"]), new("Athletics", 5, []), new("Perception", 5, []),
            new("Stealth", 5, []), new("ExoticWeapons", 2, ["Whip"])]) };
        var quote = fixture.Preview(selection);
        Assert.IsTrue(quote.Skills!.AllPointsSpent);
        Assert.AreEqual(24, quote.Skills.PointsSpent);
        Assert.IsTrue(quote.Skills.SpecializationsNeedGmReview);
        Assert.AreEqual(0, quote.Skills.Values.Single(row => row.SkillId == "ExoticWeapons").SpecializationCost);
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(3L, reopened.Binding.ContentRevision);
        Assert.AreEqual(3L, reopened.Binding.SavedRevision);
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.IsNotNull(reopened.Selection.Attributes);
        Assert.HasCount(19, reopened.SkillOptions!);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    [DataRow("mundane", "Tasking", false)]
    [DataRow("mundane", "Sorcery", false)]
    [DataRow("mundane", "Astral", false)]
    [DataRow("technomancer", "Tasking", true)]
    [DataRow("technomancer", "Conjuring", false)]
    [DataRow("magician", "Sorcery", true)]
    [DataRow("magician", "Astral", true)]
    [DataRow("mystic-adept", "Conjuring", true)]
    [DataRow("mystic-adept", "Astral", false)]
    [DataRow("adept", "Enchanting", false)]
    [DataRow("adept", "Astral", false)]
    public void Skill_availability_comes_from_talent_not_client_flags(string talent, string skill, bool allowed)
    {
        using var fixture = new Fixture();
        var selection = talent == "mundane" ? Selection() : Ranked("talent", "A") with { TalentId = talent };
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            selection with { Skills = new([new(skill, 1, [])]) });
        Assert.AreEqual(allowed, result.Value is not null);
    }

    [TestMethod]
    public void Aspected_magic_requires_exact_aspect_and_cannot_buy_other_magical_skills()
    {
        using var fixture = new Fixture();
        var selection = Ranked("talent", "A") with { TalentId = "aspected-magician" };
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            selection with { Skills = new([new("Sorcery", 4, [])]) });
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationSkillBlockers.AspectRequired);
        Assert.IsNotNull(fixture.Preview(selection with { Skills = new([new("Sorcery", 4, []), new("Astral", 2, [])], "Sorcery") }));
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            selection with { Skills = new([new("Conjuring", 4, [])], "Sorcery") }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            Selection() with { Skills = new([], "Sorcery") }).Value);
    }

    [TestMethod]
    public void Skills_enforce_creation_maximum_count_and_specialization_costs()
    {
        using var fixture = new Fixture();
        var cases = new (Sr6CreationSkillSpend[] Rows, string Reason)[]
        {
            ([new("Firearms", 6, []), new("Athletics", 6, [])], Sr6CreationSkillBlockers.MaximumCountExceeded),
            ([new("Firearms", 0, ["Pistols"])], Sr6CreationSkillBlockers.SpecializationInvalid),
            ([new("Firearms", 1, ["Pistols", "Rifles"])], Sr6CreationSkillBlockers.SpecializationInvalid),
            ([new("ExoticWeapons", 1, [])], Sr6CreationSkillBlockers.SpecializationInvalid),
            ([new("Firearms", 5, ["Pistols"]), new("Athletics", 5, []), new("Perception", 5, []),
                new("Stealth", 5, []), new("Con", 4, [])], Sr6CreationSkillBlockers.BudgetExceeded)
        };
        foreach (var item in cases)
        {
            var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with { Skills = new(item.Rows) });
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(), item.Reason);
        }
        var quote = fixture.Preview(Selection() with { Skills = new([new("ExoticWeapons", 3, ["Whip", "Net"] )]) });
        Assert.AreEqual(4, quote.Skills!.PointsSpent);
        Assert.IsFalse(quote.Skills.AllPointsSpent);
    }

    [TestMethod]
    public void Skill_shapes_are_bounded_unique_canonical_and_deep_frozen()
    {
        var invalid = new Sr6CreationSkillSelection[]
        {
            new(null!), new([new("Pistols", 1, [])]), new([new("Firearms", 8, [])]),
            new([new("Firearms", -1, [])]), new([new("Firearms", 1, null!)]),
            new([new("Firearms", 1, [" "])]), new([new("Firearms", 1, ["Pistols\n"])]),
            new([new("Firearms", 1, [new string('x', 81)])]),
            new([new("ExoticWeapons", 1, ["Whip", "whip"])]),
            new([new("Firearms", 1, []), new("Firearms", 2, [])]), new([], "Tasking")
        };
        foreach (var selection in invalid)
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeSkills(selection, out _));
        using var fixture = new Fixture();
        // Seven is a valid shape, but requires a Core-issued Aptitude cap.
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeSkills(new([new("Firearms", 7, [])]), out _));
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            Selection() with { Skills = new([new("Firearms", 7, [])]) }).Value);
        var malformed = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with { Skills = invalid[0] });
        CollectionAssert.Contains(malformed.Blockers.ToArray(), Sr6CreationSkillBlockers.InvalidAllocation);
        string[] names = ["Whip", "Net"];
        Sr6CreationSkillSpend[] rows = [new("ExoticWeapons", 2, names), new("Athletics", 1, [])];
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeSelection(Selection() with { Skills = new(rows) }, out var frozen));
        names[0] = "Changed";
        rows[0] = new("ExoticWeapons", 6, []);
        Assert.AreEqual("Athletics", frozen.Skills!.Allocations[0].SkillId);
        CollectionAssert.AreEqual(new[] { "Net", "Whip" }, frozen.Skills.Allocations[1].Specializations.ToArray());
    }

    [TestMethod]
    public void Edited_skill_confirmation_and_forged_projection_never_save()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Skills = new([new("Athletics", 3, [])]) });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, request with { Selection = request.Selection with { Skills = new([]) } }).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = quote with { Skills = quote.Skills! with { PointsRemaining = 99 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
