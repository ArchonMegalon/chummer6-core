using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerQualityGraphTests
{
    private static readonly Guid SourceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string Digest = CharacterCareerQualityGraph.Hash("synthetic authority");
    private const string Empty = "<character><created>True</created><karma>100</karma><qualities/><improvements/><expenses/><notes>keep me</notes></character>";

    [TestMethod]
    public void Career_quality_graph_adds_actual_quality_effect_karma_and_expense_together()
    {
        var graph = Project(Empty, Source(), InstanceId);
        var (quote, plan) = Plan(graph);
        Assert.AreEqual(8, quote.RuleKarmaCost);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(Empty, graph, quote, plan, out string? payload));
        var root = XElement.Parse(payload!);
        Assert.AreEqual("92", root.Element("karma")!.Value);
        var quality = root.Element("qualities")!.Element("quality")!;
        Assert.AreEqual(InstanceId.ToString("D"), quality.Element("guid")!.Value);
        Assert.AreEqual(SourceId.ToString("D"), quality.Element("sourceid")!.Value);
        Assert.AreEqual("4", quality.Element("bp")!.Value, "Saved BP is base cost, not Career's doubled price.");
        Assert.AreEqual("Selected", quality.Element("qualitysource")!.Value);
        var effect = root.Element("improvements")!.Element("improvement")!;
        Assert.AreEqual("Ambidextrous", effect.Element("improvementttype")!.Value);
        Assert.AreEqual(InstanceId.ToString("D"), effect.Element("sourcename")!.Value);
        Assert.AreEqual("Quality", effect.Element("improvementsource")!.Value);
        var expense = root.Element("expenses")!.Element("expense")!;
        Assert.AreEqual("-8", expense.Element("amount")!.Value);
        Assert.AreEqual("False", expense.Element("forcecareervisible")!.Value);
        Assert.AreEqual("AddQuality", expense.Element("undo")!.Element("karmatype")!.Value);
        Assert.AreEqual(plan.ExpenseDateLocal, DateTime.Parse(expense.Element("date")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual("keep me", root.Element("notes")!.Value);
        Assert.AreEqual("100", XElement.Parse(Empty).Element("karma")!.Value);
    }

    [TestMethod]
    public void Career_quality_graph_negative_acquisition_does_not_award_karma_and_buyoff_charges_once()
    {
        var source = Source(negative: true);
        string added = Apply(Empty, source, InstanceId);
        var root = XElement.Parse(added);
        Assert.AreEqual("100", root.Element("karma")!.Value);
        Assert.AreEqual("0", root.Element("expenses")!.Element("expense")!.Element("amount")!.Value);
        var graph = Project(added, source, InstanceId, CharacterCareerQualityOperation.RemoveLevel);
        var (quote, plan) = Plan(graph);
        Assert.AreEqual(8, quote.RuleKarmaCost);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(added, graph, quote, plan, out var removed));
        root = XElement.Parse(removed!);
        Assert.AreEqual("92", root.Element("karma")!.Value);
        Assert.IsFalse(root.Element("qualities")!.HasElements);
        Assert.IsFalse(root.Element("improvements")!.HasElements);
        Assert.HasCount(2, root.Element("expenses")!.Elements().ToArray());
    }

    [TestMethod]
    public void Career_quality_graph_first_level_bonus_is_not_repeated_or_reassigned_on_removal()
    {
        var source = Source(firstLevel: true);
        string first = Apply(Empty, source, InstanceId);
        string second = Apply(first, source, SecondId);
        var before = XElement.Parse(second);
        Assert.HasCount(3, before.Element("improvements")!.Elements().ToArray());
        string removed = Apply(second, source, InstanceId, CharacterCareerQualityOperation.RemoveLevel);
        var after = XElement.Parse(removed);
        Assert.HasCount(1, after.Element("qualities")!.Elements().ToArray());
        Assert.HasCount(1, after.Element("improvements")!.Elements().ToArray());
        Assert.AreEqual(SecondId.ToString("D"), after.Element("improvements")!.Element("improvement")!.Element("sourcename")!.Value);
        Assert.AreEqual("Ambidextrous", after.Element("improvements")!.Element("improvement")!.Element("improvementttype")!.Value);
        // Chummer5 removes improvements by the removed instance ID. A surviving
        // level does not acquire a new first-level grant behind the user's back.
        string third = Apply(removed, source, Guid.Parse("33333333-3333-3333-3333-333333333333"));
        Assert.HasCount(2, XElement.Parse(third).Element("improvements")!.Elements().ToArray());
    }

    [TestMethod]
    [DataRow("duplicate-first-level")]
    [DataRow("conflicting-type")]
    public void Career_quality_graph_rejects_inconsistent_other_levels_for_the_same_source(string damage)
    {
        var source = Source(firstLevel: true);
        var root = XElement.Parse(Apply(Apply(Empty, source, InstanceId), source, SecondId));
        if (damage == "duplicate-first-level")
        {
            var firstLevel = new XElement(root.Element("improvements")!.Elements()
                .Single(row => row.Element("improvementttype")!.Value == "Erased"));
            firstLevel.Element("sourcename")!.Value = SecondId.ToString("D");
            root.Element("improvements")!.Add(firstLevel);
        }
        else root.Element("qualities")!.Elements()
            .Single(row => row.Element("guid")!.Value == SecondId.ToString("D"))
            .Element("qualitytype")!.Value = "Negative";
        string xml = root.ToString();
        Assert.IsFalse(CharacterCareerQualityGraph.TryProject(xml, source, new(InstanceId, SourceId),
            CharacterCareerQualityOperation.RemoveLevel, null, out _));
        Assert.IsFalse(CharacterCareerQualityGraph.TryObserve(xml, source, new(InstanceId, SourceId), null,
            new("synthetic-local-owner", "career-quality-fixture", 2, 2, Digest, Digest), Digest,
            Guid.NewGuid(), out _, out _));
    }

    [TestMethod]
    public void Career_quality_graph_full_removal_preserves_unrelated_quality_effects_and_notes()
    {
        var source = Source(firstLevel: true);
        string current = Apply(Apply(Empty, source, InstanceId), source, SecondId);
        Guid otherId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var other = Source(otherSource: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        current = Apply(current, other, otherId);
        var before = XElement.Parse(current);
        var savedOther = before.Element("qualities")!.Elements().Single(row => row.Element("guid")!.Value == otherId.ToString("D"));
        var otherEffect = before.Element("improvements")!.Elements().Single(row => row.Element("sourcename")!.Value == otherId.ToString("D"));
        string result = Apply(current, source, SecondId, CharacterCareerQualityOperation.RemoveAllLevels);
        var after = XElement.Parse(result);
        Assert.IsTrue(XNode.DeepEquals(savedOther, after.Element("qualities")!.Elements().Single()));
        Assert.IsTrue(XNode.DeepEquals(otherEffect, after.Element("improvements")!.Elements().Single()));
        Assert.AreEqual(before.Element("karma")!.Value, after.Element("karma")!.Value);
        Assert.IsTrue(XNode.DeepEquals(before.Element("expenses"), after.Element("expenses")));
    }

    [TestMethod]
    [DataRow("<required><oneof><quality>Something</quality></oneof></required>")]
    [DataRow("<forbidden><oneof><quality>Something</quality></oneof></forbidden>")]
    [DataRow("<costdiscount><value>-1</value></costdiscount>")]
    [DataRow("<unknown/>")]
    [DataRow("<careeronly>False</careeronly>")]
    [DataRow("<bonus><selecttext/></bonus>")]
    [DataRow("<firstlevelbonus><addweapon>Something</addweapon></firstlevelbonus>")]
    public void Career_quality_graph_never_discards_uncompiled_semantics(string addition)
    {
        var source = Source();
        source = source with { RawSourceXml = source.RawSourceXml.Replace("</quality>", addition + "</quality>") };
        Assert.IsFalse(CharacterCareerQualityGraph.TryProject(Empty, source, new(InstanceId, source.SourceId),
            CharacterCareerQualityOperation.AcquireLevel, null, out _));
    }

    [TestMethod]
    [DataRow("duplicate-id")]
    [DataRow("altered-effect")]
    [DataRow("foreign-effect")]
    [DataRow("missing-effect")]
    [DataRow("altered-price")]
    [DataRow("shadow-container")]
    [DataRow("source-id")]
    public void Career_quality_graph_rejects_ambiguous_or_inconsistent_saved_graph(string damage)
    {
        var source = Source();
        var root = XElement.Parse(Apply(Empty, source, InstanceId));
        var quality = root.Element("qualities")!.Element("quality")!;
        var effect = root.Element("improvements")!.Element("improvement")!;
        switch (damage)
        {
            case "duplicate-id": quality.AddAfterSelf(new XElement(quality)); break;
            case "altered-effect": effect.Element("val")!.Value = "99"; break;
            case "foreign-effect": effect.Element("improvementsource")!.Value = "Gear"; break;
            case "missing-effect": effect.Remove(); break;
            case "altered-price": quality.Element("bp")!.Value = "1"; break;
            case "shadow-container": root.Add(new XElement(XName.Get("qualities", "urn:shadow"))); break;
            case "source-id": quality.Element("sourceid")!.Value = SecondId.ToString("D"); break;
        }
        Assert.IsFalse(CharacterCareerQualityGraph.TryProject(root.ToString(), source, new(InstanceId, source.SourceId),
            CharacterCareerQualityOperation.RemoveLevel, null, out _));
    }

    [TestMethod]
    public void Career_quality_graph_refuses_stale_payload_forged_cost_and_duplicate_expense()
    {
        var source = Source();
        var graph = Project(Empty, source, InstanceId);
        var (quote, plan) = Plan(graph);
        Assert.IsFalse(CharacterCareerQualityGraph.TryApply(Empty.Replace("100", "99"), graph, quote, plan, out _));
        Assert.IsFalse(CharacterCareerQualityGraph.TryApply(Empty, graph, quote, plan with { SavedCharacterKarma = 100 }, out _));
        Assert.IsFalse(CharacterCareerQualityGraph.TryApply(Empty, graph, quote with { Authority = quote.Authority with
            { Effects = quote.Authority.Effects with { DeltaDigest = Digest } } }, plan, out _));
        string collision = Empty.Replace("<expenses/>", $"<expenses><expense><guid>{plan.TransactionId:D}</guid></expense></expenses>");
        var collisionGraph = Project(collision, source, InstanceId);
        var (collisionQuote, collisionPlan) = Plan(collisionGraph, plan.TransactionId);
        Assert.IsFalse(CharacterCareerQualityGraph.TryApply(collision, collisionGraph, collisionQuote, collisionPlan, out _));
    }

    [TestMethod]
    public void Career_quality_source_flags_and_unlimited_levels_retain_career_meaning()
    {
        var source = Source();
        string xml = source.RawSourceXml.Replace("<limit>5</limit>", "<limit>False</limit><careeronly/><refundkarma/>");
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(xml, source.SourceId,
            InstanceId, null, false, out var definition, out _, out _));
        Assert.IsTrue(definition.CareerOnly);
        Assert.IsTrue(definition.RefundKarmaOnRemove);
        Assert.IsTrue(definition.LimitIsUnlimited);
        Assert.AreEqual(0, definition.LevelLimit);
        xml = xml.Replace("<limit>False</limit>", "<nolevels/>").Replace("<careeronly/>", "<chargenonly/>");
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(xml, source.SourceId,
            InstanceId, null, false, out definition, out _, out _));
        Assert.IsTrue(definition.NoLevels);
        Assert.IsTrue(definition.ChargenOnly);
        Assert.AreEqual(1, definition.LevelLimit);
    }

    [TestMethod]
    [DataRow(CharacterCareerQualityOperation.AcquireLevel)]
    [DataRow(CharacterCareerQualityOperation.RemoveLevel)]
    [DataRow(CharacterCareerQualityOperation.RemoveAllLevels)]
    public void Career_quality_receipt_is_recovered_from_serialized_state_and_actual_expense(
        CharacterCareerQualityOperation operation)
    {
        var source = Source();
        string before = operation == CharacterCareerQualityOperation.AcquireLevel
            ? Empty : Apply(Empty, source, InstanceId);
        var (quote, plan) = Plan(Project(before, source, InstanceId, operation));
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(before,
            Project(before, source, InstanceId, operation), quote, plan, out var payload));
        var binding = quote.Binding with { WorkspaceRevision = plan.TargetWorkspaceRevision,
            SavedRevision = plan.TargetSavedRevision };
        // A separately parsed/formatted payload represents reading serialized
        // bytes, not copying the desired plan into an observation.
        string reread = XElement.Parse(payload!).ToString();
        if (operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            Assert.IsTrue(CharacterCareerQualityGraph.TryProject(reread, source, new(SecondId, SourceId),
                CharacterCareerQualityOperation.AcquireLevel, null, out _),
                "The serialized acquired graph must also remain usable for a later quote.");
        }
        Assert.IsTrue(CharacterCareerQualityGraph.TryObserve(reread, source, plan.Identity,
            null, binding, quote.RuleDigest, plan.TransactionId, out var observed, out var expense));
        Assert.AreEqual(plan.SavedCharacterKarma, observed!.AvailableKarma);
        Assert.AreEqual(plan.InstancesAfter.Count, observed.Instances.Count);
        Assert.AreEqual(plan.CreatesExpense ? 1 : 0, expense!.MatchingEntryCount);
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateReceipt(plan.TransactionId, quote, plan,
            observed, expense, out var receipt));
        var savedReceipt = JsonSerializer.Deserialize<CharacterCareerQualityReceipt>(JsonSerializer.Serialize(receipt))!;
        Assert.IsTrue(CharacterCareerQualityRules.TryRecoverReceipt(savedReceipt, plan.TransactionId,
            observed, expense, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryRecoverReceipt(savedReceipt, plan.TransactionId,
            observed with { Binding = binding with { OwnerId = "another-owner" } }, expense,
            receipt.ReceiptDigest, out _));
    }

    [TestMethod]
    [DataRow("karma")]
    [DataRow("expense-amount")]
    [DataRow("expense-reason")]
    [DataRow("expense-undo")]
    [DataRow("expense-missing")]
    [DataRow("expense-duplicate")]
    [DataRow("expense-structured")]
    [DataRow("expense-zoned")]
    [DataRow("effects")]
    [DataRow("quality")]
    [DataRow("origin")]
    [DataRow("scalar-whitespace")]
    [DataRow("effect-scalar-whitespace")]
    [DataRow("revision")]
    public void Career_quality_observation_cannot_turn_damaged_saved_bytes_into_success(string damage)
    {
        var source = Source();
        var graph = Project(Empty, source, InstanceId);
        var (quote, plan) = Plan(graph);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(Empty, graph, quote, plan, out var payload));
        var root = XElement.Parse(payload!);
        var entry = root.Element("expenses")!.Element("expense")!;
        var binding = quote.Binding with { WorkspaceRevision = plan.TargetWorkspaceRevision,
            SavedRevision = plan.TargetSavedRevision };
        switch (damage)
        {
            case "karma": root.Element("karma")!.Value = "100"; break;
            case "expense-amount": entry.Element("amount")!.Value = "0"; break;
            case "expense-reason": entry.Element("reason")!.Value = "Something else"; break;
            case "expense-undo": entry.Element("undo")!.Element("objectid")!.Value = SecondId.ToString("D"); break;
            case "expense-missing": entry.Remove(); break;
            case "expense-duplicate": entry.AddAfterSelf(new XElement(entry)); break;
            case "expense-structured": entry.Element("amount")!.ReplaceNodes(new XElement("value", "-8")); break;
            case "expense-zoned": entry.Element("date")!.Value += "Z"; break;
            case "effects": root.Element("improvements")!.RemoveNodes(); break;
            case "quality": root.Element("qualities")!.RemoveNodes(); break;
            case "origin": root.Element("qualities")!.Element("quality")!.Element("qualitysource")!.Value = "Metatype"; break;
            case "scalar-whitespace": root.Element("qualities")!.Element("quality")!.Element("extra")!.Value = " "; break;
            case "effect-scalar-whitespace": root.Element("improvements")!.Element("improvement")!.Element("target")!.Value = " "; break;
            case "revision": binding = binding with { WorkspaceRevision = 99 }; break;
        }
        bool read = CharacterCareerQualityGraph.TryObserve(root.ToString(), source, plan.Identity,
            null, binding, quote.RuleDigest, plan.TransactionId, out var observed, out var expense);
        Assert.IsFalse(read && CharacterCareerQualityRules.TryCreateReceipt(plan.TransactionId, quote, plan,
            observed, expense, out _), damage);
    }

    [TestMethod]
    public void Career_quality_readback_rejects_orphaned_effect_after_removal()
    {
        var source = Source();
        string before = Apply(Empty, source, InstanceId);
        var graph = Project(before, source, InstanceId, CharacterCareerQualityOperation.RemoveAllLevels);
        var (quote, plan) = Plan(graph);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(before, graph, quote, plan, out var payload));
        var root = XElement.Parse(payload!);
        root.Element("improvements")!.Add(XElement.Parse(before).Element("improvements")!.Elements());
        Assert.IsFalse(CharacterCareerQualityGraph.TryObserve(root.ToString(), source, plan.Identity, null,
            quote.Binding with { WorkspaceRevision = 2, SavedRevision = 2 }, quote.RuleDigest,
            plan.TransactionId, out _, out _));
    }

    private static CharacterCareerQualitySource Source(bool negative = false, bool firstLevel = false, Guid? otherSource = null)
    {
        Guid id = otherSource ?? SourceId;
        string xml = $"<quality><id>{id:D}</id><name>Fixture quality</name><category>{(negative ? "Negative" : "Positive")}</category>"
            + $"<karma>{(negative ? -4 : 4)}</karma><limit>5</limit><bonus><ambidextrous/></bonus>"
            + (firstLevel ? "<firstlevelbonus><erased/></firstlevelbonus>" : "") + "<source>SR5</source><page>1</page></quality>";
        return new(id, "Fixture quality", "SR5", xml, "sha256:" + Digest, "sha256:" + Digest, "sha256:" + Digest);
    }

    private static CharacterCareerQualityGraph.Projection Project(string xml, CharacterCareerQualitySource source,
        Guid id, CharacterCareerQualityOperation operation = CharacterCareerQualityOperation.AcquireLevel)
    {
        Assert.IsTrue(CharacterCareerQualityGraph.TryProject(xml, source, new(id, source.SourceId), operation, null, out var graph));
        return graph!;
    }

    private static (CharacterCareerQualityQuote, CharacterCareerQualityPlan) Plan(CharacterCareerQualityGraph.Projection graph,
        Guid? transactionId = null)
    {
        var input = new CharacterCareerQualityInput(graph.Operation, graph.Identity, true, "sr5", true, true,
            graph.Operation == CharacterCareerQualityOperation.AcquireLevel, graph.Operation != CharacterCareerQualityOperation.AcquireLevel,
            true, false, false, 0, graph.KarmaBefore, graph.Extra, graph.SourceName, graph.Definition, new(1, false, false),
            new(true, true, true, true, true, true, [], [], Digest), graph.Effects, graph.InstancesBefore,
            new("synthetic-local-owner", "career-quality-fixture", 1, 1, Digest, Digest), graph.SourceState, "synthetic-rule-profile");
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(quote.CanApply, quote.Blocker.ToString());
        Assert.IsTrue(CharacterCareerQualityRules.TryPlan(quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
            Digest, Digest, 1, 1, true, false, transactionId ?? Guid.NewGuid(),
            new DateTime(2081, 5, 12, 14, 30, 0).AddTicks(1234), out var plan));
        return (quote, plan);
    }

    private static string Apply(string xml, CharacterCareerQualitySource source, Guid id,
        CharacterCareerQualityOperation operation = CharacterCareerQualityOperation.AcquireLevel)
    {
        var graph = Project(xml, source, id, operation);
        var (quote, plan) = Plan(graph);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApply(xml, graph, quote, plan, out var payload));
        return payload!;
    }
}
