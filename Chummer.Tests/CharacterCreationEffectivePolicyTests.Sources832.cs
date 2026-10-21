using System.Xml.Linq;
using System.Text.Json;
using Chummer.Infrastructure.Xml;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready832 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D832 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // ReadyContext copies the complete shipped catalogues, then builds a real
    // domain-created character. These never replace strict674's assertions.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sources832_Actual_catalogues_execute_movement_and_supporting_lifestyle_selections(bool purchases)
    {
        using var context = Ready832.Create(includeGearReview: true, includeNonEmptyPurchases: purchases,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var prepared = PrepareSources832(context);
        var observed = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var weapons = observed.Capture.Reports.Single(r => r.Scope == "weapons");
        Assert.IsTrue(weapons.IsComplete);
        Assert.IsTrue(weapons.Operations.Any(o => o.Path.EndsWith("/@gunneryspec", StringComparison.Ordinal)
            && o.Disposition == D832.NotSelected));
        var metatypes = observed.Capture.Reports.Single(r => r.Scope == "metatypes");
        foreach (string field in new[] { "walk", "run", "sprint" })
            Assert.IsTrue(metatypes.Operations.Any(o => HasField832(o, field) && o.Disposition == D832.Compiled));
        Assert.IsFalse(metatypes.IsComplete);
        Assert.IsTrue(metatypes.Operations.Any(o => HasField832(o, "inimin") && o.Disposition == D832.Unconsumed));
        var lifestyles = observed.Capture.Reports.Single(r => r.Scope == "lifestyles");
        foreach (string field in new[] { "minimum", "limit" })
            Assert.IsTrue(lifestyles.Operations.Any(o => HasField832(o, field) && o.Disposition == D832.Compiled));
        Assert.IsTrue(lifestyles.Operations.Any(o => HasField832(o, "quality") && o.Disposition == D832.NotSelected));
        Assert.IsTrue(lifestyles.IsComplete, string.Join(',', lifestyles.Operations
            .Where(operation => operation.Disposition == D832.Unconsumed).Select(operation => operation.Path)));
        PrintGaps832(observed.Capture.Reports);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Sources832_Changed_selected_movement_reaches_real_compiled_graph_and_cold_replay(string method)
    {
        using var context = Ready832.Create(includeGearReview: true, buildMethod: method, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "metatypes.xml");
            var source = XDocument.Load(path);
            source.Root!.Element("metatypes")!.Elements("metatype")
                .Single(row => row.Element("name")?.Value == "Human").SetElementValue("walk", "3/1/0");
            source.Save(path);
        });
        var prepared = PrepareSources832(context);
        var request = SourceRequest696(prepared.Inputs);
        var before = ObserveSources839(context, prepared, request);
        var execution = before.Capture.Invocations.Last(i => i.CompiledMovement832 is not null);
        Assert.AreEqual(3m, execution.CompiledMovement832!.Walk.Ground);
        var sourceReport = before.Capture.Reports.Single(r => r.Scope == "metatypes");
        Assert.IsTrue(sourceReport.Operations.Any(o => HasField832(o, "walk") && o.Disposition == D832.Compiled
            && o.ConsumedValue718 == "3/1/0"));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var after = ObserveSources839(context, prepared, request);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(before.Capture.Reports),
            CharacterCreationFinalizationDigest.Compute(after.Capture.Reports));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(before.Review.CompleteAssessment),
            CharacterCreationFinalizationDigest.Compute(after.Review.CompleteAssessment));
    }

    [TestMethod]
    public void Sources832_Unknown_selected_aspect_instruction_stays_unconsumed()
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "lifestyles.xml");
            var source = XDocument.Load(path);
            source.Root!.Element("comforts")!.Elements("comfort")
                .Single(row => row.Element("name")?.Value == "Street").Add(new XElement("unknown832", 1));
            source.Save(path);
        });
        var prepared = PrepareSources832(context);
        var observed = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        var report = observed.Capture.Reports.Single(r => r.Scope == "lifestyles");
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Operations.Any(o => HasField832(o, "unknown832") && o.Disposition == D832.Unconsumed));
    }

    [TestMethod]
    public void Sources832_Unknown_weapon_category_attribute_cannot_borrow_gunnery_inactivity()
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "weapons.xml");
            var source = XDocument.Load(path);
            source.Root!.Element("categories")!.Elements("category").First()
                .SetAttributeValue("unknown832", "unhandled");
            source.Save(path);
        });
        var prepared = PrepareSources832(context);
        var observed = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        var report = observed.Capture.Reports.Single(r => r.Scope == "weapons");
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Operations.Any(o => o.Path.EndsWith("/@unknown832", StringComparison.Ordinal)
            && o.Disposition == D832.Unconsumed));
    }

    [TestMethod]
    public void Sources832_Changed_selected_aspect_reaches_the_canonical_lifestyle_projection()
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "lifestyles.xml");
            var source = XDocument.Load(path);
            var comfort = source.Root!.Element("comforts")!.Elements("comfort")
                .Single(row => row.Element("name")?.Value == "Street");
            comfort.SetElementValue("minimum", 1);
            comfort.SetElementValue("limit", 2);
            source.Save(path);
        });
        var prepared = PrepareSources832(context);
        var observed = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        var report = observed.Capture.Reports.Single(r => r.Scope == "lifestyles");
        Assert.IsTrue(report.Operations.Any(o => HasField832(o, "comfort") && HasField832(o, "minimum")
            && o.Disposition == D832.Compiled && o.ConsumedValue718 == "1"));
        var authority = observed.Capture.Invocations.Last(i => i.LifestyleAuthority718 is not null).LifestyleAuthority718!;
        Assert.AreEqual(1, authority.LifestyleOptions.Single(o => o.Name == "Street").BaseComforts);
    }

    private static bool HasField832(CharacterCreationSourceOperation operation, string field)
        => operation.Path.Contains("/" + field + "[", StringComparison.Ordinal);

    private static void PrintGaps832(IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports)
    {
        // Diagnostics contain fixed structural labels/counts only. No values,
        // source XML, source IDs, character data, or raw paths are emitted.
        string[] labels = ["inimin", "inimax", "iniaug", "karma", "category", "source", "page", "bonus",
            "freegrids", "categories", "qualities", "cities", "comforts", "neighborhoods", "securities"];
        foreach (string scope in new[] { "metatypes", "weapons", "lifestyles" })
        {
            var report = reports.Single(r => r.Scope == scope);
            var groups = report.Operations.Where(o => o.Disposition == D832.Unconsumed)
                .GroupBy(o => labels.FirstOrDefault(label => HasField832(o, label)) ?? "other")
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in groups)
                Console.WriteLine($"Sources832 scope={scope}; structuralClass={group.Key}; unconsumed={group.Count()}");
        }
    }

    [TestMethod]
    public void Sources832_V3_full_and_selected_projection_cold_replay_preserves_movement_priority()
        => AssertSources832V3ColdReplay(CharacterCreationBuildMethods.Priority, false);

    [TestMethod]
    public void Sources832_V3_full_and_selected_projection_cold_replay_preserves_movement_sum_to_ten()
        => AssertSources832V3ColdReplay(CharacterCreationBuildMethods.SumToTen, false);

    [TestMethod]
    public void Sources832_V3_full_and_selected_projection_cold_replay_preserves_movement_priority_books()
        => AssertSources832V3ColdReplay(CharacterCreationBuildMethods.Priority, true);

    [TestMethod]
    public void Sources832_V3_full_and_selected_projection_cold_replay_preserves_movement_sum_to_ten_books()
        => AssertSources832V3ColdReplay(CharacterCreationBuildMethods.SumToTen, true);

    private void AssertSources832V3ColdReplay(string method, bool books)
    {
        using var context = Ready832.Create(includeGearReview: true, buildMethod: method, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "metatypes.xml");
            var document = XDocument.Load(path);
            var human = document.Root!.Element("metatypes")!.Elements("metatype")
                .Single(row => row.Element("name")?.Value == "Human");
            human.SetElementValue("walk", "3/1/0");
            human.SetElementValue("run", "7/2/0");
            human.SetElementValue("sprint", "4/1/0");
            document.Save(path);
        });
        var prepared = PrepareSources832(context, books);
        var inputs = prepared.Inputs;
        var request = SourceRequest832(inputs, CharacterCreationPolicyRequirement.None) with
        { SourcebookSelection = inputs.SourcebookSelection };
        var full = ReviewScopedSources839(prepared, request);
        Assert.AreEqual(0, full.Blockers.Count);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3, request.RuleSourceExecutionSchema);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string originalBytes = JsonSerializer.Serialize(original);
        var bootstrap = inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request);
        Assert.IsNotNull(cash.Value, string.Join(",", cash.Blockers));
        var choice = new CharacterCreationStartingCashChoice(cash.Value!.AuthorityDigest, cash.Value.Dice * 3);
        var projected = CharacterCreationCompletionFinalizationProjector.Project(original, bootstrap, inputs, full,
            cash.Value, choice, ruleSources: prepared.Execution);
        Assert.IsNotNull(projected.Value, string.Join(",", projected.Blockers));
        AssertMovement832(projected.Value!.CharacterXml);
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request,
            ruleSources: prepared.Execution);
        Assert.IsNotNull(capture.Value, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), inputs.RuleSourceCaptureDigest!, out var retained));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var witness));
        var coldSources = new CharacterCreationRuleSourceExecutionContext(retained!, context.Queries);
        var fullCold = CharacterCreationCompletionFinalizationProjector.Project(original, coldBootstrap!, inputs, full,
            cash.Value, choice, ruleSources: coldSources);
        var selectedCold = CharacterCreationCompletionTransaction.ProjectSelected(original, coldBootstrap!, witness!,
            request, cash.Value, choice, ruleSources: coldSources);
        Assert.IsNotNull(fullCold.Value, string.Join(",", fullCold.Blockers));
        Assert.IsNotNull(selectedCold.Value, string.Join(",", selectedCold.Blockers));
        Assert.AreEqual(projected.Value.CharacterXml, fullCold.Value!.CharacterXml);
        Assert.AreEqual(projected.Value.CharacterXml, selectedCold.Value!.CharacterXml);
        Assert.AreEqual(projected.Value.ProjectionDigest, selectedCold.Value.ProjectionDigest);
        AssertMovement832(selectedCold.Value.CharacterXml);
        Assert.AreEqual(originalBytes, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));

        // Existing schema/request/input identities prohibit relabeling an archive.
        var downgraded = request with { RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap!, witness!, downgraded,
            ruleSources: coldSources).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, downgraded, ruleSources: coldSources).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sources832_V1_V2_keep_original_movement_graph_and_V2_ledger(bool legacyV1)
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSources696(context, legacyV1);
        var request = SourceRequest832(prepared.Inputs, CharacterCreationPolicyRequirement.None);
        var full = ReviewScopedSources839(prepared, request);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, request);
        var effects = CharacterCreationSelectedEffectsConsumer.FromFull(prepared.Inputs, full, authority);
        Assert.IsNotNull(effects);
        var original = XElement.Parse(context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        foreach (string name in new[] { "movement", "walk", "run", "sprint" })
            Assert.AreEqual(string.Concat(original.Elements(name).Select(e => e.ToString(SaveOptions.DisableFormatting))),
                string.Concat(effects!.CopyCompiledGraph().Elements(name).Select(e => e.ToString(SaveOptions.DisableFormatting))));
        if (legacyV1) return;
        var complete = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        Assert.IsTrue(complete.Capture.Invocations.All(i => !i.SourceExecution832 && i.CompiledMovement832 is null));
        var metatypes = complete.Capture.Reports.Single(r => r.Scope == "metatypes");
        Assert.IsTrue(metatypes.Operations.Any(o => HasField832(o, "walk") && o.Disposition == D832.Unconsumed));
        var weapons = complete.Capture.Reports.Single(r => r.Scope == "weapons");
        Assert.IsTrue(weapons.Operations.Any(o => o.Path.EndsWith("/@gunneryspec", StringComparison.Ordinal)
            && o.Disposition == D832.Unconsumed));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = ObserveSources839(context, prepared, SourceRequest696(prepared.Inputs));
        Assert.AreEqual(JsonSerializer.Serialize(complete.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        Assert.AreEqual(JsonSerializer.Serialize(complete.Review), JsonSerializer.Serialize(cold.Review));
    }

    [TestMethod]
    public void Sources832_V3_retains_changed_source_and_unknown_schema_refusal()
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSources832(context, books: true);
        var request = SourceRequest832(prepared.Inputs, CharacterCreationPolicyRequirement.None) with
        { SourcebookSelection = prepared.Inputs.SourcebookSelection };
        Assert.IsFalse(CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request with
        { RuleSourceExecutionSchema = "chummer.creation-rule-source-execution.future" }));
        string path = Path.Combine(context.Directory, "source", "data", "metatypes.xml");
        var document = XDocument.Load(path);
        document.Root!.Element("metatypes")!.Elements("metatype")
            .Single(row => row.Element("name")?.Value == "Human").SetElementValue("run", "8/2/0");
        document.Save(path);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        Assert.IsNull(service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request).Value);
    }

    private static void AssertMovement832(string xml)
    {
        var root = XElement.Parse(xml);
        foreach (var pair in new[] { ("movement", ""), ("walk", "3/1/0"), ("run", "7/2/0"), ("sprint", "4/1/0") })
        {
            var fields = root.Elements(pair.Item1).ToArray();
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(pair.Item2, fields[0].Value);
        }
    }

    private static CharacterCreationRuleSourcePreparation PrepareSources832(Ready832 context, bool books = false)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId),
            includeLoadout: true, includeSourcebooks: books);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value!;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sources832_V3_sourcebook_success_does_not_discharge_unknown_selected_metatype_fields(bool books)
    {
        string unknownPath = string.Empty;
        string unknownDigest = string.Empty;
        using var context = Ready832.Create(includeGearReview: true, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "metatypes.xml");
            var document = XDocument.Load(path);
            // A direct unknown child is rejected by bootstrap (covered below). The
            // catalog admits this container attribute, but its source consumer has
            // no semantics for it. Keep that reachable accounting gap explicit.
            document.Root!.Element("metatypes")!.Elements("metatype")
                .Single(row => row.Element("name")?.Value == "Human").Element("metavariants")!
                .SetAttributeValue("unknown832", 1);
            document.Save(path);
            // Use the production reader's whitespace handling: occurrence paths
            // count all XML nodes, including whitespace between elements.
            using var sourceReader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var injected = XDocument.Load(sourceReader, LoadOptions.None).Root!.Element("metatypes")!.Elements("metatype")
                .Single(row => row.Element("name")?.Value == "Human").Element("metavariants")!.Attribute("unknown832")!;
            unknownPath = SourcePath839(injected.Parent!) + "/@" + injected.Name;
            unknownDigest = CharacterCreationFinalizationDigest.Compute(injected.ToString());
        });
        var prepared = PrepareSources832(context, books);
        var request = SourceRequest832(prepared.Inputs);
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, full.Review.Request.PolicyRequirement);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsFalse(full.Review.CompleteAssessment.SatisfiesComplete);
        if (books) AssertBooks800(prepared.Inputs, full.Review.CompleteAssessment);
        AssertSource696(prepared.Inputs, full.Review.CompleteAssessment, "metatypes", CharacterCreationCompleteCoverage.Unresolved);
        var metatypeReports = full.Capture.Reports.Where(row => row.Scope == "metatypes").ToArray();
        Assert.AreEqual(1, metatypeReports.Length, "Expected actual metatype source execution; observed scopes: "
            + string.Join(",", full.Capture.Reports.Select(row => row.Scope)));
        var operation = IdentifyOperation839(metatypeReports[0], unknownPath, unknownDigest);
        AssertUnknown839(full.Capture, operation);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var scoped = request with { PolicyRequirement = CharacterCreationPolicyRequirement.None };
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, scoped,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, witness.Outcome, string.Join(",", witness.Blockers));
        var selected = AssessSelectedSources839(context, prepared, request, original, bootstrap, witness.Value!);
        AssertUnknown839(selected.Capture, operation);
        Assert.AreEqual(full.Capture.Invocations.Single().GraphDigest, selected.Capture.Invocations.Single().GraphDigest);
        Assert.AreEqual(JsonSerializer.Serialize(full.Capture.Invocations.Single().CompiledMovement832),
            JsonSerializer.Serialize(selected.Capture.Invocations.Single().CompiledMovement832));
        if (books) AssertBooks800(prepared.Inputs, selected.Assessment);
        AssertSource696(prepared.Inputs, selected.Assessment, "metatypes", CharacterCreationCompleteCoverage.Unresolved);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        // Only the captured operation ID remains; another unresolved operation cannot satisfy these assertions.
        var cold = ObserveSources839(context, prepared, request);
        AssertUnknown839(cold.Capture, operation);
        var selectedCold = AssessSelectedSources839(context, prepared, request, original, bootstrap, witness.Value!);
        AssertUnknown839(selectedCold.Capture, operation);
        Assert.AreEqual(true, cold.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(full.Capture.Invocations.Single().GraphDigest, cold.Capture.Invocations.Single().GraphDigest);
        Assert.AreEqual(selected.Capture.Invocations.Single().GraphDigest, selectedCold.Capture.Invocations.Single().GraphDigest);
        if (books) AssertBooks800(prepared.Inputs, cold.Review.CompleteAssessment);
        if (books) AssertBooks800(prepared.Inputs, selectedCold.Assessment);
        Assert.AreEqual(JsonSerializer.Serialize(full.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        Assert.AreEqual(JsonSerializer.Serialize(selected.Capture.Reports), JsonSerializer.Serialize(selectedCold.Capture.Reports));
        Assert.AreEqual(JsonSerializer.Serialize(selected.Assessment), JsonSerializer.Serialize(selectedCold.Assessment));
    }

    [TestMethod]
    public void Sources876_Unknown_direct_metatype_child_is_rejected_by_real_bootstrap()
    {
        using var context = Ready832.Create(includeGearReview: true, prepareSource: _ => { });
        string sourceRoot = Path.Combine(context.Directory, "source");
        string path = Path.Combine(sourceRoot, "data", "metatypes.xml");
        var document = XDocument.Load(path);
        document.Root!.Element("metatypes")!.Elements("metatype")
            .Single(row => row.Element("name")?.Value == "Human").Add(new XElement("unknown832", 1));
        document.Save(path);
        // A fresh resolver proves unknown-semantics refusal, not cached source drift.
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null));
        var codec = new Sr5WorkspaceCodec(context.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new CharacterCreationBootstrapService(context.Store,
            new RulesetWorkspaceCodecResolver([codec]), context.Queries, resolver);
        var result = bootstrap.Create(new CharacterCreationBootstrapRequest(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
            "Sources876", "Unknown metatype child", CharacterCreationBuildMethods.Priority,
            CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationMetatypeCatalogBlockers.UnknownSemantics);
    }

    private static CharacterCreationCompletionReviewRequest SourceRequest832(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationPolicyRequirement requirement = CharacterCreationPolicyRequirement.Complete)
        => SourceRequest696(inputs) with { PolicyRequirement = requirement, SourcebookSelection = inputs.SourcebookSelection };
}
