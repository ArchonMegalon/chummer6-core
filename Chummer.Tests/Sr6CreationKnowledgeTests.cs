using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    private static Sr6CreationKnowledgeSelection Knowledge(string native = "German")
        => new(native, [], []);

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    public void Knowledge_and_languages_save_and_reopen_with_stable_ids_and_other_allocations(string method)
    {
        using var fixture = new Fixture(method);
        var topicId = Guid.NewGuid();
        var languageId = Guid.NewGuid();
        var selection = Selection(method) with { Attributes = Spend(EmptyAttributes(), "Logic", 2, 0),
            Skills = new([new("Athletics", 3, [])]), Knowledge = new("German",
                [new(topicId, "Seattle gangs")], [new(languageId, "Sperethiel", Sr6CreationLanguageLevels.Specialist)]) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(3, quote.Knowledge!.Logic);
        Assert.AreEqual(3, quote.Knowledge.PointsSpent);
        Assert.IsTrue(quote.Knowledge.AllPointsSpent);
        Assert.AreEqual(2, quote.Knowledge.Languages.Single().ComprehensionBonus);
        Assert.AreEqual(3, quote.Skills!.PointsSpent, "Independent point pools must not be mixed.");
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(topicId, reopened.Selection.Knowledge!.KnowledgeSkills.Single().Id);
        Assert.AreEqual(languageId, reopened.Selection.Knowledge.Languages.Single().Id);
        Assert.AreEqual(3, reopened.KnowledgePointBudget);
        Assert.AreEqual(2L, reopened.Binding.SavedRevision);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    [DataRow("basic", 1, 0)]
    [DataRow("specialist", 2, 2)]
    [DataRow("expert", 3, 3)]
    public void Language_levels_consume_cumulative_knowledge_points(string level, int cost, int bonus)
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Attributes = Spend(EmptyAttributes(), "Logic", 4, 0),
            Knowledge = new("German", [], [new(Guid.NewGuid(), "English", level)]) });
        Assert.AreEqual(cost, quote.Knowledge!.PointsSpent);
        Assert.AreEqual(5 - cost, quote.Knowledge.PointsRemaining);
        Assert.AreEqual(bonus, quote.Knowledge.Languages.Single().ComprehensionBonus);
        Assert.IsFalse(quote.Knowledge.AllPointsSpent);
    }

    [TestMethod]
    public void Native_language_is_free_and_knowledge_requires_attribute_authority()
    {
        using var fixture = new Fixture();
        var missing = fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection() with { Knowledge = Knowledge() });
        CollectionAssert.Contains(missing.Blockers.ToArray(), Sr6CreationKnowledgeBlockers.AttributesRequired);
        var quote = fixture.Preview(Selection() with { Attributes = EmptyAttributes(), Knowledge = Knowledge() });
        Assert.AreEqual(0, quote.Knowledge!.PointsSpent);
        Assert.AreEqual(1, quote.Knowledge.PointsRemaining);
        Assert.AreEqual("German", quote.Knowledge.NativeLanguage);
        Assert.AreEqual(quote.Attributes!.AuthorityDigest, quote.Knowledge.AttributeAuthorityDigest);
    }

    [TestMethod]
    public void Lowering_logic_cannot_silently_keep_overbudget_knowledge_or_reuse_old_confirmation()
    {
        using var fixture = new Fixture();
        var selection = Selection() with { Attributes = Spend(EmptyAttributes(), "Logic", 2, 0),
            Knowledge = new("German", [], [new(Guid.NewGuid(), "English", "expert")]) };
        var quote = fixture.Preview(selection);
        var changed = selection with { Attributes = EmptyAttributes() };
        var lower = fixture.Service.Preview(fixture.Stamp, fixture.Binding, changed);
        CollectionAssert.Contains(lower.Blockers.ToArray(), Sr6CreationKnowledgeBlockers.BudgetExceeded);
        Assert.IsNull(fixture.Service.Confirm(fixture.Stamp, new(quote.Binding, changed, quote.PreviewDigest, Guid.NewGuid(), true)).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        var higher = fixture.Preview(selection with { Attributes = Spend(EmptyAttributes(), "Logic", 3, 0) });
        Assert.AreEqual(1, higher.Knowledge!.PointsRemaining);
        Assert.AreNotEqual(quote.Knowledge!.AuthorityDigest, higher.Knowledge.AuthorityDigest);
    }

    [TestMethod]
    public void Knowledge_shapes_reject_duplicates_invalid_names_levels_and_aliasing()
    {
        var id = Guid.NewGuid();
        var invalid = new Sr6CreationKnowledgeSelection[]
        {
            Knowledge(""), Knowledge(" German"), Knowledge("German\n"), Knowledge(new string('x', 81)), Knowledge("\ud800"),
            new("German", null!, []), new("German", [], null!),
            new("German", [new(Guid.Empty, "Seattle")], []),
            new("German", [new(id, "Seattle")], [new(id, "English", "basic")]),
            new("German", [new(id, "Seattle"), new(Guid.NewGuid(), "SEATTLE")], []),
            new("German", [new(id, "Cafe\u0301")], []),
            new("German", [], [new(id, "german", "basic")]),
            new("German", [], [new(id, "English", "native")]),
            new("German", [], [new(id, "English", "expert"), new(Guid.NewGuid(), "ENGLISH", "basic")]),
            new("German", Enumerable.Range(0, 33).Select(i => new Sr6CreationKnowledgeEntry(Guid.NewGuid(), "Topic " + i)).ToArray(), [])
        };
        using var fixture = new Fixture();
        foreach (var selection in invalid)
        {
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeKnowledge(selection, out _));
            var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                Selection() with { Attributes = EmptyAttributes(), Knowledge = selection });
            CollectionAssert.Contains(result.Blockers.ToArray(), Sr6CreationKnowledgeBlockers.InvalidSelection);
        }
        Sr6CreationKnowledgeEntry[] topics = [new(id, "Café")];
        Sr6CreationLanguageEntry[] languages = [new(Guid.NewGuid(), "English", "basic")];
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeSelection(Selection() with { Knowledge = new("German", topics, languages) }, out var frozen));
        topics[0] = new(id, "Changed");
        languages[0] = languages[0] with { Level = "expert" };
        Assert.AreEqual("Café", frozen.Knowledge!.KnowledgeSkills.Single().Name);
        Assert.AreEqual("basic", frozen.Knowledge.Languages.Single().Level);
    }

    [TestMethod]
    public void Knowledge_projection_cannot_be_forged_by_rehashing_a_saved_decision()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Attributes = EmptyAttributes(), Knowledge = Knowledge() });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = quote with { Knowledge = quote.Knowledge! with { Logic = 99, PointsRemaining = 99 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
