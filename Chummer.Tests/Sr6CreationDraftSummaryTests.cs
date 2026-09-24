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
    public void Draft_summary_is_saved_coverage_not_finalization_or_implicit_selection(string method)
    {
        using var fixture = new Fixture(method);
        var initial = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!;
        var summary = initial.DraftSummary!;
        Assert.AreEqual(initial.Binding, summary.Binding);
        Assert.IsFalse(summary.FinalizationAvailable);
        Assert.IsNull(summary.Balances);
        Assert.AreEqual("missing", summary.Steps.Single(row => row.Id == "foundation").Status);
        Assert.IsTrue(summary.Steps.Where(row => row.Id != "foundation").All(row => row.Status == "waiting" && !row.CanOpen));
        var choice = (method == "PointBuy" ? PointBuy() : Selection(method)) with
        {
            Attributes = EmptyAttributes(), Skills = new([]), Karma = new([], [], 5),
            Gear = new([new(Guid.NewGuid(), "lined-coat", 2)]), Lifestyle = new("low", 2)
        };
        var request = fixture.Request(choice);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        string bytes = Sr6CreationFoundationIntegrity.Digest(before);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var loaded = cold.Load(fixture.Stamp, fixture.Id).Value!;
        summary = loaded.DraftSummary!;
        Assert.AreEqual(loaded.Binding, summary.Binding);
        Assert.AreNotEqual(request.Binding, summary.Binding, "Summary belongs to current saved revision, not historical review.");
        Assert.IsFalse(summary.FinalizationAvailable);
        Assert.AreEqual("none-selected", summary.Steps.Single(row => row.Id == "qualities").Status);
        Assert.AreEqual("missing", summary.Steps.Single(row => row.Id == "knowledge").Status);
        Assert.AreEqual("unspent", summary.Steps.Single(row => row.Id == "attributes").Status);
        Assert.AreEqual("unspent", summary.Steps.Single(row => row.Id == "skills").Status);
        Assert.AreEqual("saved", summary.Steps.Single(row => row.Id == "lifestyle").Status);
        Assert.IsFalse(summary.Steps.Any(row => row.Id is "talent" or "powers" or "spells" or "forms"));
        var balance = summary.Balances!;
        Assert.AreEqual(1800m, balance.GearSpentNuyen);
        Assert.AreEqual(4000m, balance.LifestyleSpentNuyen);
        Assert.AreEqual(loaded.Selection!.Lifestyle!.RemainingNuyen, balance.RemainingNuyen);
        Assert.AreEqual(loaded.Selection.Lifestyle.ProjectedStartingNuyen, balance.ProjectedStartingNuyen);
        Assert.AreEqual(45, balance.RemainingKarma);
        Assert.AreEqual(5, balance.ProjectedStartingKarma);
        Assert.AreEqual(40, balance.KarmaAboveCarryOver);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        var empty = choice with { Qualities = new([]), Gear = new([]) };
        Assert.IsNotNull(cold.Confirm(fixture.Stamp, fixture.Request(empty)).Value);
        var updated = cold.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!;
        Assert.AreEqual("none-selected", updated.Steps.Single(row => row.Id == "qualities").Status,
            "An immutable empty-quality choice is normalized to null; do not infer reviewed or mandatory missing.");
        Assert.AreEqual("saved", updated.Steps.Single(row => row.Id == "gear").Status);
        Assert.AreEqual(0m, updated.Balances!.GearSpentNuyen);
        Assert.AreEqual(balance.RemainingNuyen + 1800m, updated.Balances.RemainingNuyen);
    }

    [TestMethod]
    [DataRow("Priority", "magician", "spells", true)]
    [DataRow("PointBuy", "magician", "spells", false)]
    [DataRow("Priority", "technomancer", "forms", true)]
    [DataRow("PointBuy", "technomancer", "forms", false)]
    public void Draft_summary_distinguishes_free_entitlements_from_optional_purchase_caps(string method, string talent, string step, bool free)
    {
        using var fixture = new Fixture(method);
        var choice = (method == "PointBuy" ? PointBuy(talent: talent) : Ranked("talent", "A") with { TalentId = talent })
            with { Attributes = EmptyAttributes(), TalentAllocation = new(0) };
        choice = step == "spells" ? choice with { Spells = new([]) } : choice with { ComplexForms = new([]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(choice)).Value);
        var summary = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!;
        var item = summary.Steps.Single(row => row.Id == step);
        Assert.AreEqual(free ? "unspent" : "saved", item.Status);
        Assert.AreEqual(free ? 1 : 0, item.Remainders.Count);
        Assert.IsTrue(item.CanOpen);
        Assert.IsFalse(summary.FinalizationAvailable);
    }

    [TestMethod]
    public void Draft_summary_aspect_dependencies_and_karma_adjusted_knowledge_are_current()
    {
        using var fixture = new Fixture();
        var choice = Ranked("talent", "A") with { TalentId = "aspected-magician", Attributes = EmptyAttributes(),
            Skills = new([], "Conjuring"), Knowledge = new("German", [new(Guid.NewGuid(), "Street lore")], []) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(choice)).Value);
        var summary = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!;
        Assert.AreEqual("waiting", summary.Steps.Single(row => row.Id == "spells").Status);
        Assert.AreEqual("saved", summary.Steps.Single(row => row.Id == "knowledge").Status);
        choice = choice with { TalentAllocation = new(0), Karma = new([new("Logic", 1)], [], 0) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(choice)).Value);
        summary = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!;
        Assert.IsFalse(summary.Steps.Any(row => row.Id == "spells"));
        Assert.AreEqual("unspent", summary.Steps.Single(row => row.Id == "knowledge").Status);
        Assert.AreEqual(1m, summary.Steps.Single(row => row.Id == "knowledge").Remainders.Single().Amount);
        Assert.AreEqual(40, summary.Balances!.RemainingKarma);
    }
}
