using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    private static Sr6CreationAttributeSelection EmptyAttributes()
        => new(Sr6CreationAttributeIds.Ordered.Select(id => new Sr6CreationAttributeSpend(id, 0, 0)).ToArray());

    private static Sr6CreationAttributeSelection Spend(Sr6CreationAttributeSelection selection, string id, int normal, int adjustment)
        => new(selection.Allocations.Select(row => row.AttributeId == id ? new(id, normal, adjustment) : row).ToArray());

    [TestMethod]
    [DataRow("Priority", 3)]
    [DataRow("SumtoTen", 2)]
    public void Attribute_allocation_saves_reopens_and_replays_without_rewriting_character(string method, int perAttribute)
    {
        using var fixture = new Fixture(method);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request()).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var allocation = new Sr6CreationAttributeSelection(Sr6CreationAttributeIds.Ordered.Select((id, index) =>
            new Sr6CreationAttributeSpend(id, index < 8 ? perAttribute : 0, id == "Edge" ? 4 : 0)).ToArray());
        var quote = fixture.Preview(Selection(method) with { Attributes = allocation });
        Assert.IsTrue(quote.Attributes!.AllPointsSpent);
        Assert.AreEqual(0, quote.Attributes.AttributePointsRemaining);
        Assert.AreEqual(0, quote.Attributes.AdjustmentPointsRemaining);
        Assert.AreEqual(5, quote.Attributes.Values.Single(row => row.AttributeId == "Edge").Value);
        Assert.AreEqual(0, quote.Attributes.Values.Single(row => row.AttributeId == "Magic").Value);
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var coldStore = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(coldStore, fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(3L, reopened.Binding.ContentRevision);
        Assert.AreEqual(3L, reopened.Binding.SavedRevision);
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.HasCount(11, reopened.AttributeOptions!);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(before.Document.Content, coldStore.Get(fixture.Id).Value!.Document.Content);
        Assert.HasCount(2, coldStore.Get(fixture.Id).Value!.Document.AuxiliaryState.Sr6CreationFoundationDecisions!);
    }

    [TestMethod]
    [DataRow("dwarf", "Reaction", 4, 5)]
    [DataRow("ork", "Charisma", 4, 5)]
    [DataRow("troll", "Agility", 4, 5)]
    [DataRow("elf", "Agility", 4, 5)]
    public void Adjustment_points_include_reduced_metatype_ranges(string metatype, string id, int spent, int expected)
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { MetatypeId = metatype, Attributes = Spend(EmptyAttributes(), id, 0, spent) });
        Assert.AreEqual(expected, quote.Attributes!.Values.Single(row => row.AttributeId == id).Value);
        Assert.IsFalse(quote.Attributes.AllPointsSpent, "Drafts may retain unspent attribute points, not imply completion.");
        Assert.AreEqual(24, quote.Attributes.AttributePointsRemaining);
    }

    [TestMethod]
    [DataRow("magician", "Magic", 4, 2)]
    [DataRow("aspected-magician", "Magic", 5, 1)]
    [DataRow("adept", "Magic", 4, 2)]
    [DataRow("mystic-adept", "Magic", 4, 2)]
    [DataRow("technomancer", "Resonance", 4, 2)]
    public void Adjustment_changes_final_magic_not_the_priority_base(string talent, string id, int baseRating, int adjustment)
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Ranked("talent", "A") with
            { TalentId = talent, Attributes = Spend(EmptyAttributes(), id, 0, adjustment) });
        Assert.AreEqual(baseRating, id == "Magic" ? quote.BaseMagic : quote.BaseResonance);
        Assert.AreEqual(6, quote.Attributes!.Values.Single(row => row.AttributeId == id).Value);
        Assert.AreEqual(baseRating, quote.Attributes.Values.Single(row => row.AttributeId == id).BaseValue);
    }

    [TestMethod]
    [DataRow("human", "Body", 0, 1, Sr6CreationAttributeBlockers.PointKindUnavailable)]
    [DataRow("elf", "Body", 0, 1, Sr6CreationAttributeBlockers.PointKindUnavailable)]
    [DataRow("human", "Edge", 1, 0, Sr6CreationAttributeBlockers.PointKindUnavailable)]
    [DataRow("human", "Magic", 0, 1, Sr6CreationAttributeBlockers.PointKindUnavailable)]
    [DataRow("human", "Resonance", 0, 1, Sr6CreationAttributeBlockers.PointKindUnavailable)]
    [DataRow("dwarf", "Reaction", 5, 0, Sr6CreationAttributeBlockers.RatingExceeded)]
    [DataRow("human", "Body", 6, 0, Sr6CreationAttributeBlockers.RatingExceeded)]
    [DataRow("human", "Edge", 0, 5, Sr6CreationAttributeBlockers.AdjustmentBudgetExceeded)]
    public void Illegal_attribute_spend_has_no_preview_or_write(string metatype, string id, int normal, int adjustment, string blocker)
    {
        using var fixture = new Fixture();
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with
            { MetatypeId = metatype, Attributes = Spend(EmptyAttributes(), id, normal, adjustment) });
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), blocker);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Normal_maximum_count_and_both_point_pools_are_enforced_separately()
    {
        using var fixture = new Fixture();
        var twoMaximums = Spend(Spend(EmptyAttributes(), "Body", 5, 0), "Agility", 5, 0);
        var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with { Attributes = twoMaximums });
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationAttributeBlockers.MaximumCountExceeded);
        var overspend = new Sr6CreationAttributeSelection(Sr6CreationAttributeIds.Ordered.Select((id, index) =>
            new Sr6CreationAttributeSpend(id, index < 8 ? 4 : 0, 0)).ToArray());
        result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with { Attributes = overspend });
        CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationAttributeBlockers.AttributeBudgetExceeded);
        var humanWithNineAdjustment = new Sr6CreationFoundationSelection("human", "mundane",
            [new("heritage", "C"), new("talent", "E"), new("attributes", "A"), new("skills", "B"), new("resources", "D")])
            { Attributes = Spend(Spend(EmptyAttributes(), "Body", 5, 0), "Edge", 0, 6) };
        var preview = fixture.Preview(humanWithNineAdjustment);
        Assert.AreEqual(7, preview.Attributes!.Values.Single(row => row.AttributeId == "Edge").Value);
        Assert.IsFalse(preview.Attributes.AllPointsSpent);
    }

    [TestMethod]
    public void Attribute_shape_is_canonical_bounded_and_detached_from_caller_arrays()
    {
        var valid = EmptyAttributes();
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeAttributes(new(valid.Allocations.Reverse().ToArray()), out var frozen));
        CollectionAssert.AreEqual(Sr6CreationAttributeIds.Ordered.ToArray(), frozen!.Allocations.Select(row => row.AttributeId).ToArray());
        foreach (var invalid in new[] {
            new Sr6CreationAttributeSelection([]), new Sr6CreationAttributeSelection(null!),
            new Sr6CreationAttributeSelection(valid.Allocations.Append(valid.Allocations[0]).ToArray()),
            new Sr6CreationAttributeSelection(valid.Allocations.Select(row => row with { AttributeId = "Body" }).ToArray()),
            Spend(valid, "Body", -1, 0), Spend(valid, "Body", int.MaxValue, 0), Spend(valid, "Body", 0, 14),
            new Sr6CreationAttributeSelection(valid.Allocations.Select(row => row with { AttributeId = row.AttributeId.ToLowerInvariant() }).ToArray()) })
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeAttributes(invalid, out _));
        var array = valid.Allocations.ToArray();
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeSelection(Selection() with { Attributes = new(array) }, out var choice));
        array[0] = array[0] with { AttributePoints = 24 };
        Assert.AreEqual(0, choice.Attributes!.Allocations[0].AttributePoints);
    }

    [TestMethod]
    public void Legacy_foundation_serialization_and_preview_digests_do_not_change()
    {
        using var fixture = new Fixture();
        var selection = Selection();
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(new { selection.MetatypeId, selection.TalentId, selection.Assignments }),
            Sr6CreationFoundationIntegrity.Digest(selection));
        var quote = fixture.Preview(selection);
        var oldShape = new { quote.Binding, quote.Selection, quote.Budget, quote.BaseMagic, quote.BaseResonance, quote.SourceAnchorIds, quote.PreviewDigest };
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(oldShape), Sr6CreationFoundationIntegrity.Digest(quote));
    }

    [TestMethod]
    public void Attribute_review_tampering_stale_foundation_and_redigested_values_are_rejected()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Attributes = Spend(EmptyAttributes(), "Edge", 0, 4) });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, request with { Selection = request.Selection with { Attributes = EmptyAttributes() } }).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = quote with { Attributes = quote.Attributes! with { AttributePointsRemaining = 500 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(Selection() with { MetatypeId = "elf" })).Value);
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
    }
}
