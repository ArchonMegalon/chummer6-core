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
    public void Typed_loadout_full_review_selected_replay_and_cold_archive_preserve_real_graph_and_one_budget()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Capture(context);
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

        // Claim this exact completed, history-rich local runner using the actual
        // account handoff, then transfer it to a fresh installation of that account.
        // Do not replace it with a smaller linked prerequisite or trim its archive.
        var owner = new OwnerScope("completion-loadout-linked-owner");
        owners.Set(owner);
        var linkedStamp = owners.Capture();
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
        Assert.AreEqual(CharacterCreationCompletionAuthority.SchemaV3, transferredAuthority.Schema);
        Assert.IsNotNull(transferredAuthority.SelectedReplay);
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

    private static Fixture Capture(ReadyContext context)
    {
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
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
            { Loadout = new([new(weapon.OptionId)], [new(armor.OptionId)]) };
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
