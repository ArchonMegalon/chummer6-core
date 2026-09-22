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
    public void Karma_knowledge_and_languages_charge_only_new_levels_and_reopen(string method)
    {
        using var fixture = new Fixture(method);
        Guid spanish = Guid.NewGuid(), topic = Guid.NewGuid(), german = Guid.NewGuid();
        var seed = KarmaSeed(method) with { Knowledge = new("English", [], [new(spanish, "Spanish", "basic")]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        seed = seed with { Karma = new([], [], 32) { Knowledge = new([new(topic, "Seattle gangs")],
            [new(spanish, "Spanish", "expert"), new(german, "German", "expert")]) } };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(50, quote.Karma!.KarmaSpent);
        Assert.AreEqual(18, quote.Karma.Knowledge!.KarmaCost);
        Assert.AreEqual(6, quote.Karma.Knowledge.Languages.Single(row => row.Id == spanish).KarmaCost);
        Assert.AreEqual(9, quote.Karma.Knowledge.Languages.Single(row => row.Id == german).KarmaCost);
        Assert.AreEqual(3, quote.Karma.Knowledge.Languages.Single(row => row.Id == german).ComprehensionBonus);
        Assert.AreEqual(1, quote.Knowledge!.PointsSpent);
        Assert.AreEqual("basic", quote.Knowledge.Languages.Single().Level);
        Assert.AreEqual(fixture.Preview(seed with { Karma = null }).PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var loaded = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, loaded.Selection!.PreviewDigest);
        Assert.AreEqual(topic, loaded.Selection.Karma!.Knowledge!.KnowledgeSkills.Single().Id);
        Assert.AreEqual(3, loaded.KarmaKnowledgeOptions!.KnowledgeKarmaCost);
        Assert.HasCount(3, loaded.KarmaKnowledgeOptions.LanguageLevels);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(3L, store.Get(fixture.Id).Value!.ContentRevision);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
    }

    [TestMethod]
    public void Karma_knowledge_rejects_duplicate_topics_languages_native_and_identity_rebinding()
    {
        using var fixture = new Fixture();
        Guid poolTopic = Guid.NewGuid(), spanish = Guid.NewGuid();
        var seed = KarmaSeed("Priority") with { Knowledge = new("English", [new(poolTopic, "Seattle")], [new(spanish, "Spanish", "basic")]),
            Karma = new([new("Logic", 1)], [], 0) };
        Sr6CreationFoundationSelection Buy(Sr6CreationKarmaKnowledgeSelection purchase)
            => seed with { Karma = seed.Karma! with { Knowledge = purchase } };
        foreach (var bad in new[] {
            Buy(new([new(Guid.NewGuid(), "SEATTLE")], [])),
            Buy(new([new(poolTopic, "Magic")], [])),
            Buy(new([new(spanish, "Magic")], [])),
            Buy(new([], [new(Guid.NewGuid(), "ENGLISH", "basic")])),
            Buy(new([], [new(Guid.NewGuid(), "SPANISH", "expert")])),
            Buy(new([], [new(spanish, "German", "expert")])),
            Buy(new([], [new(poolTopic, "German", "expert")])),
            Buy(new([], [new(spanish, "Spanish", "basic")]))
        }) Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, bad).Value);
        var valid = Buy(new([], [new(spanish, "Spanish", "specialist")]));
        Assert.AreEqual(13, fixture.Preview(valid).Karma!.KarmaSpent); // Logic 10 + language 3
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, valid with { Knowledge = null }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            valid with { Karma = valid.Karma! with { Attributes = [] } }).Value); // free pool exceeds Logic
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_knowledge_uses_shared_budget_and_rechecks_changed_pool_baseline()
    {
        using var fixture = new Fixture();
        Guid language = Guid.NewGuid();
        var seed = KarmaSeed("Priority") with { Knowledge = new("English", [], [new(language, "Spanish", "basic")]),
            Karma = new([], [], 39) { Specializations = [new("Athletics", "Climbing")],
                Knowledge = new([], [new(language, "Spanish", "expert")]) } };
        Assert.AreEqual(50, fixture.Preview(seed).Karma!.KarmaSpent);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Karma = seed.Karma! with { KarmaForNuyen = 40 } }).Value);
        var raised = seed with { Karma = seed.Karma! with { Attributes = [new("Logic", 1)], KarmaForNuyen = 0 },
            Knowledge = new("English", [], [new(language, "Spanish", "specialist")]) };
        Assert.AreEqual(3, fixture.Preview(raised).Karma!.Knowledge!.Languages.Single().KarmaCost);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            raised with { Knowledge = new("English", [], [new(language, "Spanish", "expert")]) }).Value);
    }

    [TestMethod]
    public void Karma_knowledge_shape_is_bounded_detached_and_preserves_empty_history_bytes()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { Karma = new([], [], 0) };
        Assert.AreEqual(fixture.Preview(seed).PreviewDigest,
            fixture.Preview(seed with { Karma = seed.Karma! with { Knowledge = new([], []) } }).PreviewDigest);
        Guid id = Guid.NewGuid();
        foreach (var malformed in new Sr6CreationKarmaKnowledgeSelection[] {
            new(null!, []), new([], null!), new([null!], []), new([], [null!]),
            new([new(Guid.Empty, "Seattle")], []), new([new(id, " Seattle")], []),
            new([new(id, "a\nb")], []), new([new(id, new string('x', 81))], []),
            new([new(id, "e\u0301")], []), new([new(id, "Seattle")], [new(id, "German", "basic")]),
            new([new(id, "Seattle"), new(Guid.NewGuid(), "SEATTLE")], []),
            new([], [new(id, "German", "native")]), new([], [new(id, "German", "Basic")]),
            new([], [new(id, "German", "basic"), new(Guid.NewGuid(), "GERMAN", "expert")]),
            new(Enumerable.Range(0, 33).Select(i => new Sr6CreationKnowledgeEntry(Guid.NewGuid(), "Topic" + i)).ToArray(), [])
        }) Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeKarma(new([], [], 0) { Knowledge = malformed }, out _));
        var topics = new List<Sr6CreationKnowledgeEntry> { new(id, "Seattle"), new(Guid.NewGuid(), "Magic") };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeKarma(new([], [], 0) { Knowledge = new(topics, []) }, out var frozen));
        string digest = Sr6CreationFoundationIntegrity.Digest(frozen);
        topics.Reverse();
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeKarma(new([], [], 0) { Knowledge = new(topics, []) }, out var reordered));
        Assert.AreEqual(digest, Sr6CreationFoundationIntegrity.Digest(reordered));
        topics.Clear();
        Assert.HasCount(2, frozen!.Knowledge!.KnowledgeSkills);
    }

    [TestMethod]
    public void Redigested_Karma_language_cost_or_comprehension_is_not_rule_authority()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { Knowledge = new("English", [], []), Karma = new([], [], 0)
            { Knowledge = new([], [new(Guid.NewGuid(), "German", "expert")]) } };
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, fixture.Request(seed), out var candidate, out var decision));
        var knowledge = decision.Preview.Karma!.Knowledge!;
        foreach (var changed in new[] { knowledge.Languages.Single() with { KarmaCost = 0 },
            knowledge.Languages.Single() with { ComprehensionBonus = 9 } })
        {
            var preview = decision.Preview with { Karma = decision.Preview.Karma with
                { Knowledge = knowledge with { Languages = [changed] } } };
            preview = preview with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(preview) };
            var forged = decision with { Preview = preview, Command = decision.Command with { PreviewDigest = preview.PreviewDigest } };
            forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
            var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
            Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
            Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
                Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
        }
    }
}
