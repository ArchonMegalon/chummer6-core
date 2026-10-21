using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionLoadoutReplayTests
{
    [TestMethod]
    public void Legacy_selected_witness_omits_new_fields_and_keeps_exact_cold_encoding()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        Assert.IsNull(inputs.Loadout);
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest,
            CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs));
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV1, witness.Schema);
        Assert.IsNull(witness.Loadout);
        Assert.IsFalse(JsonSerializer.SerializeToElement(inputs).TryGetProperty("Loadout", out _));
        Assert.IsFalse(JsonSerializer.SerializeToElement(request.Selections).TryGetProperty("Loadout", out _));
        Assert.IsFalse(JsonSerializer.SerializeToElement(witness).TryGetProperty("Loadout", out _));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.IsFalse(JsonSerializer.SerializeToElement(replay.Value).TryGetProperty("Loadout", out _));
        Assert.IsFalse(JsonSerializer.SerializeToElement(replay.Value!.Balances).TryGetProperty("LoadoutCost", out _));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var decodedBootstrap, out var decodedWitness));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(decodedBootstrap, decodedWitness, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Typed_loadout_full_review_selected_replay_and_cold_archive_preserve_real_graph_and_one_budget(bool orderedHistory)
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext(
            amendSettings: orderedHistory ? settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True") : null);
        var fixture = Capture(context, includeKarmaPurchasePolicy: orderedHistory);
        Assert.AreEqual(orderedHistory, fixture.Inputs.Skills.KarmaPurchasePolicy?.CompensateSkillGroupKarmaDifference == true);
        Assert.AreEqual(orderedHistory, fixture.Inputs.Skills.KarmaCompensationSourceOrder is not null);
        var before = JsonSerializer.Serialize(fixture.Original);
        var reviewed = CharacterCreationCompletionReviewService.Review(fixture.Inputs, fixture.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value!;
        Assert.IsNotNull(review.Loadout);
        decimal loadoutCost = fixture.Weapon.UnitCost + fixture.Armor.UnitCost;
        Assert.AreEqual(loadoutCost, review.Loadout.PurchaseCost);
        Assert.AreEqual((decimal?)loadoutCost, review.Balances.LoadoutCost);
        Assert.AreEqual(review.Balances.GearCost + review.Balances.LifestyleCost + loadoutCost,
            (decimal?)review.Lifestyles!.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(review.Balances.CreationNuyen - review.Balances.GearCost
            - review.Balances.LifestyleCost - loadoutCost, review.Balances.RemainingNuyen);

        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV2, witness.Schema);
        Assert.IsNotNull(witness.Loadout);
        Assert.AreEqual(2, witness.Loadout.Options.Count, "Retain selected source closure, not the entire catalog.");
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, witness, out var encoded));
        Assert.AreEqual(CharacterCreationCompletionReplayCodec.LoadoutEncodingName, encoded.Encoding);
        Assert.IsFalse(encoded.Data.Any(character => character is '+' or '/' or '='));
        string legacyData = encoded.Data.Replace('-', '+').Replace('_', '/')
            .PadRight((encoded.Data.Length + 3) / 4 * 4, '=');
        var legacyEncoded = encoded with { Encoding = CharacterCreationCompletionReplayCodec.EncodingName, Data = legacyData };
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(legacyEncoded, out _, out var legacyWitness),
            "Previously encoded base64 V2 witnesses remain readable without rewriting their archive.");
        Assert.AreEqual(JsonSerializer.Serialize(witness), JsonSerializer.Serialize(legacyWitness));
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(encoded).Length < JsonSerializer.SerializeToUtf8Bytes(legacyEncoded).Length,
            "Only escaping overhead is removed; no source or history data may be omitted.");
        foreach (string malformed in new[] { encoded.Data + "=", encoded.Data + " ", "+" + encoded.Data[1..],
            "/" + encoded.Data[1..], encoded.Data[..1] })
            Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryDecode(encoded with { Data = malformed }, out _, out _));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(encoded, out var bootstrap, out var decoded));
        Assert.AreEqual(JsonSerializer.Serialize(witness), JsonSerializer.Serialize(decoded));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, bootstrap!, decoded!, fixture.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Loadout), JsonSerializer.Serialize(replay.Value!.Loadout));
        Assert.AreEqual(JsonSerializer.Serialize(review.Balances), JsonSerializer.Serialize(replay.Value.Balances));

        Assert.IsTrue(CharacterCreationCompletionTransaction.TryAdmit(context.Store, OwnerScope.LocalSingleUser,
            fixture.Original, context.Resolver, context.Queries, fixture.Request, fixture.CashChoice, default,
            out var authority, out var projection, out var blockers), string.Join(",", blockers));
        Assert.IsNotNull(authority);
        Assert.IsNotNull(projection);
        Assert.AreEqual(orderedHistory ? CharacterCreationCompletionAuthority.SchemaV4
            : CharacterCreationCompletionAuthority.SchemaV3, authority.Schema);
        Assert.AreEqual(orderedHistory ? CharacterCreationCompletionFinalizationSchemas.ProjectionV3
            : CharacterCreationCompletionFinalizationSchemas.ProjectionV2, projection.Schema);
        if (orderedHistory)
        {
            Assert.IsNotNull(projection.SkillGroupOrderAuthority);
            var order = projection.SkillGroupOrderAuthority;
            Assert.AreEqual(CharacterCreationSkillGroupOrderAuthority.SchemaV1, order.Schema);
            Assert.AreEqual(fixture.Inputs.Skills.KarmaPurchasePolicy!.AuthorityDigest, order.PurchasePolicyDigest);
            Assert.IsTrue(order.ActiveSkillSourceOrder.Count > 0);
            CollectionAssert.AreEqual(fixture.Inputs.Skills.KarmaCompensationSourceOrder!.ToArray(),
                order.ActiveSkillSourceOrder.ToArray());
        }
        else Assert.IsNull(projection.SkillGroupOrderAuthority);
        var root = XDocument.Parse(projection.CharacterXml).Root!;
        var weapon = root.Element("weapons")!.Elements("weapon").Single();
        var armor = root.Element("armors")!.Elements("armor").Single();
        Assert.AreEqual(fixture.Weapon.SourceId.ToString("D"), weapon.Element("sourceid")!.Value);
        Assert.AreEqual(fixture.Armor.SourceId.ToString("D"), armor.Element("sourceid")!.Value);
        Assert.AreNotEqual(weapon.Element("guid")!.Value, armor.Element("guid")!.Value);
        Assert.IsTrue(root.Element("gears")!.HasElements, "Retained generic Gear must survive distinct loadout creation.");
        Assert.AreEqual(1, projection.OrderedDeltas.Count(row => row.Kind == CharacterCreationLoadoutKinds.Weapon));
        Assert.AreEqual(1, projection.OrderedDeltas.Count(row => row.Kind == CharacterCreationLoadoutKinds.Armor));

        var request = new CharacterCreationCompletionApplyRequest(projection.Binding, fixture.Request,
            projection.ReviewDigest, fixture.CashChoice, projection.ProjectionDigest, "typed-loadout-cold-replay", true);
        Assert.IsTrue(CharacterCreationCompletionTransaction.TryBuild(context.Store, OwnerScope.LocalSingleUser,
            fixture.Original, context.Resolver, context.Queries, request, default, out var replacement, out var receipt));
        Assert.IsNotNull(replacement);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(fixture.Original.ContentRevision + 1, receipt.ContentRevision);
        Assert.AreEqual(projection.CharacterXml, replacement.Content);
        var cold = JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(
            JsonSerializer.Serialize(replacement.AuxiliaryState))!;
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(fixture.Original.Id,
            cold.CharacterCreationFinalizationArchive!, receipt));
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(cold).Length <= 524288,
            "The auxiliary state alone must not exhaust the unchanged continuation ceiling.");
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value),
            "Review/admission/replacement construction must not create intermediate saved drafts.");

        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var writes = new AtomicReplacementProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var publicReview = service.Review(stamp, context.WorkspaceId, fixture.Request, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, publicReview.Outcome,
            string.Join(",", publicReview.Blockers));
        Assert.AreEqual(projection.CharacterXml, publicReview.Value!.Projection.CharacterXml);
        Assert.AreEqual(0, writes.Replacements);
        var command = new CharacterCreationCompletionApplyRequest(publicReview.Value.Projection.Binding,
            publicReview.Value.Proposal, publicReview.Value.Projection.ReviewDigest, fixture.CashChoice,
            publicReview.Value.Projection.ProjectionDigest, "typed-loadout-public-apply", true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.IsNotNull(applied.Value);
        Assert.AreEqual(1, writes.Replacements, "The complete graph must be committed with one atomic replacement.");
        var committed = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(fixture.Original.ContentRevision + 1, committed.ContentRevision);
        Assert.AreEqual(committed.ContentRevision, committed.SavedRevision);
        Assert.AreEqual(committed.ContentRevision, applied.Value.ContentRevision);
        Assert.AreEqual(projection.CharacterXml, committed.Document.Content);
        Assert.AreEqual(1, committed.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Count);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            fixture.Original.Document.AuxiliaryState,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State),
            "Keep the player's original drafts and purchase history in the finalization archive.");
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive, applied.Value));
        Assert.IsNotNull(writes.TargetPath);
        byte[] committedBytes = File.ReadAllBytes(writes.TargetPath!);

        var coldStore = new FileWorkspaceStore(context.Directory, writes);
        var reopened = coldStore.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        Assert.AreEqual(committed.Document.Content, reopened.Value!.Document.Content);
        Assert.AreEqual(committed.Document.AuxiliaryStateDigest, reopened.Value.Document.AuxiliaryStateDigest);
        var repeatedApply = new OwnerBoundCharacterCreationCompletionService(coldStore, owners,
            context.Resolver, context.Queries).Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeatedApply.Outcome,
            string.Join(",", repeatedApply.Blockers));
        Assert.AreEqual(applied.Value.ReceiptDigest, repeatedApply.Value!.ReceiptDigest);
        Assert.AreEqual(1, writes.Replacements, "Cold replay must not buy the loadout or finalize a second time.");
        CollectionAssert.AreEqual(committedBytes, File.ReadAllBytes(writes.TargetPath!));
        Assert.AreEqual(0, owners.ActiveLeases);

        var completedArchive = committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        string[] nativeParentIds = NativeSkillParentIds(committed.Document.Content);
        Assert.IsTrue(nativeParentIds.Length > 0);
        Assert.AreEqual(nativeParentIds.Length, nativeParentIds.Distinct(StringComparer.Ordinal).Count());
        foreach (string parentId in nativeParentIds)
            Assert.IsTrue(Guid.TryParse(parentId, out var parsed) && parsed != Guid.Empty);
        if (orderedHistory)
        {
            // Exercise the later-revision historical path through a real Career transaction.
            var career = new WorkspaceCharacterCareerReputationService(coldStore, context.Resolver);
            var careerPreview = career.Preview(new(context.WorkspaceId, Guid.NewGuid(),
                CharacterCareerReputationOperation.AdjustManualAwards, new(1, null, null), "Ordered completion handoff"));
            Assert.AreEqual(CharacterCareerReputationOutcome.Available, careerPreview.Outcome, careerPreview.Error);
            var careerCommit = career.Commit(careerPreview.Preview!.Command with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCareerReputationOutcome.Applied, careerCommit.Outcome, careerCommit.Error);
            var advanced = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId);
            Assert.IsTrue(advanced.Success, advanced.Error);
            Assert.AreEqual(committed.ContentRevision + 1, advanced.Value!.ContentRevision);
            Assert.AreEqual(advanced.Value.ContentRevision, advanced.Value.SavedRevision);
            Assert.AreEqual(careerCommit.Receipt!.ReceiptDigest,
                advanced.Value.Document.AuxiliaryState.CharacterCareerReputationReceipts!.Single().ReceiptDigest);
            Assert.IsTrue(CharacterCareerReputationTransaction.IsValidHistory(advanced.Value));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(completedArchive,
                advanced.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive));
            CollectionAssert.AreEqual(nativeParentIds, NativeSkillParentIds(advanced.Value.Document.Content));
            var careerRoot = XDocument.Parse(advanced.Value.Document.Content).Root!;
            Assert.AreEqual(root.Element("karma")!.Value, careerRoot.Element("karma")!.Value);
            Assert.AreEqual(root.Element("nuyen")!.Value, careerRoot.Element("nuyen")!.Value);
            Assert.AreEqual(stamp, owners.Capture());
            committed = advanced.Value;
        }

        // Claim this exact completed, history-rich local runner using the actual
        // account handoff, then transfer it to a fresh installation of that account.
        // Do not replace it with a smaller linked prerequisite or trim its archive.
        var owner = new OwnerScope("completion-loadout-linked-owner");
        owners.Set(owner);
        var linkedStamp = owners.Capture();
        Assert.AreNotEqual(stamp, linkedStamp);
        var adoption = new WorkspaceLocalAdoptionService(coldStore, owners);
        var adoptionReview = adoption.Review(linkedStamp, context.WorkspaceId);
        Assert.IsNotNull(adoptionReview);
        var adopted = adoption.Confirm(linkedStamp, adoptionReview, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceLocalAdoptionOutcome.Applied, adopted.Outcome);
        var linked = coldStore.Get(owner, context.WorkspaceId);
        Assert.IsTrue(linked.Success, linked.Error);
        Assert.AreEqual(committed.Document.Content, linked.Value!.Document.Content);
        Assert.AreEqual(committed.Document.AuxiliaryStateDigest, linked.Value.Document.AuxiliaryStateDigest);
        Assert.AreEqual(committed.ContentRevision, linked.Value.ContentRevision);
        Assert.AreEqual(committed.SavedRevision, linked.Value.SavedRevision);
        Assert.IsFalse(coldStore.Get(context.WorkspaceId).Success);
        string linkedBeforeExport = JsonSerializer.Serialize(linked.Value);

        var exported = new WorkspaceContinuationExportService(coldStore, owners)
            .Export(linkedStamp, context.WorkspaceId);
        Assert.IsTrue(exported.Success, exported.Error);
        Assert.IsNotNull(exported.Value);
        Assert.AreEqual(owner.NormalizedValue, exported.Value.Snapshot.OwnerId);
        const int transportMaximumBytes = 524288;
        Console.WriteLine($"Typed loadout ownership transport: authority={committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!.Schema}, "
            + $"projection={projection.Schema}, completionRevision={applied.Value.ContentRevision}, exportedRevision={committed.ContentRevision}.");
        Console.WriteLine($"Typed loadout continuation components: auxiliary={JsonSerializer.SerializeToUtf8Bytes(committed.Document.AuxiliaryState).Length}, "
            + $"payloadJson={JsonSerializer.SerializeToUtf8Bytes(committed.Document.Content).Length}, "
            + $"archive={JsonSerializer.SerializeToUtf8Bytes(committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive).Length}, "
            + $"selectedReplay={JsonSerializer.SerializeToUtf8Bytes(committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!.SelectedReplay).Length}.");
        byte[] wire = WorkspaceContinuationCodec.Encode(exported.Value, transportMaximumBytes);
        Console.WriteLine($"Typed loadout continuation actual wire: bytes={wire.Length}, limit={transportMaximumBytes}.");
        Assert.IsTrue(wire.Length <= transportMaximumBytes);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(wire, transportMaximumBytes, out var transferred),
            "The complete typed-loadout continuation must fit and decode at the unchanged real transport cap.");
        CollectionAssert.AreEqual(wire, WorkspaceContinuationCodec.Encode(transferred!, transportMaximumBytes));
        Assert.AreEqual(exported.Value.SnapshotDigest, transferred!.SnapshotDigest);
        Assert.AreEqual(committed.Document.Content, transferred.Snapshot.Workspace.Document.Content);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            committed.Document.AuxiliaryState, transferred.Snapshot.Workspace.Document.AuxiliaryState));
        var transferredAuthority = transferred.Snapshot.Workspace.Document.AuxiliaryState
            .CharacterCreationFinalizationArchive!.CompletionAuthority!;
        Assert.AreEqual(orderedHistory ? CharacterCreationCompletionAuthority.SchemaV4
            : CharacterCreationCompletionAuthority.SchemaV3, transferredAuthority.Schema);
        Assert.IsNotNull(transferredAuthority.SelectedReplay);
        if (orderedHistory)
        {
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(transferredAuthority.SelectedReplay,
                out _, out var transferredWitness));
            Assert.IsNotNull(transferredWitness!.Skills.KarmaCompensationSourceOrder);
            CollectionAssert.AreEqual(fixture.Inputs.Skills.KarmaCompensationSourceOrder!.ToArray(),
                transferredWitness.Skills.KarmaCompensationSourceOrder.ToArray());
        }
        Assert.IsNull(transferredAuthority.Inputs);
        Assert.IsNull(transferredAuthority.BootstrapSourceSnapshot);

        var targetOwners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        targetOwners.Set(owner);
        var targetStamp = targetOwners.Capture();
        Assert.AreNotEqual(linkedStamp, targetStamp);
        string targetDirectory = Path.Combine(context.Directory, "typed-loadout-continuation-target");
        var target = new FileWorkspaceStore(targetDirectory);
        Assert.IsFalse(target.Get(owner, context.WorkspaceId).Success);
        var restore = new WorkspaceContinuationRestoreService(target, targetOwners,
            context.Resolver, context.Queries, ContinuationCatalog(), transportMaximumBytes);
        using var restoreReview = restore.Review(targetStamp, wire);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, restoreReview.Result.Outcome,
            JsonSerializer.Serialize(restoreReview.Result));
        Assert.IsFalse(restoreReview.Result.Target!.Exists);
        var restored = restore.Confirm(restoreReview, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restored.Outcome, JsonSerializer.Serialize(restored));
        Assert.IsNotNull(restored.Receipt);

        var restoredColdStore = new FileWorkspaceStore(targetDirectory);
        var restoredCold = restoredColdStore.Get(owner, context.WorkspaceId);
        Assert.IsTrue(restoredCold.Success, restoredCold.Error);
        Assert.AreEqual(committed.Document.Content, restoredCold.Value!.Document.Content);
        Assert.AreEqual(committed.Document.AuxiliaryStateDigest, restoredCold.Value.Document.AuxiliaryStateDigest);
        Assert.AreEqual(committed.ContentRevision, restoredCold.Value.ContentRevision);
        Assert.AreEqual(committed.SavedRevision, restoredCold.Value.SavedRevision);
        Assert.AreEqual(committed.ContentRevision, restoredCold.Value.LocalHistory!.ImportedThroughRevision);
        Assert.AreEqual(exported.Value.SnapshotDigest, restoredCold.Value.LocalHistory.ImportedSnapshotDigest);
        Assert.AreEqual(restored.Receipt, restoredCold.Value.LocalHistory.LastRestore);
        Assert.IsFalse(restoredCold.Value.CanReplayReceipt(applied.Value.ContentRevision));
        CollectionAssert.AreEqual(nativeParentIds, NativeSkillParentIds(restoredCold.Value.Document.Content));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(completedArchive,
            restoredCold.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive));
        if (orderedHistory) Assert.IsTrue(CharacterCareerReputationTransaction.IsValidHistory(restoredCold.Value));
        Assert.IsFalse(restoredColdStore.Get(context.WorkspaceId).Success);
        Assert.IsFalse(restoredColdStore.Get(new OwnerScope("completion-loadout-other-owner"), context.WorkspaceId).Success);
        var coldExport = new WorkspaceContinuationExportService(restoredColdStore, targetOwners)
            .Export(targetStamp, context.WorkspaceId);
        Assert.IsTrue(coldExport.Success, coldExport.Error);
        CollectionAssert.AreEqual(wire, WorkspaceContinuationCodec.Encode(coldExport.Value!, transportMaximumBytes));

        string targetBeforeReplay = JsonSerializer.Serialize(restoredCold.Value);
        var historicalReplay = new OwnerBoundCharacterCreationCompletionService(restoredColdStore, targetOwners,
            context.Resolver, context.Queries).Apply(targetStamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, historicalReplay.Outcome);
        Assert.IsNull(historicalReplay.Value, "A transferred receipt is history, not authority to buy or finalize again.");
        CollectionAssert.Contains(historicalReplay.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        Assert.AreEqual(targetBeforeReplay,
            JsonSerializer.Serialize(restoredColdStore.Get(owner, context.WorkspaceId).Value));
        Assert.AreEqual(linkedBeforeExport, JsonSerializer.Serialize(coldStore.Get(owner, context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(0, targetOwners.ActiveLeases);
    }

    [TestMethod]
    public void Loadout_witness_cannot_be_downgraded_or_omit_required_selected_authority()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        foreach (var malformed in new[]
        {
            captured.Value! with { Schema = CharacterCreationSelectedCompletionWitness.SchemaV1 },
            captured.Value! with { Loadout = null }
        })
        {
            var resealed = malformed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(malformed) };
            Assert.IsFalse(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, resealed, out _));
            var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
                resealed, fixture.Request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, replay.Outcome);
            Assert.IsNull(replay.Value);
        }
    }

    [TestMethod]
    public void Rehashed_selected_loadout_cannot_change_source_price_or_claim_original_player_purchases()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, fixture.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var loadout = witness.Loadout!;
        var changed = loadout.Options[0] with { UnitCost = loadout.Options[0].UnitCost + 1m };
        changed = changed with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(changed) };
        foreach (var invalid in new[]
        {
            loadout with { Options = new[] { changed }.Concat(loadout.Options.Skip(1)).ToArray() },
            loadout with { OriginalSelections = fixture.Request.Selections.Loadout! }
        })
        {
            var selected = invalid with { WitnessDigest = CharacterCreationLoadoutRules.ComputeSelectedAuthorityDigest(invalid) };
            var candidate = witness with { Loadout = selected };
            candidate = candidate with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(candidate) };
            var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
                candidate, fixture.Request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, replay.Outcome);
            Assert.IsNull(replay.Value);
        }
    }

    [TestMethod]
    public void Loadout_observation_rejects_existing_or_ambiguous_owned_graph_without_replacing_it()
    {
        using var context = ReadyContext.Create(true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var before = JsonSerializer.Serialize(original);
        foreach (string name in new[] { "weapons", "armors" })
        foreach (bool duplicate in new[] { false, true })
        {
            var root = XDocument.Parse(original.Document.Content).Root!;
            root.Elements(name).Remove();
            root.Add(new XElement(name, duplicate ? null : new XElement("manual-item", "retain me")));
            if (duplicate) root.Add(new XElement(name));
            var changed = original with { Document = original.Document with
                { State = original.Document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } } };
            var result = CharacterCreationLoadoutCompletionService.LoadFromObservation(changed, context.Resolver, true);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationLoadoutCompletionSchemas.ExistingGraphUnsupported);
        }
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Whole_plan_ammunition_replays_and_persists_owned_cartridges_without_loading_weapons()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var baseline = CharacterCreationCompletionReviewService.Review(fixture.Inputs, fixture.Request).Value!;
        Assert.IsNotNull(baseline);
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        decimal loadoutCost = fixture.Weapon.UnitCost + fixture.Armor.UnitCost;
        var planRequest = CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
        {
            Purchases = [new() { Weapon = new(fixture.Weapon.OptionId) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) }],
            MaximumAdditionalPurchaseSpend = loadoutCost + 74m
        };
        var planned = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, planRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var plan = planned.Value!;
        Assert.AreEqual(74m, plan.Purchases!.Targets[2].Ammunition!.TotalCost);
        Assert.AreEqual(loadoutCost + 74m, plan.Purchases.AdditionalSpend);
        Assert.AreEqual((decimal?)56000m, plan.Review.Balances.CreationNuyen);
        Assert.AreEqual((decimal?)174m, plan.Review.Balances.GearCost);
        Assert.AreEqual((decimal?)2000m, plan.Review.Balances.LifestyleCost);
        Assert.AreEqual((decimal?)loadoutCost, plan.Review.Balances.LoadoutCost);
        Assert.AreEqual((decimal?)(53826m - loadoutCost), plan.Review.Balances.RemainingNuyen);
        Assert.AreEqual(baseline.Balances.RemainingNuyen - 74m, plan.Review.Balances.RemainingNuyen);
        Assert.IsTrue(fixture.Request.Selections.Gear.All(plan.Selections.Gear.Contains));
        // The full product requirement is still an explicit gap. No fixture or
        // caller requirement is weakened to make this purchase look complete.
        var wholeProduct = CharacterCreationCompletionPlanner.Plan(fixture.Inputs,
            planRequest with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wholeProduct.Outcome);
        CollectionAssert.Contains(wholeProduct.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var proposal = fixture.Request with { Selections = plan.Selections };
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var retained = witness.Gear.Options.Single(row => row.OptionId == ammo.OptionId);
        Assert.AreEqual(ammo.SourceNodeXml, retained.SourceNodeXml);
        Assert.AreEqual(ammo.SourceNodeDigest, retained.SourceNodeDigest);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, bootstrap!, coldWitness!, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(plan.Review.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.AreEqual(JsonSerializer.Serialize(plan.Review.Lifestyles!.Gear.Lines),
            JsonSerializer.Serialize(replay.Value.Gear));

        var staleSource = witness.Gear with { Options = witness.Gear.Options.Select(row => row.OptionId == ammo.OptionId
            ? row with { SourceNodeXml = row.SourceNodeXml.Replace("<cost>20</cost>", "<cost>21</cost>", StringComparison.Ordinal) } : row).ToArray() };
        staleSource = staleSource with { WitnessDigest = CharacterCreationGearRules.ComputeSelectedAuthorityDigest(staleSource) };
        var staleWitness = witness with { Gear = staleSource };
        staleWitness = staleWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(staleWitness) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
            staleWitness, proposal).Value);

        var missing = witness.Gear with { Options = witness.Gear.Options.Where(row => row.OptionId != ammo.OptionId).ToArray() };
        missing = missing with { WitnessDigest = CharacterCreationGearRules.ComputeSelectedAuthorityDigest(missing) };
        var forged = witness with { Gear = missing };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
            forged, proposal).Value);

        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var writes = new AtomicReplacementProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var reviewed = service.Review(stamp, context.WorkspaceId, proposal, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var projection = reviewed.Value!.Projection;
        var root = XDocument.Parse(projection.CharacterXml).Root!;
        var cartridges = root.Element("gears")!.Elements("gear").Single(row =>
            row.Element("sourceid")?.Value == ammo.SourceId.ToString("D"));
        Assert.AreEqual("37", cartridges.Element("qty")!.Value);
        Assert.AreEqual("10", cartridges.Element("costfor")!.Value);
        Assert.AreEqual("20", cartridges.Element("cost")!.Value);
        Assert.AreEqual("gun", cartridges.Element("ammoforweapontype")!.Value);
        Assert.AreEqual(string.Empty, cartridges.Element("extra")!.Value);
        // Compare the entire weapon graph to the identical weapon/armor proposal
        // without ammunition. Adding owned cartridges must not mutate capacity,
        // create clips, select an ammo slot or imply a loaded round count.
        var withoutAmmo = service.Review(stamp, context.WorkspaceId, fixture.Request, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, withoutAmmo.Outcome);
        var originalWeapon = XDocument.Parse(withoutAmmo.Value!.Projection.CharacterXml).Root!.Element("weapons")!.Elements("weapon").Single();
        var proposedWeapon = root.Element("weapons")!.Elements("weapon").Single();
        // Instance IDs bind the review digest; mechanical fields must be identical.
        originalWeapon.Element("guid")!.Remove();
        var comparableWeapon = new XElement(proposedWeapon);
        comparableWeapon.Element("guid")!.Remove();
        Assert.IsTrue(XNode.DeepEquals(originalWeapon, comparableWeapon));
        Assert.AreEqual(0, writes.Replacements);
        var stale = service.Review(stamp, context.WorkspaceId,
            proposal with { InputsSnapshotDigest = "sha256:" + new string('0', 64) }, fixture.CashChoice);
        Assert.IsNull(stale.Value);
        Assert.AreEqual(0, writes.Replacements);

        var command = new CharacterCreationCompletionApplyRequest(projection.Binding, reviewed.Value.Proposal,
            projection.ReviewDigest, fixture.CashChoice, projection.ProjectionDigest, "ammunition-owned-cartridges", true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, writes.Replacements);
        var committed = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(projection.CharacterXml, committed.Document.Content);
        Assert.AreEqual(fixture.Original.ContentRevision + 1, committed.ContentRevision);
        Assert.AreEqual(committed.ContentRevision, committed.SavedRevision);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(fixture.Original.Document.AuxiliaryState,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive, applied.Value!));
        var coldStore = new FileWorkspaceStore(context.Directory, writes);
        var repeated = new OwnerBoundCharacterCreationCompletionService(coldStore, owners, context.Resolver, context.Queries)
            .Apply(stamp, command);
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(1, writes.Replacements);
        Assert.AreEqual(committed.Document.Content, coldStore.Get(context.WorkspaceId).Value!.Document.Content);
    }

    [TestMethod]
    public void Assigned_cartridges_bind_the_second_selected_instance_through_cold_replay_without_loading()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        fixture = fixture with { Request = fixture.Request with { Selections = fixture.Request.Selections with
            { Loadout = new([new(fixture.Weapon.OptionId, 2)], [new(fixture.Armor.OptionId)]) } } };
        var target = AmmunitionTarget(fixture.Weapon, 1);
        var baseline = CharacterCreationCompletionReviewService.Review(fixture.Inputs, fixture.Request).Value!;
        Assert.IsNotNull(baseline);
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        decimal loadoutCost = 2 * fixture.Weapon.UnitCost + fixture.Armor.UnitCost;
        var planRequest = CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
        {
            Purchases = [new() { Weapon = new(fixture.Weapon.OptionId, 2) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = target } }],
            MaximumAdditionalPurchaseSpend = loadoutCost + 74m
        };
        var planned = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, planRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var plan = planned.Value!;
        Assert.AreEqual(74m, plan.Purchases!.Targets[2].Ammunition!.TotalCost);
        Assert.AreEqual(target, plan.Purchases.Targets[2].Ammunition!.Weapon);
        Assert.IsFalse(plan.Purchases.Targets[2].Ammunition!.RemainingGaps.Contains(
            CharacterCreationAmmunitionCompletionGaps.WeaponCompatibility));
        CollectionAssert.Contains(plan.Purchases.Targets[2].Ammunition!.RemainingGaps.ToArray(),
            CharacterCreationAmmunitionCompletionGaps.LoadedState);
        Assert.AreEqual(new CharacterCreationAmmunitionAssignment(ammo.OptionId, ammo.SourceNodeDigest, target),
            plan.Selections.AmmunitionAssignments!.Single());
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Request.Selections.Loadout),
            JsonSerializer.Serialize(plan.Selections.Loadout));
        Assert.AreEqual(loadoutCost + 74m, plan.Purchases.AdditionalSpend);
        Assert.AreEqual((decimal?)56000m, plan.Review.Balances.CreationNuyen);
        Assert.AreEqual((decimal?)174m, plan.Review.Balances.GearCost);
        Assert.AreEqual((decimal?)2000m, plan.Review.Balances.LifestyleCost);
        Assert.AreEqual((decimal?)loadoutCost, plan.Review.Balances.LoadoutCost);
        Assert.AreEqual((decimal?)(53826m - loadoutCost), plan.Review.Balances.RemainingNuyen);
        Assert.AreEqual(baseline.Balances.RemainingNuyen - 74m, plan.Review.Balances.RemainingNuyen);
        Assert.IsTrue(fixture.Request.Selections.Gear.All(plan.Selections.Gear.Contains));
        // The full product requirement is still an explicit gap. No fixture or
        // caller requirement is weakened to make this purchase look complete.
        var wholeProduct = CharacterCreationCompletionPlanner.Plan(fixture.Inputs,
            planRequest with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wholeProduct.Outcome);
        CollectionAssert.Contains(wholeProduct.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var proposal = fixture.Request with { Selections = plan.Selections };
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var retained = witness.Gear.Options.Single(row => row.OptionId == ammo.OptionId);
        Assert.AreEqual(ammo.SourceNodeXml, retained.SourceNodeXml);
        Assert.AreEqual(ammo.SourceNodeDigest, retained.SourceNodeDigest);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(fixture.Bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, bootstrap!, coldWitness!, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(plan.Review.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        Assert.AreEqual(JsonSerializer.Serialize(plan.Review.Lifestyles!.Gear.Lines),
            JsonSerializer.Serialize(replay.Value.Gear));

        var staleSource = witness.Gear with { Options = witness.Gear.Options.Select(row => row.OptionId == ammo.OptionId
            ? row with { SourceNodeXml = row.SourceNodeXml.Replace("<cost>20</cost>", "<cost>21</cost>", StringComparison.Ordinal) } : row).ToArray() };
        staleSource = staleSource with { WitnessDigest = CharacterCreationGearRules.ComputeSelectedAuthorityDigest(staleSource) };
        var staleWitness = witness with { Gear = staleSource };
        staleWitness = staleWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(staleWitness) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
            staleWitness, proposal).Value);

        var missing = witness.Gear with { Options = witness.Gear.Options.Where(row => row.OptionId != ammo.OptionId).ToArray() };
        missing = missing with { WitnessDigest = CharacterCreationGearRules.ComputeSelectedAuthorityDigest(missing) };
        var forged = witness with { Gear = missing };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
            forged, proposal).Value);

        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var writes = new AtomicReplacementProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var reviewed = service.Review(stamp, context.WorkspaceId, proposal, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var projection = reviewed.Value!.Projection;
        var root = XDocument.Parse(projection.CharacterXml).Root!;
        var cartridges = root.Element("gears")!.Elements("gear").Single(row =>
            row.Element("sourceid")?.Value == ammo.SourceId.ToString("D"));
        Assert.AreEqual("37", cartridges.Element("qty")!.Value);
        Assert.AreEqual("10", cartridges.Element("costfor")!.Value);
        Assert.AreEqual("20", cartridges.Element("cost")!.Value);
        Assert.AreEqual("gun", cartridges.Element("ammoforweapontype")!.Value);
        Assert.AreEqual(target.AmmoCategory, cartridges.Element("extra")!.Value);
        Assert.AreEqual(2, root.Element("weapons")!.Elements("weapon").Count());
        var association = cartridges.Element("ammunitionassignment")!;
        Assert.AreEqual(cartridges.Element("guid")!.Value, association.Element("gearinstanceid")!.Value);
        Assert.AreEqual(root.Element("weapons")!.Elements("weapon").ElementAt(1).Element("guid")!.Value,
            association.Element("weaponinstanceid")!.Value);
        Assert.AreNotEqual(root.Element("weapons")!.Elements("weapon").First().Element("guid")!.Value,
            association.Element("weaponinstanceid")!.Value);
        Assert.AreEqual(projection.ReviewDigest, association.Element("reviewdigest")!.Value);
        Assert.AreEqual(ammo.SourceNodeDigest, association.Element("gearsourcedigest")!.Value);
        Assert.AreEqual(target.SourceNodeDigest, association.Element("weaponsourcedigest")!.Value);
        Assert.AreEqual("1", association.Element("weaponinstanceordinal")!.Value);
        Assert.AreEqual(1, projection.OrderedDeltas.Count(row => row.Kind == "ammunition-assignment"));
        // Compare the entire weapon graph to the identical weapon/armor proposal
        // without ammunition. Adding owned cartridges must not mutate capacity,
        // create clips, select an ammo slot or imply a loaded round count.
        var withoutAmmo = service.Review(stamp, context.WorkspaceId, fixture.Request, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, withoutAmmo.Outcome);
        var originalWeapon = XDocument.Parse(withoutAmmo.Value!.Projection.CharacterXml).Root!.Element("weapons")!.Elements("weapon").ElementAt(1);
        var proposedWeapon = root.Element("weapons")!.Elements("weapon").ElementAt(1);
        // Instance IDs bind the review digest; mechanical fields must be identical.
        originalWeapon.Element("guid")!.Remove();
        var comparableWeapon = new XElement(proposedWeapon);
        comparableWeapon.Element("guid")!.Remove();
        Assert.IsTrue(XNode.DeepEquals(originalWeapon, comparableWeapon));
        Assert.AreEqual(0, root.Element("weapons")!.Descendants("clip").Count());
        Assert.IsFalse(root.Element("weapons")!.Descendants("ammoremaining").Any());
        var beforeGear = XDocument.Parse(withoutAmmo.Value!.Projection.CharacterXml).Root!.Element("gears")!;
        foreach (var existingGear in beforeGear.Elements("gear"))
        {
            var preserved = new XElement(root.Element("gears")!.Elements("gear")
                .Single(row => row.Element("sourceid")!.Value == existingGear.Element("sourceid")!.Value));
            var expected = new XElement(existingGear);
            expected.Element("guid")!.Remove();
            preserved.Element("guid")!.Remove();
            Assert.IsTrue(XNode.DeepEquals(expected, preserved), "Original selected Gear must be preserved.");
        }
        Assert.AreEqual(0, writes.Replacements);
        var stale = service.Review(stamp, context.WorkspaceId,
            proposal with { InputsSnapshotDigest = "sha256:" + new string('0', 64) }, fixture.CashChoice);
        Assert.IsNull(stale.Value);
        Assert.AreEqual(0, writes.Replacements);

        var command = new CharacterCreationCompletionApplyRequest(projection.Binding, reviewed.Value.Proposal,
            projection.ReviewDigest, fixture.CashChoice, projection.ProjectionDigest, "ammunition-assigned-cartridges", true);
        var changedSource = new AssignmentSourceResolver(context.Resolver, fixture.Weapon.OptionId);
        var changedSourceService = new OwnerBoundCharacterCreationCompletionService(
            store, owners, changedSource, context.Queries);
        Assert.IsNull(changedSourceService.Apply(stamp, command).Value);
        Assert.IsTrue(changedSource.ChangedReads > 0, "Apply must re-admit current weapon source.");
        Assert.AreEqual(0, writes.Replacements);
        var sourceBefore = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        owners.Set(new OwnerScope("ammunition-assignment-foreign"));
        Assert.IsNull(service.Review(owners.Capture(), context.WorkspaceId, proposal, fixture.CashChoice).Value);
        Assert.IsNull(service.Apply(owners.Capture(), command).Value);
        Assert.IsNull(service.Apply(stamp, command).Value);
        Assert.AreEqual(0, writes.Replacements);
        Assert.AreEqual(sourceBefore, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        owners.Set(stamp.Owner);
        Assert.IsNull(service.Apply(stamp, command).Value);
        stamp = owners.Capture();
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, writes.Replacements);
        var committed = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(projection.CharacterXml, committed.Document.Content);
        Assert.AreEqual(fixture.Original.ContentRevision + 1, committed.ContentRevision);
        Assert.AreEqual(committed.ContentRevision, committed.SavedRevision);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(fixture.Original.Document.AuxiliaryState,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive, applied.Value!));
        var coldStore = new FileWorkspaceStore(context.Directory, writes);
        var coldRoot = XDocument.Parse(coldStore.Get(context.WorkspaceId).Value!.Document.Content).Root!;
        Assert.IsTrue(XNode.DeepEquals(association, coldRoot.Element("gears")!.Elements("gear")
            .Single(row => row.Element("sourceid")?.Value == ammo.SourceId.ToString("D"))
            .Element("ammunitionassignment")));
        Assert.AreEqual("37", coldRoot.Element("gears")!.Elements("gear")
            .Single(row => row.Element("sourceid")?.Value == ammo.SourceId.ToString("D")).Element("qty")!.Value);
        var repeated = new OwnerBoundCharacterCreationCompletionService(coldStore, owners, context.Resolver, context.Queries)
            .Apply(stamp, command);
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(1, writes.Replacements);
        Assert.AreEqual(committed.Document.Content, coldStore.Get(context.WorkspaceId).Value!.Document.Content);
    }

    [TestMethod]
    public void Ammunition_assignment_validates_source_caliber_selection_and_effects_even_when_resealed()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        var target = AmmunitionTarget(fixture.Weapon, 0);
        var other = fixture.Inputs.Loadout!.Authority.Options.First(row =>
            row.Kind == CharacterCreationLoadoutKinds.Weapon && row.IsSelectable
            && AmmunitionTarget(row, 0).AmmoCategory != target.AmmoCategory);
        var request = CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
        {
            Purchases = [new() { Weapon = new(fixture.Weapon.OptionId) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = target } }],
            MaximumAdditionalPurchaseSpend = fixture.Weapon.UnitCost + fixture.Armor.UnitCost + 74m
        };
        var result = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        var proposal = plan.Review.Request;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(fixture.Original, fixture.Bootstrap,
            fixture.Inputs, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        foreach (var (invalid, expected) in new[]
        {
            (target with { AmmoCategory = AmmunitionTarget(other, 0).AmmoCategory },
                CharacterCreationAmmunitionCompletionBlockers.CaliberMismatch),
            (target with { OptionId = other.OptionId, SourceNodeDigest = other.SourceNodeDigest,
                AmmoCategory = AmmunitionTarget(other, 0).AmmoCategory },
                CharacterCreationAmmunitionCompletionBlockers.WeaponNotSelected),
            (target with { InstanceOrdinal = 1 }, CharacterCreationAmmunitionCompletionBlockers.WeaponNotSelected),
            (target with { SourceNodeDigest = "sha256:" + new string('0', 64) },
                CharacterCreationAmmunitionCompletionBlockers.WeaponSourceChanged)
        })
        {
            var rejected = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request with
            {
                Purchases = request.Purchases.Take(2).Append(new CharacterCreationPurchaseCompletionTarget
                    { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = invalid } }).ToArray()
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
            Assert.IsNotNull(rejected.Value);
            CollectionAssert.Contains(rejected.Value.Purchases!.Targets[2].Blockers.ToArray(), expected);
            Assert.IsFalse(rejected.Value.Selections.Gear.Any(row => row.OptionId == ammo.OptionId));
            Assert.IsNull(rejected.Value.Selections.AmmunitionAssignments);
            var changed = proposal with { Selections = proposal.Selections with
            {
                AmmunitionAssignments = [new(ammo.OptionId, ammo.SourceNodeDigest, invalid)]
            } };
            var review = CharacterCreationCompletionReviewService.Review(fixture.Inputs, changed);
            Assert.IsNull(review.Value);
            CollectionAssert.Contains(review.Blockers.ToArray(), expected);
            // Make envelope hashes internally consistent to exercise semantic replay,
            // rather than merely proving a stale digest is rejected.
            var resealed = witness with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(changed) };
            resealed = resealed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(resealed) };
            var replay = CharacterCreationSelectedCompletionEvaluator.Replay(fixture.Original, fixture.Bootstrap,
                resealed, changed);
            Assert.IsNull(replay.Value);
            CollectionAssert.Contains(replay.Blockers.ToArray(), expected);
        }
        var line = plan.Review.Lifestyles!.Gear.Lines.Single(row => row.OptionId == ammo.OptionId);
        var source = XElement.Parse(line.SourceNodeXml);
        source.Add(new XElement("weaponbonus", new XElement("damage", "1")));
        string xml = source.ToString(SaveOptions.DisableFormatting);
        var effectAmmo = line with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationGearRules.ComputeSourceNodeDigest(xml) };
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TryValidateAssignments(
            [new(effectAmmo.OptionId, effectAmmo.SourceNodeDigest, target)], [effectAmmo], plan.Review.Loadout!.Lines, out var blockers));
        CollectionAssert.Contains(blockers, CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics);

        var weaponLine = plan.Review.Loadout.Lines.Single(row => row.OptionId == fixture.Weapon.OptionId);
        var weaponSource = XElement.Parse(weaponLine.SourceNodeXml);
        weaponSource.Add(new XElement("weaponbonus", new XElement("damage", "1")));
        xml = weaponSource.ToString(SaveOptions.DisableFormatting);
        var effectWeapon = weaponLine with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationLoadoutRules.ComputeSourceNodeDigest(xml) };
        effectWeapon = effectWeapon with { LineDigest = CharacterCreationLoadoutRules.ComputeLineDigest(effectWeapon) };
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TryValidateAssignments(
            [new(line.OptionId, line.SourceNodeDigest, target with { SourceNodeDigest = effectWeapon.SourceNodeDigest })],
            [line], [effectWeapon], out blockers));
        CollectionAssert.Contains(blockers, CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics);

        var staleAssignment = proposal.Selections.AmmunitionAssignments!.Single() with
            { GearSourceNodeDigest = "sha256:" + new string('0', 64) };
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TryValidateAssignments(
            [staleAssignment], plan.Review.Lifestyles.Gear.Lines, plan.Review.Loadout.Lines, out blockers));
        CollectionAssert.Contains(blockers, CharacterCreationAmmunitionCompletionBlockers.SourceChanged);
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TryValidateAssignments(
            [proposal.Selections.AmmunitionAssignments!.Single(), proposal.Selections.AmmunitionAssignments!.Single()],
            plan.Review.Lifestyles.Gear.Lines, plan.Review.Loadout.Lines, out blockers));
        CollectionAssert.Contains(blockers, CharacterCreationAmmunitionCompletionBlockers.AssignmentInvalid);
    }

    [TestMethod]
    public void Assignment_does_not_split_or_reassign_owned_quantity_or_bypass_the_shared_budget()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        var target = AmmunitionTarget(fixture.Weapon, 0);
        var purchase = new CharacterCreationAmmunitionPurchase(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = target };
        var request = CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
        {
            Purchases = [new() { Weapon = new(fixture.Weapon.OptionId, 2) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Ammunition = purchase },
                new() { Ammunition = purchase with { Weapon = target with { InstanceOrdinal = 1 } } },
                new() { Ammunition = purchase with { Cartridges = 38 } }],
            MaximumAdditionalPurchaseSpend = 2 * fixture.Weapon.UnitCost + fixture.Armor.UnitCost + 74m
        };
        var result = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.AreEqual(37, plan.Selections.Gear.Single(row => row.OptionId == ammo.OptionId).Quantity);
        Assert.AreEqual(target, plan.Selections.AmmunitionAssignments!.Single().Weapon);
        CollectionAssert.Contains(plan.Purchases!.Targets[3].Blockers.ToArray(),
            CharacterCreationAmmunitionCompletionBlockers.AssignmentConflict);
        CollectionAssert.Contains(plan.Purchases.Targets[4].Blockers.ToArray(),
            CharacterCreationPurchaseCompletionBlockers.ExistingSelectionLocked);
        Assert.AreEqual(request.MaximumAdditionalPurchaseSpend, plan.Purchases.AdditionalSpend);

        var denied = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request with
        {
            Purchases = request.Purchases.Take(3).ToArray(),
            MaximumAdditionalPurchaseSpend = request.MaximumAdditionalPurchaseSpend - 1m
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        CollectionAssert.Contains(denied.Value!.Purchases!.Targets[2].Blockers.ToArray(),
            CharacterCreationPurchaseCompletionBlockers.SpendLimit);
        Assert.IsFalse(denied.Value.Selections.Gear.Any(row => row.OptionId == ammo.OptionId));
        Assert.IsNull(denied.Value.Selections.AmmunitionAssignments);

        // The request order is explicit. Catalog visibility is not weapon ownership.
        denied = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request with
        {
            Purchases = [request.Purchases[2], request.Purchases[0], request.Purchases[1]]
        });
        CollectionAssert.Contains(denied.Value!.Purchases!.Targets[0].Blockers.ToArray(),
            CharacterCreationAmmunitionCompletionBlockers.WeaponNotSelected);
        Assert.IsNull(denied.Value.Selections.AmmunitionAssignments);
    }

    [TestMethod]
    public void Assignment_preserves_manually_purchased_weapon_instance_ids_and_existing_gear()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var before = Capture(context);
        var service = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var state = service.Prepare(context.WorkspaceId).Value!;
        var selected = new CharacterCreationLoadoutSelections(
            [new(before.Weapon.OptionId, 2)], [new(before.Armor.OptionId)]);
        var preview = service.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest, selected));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var purchased = service.Confirm(new(preview.Value.Request, preview.Value.PreviewDigest,
            "ammunition-retained-weapons", true));
        Assert.IsNotNull(purchased.Value, string.Join(",", purchased.Blockers));
        // The saved fixture already has a Lifestyle. Confirm a real Gear successor
        // while retaining every prior line, both manually chosen weapon IDs and the home.
        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        var gearState = gear.Load(new(context.WorkspaceId)).Value!;
        var successor = before.Request.Selections.Gear.Select((line, index) =>
            index == 0 ? line with { Quantity = line.Quantity + 1 } : line).ToArray();
        Assert.IsTrue(successor.Length > 0);
        var gearPreview = gear.Preview(new(gearState.Binding, successor)).Value!;
        Assert.IsTrue(gearPreview.CanConfirm, string.Join(",", gearPreview.Blockers));
        var gearReceipt = gear.Confirm(new(gearState.Binding, successor, gearPreview.PreviewDigest,
            "ammunition-retained-weapons-gear-successor", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gearReceipt.Outcome, string.Join(",", gearReceipt.Blockers));
        var fixture = Capture(context);
        Assert.AreEqual(JsonSerializer.Serialize(before.Request.Selections.Lifestyles), JsonSerializer.Serialize(fixture.Request.Selections.Lifestyles));
        Assert.AreEqual(before.Request.Selections.StartingLifestyleId, fixture.Request.Selections.StartingLifestyleId);
        Assert.AreEqual(JsonSerializer.Serialize(successor), JsonSerializer.Serialize(fixture.Request.Selections.Gear));
        var retained = fixture.Original.Document.AuxiliaryState.CharacterCreationLoadoutDraft!;
        var retainedIds = retained.Instances.Single(row => row.OptionId == before.Weapon.OptionId).InstanceIds;
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        var target = AmmunitionTarget(before.Weapon, 1);
        var planned = CharacterCreationCompletionPlanner.Plan(fixture.Inputs,
            CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
            {
                Purchases = [new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = target } }],
                MaximumAdditionalPurchaseSpend = 74m
            });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var plan = planned.Value!;
        Assert.AreEqual(JsonSerializer.Serialize(selected), JsonSerializer.Serialize(plan.Selections.Loadout));
        Assert.AreEqual(74m, plan.Purchases!.AdditionalSpend);
        Assert.IsTrue(fixture.Original.Document.AuxiliaryState.CharacterCreationGearDraft!.Lines
            .All(line => plan.Selections.Gear.Contains(new CharacterCreationGearSelection(line.OptionId, line.Quantity))));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var completion = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var reviewed = completion.Review(owners.Capture(), context.WorkspaceId, plan.Review.Request, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var root = XDocument.Parse(reviewed.Value!.Projection.CharacterXml).Root!;
        CollectionAssert.AreEqual(retainedIds.Select(id => id.ToString("D")).ToArray(),
            root.Element("weapons")!.Elements("weapon").Select(node => node.Element("guid")!.Value).ToArray());
        var savedAmmo = root.Element("gears")!.Elements("gear").Single(node =>
            node.Element("sourceid")!.Value == ammo.SourceId.ToString("D"));
        Assert.AreEqual(retainedIds[1].ToString("D"), savedAmmo.Element("ammunitionassignment")!.Element("weaponinstanceid")!.Value);
        Assert.AreEqual("37", savedAmmo.Element("qty")!.Value);
        Assert.AreEqual(0, root.Element("weapons")!.Descendants("clip").Count());
        Assert.AreEqual(fixture.Original.Document.Content, context.Store.Get(context.WorkspaceId).Value!.Document.Content);

        var stamp = owners.Capture();
        var writes = new AtomicReplacementProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var projection = reviewed.Value.Projection;
        var command = new CharacterCreationCompletionApplyRequest(projection.Binding, reviewed.Value.Proposal,
            projection.ReviewDigest, fixture.CashChoice, projection.ProjectionDigest, "ammunition-retained-weapons-apply", true);
        var applied = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries)
            .Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, writes.Replacements);
        var committed = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(projection.CharacterXml, committed.Document.Content);
        Assert.AreEqual(fixture.Original.ContentRevision + 1, committed.ContentRevision);
        Assert.AreEqual(committed.ContentRevision, committed.SavedRevision);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(fixture.Original.Document.AuxiliaryState,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
            committed.Document.AuxiliaryState.CharacterCreationFinalizationArchive, applied.Value!));
        byte[] committedBytes = File.ReadAllBytes(writes.TargetPath!);
        var coldStore = new FileWorkspaceStore(context.Directory, writes);
        var cold = coldStore.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(committed.Document.Content, cold.Document.Content);
        Assert.AreEqual(committed.Document.AuxiliaryStateDigest, cold.Document.AuxiliaryStateDigest);
        var coldRoot = XDocument.Parse(cold.Document.Content).Root!;
        CollectionAssert.AreEqual(retainedIds.Select(id => id.ToString("D")).ToArray(),
            coldRoot.Element("weapons")!.Elements("weapon").Select(node => node.Element("guid")!.Value).ToArray());
        Assert.IsTrue(XNode.DeepEquals(savedAmmo, coldRoot.Element("gears")!.Elements("gear")
            .Single(node => node.Element("sourceid")?.Value == ammo.SourceId.ToString("D"))));
        var repeated = new OwnerBoundCharacterCreationCompletionService(coldStore, owners, context.Resolver, context.Queries)
            .Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeated.Outcome, string.Join(",", repeated.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(1, writes.Replacements);
        CollectionAssert.AreEqual(committedBytes, File.ReadAllBytes(writes.TargetPath!));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Unassigned_ammunition_optional_members_preserve_legacy_serialization_and_digests()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var request = BoundaryAmmunitionRequest(fixture, weapon: null);
        var result = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        var purchasePlan = plan.Purchases!;
        var purchase = AssertOptionalNull(request.Purchases[2].Ammunition!, "Weapon");
        var allocation = AssertOptionalNull(purchasePlan.Targets[2].Ammunition!, "Weapon");
        var selections = AssertOptionalNull(plan.Selections, "AmmunitionAssignments");
        var purchases = AssertOptionalNull(purchasePlan, "AmmunitionAssignments") with
        {
            Targets = purchasePlan.Targets.Take(2).Append(purchasePlan.Targets[2] with
            { Ammunition = allocation }).ToArray()
        };
        var review = plan.Review with { Request = plan.Review.Request with { Selections = selections } };
        var roundTrip = plan with
        {
            Request = request with { Purchases = request.Purchases.Take(2).Append(request.Purchases[2] with
                { Ammunition = purchase }).ToArray() },
            Selections = selections,
            Purchases = purchases,
            Review = review
        };
        Assert.AreEqual(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(roundTrip));
        Assert.AreEqual(purchasePlan.PlanDigest, CharacterCreationPurchaseCompletionRules.ComputePlanDigest(purchases));
        Assert.AreEqual(plan.Review.ReviewDigest, CharacterCreationCompletionReviewRules.ComputeReviewDigest(review));
        Assert.AreEqual(plan.PlanDigest, CharacterCreationCompletionPlanRules.ComputePlanDigest(roundTrip));

        static T AssertOptionalNull<T>(T value, string property) where T : class
        {
            string canonical = JsonSerializer.Serialize(value);
            Assert.IsFalse(JsonSerializer.SerializeToElement(value).TryGetProperty(property, out _));
            var absent = JsonSerializer.Deserialize<T>(canonical)!;
            var explicitNull = JsonSerializer.Deserialize<T>(canonical[..^1] + ",\"" + property + "\":null}")!;
            Assert.AreEqual(canonical, JsonSerializer.Serialize(absent));
            Assert.AreEqual(canonical, JsonSerializer.Serialize(explicitNull));
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(value), CharacterCreationFinalizationDigest.Compute(absent));
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(value), CharacterCreationFinalizationDigest.Compute(explicitNull));
            return explicitNull;
        }
    }

    [TestMethod]
    public void Public_review_rejects_malformed_ammunition_assignments_without_writes_or_exception_leaks()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        var planned = CharacterCreationCompletionPlanner.Plan(fixture.Inputs,
            BoundaryAmmunitionRequest(fixture, AmmunitionTarget(fixture.Weapon, 0)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        var proposal = planned.Value!.Review.Request;
        var assignment = proposal.Selections.AmmunitionAssignments!.Single();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var stamp = owners.Capture();
        var writes = new AtomicReplacementProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        var valid = service.Review(stamp, context.WorkspaceId, proposal, fixture.CashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, valid.Outcome, string.Join(",", valid.Blockers));
        foreach (var (name, assignments) in new (string, CharacterCreationAmmunitionAssignment[])[]
        {
            ("null entry", [null!]),
            ("null weapon target", [assignment with { Weapon = null! }]),
            ("too many entries", Enumerable.Range(0, CharacterCreationPurchaseCompletionPlanner.MaximumTargets + 1)
                .Select(index => assignment with { GearOptionId = $"gear:overflow-{index}" }).ToArray()),
            ("noncanonical Gear digest", [assignment with { GearSourceNodeDigest = assignment.GearSourceNodeDigest + " " }]),
            ("noncanonical weapon digest", [assignment with
                { Weapon = assignment.Weapon with { SourceNodeDigest = assignment.Weapon.SourceNodeDigest.ToUpperInvariant() } }])
        })
        {
            var rejected = service.Review(stamp, context.WorkspaceId, proposal with
            { Selections = proposal.Selections with { AmmunitionAssignments = assignments } }, fixture.CashChoice);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome, name);
            Assert.IsNull(rejected.Value, name);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationAmmunitionCompletionBlockers.AssignmentInvalid);
            Assert.AreEqual(0, writes.Replacements, name);
            Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value), name);
            Assert.AreEqual(0, owners.ActiveLeases, name);
        }
    }

    [TestMethod]
    public void Ammunition_assignment_respects_the_shared_minimum_cash_reserve_at_the_exact_boundary()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
        string before = JsonSerializer.Serialize(fixture.Original);
        var request = BoundaryAmmunitionRequest(fixture, AmmunitionTarget(fixture.Weapon, 0));
        var baseline = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baseline.Outcome, string.Join(",", baseline.Blockers));
        var plan = baseline.Value!;
        decimal reserve = plan.Review.Balances.RemainingNuyen!.Value;
        var accepted = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request with { NuyenToReserve = reserve });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, accepted.Outcome, string.Join(",", accepted.Blockers));
        Assert.AreEqual((decimal?)reserve, accepted.Value!.Review.Balances.RemainingNuyen);
        Assert.AreEqual(JsonSerializer.Serialize(plan.Selections), JsonSerializer.Serialize(accepted.Value.Selections));
        Assert.AreEqual(plan.Purchases!.AdditionalSpend, accepted.Value.Purchases!.AdditionalSpend);
        Assert.AreEqual(1, accepted.Value.Selections.AmmunitionAssignments!.Count);

        var denied = CharacterCreationCompletionPlanner.Plan(fixture.Inputs, request with { NuyenToReserve = reserve + 1m });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        var rejected = denied.Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ReserveRequired, rejected.Purchases!.Targets[2].Outcome);
        CollectionAssert.Contains(rejected.Purchases.Targets[2].Blockers.ToArray(), CharacterCreationPurchaseCompletionBlockers.ReserveRequired);
        Assert.IsFalse(rejected.Purchases.Targets[2].Blockers.Contains(CharacterCreationPurchaseCompletionBlockers.SpendLimit));
        Assert.IsNull(rejected.Selections.AmmunitionAssignments);
        Assert.AreEqual(JsonSerializer.Serialize(plan.Selections.Loadout), JsonSerializer.Serialize(rejected.Selections.Loadout));
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Request.Selections.Gear), JsonSerializer.Serialize(rejected.Selections.Gear));
        Assert.AreEqual(plan.Purchases.AdditionalSpend - plan.Purchases.Targets[2].Ammunition!.TotalCost,
            rejected.Purchases.AdditionalSpend);
        Assert.AreEqual((decimal?)(reserve + plan.Purchases.Targets[2].Ammunition!.TotalCost), rejected.Review.Balances.RemainingNuyen);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static CharacterCreationCompletionPlanRequest BoundaryAmmunitionRequest(Fixture fixture,
        CharacterCreationAmmunitionWeaponTarget? weapon)
    {
        var ammo = fixture.Inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);
        return CharacterCreationCompletionPlannerTests.RetainingRequest(fixture.Inputs) with
        {
            Purchases = [new() { Weapon = new(fixture.Weapon.OptionId) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) { Weapon = weapon } }],
            MaximumAdditionalPurchaseSpend = fixture.Weapon.UnitCost + fixture.Armor.UnitCost + 74m
        };
    }

    private static CharacterCreationAmmunitionWeaponTarget AmmunitionTarget(
        CharacterCreationLoadoutCatalogOption weapon, int ordinal)
    {
        var source = XElement.Parse(weapon.SourceNodeXml);
        string category = source.Element("ammocategory")?.Value ?? string.Empty;
        if (category.Length == 0) category = source.Element("category")!.Value;
        return new(weapon.OptionId, weapon.SourceNodeDigest, ordinal, category);
    }

    private sealed class AssignmentSourceResolver(ICharacterSourceDataResolver inner, string optionId)
        : ICharacterSourceDataResolver
    {
        public int ChangedReads { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            var context = inner.TryCreateContext(xml);
            if (context is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, AssignmentSourceProxy>();
            var wrapper = (AssignmentSourceProxy)(object)proxy;
            wrapper.Inner = context;
            wrapper.Transform = authority =>
            {
                var options = authority.Options.Select(option =>
                {
                    if (option.OptionId != optionId) return option;
                    ChangedReads++;
                    var source = XElement.Parse(option.SourceNodeXml);
                    // A different current source remains internally coherent and
                    // selectable, but cannot authorize the already reviewed source.
                    source.SetElementValue("cost", option.UnitCost + 1m);
                    string sourceXml = source.ToString(SaveOptions.DisableFormatting);
                    var changed = option with { UnitCost = option.UnitCost + 1m,
                        SourceNodeXml = sourceXml,
                        SourceNodeDigest = CharacterCreationLoadoutRules.ComputeSourceNodeDigest(sourceXml) };
                    return changed with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(changed) };
                }).ToArray();
                var changedAuthority = authority with { Options = options,
                    SourceDigest = CharacterCreationLoadoutRules.Compute(options) };
                return changedAuthority with
                    { AuthorityDigest = CharacterCreationLoadoutRules.ComputeAuthorityDigest(changedAuthority) };
            };
            return proxy;
        }
    }

    public class AssignmentSourceProxy : DispatchProxy
    {
        public ICharacterSourceDataContext Inner { get; set; } = null!;
        public Func<CharacterCreationLoadoutAuthority, CharacterCreationLoadoutAuthority> Transform { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name == nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutAuthority)
                && result is true && args is { Length: 1 } && args[0] is CharacterCreationLoadoutAuthority authority)
                args[0] = Transform(authority);
            return result;
        }
    }

    private sealed record Fixture(WorkspaceStoredDocument Original,
        CharacterCreationCompletionReviewInputs Inputs, CharacterCreationCompletionReviewRequest Request,
        CharacterCreationCompletionBootstrapSnapshot Bootstrap, CharacterCreationStartingCashChoice CashChoice,
        CharacterCreationLoadoutCatalogOption Weapon, CharacterCreationLoadoutCatalogOption Armor);

    private sealed class AtomicReplacementProbe : IFileWorkspaceStoreFaultInjector
    {
        public int Replacements { get; private set; }
        public string? TargetPath { get; private set; }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != FileWorkspaceStoreFaultStage.AfterTargetReplaced) return;
            Replacements++;
            TargetPath = targetPath;
        }
    }

    private static XmlLifeModulesCatalogService ContinuationCatalog()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
        }
        throw new DirectoryNotFoundException("Canonical Life Modules data is missing.");
    }

    private static string[] NativeSkillParentIds(string xml)
    {
        var root = XDocument.Parse(xml).Root!.Elements("newskills").Single();
        return root.Element("skills")!.Elements("skill").Concat(root.Element("knoskills")!.Elements("skill"))
            .Select(row => row.Element("guid")!.Value)
            .Concat(root.Element("groups")!.Elements("group").Select(row => row.Element("id")!.Value)).ToArray();
    }

    private static Fixture Capture(ReadyContext context, bool includeKarmaPurchasePolicy = false)
    {
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context, includeKarmaPurchasePolicy);
        var loadout = CharacterCreationLoadoutCompletionService.LoadFromObservation(original, context.Resolver, true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loadout.Outcome, string.Join(",", loadout.Blockers));
        Assert.IsNotNull(loadout.Value);
        var weapon = loadout.Value.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
                && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var armor = loadout.Value.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Armor
                && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        inputs = inputs with { Schema = CharacterCreationCompletionReviewSchemas.InputsV2, Loadout = loadout.Value };
        inputs = inputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) };
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { Loadout = new([new(weapon.OptionId)], [new(armor.OptionId)]),
                SkillKarmaPurchases = includeKarmaPurchasePolicy ? [] : null };
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var selected = choices.Lifestyles.Single(row => row.LifestyleId == choices.StartingLifestyleId);
        var lifestyle = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == selected.BaseLifestyleOptionId);
        Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(lifestyle.SourceId, out var cash));
        Assert.IsNotNull(cash);
        return new(original, inputs, new(inputs.SnapshotDigest, choices), bootstrap,
            new(cash.AuthorityDigest, cash.Dice * 3), weapon, armor);
    }
}
