using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using static Chummer.Application.Characters.CharacterCareerReputationProjector;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(true, true, 6, 1)]
    [DataRow(true, false, 6, 6)]
    [DataRow(false, true, 0, 0)]
    public void Reputation414_Preserved_custom_awareness_and_shipped_Erased_compute_on_the_real_graph(
        bool preserved, bool erased, int uncapped, int total)
    {
        using var context = ReputationContext414(preserved, erased);
        string saved = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var fixture = PrepareReputation414(context);
        var execution = fixture.Execution;
        var result = execution.Creation;
        Assert.AreEqual(uncapped, result.Reputation.Inputs.PublicAwarenessImprovement);
        Assert.AreEqual(uncapped, result.Reputation.CalculatedPublicAwareness);
        Assert.AreEqual(total, result.Reputation.TotalPublicAwareness);
        Assert.IsFalse(result.Reputation.Inputs.IsCareer);
        Assert.IsFalse(result.Reputation.CanBurnStreetCred);
        Assert.AreEqual(preserved ? 1 : 0, result.Improvements.Count(row => row.ImprovementType == "PublicAwareness"
            && row.Custom && row.Applicable && row.Selected && row.Value == 6m));
        Assert.AreEqual(erased ? 1 : 0, result.Improvements.Count(row => row.ImprovementType == "Erased"
            && !row.Custom && row.Applicable && row.Selected));
        if (erased)
        {
            Assert.IsTrue(fixture.Effects.CopyCompiledGraph().Element("qualities")!.Elements("quality")
                .Any(row => row.Element("sourceid")?.Value == "07f1833e-e5e0-41e1-91de-9044b2f48367"));
            Assert.IsFalse(fixture.OriginalSource.CopyDocument().Descendants("improvementttype")
                .Any(row => row.Value == "Erased"));
        }
        var native = ProjectNative414(fixture);
        Assert.IsTrue(execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!, native.OrderedDeltas,
            out var finish, out string error), error);
        Assert.IsTrue(finish!.Calculation.Reputation.Inputs.IsCareer);
        Assert.AreEqual(total, finish.Calculation.Reputation.TotalPublicAwareness);
        Assert.AreEqual("{}", JsonSerializer.Serialize(execution));
        Assert.AreEqual("{}", JsonSerializer.Serialize(finish));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<CharacterCreationReputationEffectsExecution>(
            JsonSerializer.Serialize(new { execution.BindingDigest, execution.Creation })));
        Assert.AreEqual(nameof(CharacterCreationReputationEffectsExecution), execution.ToString());
        AssertSame396(saved, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved,
            Operation332(fixture.Review.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.CharacterMechanics).Coverage);
    }

    [TestMethod]
    public void Reputation414_Fresh_cold_sources_recompute_equal_graphs_deltas_contributors_without_ambient_fallback()
    {
        using var context = ReputationContext414();
        var fixture = PrepareReputation414(context);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Request, ruleSources: new(fixture.Capture, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(fixture.Capture.ExportPrivateInputs(),
            fixture.Capture.IntegrityDigest, out var restored));
        Assert.AreNotSame(fixture.Capture, restored);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsNull(context.Resolver.TryCreateContext(fixture.Original.Document.Content));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var witness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, bootstrap, witness,
            fixture.Request, ruleSources: new(restored!, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome);
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromSelected(fixture.Original, bootstrap, witness,
            fixture.Request, out var original));
        var authority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, fixture.Request,
            CharacterCreationCompletionPreservationRules.SnapshotOriginal(fixture.Inputs), fixture.Inputs.Contacts.OriginalEffects);
        var effects = CharacterCreationSelectedEffectsConsumer.FromSelected(witness, fixture.Request, replay.Value!, authority,
            fixture.Inputs.Loadout?.PendingDraft?.Instances, originalCharacter: original);
        Assert.IsNotNull(effects);
        Assert.IsTrue(CharacterCreationReputationEffectsExecution.TryEvaluate(fixture.Original, bootstrap, fixture.Request,
            restored!, context.Queries, original!, authority, effects, out var cold, out string error), error);
        Assert.AreEqual(fixture.Execution.BindingDigest, cold!.BindingDigest);
        AssertSame396(JsonSerializer.Serialize(fixture.Execution.Creation), JsonSerializer.Serialize(cold.Creation));
        AssertSame396(fixture.Execution.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting),
            cold.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting));
        AssertSame396(JsonSerializer.Serialize(fixture.Execution.CopyDeltas()), JsonSerializer.Serialize(cold.CopyDeltas()));
        Assert.IsTrue(fixture.Execution.CopyDeltas().Count > 0);
        var detachedGraph = cold.CopyCompiledGraph();
        detachedGraph.Element("improvements")!.RemoveNodes();
        Assert.IsTrue(cold.CopyCompiledGraph().Element("improvements")!.HasElements);
        var detachedDeltas = (CharacterCreationFinalizationDelta[])cold.CopyDeltas();
        detachedDeltas[0] = detachedDeltas[0] with { AfterValue = "changed-copy" };
        AssertSame396(JsonSerializer.Serialize(fixture.Execution.CopyDeltas()), JsonSerializer.Serialize(cold.CopyDeltas()));
        byte[] tampered = fixture.Capture.ExportPrivateInputs();
        tampered[^1] ^= 1;
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered, fixture.Capture.IntegrityDigest, out _));
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture([], fixture.Capture.IntegrityDigest, out _));
    }

    [TestMethod]
    public void Reputation414_Unknown_retained_effect_is_unhandled_despite_identical_totals()
    {
        using var context = ReputationContext414(unknown: true);
        using var control = ReputationContext414();
        var fixture = PrepareReputation414(context);
        var ordinary = PrepareReputation414(control);
        Assert.AreEqual(ordinary.Execution.Creation.Reputation, fixture.Execution.Creation.Reputation);
        Assert.AreEqual(ordinary.Execution.Creation.UnhandledImprovementIndexes.Count + 1,
            fixture.Execution.Creation.UnhandledImprovementIndexes.Count);
        Assert.IsTrue(fixture.Execution.Creation.UnhandledImprovementIndexes.Contains(1));
        Assert.AreNotEqual(fixture.Execution.BindingDigest, ordinary.Execution.BindingDigest);
        var native = ProjectNative414(fixture);
        Assert.IsTrue(fixture.Execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!, native.OrderedDeltas,
            out var finish, out _));
        CollectionAssert.AreEqual(fixture.Execution.Creation.UnhandledImprovementIndexes.ToArray(),
            finish!.Calculation.UnhandledImprovementIndexes.ToArray());
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("duplicate-value")]
    [DataRow("duplicate-type")]
    [DataRow("duplicate-condition")]
    [DataRow("duplicate-award")]
    [DataRow("bad-enabled")]
    [DataRow("bad-custom")]
    [DataRow("wrong-case")]
    [DataRow("integer-overflow")]
    [DataRow("sum-overflow")]
    [DataRow("zero-divisor")]
    [DataRow("divisor-overflow")]
    public void Reputation414_Relevant_malformed_ambiguous_and_impossible_values_reject(string change)
    {
        using var context = ReputationContext414();
        var root = PrepareReputation414(context).Execution.CopyCompiledGraph();
        var rows = root.Element("improvements")!;
        var row = rows.Elements().First();
        switch (change)
        {
            case "malformed": row.SetElementValue("val", "private-invalid-value"); break;
            case "duplicate-value": row.Add(new XElement("val", 6)); break;
            case "duplicate-type": row.Add(new XElement("improvementttype", "PublicAwareness")); break;
            case "duplicate-condition": row.Add(new XElement("condition", "")); break;
            case "duplicate-award": root.Add(new XElement("publicawareness", 0)); break;
            case "bad-enabled": row.SetElementValue("enabled", "true"); break;
            case "bad-custom": row.SetElementValue("custom", "1"); break;
            case "wrong-case": row.SetElementValue("improvementttype", "publicawareness"); break;
            case "integer-overflow": row.SetElementValue("val", "2147483648"); break;
            case "sum-overflow":
                row.SetElementValue("val", decimal.MaxValue);
                rows.Add(Improvement414("PublicAwareness", "1", custom: true));
                break;
            case "zero-divisor": rows.Add(Improvement414("StreetCredMultiplier", "-10")); break;
            case "divisor-overflow": rows.Add(Improvement414("StreetCredMultiplier", "2147483647")); break;
            default: Assert.Fail(); break;
        }
        Assert.IsFalse(TryEvaluateGraph(root, new(false), CharacterReputationPhase.Creation, out var result, out string error));
        Assert.IsNull(result);
        Assert.IsFalse(error.Contains("private-invalid-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Reputation414_Exact_names_partitions_unique_and_precedence_ties_keep_canonical_contributors()
    {
        using var context = ReputationContext414();
        var root = PrepareReputation414(context).Execution.CopyCompiledGraph();
        root.Element("improvements")!.ReplaceNodes(
            Improvement414("PublicAwareness", "2"),
            Improvement414("PublicAwareness", "2", unique: "precedence0"),
            Improvement414("PublicAwareness", "100", unique: "other"),
            Improvement414("PublicAwareness", "3", custom: true, unique: "precedence0"),
            Improvement414("PublicAwareness", "3", custom: true, unique: "precedence0"),
            Improvement414("PublicAwareness", "0.1", name: "x"),
            Improvement414("PublicAwareness", "0.1", name: "x"),
            Improvement414("PublicAwareness", "0.1", name: " x"));
        Assert.IsTrue(TryEvaluateGraph(root, new(false), CharacterReputationPhase.Creation, out var result, out _));
        Assert.AreEqual(6, result!.Reputation.TotalPublicAwareness);
        CollectionAssert.AreEqual(new[] { 0, 3, 5, 6, 7 }, result.Improvements.Where(row => row.Selected).Select(row => row.ImprovementIndex).ToArray());
        Assert.AreEqual(" x", result.Improvements[7].ImprovedName);
        root.Element("improvements")!.Elements().ElementAt(1).SetElementValue("val", "2.1");
        Assert.IsTrue(TryEvaluateGraph(root, new(false), CharacterReputationPhase.Creation, out var higher, out _));
        Assert.IsFalse(higher!.Improvements[0].Selected);
        Assert.IsTrue(higher.Improvements[1].Selected);
        Assert.IsFalse(higher.Improvements[2].Selected);
    }

    [TestMethod]
    public void Reputation414_Actual_finish_graph_switches_create_career_conditions_without_defaulting_unknown_conditions()
    {
        using var context = ReputationContext414(extra: rows => rows.Add(
            Improvement414("PublicAwareness", "2", condition: "create"),
            Improvement414("PublicAwareness", "5", condition: "career"),
            Improvement414("PublicAwareness", "100", condition: "Career"),
            Improvement414("PublicAwareness", "100", condition: "create "),
            Improvement414("PublicAwareness", "100", enabled: 0),
            Improvement414("PublicAwareness", "100", addToRating: 1)), preserved: false, erased: false);
        var fixture = PrepareReputation414(context);
        Assert.AreEqual(2, fixture.Execution.Creation.Reputation.TotalPublicAwareness);
        var native = ProjectNative414(fixture);
        var finalGraph = XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!;
        Assert.IsTrue(fixture.Execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            finalGraph, native.OrderedDeltas, out var finish, out string error), error);
        Assert.AreEqual(5, finish!.Calculation.Reputation.TotalPublicAwareness);
        Assert.IsTrue(fixture.Execution.Creation.Improvements[0].Applicable);
        Assert.IsFalse(finish.Calculation.Improvements[0].Applicable);
        Assert.IsFalse(fixture.Execution.Creation.Improvements[1].Applicable);
        Assert.IsTrue(finish.Calculation.Improvements[1].Applicable);
        Assert.IsTrue(finish.Calculation.Improvements.Skip(2).All(row => !row.Applicable));
        Assert.IsFalse(TryEvaluateGraph(finalGraph, new(false), CharacterReputationPhase.Creation, out _, out _));
        Assert.IsFalse(TryEvaluateGraph(fixture.Execution.CopyCompiledGraph(), new(false), CharacterReputationPhase.Career, out _, out _));
    }

    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    public void Reputation414_Custom_Erased_keeps_legacy_contributor_presence(bool normalPartition, bool unique, bool erased)
    {
        using var context = ReputationContext414();
        var root = PrepareReputation414(context).Execution.CopyCompiledGraph();
        root.Element("improvements")!.ReplaceNodes(Improvement414("PublicAwareness", "6"),
            Improvement414("Erased", "0", custom: true, unique: unique ? "same" : ""));
        if (normalPartition) root.Element("improvements")!.Add(Improvement414("Erased", "0"));
        Assert.IsTrue(TryEvaluateGraph(root, new(false), CharacterReputationPhase.Creation, out var result, out _));
        Assert.AreEqual(erased, result!.Reputation.Inputs.Erased);
        Assert.AreEqual(erased ? 1 : 6, result.Reputation.TotalPublicAwareness);
    }

    [TestMethod]
    public void Reputation414_Expense_rounding_and_signed_aggregate_rounding_reuse_canonical_arithmetic()
    {
        using var context = ReputationContext414();
        var root = PrepareReputation414(context).Execution.CopyCompiledGraph();
        root.Element("improvements")!.ReplaceNodes(Improvement414("StreetCred", "0.1"),
            Improvement414("StreetCred", "0.1"), Improvement414("PublicAwareness", "-0.1"),
            Improvement414("PublicAwareness", "-0.1"));
        root.Element("expenses")!.ReplaceNodes(
            new XElement("expense", new XElement("type", "Karma"), new XElement("amount", "0.1")),
            new XElement("expense", new XElement("type", "Karma"), new XElement("amount", "9.1")),
            new XElement("expense", new XElement("type", "Karma"), new XElement("amount", "-0.1"),
                new XElement("forcecareervisible", true)));
        Assert.IsTrue(TryEvaluateGraph(root, new(false), CharacterReputationPhase.Creation, out var result, out _));
        Assert.AreEqual(10, result!.Reputation.Inputs.CareerKarma);
        Assert.AreEqual(1, result.Reputation.Inputs.StreetCredImprovement);
        Assert.AreEqual(-1, result.Reputation.Inputs.PublicAwarenessImprovement);
        CollectionAssert.AreEqual(new[] { 1, 10, -1 }, result.Expenses.Select(row => row.CareerKarmaContribution).ToArray());
    }

    [TestMethod]
    public void Reputation414_Actual_finish_parser_may_remove_indentation_but_cannot_rebind_exact_names()
    {
        using var context = ReputationContext414(extra: rows =>
        {
            var row = rows.Elements().First();
            row.SetElementValue("improvedname", " exact name ");
            row.AddFirst(new XText("\n    "));
            row.Add(new XText("\n"));
        });
        var fixture = PrepareReputation414(context);
        var native = ProjectNative414(fixture);
        var graph = XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!;
        Assert.IsTrue(fixture.Execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            graph, native.OrderedDeltas, out _, out string error), error);
        graph.Element("improvements")!.Elements().First().SetElementValue("improvedname", "exact name");
        Assert.IsFalse(fixture.Execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            graph, native.OrderedDeltas, out _, out _));
    }

    [TestMethod]
    public void Reputation414_Equal_total_observation_request_and_capture_substitutions_reject()
    {
        using var context = ReputationContext414();
        using var foreignContext = ReputationContext414();
        var fixture = PrepareReputation414(context);
        var foreign = PrepareReputation414(foreignContext);
        Assert.AreEqual(fixture.Execution.Creation.Reputation, foreign.Execution.Creation.Reputation);
        var original = fixture.Original;
        var changedDocument = original.Document with { State = original.Document.State with { Payload = original.Document.Content + "\n" } };
        var changedAuxiliary = original.Document with { State = original.Document.State with
            { AuxiliaryState = original.Document.AuxiliaryState with { CharacterCreationBootstrapBinding = null } } };
        foreach (var changed in new[]
        {
            original with { Document = changedDocument },
            original with { ContentRevision = original.ContentRevision + 1, SavedRevision = original.SavedRevision + 1 },
            original with { Document = changedAuxiliary },
            original with { Id = new CharacterWorkspaceId("foreign-reputation414") }, foreign.Original
        })
        {
            Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(changed, fixture.Bootstrap, fixture.Request,
                fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        }
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap with { SnapshotDigest = "sha256:" + new string('0', 64) }, fixture.Request,
            fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap, fixture.Request,
            foreign.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap, fixture.Request,
            fixture.Capture, context.Queries, foreign.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap,
            fixture.Request with { RuleSourceCaptureDigest = foreign.Capture.IntegrityDigest },
            fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap,
            fixture.Request with { Selections = fixture.Request.Selections with { QualityOptionIds = [] } },
            fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original, fixture.Bootstrap, fixture.Request,
            fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, foreign.Effects, out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(original,
            fixture.Bootstrap with { RawCharacterXmlDigest = "sha256:" + new string('0', 64) }, fixture.Request,
            fixture.Capture, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects, out _, out _));
        var native = ProjectNative414(fixture);
        var graph = XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!;
        Assert.IsFalse(fixture.Execution.TryEvaluateFinalGraph(original, fixture.Bootstrap, fixture.Request,
            graph, [], out _, out _));
        var changedDeltas = native.OrderedDeltas.Select(row => row.DeltaId == fixture.Execution.CopyDeltas()[0].DeltaId
            ? row with { AfterValue = "substituted" } : row).ToArray();
        Assert.IsFalse(fixture.Execution.TryEvaluateFinalGraph(original, fixture.Bootstrap, fixture.Request,
            graph, changedDeltas, out _, out _));
        var retained = graph.Element("improvements")!.Elements().First();
        retained.SetElementValue("val", 5);
        graph.SetElementValue("publicawareness", 1); // Equal total is not equal provenance.
        Assert.IsFalse(fixture.Execution.TryEvaluateFinalGraph(original, fixture.Bootstrap, fixture.Request,
            graph, native.OrderedDeltas, out _, out _));
    }

    [TestMethod]
    public void Reputation414_Missing_settings_capture_original_and_cancelled_execution_fail_closed()
    {
        using var context = ReputationContext414();
        var fixture = PrepareReputation414(context);
        foreach (var capture in new CharacterCreationRuleSourceCapture?[] { null, new MissingReputationSettings414(fixture.Capture) })
            Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(fixture.Original, fixture.Bootstrap,
                fixture.Request, capture!, context.Queries, fixture.OriginalSource, fixture.Authority, fixture.Effects,
                out _, out _));
        Assert.IsFalse(CharacterCreationReputationEffectsExecution.TryEvaluate(fixture.Original, fixture.Bootstrap,
            fixture.Request, fixture.Capture, context.Queries, null!, fixture.Authority, fixture.Effects, out _, out _));
        Assert.Throws<OperationCanceledException>(() => CharacterCreationReputationEffectsExecution.TryEvaluate(
            fixture.Original, fixture.Bootstrap, fixture.Request, fixture.Capture, context.Queries, fixture.OriginalSource,
            fixture.Authority, fixture.Effects, out _, out _, new CancellationToken(true)));
    }

    [TestMethod]
    public void Reputation414_Career_entrypoints_keep_their_creation_guard_and_calculation()
    {
        using var context = ReputationContext414();
        var fixture = PrepareReputation414(context);
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(fixture.Original, context.Resolver, out _, out string error));
        Assert.AreEqual("reputation_workspace_not_career", error);
        var creation = fixture.Execution.Creation.Reputation;
        Assert.IsFalse(CharacterCareerReputationRules.TryProject(creation.Inputs, out _));
        Assert.IsTrue(CharacterCareerReputationRules.TryProjectCreation(creation.Inputs, out var reproduced));
        Assert.AreEqual(creation, reproduced);
        var native = ProjectNative414(fixture);
        var career = fixture.Original with { Document = fixture.Original.Document with
            { State = fixture.Original.Document.State with { Payload = native.CharacterXml } } };
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(career, context.Resolver, out var read, out error), error);
        Assert.IsFalse(CharacterCareerReputationRules.TryProjectCreation(read!.Reputation.Inputs, out _));
        Assert.IsTrue(fixture.Execution.TryEvaluateFinalGraph(fixture.Original, fixture.Bootstrap, fixture.Request,
            XDocument.Parse(native.CharacterXml, LoadOptions.PreserveWhitespace).Root!, native.OrderedDeltas,
            out var finish, out error), error);
        Assert.AreEqual(read.Reputation, finish!.Calculation.Reputation);
        AssertSame396(JsonSerializer.Serialize(read.Improvements), JsonSerializer.Serialize(finish.Calculation.Improvements));
    }

    private static ReadyContext ReputationContext414(bool preserved = true, bool erased = true, bool unknown = false,
        Action<XElement>? extra = null)
        => ReadyContext.Create(false, includeNonEmptyPurchases: erased, qualityName: erased ? "Erased" : null, beforeDrafts: root =>
        {
            var rows = root.Element("improvements");
            if (rows is null) { rows = new XElement("improvements"); root.Add(rows); }
            if (preserved) rows.Add(Improvement414("PublicAwareness", "6", custom: true));
            if (unknown) rows.Add(Improvement414("FutureEffect414", "uninterpreted-private-input"));
            extra?.Invoke(rows);
        }, prepareSource: _ => { });

    private static XElement Improvement414(string type, string value, bool custom = false, string unique = "",
        string name = "", string condition = "", int enabled = 1, int addToRating = 0)
        => new("improvement", new XElement("improvementttype", type), new XElement("val", value),
            new XElement("enabled", enabled), new XElement("custom", custom), new XElement("unique", unique),
            new XElement("improvedname", name), new XElement("condition", condition), new XElement("addtorating", addToRating));

    private static ReputationCase414 PrepareReputation414(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        Assert.AreEqual(0, owners.ActiveLeases);
        var inputs = loaded.Value!.Inputs;
        var request = OriginalRequest396(inputs) with
        {
            RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest
        };
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        Assert.IsTrue(((ICharacterCreationRuleSourceCapture)context.Resolver).TryCaptureRuleSources(original.Document.Content, out var capture));
        Assert.AreEqual(inputs.RuleSourceCaptureDigest, capture!.IntegrityDigest);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request, ruleSources: loaded.Value.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var effects = CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review.Value!, authority);
        Assert.IsNotNull(effects);
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out var source));
        Assert.IsTrue(CharacterCreationReputationEffectsExecution.TryEvaluate(original, bootstrap, request, capture,
            context.Queries, source!, authority, effects, out var execution, out string error), error);
        return new(original, bootstrap, inputs, request, review.Value!, capture, source!, authority, effects, execution!);
    }

    private static CharacterCreationCompletionFinalizationProjection ProjectNative414(ReputationCase414 fixture)
    {
        var inputs = fixture.Inputs;
        var review = fixture.Review;
        var lifestyles = review.Lifestyles!;
        var gear = lifestyles.Gear;
        var resources = gear.Resources;
        var qualities = review.UpstreamBudget!.Qualities.Qualities;
        Assert.IsTrue(CharacterCreationSkillsService.TryCaptureCompletionGroupOrderAuthority(inputs.Skills, out var groupOrder));
        var graph = new CharacterCreationCompletionFinalizationProjector.EvaluatedGraphInputs(fixture.Original,
            inputs.SnapshotDigest, review.ReviewDigest, fixture.Bootstrap.SnapshotDigest, inputs.Skills.PrerequisiteDraft,
            review.Skills!, inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints,
            inputs.Skills.Authority.SourceAnchorIds,
            new(qualities.Selections, qualities.KarmaRemaining, qualities.SourceAnchorIds),
            new(resources.SelectedOption, resources.Budget), new(gear.Lines, gear.Budget, gear.SourceAnchorIds),
            new(lifestyles.Authority, lifestyles.Lifestyles, lifestyles.StartingLifestyleId, lifestyles.SourceAnchorIds),
            new(inputs.Contacts.Policy, review.Request.Selections.Contacts,
                inputs.Contacts.Qualities.Authority.CostPolicy ?? CharacterCreationQualityCostPolicy.Default,
                inputs.Contacts.Qualities.Authority.QualityKarmaLimit, inputs.Contacts.Qualities.Authority.MayExceedPositiveQualityLimit,
                review.Contacts!.GroupKarma), inputs.Contacts.CarryoverPolicy,
            review.Balances.RemainingCreationKarma!.Value, review.Balances.RemainingNuyen!.Value,
            new CharacterCreationCompletionFinalizationProjector.FullAwakenedGraphInputs(inputs.Magic, review.UpstreamBudget.Magic))
        { Loadout = review.Loadout, AmmunitionAssignments = fixture.Request.Selections.AmmunitionAssignments, SkillGroupOrderAuthority = groupOrder };
        var source = fixture.Capture.CreateReplayResolver().TryCreateContext(fixture.Original.Document.Content)!;
        CharacterCreationStartingNuyenSource? cash;
        if (fixture.Request.Selections.StartingLifestyleId is Guid selected)
        {
            var home = fixture.Request.Selections.Lifestyles.Single(row => row.LifestyleId == selected);
            var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
            Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(option.SourceId, out cash));
        }
        else Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out cash));
        Assert.IsNotNull(cash);
        var projected = CharacterCreationCompletionFinalizationProjector.ProjectEvaluated(graph, cash,
            new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, projected.Outcome);
        return projected.Value!;
    }

    private sealed record ReputationCase414(WorkspaceStoredDocument Original, CharacterCreationCompletionBootstrapSnapshot Bootstrap,
        CharacterCreationCompletionReviewInputs Inputs, CharacterCreationCompletionReviewRequest Request,
        CharacterCreationCompletionReview Review, CharacterCreationRuleSourceCapture Capture,
        CharacterCreationOriginalCharacterSource OriginalSource, CharacterCreationCompleteAssessment Authority,
        CharacterCreationSelectedEffectsConsumer Effects, CharacterCreationReputationEffectsExecution Execution);

    private sealed class MissingReputationSettings414(CharacterCreationRuleSourceCapture inner) : CharacterCreationRuleSourceCapture
    {
        public override string IntegrityDigest => inner.IntegrityDigest;
        public override string EffectivePolicyDigest => inner.EffectivePolicyDigest;
        public override byte[] ExportPrivateInputs() => inner.ExportPrivateInputs();
        public override bool MatchesOriginalCharacter(string xml) => inner.MatchesOriginalCharacter(xml);
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution() => inner.CreateExecutionResolution();
        public override ICharacterSourceDataResolver CreateReplayResolver() => new MissingReputationResolver414(inner.CreateReplayResolver());
    }

    private sealed class MissingReputationResolver414(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            var source = inner.TryCreateContext(xml);
            if (source is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, SupplementalPolicyProbe>();
            ((SupplementalPolicyProbe)(object)proxy).Call = (method, args) =>
            {
                if (method.Name == nameof(ICharacterSourceDataContext.TryResolveCareerReputationSettings))
                { args![0] = null; args[1] = string.Empty; return false; }
                return method.Invoke(source, args);
            };
            return proxy;
        }
    }
}
