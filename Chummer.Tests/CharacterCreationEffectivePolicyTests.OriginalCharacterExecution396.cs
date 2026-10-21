using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Complete396_Actual_owner_observation_survives_reader_and_review_detachment()
    {
        using var context = OriginalContext396();
        var observed = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(observed);
        var inputs = LoadOriginal396(context);
        var detached = JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(JsonSerializer.Serialize(inputs))!;
        AssertSame396(observed.Document.Content, detached.OriginalCharacter!.RawCharacterXml);
        Assert.AreEqual(observed.Id, detached.OriginalCharacter.WorkspaceId);
        Assert.AreEqual(observed.ContentRevision, detached.OriginalCharacter.ContentRevision);
        Assert.AreEqual(observed.SavedRevision, detached.OriginalCharacter.SavedRevision);
        AssertSame396(JsonSerializer.Serialize(observed.Document.AuxiliaryState),
            JsonSerializer.Serialize(detached.OriginalCharacter.AuxiliaryState));
        Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(detached));
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(detached, out var bound));
        Assert.AreEqual("{}", JsonSerializer.Serialize(bound));
        var request = OriginalRequest396(detached);
        var reviewed = CharacterCreationCompletionReviewService.Review(detached, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        AssertSame396(JsonSerializer.Serialize(inputs), JsonSerializer.Serialize(detached));
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete396_Original_graph_and_deltas_match_full_and_cold_without_overwriting_custom_nodes(bool maximumDefault)
    {
        using var context = OriginalContext396();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs) with
        {
            MaximumAttributesDefaultExecutionSchema = maximumDefault
                ? CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1 : null
        };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var full = CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review.Value!, authority);
        Assert.IsNotNull(full);
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromSelected(original, coldBootstrap, coldWitness,
            request, out var coldOriginal));
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(coldWitness, request,
            CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs), inputs.Contacts.OriginalEffects);
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromSelected(coldWitness, request, cold,
            selectedAuthority, inputs.Loadout?.PendingDraft?.Instances), "A copied assessment cannot supply an original graph.");
        var selected = CharacterCreationSelectedEffectsConsumer.FromSelected(coldWitness, request, cold,
            selectedAuthority, inputs.Loadout?.PendingDraft?.Instances, originalCharacter: coldOriginal);
        Assert.IsNotNull(selected);
        Assert.AreEqual(full.DigestFor(authority), selected.DigestFor(selectedAuthority));
        AssertSame396(full.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting),
            selected.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting));
        AssertSame396(JsonSerializer.Serialize(full.CopyDeltas()), JsonSerializer.Serialize(selected.CopyDeltas()));
        Assert.IsTrue(full.CopyDeltas().Count > 0);
        var originalGraph = XDocument.Parse(original.Document.Content).Root!;
        foreach (string name in new[] { "notes", "customnote", CharacterCreationBootstrapXml.MarkerElement })
            Assert.IsTrue(XNode.DeepEquals(originalGraph.Element(name), full.CopyCompiledGraph().Element(name)), name);
        Assert.IsTrue(full.CopyCompiledGraph().Element("newskills")!.Descendants("skill").Any());
        AssertSame396(JsonSerializer.Serialize(review.Value!.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated,
            Operation332(cold.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects).Coverage);
        Assert.IsFalse(cold.CompleteAssessment!.SatisfiesComplete);
        Assert.IsTrue(cold.CompleteAssessment.Obligations.Where(row => row.Kind is CharacterCreationCompleteObligationKind.Source
                or CharacterCreationCompleteObligationKind.Sourcebook
            || row.Kind == CharacterCreationCompleteObligationKind.ActiveGraph && row.Operation is
                CharacterCreationConsumerPolicyOperation.Amendments or CharacterCreationConsumerPolicyOperation.Errata
                or CharacterCreationConsumerPolicyOperation.Campaign or CharacterCreationConsumerPolicyOperation.CharacterMechanics)
            .All(row => row.Coverage == CharacterCreationCompleteCoverage.Unresolved));
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.IsFalse(JsonSerializer.Serialize(witness).Contains("RawCharacterXml\":", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(witness).Contains("customnote", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("xml")]
    [DataRow("revision")]
    [DataRow("auxiliary")]
    [DataRow("workspace")]
    [DataRow("selection")]
    [DataRow("bootstrap")]
    [DataRow("doctype")]
    public void Complete396_Resealing_outer_digest_cannot_rebind_conflicting_original_inputs(string change)
    {
        using var context = OriginalContext396();
        var inputs = LoadOriginal396(context);
        var original = inputs.OriginalCharacter!;
        var changed = change switch
        {
            "xml" => original with { RawCharacterXml = original.RawCharacterXml.Replace("Keep my notes", "Changed notes", StringComparison.Ordinal) },
            "revision" => original with { ContentRevision = original.ContentRevision + 1, SavedRevision = original.SavedRevision + 1 },
            "auxiliary" => original with { AuxiliaryState = original.AuxiliaryState with { CharacterCreationBootstrapBinding = null } },
            "workspace" => original with { WorkspaceId = new CharacterWorkspaceId("other-original") },
            "selection" => original with { AuxiliaryState = original.AuxiliaryState with { CharacterCreationSkillsDraft = null } },
            "bootstrap" => original with { BootstrapSourceSnapshot = JsonSerializer.SerializeToElement(new { Schema = "unknown" }) },
            "doctype" => original with { RawCharacterXml = "<!DOCTYPE character [<!ENTITY value 'expanded'>]>" + original.RawCharacterXml },
            _ => throw new InvalidOperationException()
        };
        var forged = inputs with { OriginalCharacter = changed };
        forged = forged with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(forged) };
        var request = OriginalRequest396(forged);
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(forged, out var execution));
        Assert.IsNull(execution);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(forged, request).Value);
    }

    [TestMethod]
    public void Complete396_Original_execution_does_not_relax_saved_selection_preservation()
    {
        using var context = OriginalContext396(includeGearReview: true);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs);
        Assert.IsTrue(request.Selections.Gear.Count > 0);
        var removed = request with { Selections = request.Selections with { Gear = [] } };
        var review = CharacterCreationCompletionReviewService.Review(inputs, removed);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, review.Outcome);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var forged = captured.Value! with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(removed) };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, removed).Value);
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Complete396_Coherent_foreign_snapshot_cannot_replace_the_actual_capture_or_live_observation()
    {
        using var context = OriginalContext396();
        using var other = ReadyContext.Create(false, includeSkillPurchase: true, beforeDrafts: root =>
            root.Add(new XElement("notes", "A different coherent character")));
        var actual = context.Store.Get(context.WorkspaceId).Value!;
        var foreign = LoadOriginal396(other);
        var request = OriginalRequest396(foreign);
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(foreign, out _));
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(actual.Document.Content)!, actual.Document.Content, out var bootstrap));
        var refused = CharacterCreationSelectedCompletionEvaluator.Capture(actual, bootstrap, foreign, request);
        Assert.IsNull(refused.Value);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationSelectedCompletionEvaluator.HistoryMismatch);
        var foreignCold = Cold342(other, foreign, request, out var witness);
        Assert.IsNotNull(foreignCold);
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(actual, bootstrap, witness, request).Value);
        var recaptured = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(
            new WorkspaceContinuationReadView(actual), context.Resolver, context.Queries,
            new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true }, request, default);
        Assert.IsNull(recaptured.Value);
    }

    [TestMethod]
    public void Complete396_Archive_reopen_reuses_retained_original_and_rejects_rebound_history()
    {
        using var context = OriginalContext396();
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.IsNotNull(cash.Value, string.Join(",", cash.Blockers));
        var reviewed = service.Review(stamp, context.WorkspaceId, request, new(cash.Value.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var applied = service.Apply(stamp, Command(reviewed.Value!));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        using var reopened = context.Restart();
        var current = reopened.Store.Get(context.WorkspaceId).Value!;
        var archive = current.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        var completion = archive.CompletionAuthority!;
        Assert.IsNull(completion.Inputs);
        AssertSame396(inputs.OriginalCharacter!.RawCharacterXml, completion.RawCharacterXml);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(completion.SelectedReplay,
            out var bootstrap, out var witness));
        var document = new WorkspaceDocument(completion.RawCharacterXml, "sr5");
        document = document with { State = document.State with { AuxiliaryState = archive.State } };
        var receipt = current.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt;
        var original = new WorkspaceStoredDocument(current.Id, document, receipt.PreviousContentRevision,
            receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        var changed = document with { State = document.State with { Payload = document.Content + "\n<!-- altered -->" } };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original with { Document = changed },
            bootstrap, witness, request).Value);
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original with { ContentRevision = original.ContentRevision + 1 },
            bootstrap, witness, request).Value);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidCurrentDocument(current.Document, current.ContentRevision));
    }

    [TestMethod]
    public void Complete396_Live_boundaries_reacquire_owner_and_current_sources()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: _ => { });
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.IsNotNull(cash.Value, string.Join(",", cash.Blockers));
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var command = Command(review.Value!);
        owners.Set(new OwnerScope("original-396-other-owner"));
        Assert.IsNull(service.Apply(stamp, command).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.IsNull(service.Apply(stamp, command).Value);
        string path = Path.Combine(context.Directory, "source", "data",
            CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
        var settings = XDocument.Load(path);
        settings.Root!.Element("settings")!.Elements("setting")
            .Single(row => row.Element("id")!.Value == CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
            .Element("karmacost")!.SetElementValue("karmaimproveactiveskill", 3);
        settings.Save(path);
        Assert.IsNull(service.Apply(owners.Capture(), command).Value);
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Complete396_Explicit_shape_admission_and_cancellation_do_not_default_old_readers()
    {
        IOwnerBoundCharacterCreationCompletionReader historical = new HistoricalOnlyCompletionReader();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        Assert.IsNull(historical.LoadCompletionInputsWithOriginalCharacter(owners.Capture(),
            new(new CharacterWorkspaceId("not-loaded"))).Value);
        using var context = OriginalContext396();
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs);
        foreach (string? variant in new string?[] { null, "", "unknown-original-version" })
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
                request with { OriginalCharacterExecutionSchema = variant }).Value);
        var missing = inputs with { OriginalCharacter = null };
        missing = missing with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(missing) };
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(missing,
            request with { InputsSnapshotDigest = missing.SnapshotDigest }).Value);
        foreach (string schema in new[] { CharacterCreationCompleteAssessment.SchemaV1,
                     CharacterCreationCompleteAssessment.SchemaV2, CharacterCreationCompleteAssessment.SchemaV3 })
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
                request with { CompleteAssessmentSchema = schema }).Value);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request).Value!;
        Assert.IsNotNull(witness);
        var stripped = witness with { OriginalCharacterExecutionSchema = null };
        stripped = stripped with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(stripped) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, stripped, request).Value);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        Assert.ThrowsExactly<OperationCanceledException>(() => reader.LoadCompletionInputsWithOriginalCharacter(
            owners.Capture(), new(context.WorkspaceId), cancellationToken: cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => CharacterCreationCompletionReviewService.Review(inputs, request, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => CharacterCreationSelectedCompletionEvaluator.Replay(
            original, bootstrap, witness, request, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _, cancellation.Token));
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryParse("<!DOCTYPE character SYSTEM 'file:///unread'><character/>", out _));
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryParse(
            "<character>" + new string(' ', CharacterCreationCompletionReplayCodec.MaximumUncompressedBytes) + "</character>", out _));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Complete396_Historical_readers_and_selected_archives_keep_omission_and_roundtrip_bytes()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var load = new CharacterCreationSkillsLoadRequest(context.WorkspaceId);
        var historical = new[]
        {
            reader.LoadCompletionInputs(owners.Capture(), load),
            reader.LoadCompletionInputsWithLoadout(owners.Capture(), load),
            reader.LoadCompletionInputsWithEffectivePolicy(owners.Capture(), load)
        };
        CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.InputsV1,
            CharacterCreationCompletionReviewSchemas.InputsV2, CharacterCreationCompletionReviewSchemas.InputsV3 },
            historical.Select(row => row.Value!.Schema).ToArray());
        foreach (var loaded in historical)
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
            Assert.IsNull(loaded.Value!.OriginalCharacter);
            string json = JsonSerializer.Serialize(loaded.Value);
            Assert.IsFalse(json.Contains("OriginalCharacter", StringComparison.Ordinal));
            AssertSame396(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(json)));
        }
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        Assert.IsNull(request.MaximumAttributesDefaultExecutionSchema);
        Assert.IsNull(request.OriginalCharacterExecutionSchema);
        string requestBytes = JsonSerializer.Serialize(request);
        Assert.IsFalse(requestBytes.Contains("OriginalCharacterExecutionSchema", StringComparison.Ordinal));
        Assert.IsFalse(requestBytes.Contains("MaximumAttributesDefaultExecutionSchema", StringComparison.Ordinal));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsNull(captured.Value!.OriginalCharacterExecutionSchema);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var frozen, out var witness));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(frozen, witness, out var reencoded));
        AssertSame396(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(reencoded));
        AssertSame396(JsonSerializer.Serialize(captured.Value), JsonSerializer.Serialize(witness));
    }

    // Failure output contains only digests, never original XML or source catalogs.
    private static void AssertSame396<T>(T expected, T actual)
        => Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(expected), CharacterCreationFinalizationDigest.Compute(actual));

    private static ReadyContext OriginalContext396(bool includeGearReview = false)
        => ReadyContext.Create(includeGearReview, includeNonEmptyPurchases: true, includeSkillPurchase: true, beforeDrafts: root =>
        {
            root.Add(new XElement("notes", new XCData("Keep my notes <unchanged>")));
            root.Add(new XElement("customnote", new XAttribute("source", "manual"), new XElement("text", "Keep custom node")));
        });

    private static CharacterCreationCompletionReviewInputs LoadOriginal396(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithOriginalCharacter(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value!.OriginalCharacter);
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value;
    }

    private static CharacterCreationCompletionReviewRequest OriginalRequest396(CharacterCreationCompletionReviewInputs inputs)
        => GrantRequest368(inputs) with
        {
            OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
        };
}
