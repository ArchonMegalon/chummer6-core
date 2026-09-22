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
    public void Contacts_use_final_charisma_without_spending_karma_or_character_points_and_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var id = Guid.NewGuid();
        var second = Guid.NewGuid();
        var seed = (method == "PointBuy" ? PointBuy() : Selection(method)) with
        {
            Attributes = EmptyAttributes(), Skills = new([]), Karma = new([new("Charisma", 2)], [], 0),
            Contacts = new([new(id, "Café", "Fixer", 3, 3), new(second, "Café", null, 2, 2)])
        };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(3, quote.Contacts!.Options.Charisma);
        Assert.AreEqual(18, quote.Contacts.Options.PointBudget);
        Assert.AreEqual(3, quote.Contacts.Options.MaximumRating);
        Assert.AreEqual(10, quote.Contacts.PointsSpent);
        Assert.AreEqual(8, quote.Contacts.PointsRemaining);
        Assert.IsTrue(quote.Contacts.NeedsGmReview);
        var without = fixture.Preview(seed with { Contacts = null });
        Assert.AreEqual(without.Karma!.KarmaSpent, quote.Karma!.KarmaSpent);
        Assert.AreEqual(without.PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(3, reopened.ContactOptions!.Charisma);
        Assert.AreEqual(2L, reopened.Binding.SavedRevision);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        seed = seed with { Contacts = new([new(id, "Renamed", "Street doc", 1, 3)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var edited = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(id, edited.Selection!.Contacts!.Contacts.Single().Contact.Id);
        Assert.AreEqual("Renamed", edited.Selection.Contacts.Contacts.Single().Contact.Name);
        Assert.AreEqual(4, edited.Selection.Contacts.PointsSpent);
        Assert.AreEqual(3L, edited.Binding.SavedRevision);
    }

    [TestMethod]
    public void Sr6_contacts_allow_loyalty_above_six_and_require_attributes()
    {
        using var fixture = new Fixture();
        var seed = Selection() with { MetatypeId = "elf", Contacts = new([new(Guid.NewGuid(), "Friend", null, 7, 7)]) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed).Blockers.ToArray(),
            Sr6CreationContactBlockers.AttributesRequired);
        var quote = fixture.Preview(seed with { Attributes = Spend(EmptyAttributes(), "Charisma", 6, 0) });
        Assert.AreEqual(7, quote.Contacts!.Options.Charisma);
        Assert.AreEqual(14, quote.Contacts.PointsSpent);
    }

    [TestMethod]
    public void Contacts_reject_rating_and_budget_excess_and_do_not_delete_after_charisma_changes()
    {
        using var fixture = new Fixture();
        var seed = Selection() with { Attributes = Spend(EmptyAttributes(), "Charisma", 2, 0),
            Contacts = new([new(Guid.NewGuid(), "Friend", null, 3, 3)]) };
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var original = fixture.Store.Get(fixture.Id).Value!;
        var lower = fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Attributes = EmptyAttributes() });
        CollectionAssert.Contains(lower.Blockers.ToArray(), Sr6CreationContactBlockers.RatingExceeded);
        var overspent = seed with { Contacts = new(Enumerable.Range(0, 4)
            .Select(i => new Sr6CreationContactChoice(Guid.NewGuid(), "Friend " + i, null, 3, 3)).ToArray()) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, overspent).Blockers.ToArray(),
            Sr6CreationContactBlockers.BudgetExceeded);
        var unchanged = fixture.Store.Get(fixture.Id).Value!;
        Assert.AreEqual(original.ContentRevision, unchanged.ContentRevision);
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(original.Document), Sr6CreationFoundationIntegrity.Digest(unchanged.Document));
        var empty = fixture.Preview(seed with { Contacts = new([]) });
        Assert.AreEqual(0, empty.Contacts!.PointsSpent);
        Assert.IsFalse(empty.Contacts.NeedsGmReview);
        Assert.IsFalse(empty.Contacts.AllPointsSpent);
    }

    [TestMethod]
    public void Contact_transport_rejects_hostile_shapes_and_freezes_stable_ids()
    {
        var id = Guid.NewGuid();
        var row = new Sr6CreationContactChoice(id, "Café", null, 1, 1);
        var invalid = new Sr6CreationContactSelection[]
        {
            new(null!), new([null!]), new([row with { Id = Guid.Empty }]), new([row, row]),
            new([row with { Name = "" }]), new([row with { Name = " Cafe" }]), new([row with { Name = "Cafe\u0301" }]),
            new([row with { Name = "\ud800" }]), new([row with { Name = new string('x', 81) }]),
            new([row with { Role = "" }]), new([row with { Role = "Fixer\n" }]),
            new([row with { Connection = 0 }]), new([row with { Connection = int.MaxValue }]),
            new([row with { Loyalty = -1 }]), new([row with { Loyalty = 13 }]),
            new(Enumerable.Range(0, 65).Select(i => row with { Id = Guid.NewGuid() }).ToArray())
        };
        using var fixture = new Fixture();
        foreach (var selection in invalid)
        {
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeContacts(selection, out _));
            CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                Selection() with { Attributes = EmptyAttributes(), Contacts = selection }).Blockers.ToArray(),
                Sr6CreationContactBlockers.InvalidSelection);
        }
        Sr6CreationContactChoice[] rows = [row, row with { Id = Guid.NewGuid() }];
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeContacts(new(rows), out var frozen));
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeContacts(new(rows.Reverse().ToArray()), out var reversed));
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(frozen), Sr6CreationFoundationIntegrity.Digest(reversed));
        rows[0] = row with { Name = "Changed" };
        Assert.IsTrue(frozen!.Contacts.All(contact => contact.Name == "Café"));
    }

    [TestMethod]
    public void Contacts_reject_rehashed_forged_costs_on_reopen()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Attributes = EmptyAttributes(),
            Contacts = new([new(Guid.NewGuid(), "Friend", null, 1, 1)]) });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = quote with { Contacts = quote.Contacts! with { PointsSpent = 0, PointsRemaining = 99 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
