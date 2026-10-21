using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationCampaignState.AuthenticatedAbsence)]
    [DataRow(CharacterCreationCampaignState.AdmittedUnchangedPolicy)]
    public void Campaign518_source_backed_finalization_cold_local_read_and_existing_Career_writer(
        CharacterCreationCampaignState initialState)
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store, initialState);
        var receipt = Finish518(context, store, prepared);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = cold.Get(context.WorkspaceId);
        Assert.IsTrue(first.Success);
        var saved = first.Value!;
        Assert.IsTrue(bool.Parse(XDocument.Parse(saved.Document.Content).Root!.Element("created")!.Value));
        Assert.AreEqual(receipt.ContentRevision, saved.ContentRevision);
        Assert.AreEqual(prepared.Original.ContentRevision, receipt.PreviousContentRevision);
        string archive = JsonSerializer.Serialize(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        string manifest = Manifest418(context).ToJsonString();

        // Leaving a campaign (or changing its policy) affects new suggestions,
        // not ownership of the already committed runner.
        prepared.Authority.State = CharacterCreationCampaignState.Denied;
        prepared.Authority.Failure = CharacterCreationCampaignFailure.Denied;
        int reads = prepared.Authority.Reads;
        Assert.IsTrue(cold.Get(context.WorkspaceId).Success);
        var career = new WorkspaceCharacterCareerReputationService(cold, context.Resolver);
        var preview = career.Preview(new(context.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(2, 1, 3), "campaign518-career"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        var careerCommand = preview.Preview!.Command with { ExplicitlyConfirmed = true };
        var changed = career.Commit(careerCommand);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, changed.Outcome, changed.Error);
        Assert.AreEqual(reads, prepared.Authority.Reads);

        // Remove live creation sources. Reopen from an independent real store,
        // using its retained typed source capture and committed campaign history.
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var reopenedStore = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = reopenedStore.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success);
        var root = XDocument.Parse(reopened.Value!.Document.Content).Root!;
        Assert.AreEqual(changed.Receipt!.Quote.After.Inputs.StreetCred.ToString(),
            root.Element("streetcred")!.Value);
        Assert.AreEqual(changed.Receipt.Quote.After.Inputs.Notoriety.ToString(),
            root.Element("notoriety")!.Value);
        Assert.AreEqual(changed.Receipt.Quote.After.Inputs.PublicAwareness.ToString(),
            root.Element("publicawareness")!.Value);
        Assert.AreEqual(receipt.ContentRevision + 1, reopened.Value.ContentRevision);
        Assert.AreEqual(reopened.Value.ContentRevision, reopened.Value.SavedRevision);
        Assert.AreEqual(archive, JsonSerializer.Serialize(reopened.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive));
        Assert.AreEqual(manifest, Manifest418(context).ToJsonString());
        Assert.AreEqual(prepared.Original.Document.Content,
            reopened.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!.RawCharacterXml);
        Assert.AreEqual(prepared.Binding, reopened.Value.Document.AuxiliaryState
            .CharacterCreationFinalizationArchive!.CompletionAuthority!.Proposal.CampaignAuthority);
        Assert.AreEqual(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(
            reopened.Value.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt));
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed,
            new WorkspaceCharacterCareerReputationService(reopenedStore, context.Resolver).Commit(careerCommand).Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Campaign518_post_replace_diagnostic_fault_and_exact_retry_do_not_reapply_or_reauthorize(bool loseAcknowledgement)
    {
        using var context = ReputationContext414(preserved: false);
        CampaignAuthority518? authority = null;
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTargetReplaced, () =>
        {
            if (!loseAcknowledgement) return;
            authority!.State = CharacterCreationCampaignState.Denied;
            authority.Failure = CharacterCreationCampaignFailure.Denied;
            // The store deliberately swallows diagnostic-hook exceptions after
            // the atomic rename. This is not an escaping persistence failure.
            throw new IOException("Synthetic post-replacement diagnostic fault.");
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var prepared = Prepare518(context, store);
        authority = prepared.Authority;
        var receipt = Finish518(context, store, prepared);
        Assert.IsTrue(fault.Called);
        authority.State = CharacterCreationCampaignState.Denied;
        authority.Failure = CharacterCreationCampaignFailure.Denied;
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        byte[] committed = File.ReadAllBytes(Record418(context));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, prepared.Owners, context.Resolver, context.Queries);
        // Exercise existing public Apply, with no campaign context at all.
        var replay = service.Apply(prepared.Owners.Capture(), prepared.Command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(replay.Value));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            service.Apply(prepared.Owners.Capture(), prepared.Command with { IdempotencyKey = "new-operation" }).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            service.Apply(prepared.Owners.Capture(), prepared.Command with { ProjectionDigest = Hash518("forged-command") }).Outcome);
        Assert.IsFalse(cold.Get(new OwnerScope("campaign518-other-owner"), context.WorkspaceId).Success);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            cold.CommitCompletion(new OwnerScope("campaign518-other-owner"), prepared.Command,
                context.Resolver, context.Queries).Outcome);
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void Campaign518_cancellation_after_replace_reconciles_the_committed_receipt()
    {
        using var context = ReputationContext414(preserved: false);
        using var cancellation = new CancellationTokenSource();
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTargetReplaced, () =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var prepared = Prepare518(context, store);
        var apply = CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply);
        var result = store.CommitCompletion(prepared.Owners.Capture().Owner, prepared.Command,
            context.Resolver, context.Queries, apply, cancellation.Token);
        Assert.IsTrue(fault.Called);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.AreEqual(result.Value!.ReceiptDigest, cold.Get(context.WorkspaceId).Value!.Document
            .AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt.ReceiptDigest);
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("policy")]
    [DataRow("aba")]
    [DataRow("owner")]
    public void Campaign518_new_apply_still_checks_live_authority_after_temp_flush(string change)
    {
        using var context = ReputationContext414(preserved: false);
        CampaignAuthority518? authority = null;
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, () =>
        {
            if (change == "revoked")
            {
                authority!.State = CharacterCreationCampaignState.Denied;
                authority.Failure = CharacterCreationCampaignFailure.Denied;
            }
            else if (change == "owner")
                authority!.Binding = authority.Binding with
                { RetainedIdentity = authority.Binding.RetainedIdentity with { OwnerId = "foreign-owner" } };
            else
                authority!.Binding = authority.Binding with { TransitionDigest = Hash518(change + "-new-transition") };
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var prepared = Prepare518(context, store);
        authority = prepared.Authority;
        byte[] original = File.ReadAllBytes(Record418(context));
        var apply = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Apply);
        var rejected = store.CommitCompletion(prepared.Owners.Capture().Owner, prepared.Command,
            context.Resolver, context.Queries, apply);
        Assert.IsTrue(fault.Called);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Record418(context)));
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    [DataRow("receipt")]
    [DataRow("owner")]
    [DataRow("transition")]
    [DataRow("state")]
    public void Campaign518_private_history_mismatch_is_not_repaired_from_public_archive(string change)
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Finish518(context, store, Prepare518(context, store));
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        var history = record["RetainedRuleSources"]!["CommittedCampaign"]!;
        if (change == "receipt") history["ReceiptDigest"] = Hash518("another-receipt");
        else if (change == "owner") history["Binding"]!["RetainedIdentity"]!["OwnerId"] = "foreign-owner";
        else if (change == "transition") history["Binding"]!["TransitionDigest"] = Hash518("another-transition");
        else history["State"] = (int)CharacterCreationCampaignState.Denied;
        File.WriteAllText(Record418(context), record.ToJsonString());
        Assert.IsFalse(store.Get(context.WorkspaceId).Success); // warm store must reread
        Assert.IsFalse(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Campaign518_resealed_public_history_cannot_select_a_different_committed_observation()
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store);
        Finish518(context, store, prepared);
        var saved = store.Get(context.WorkspaceId).Value!;
        var state = saved.Document.AuxiliaryState;
        var archive = state.CharacterCreationFinalizationArchive!;
        var authority = archive.CompletionAuthority!;
        authority = authority with { Proposal = authority.Proposal with
        { CampaignAuthority = prepared.Binding with { TransitionDigest = Hash518("forged-history") } } };
        authority = authority with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(authority) };
        var entry = state.CharacterCreationFinalizationReceipts!.Single();
        var receipt = entry.Receipt with { CompletionAuthorityDigest = authority.AuthorityDigest };
        receipt = receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
        state = state with
        {
            CharacterCreationFinalizationArchive = archive with { CompletionAuthority = authority },
            CharacterCreationFinalizationReceipts = [entry with { Receipt = receipt }]
        };
        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(state);
        File.WriteAllText(Record418(context), record.ToJsonString());
        Assert.IsFalse(store.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Campaign518_current_character_must_keep_finalization_native_identity_and_retained_sources()
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Finish518(context, store, Prepare518(context, store));
        var saved = store.Get(context.WorkspaceId).Value!;
        byte[] before = File.ReadAllBytes(Record418(context));
        var xml = XDocument.Parse(saved.Document.Content);
        foreach (var identity in xml.Root!.Element("newskills")!.Descendants()
            .Where(row => row.Name.LocalName is "guid" or "id"))
            identity.Value = Guid.NewGuid().ToString("D");
        var replacement = saved.Document with { State = saved.Document.State with
        { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        Assert.IsFalse(store.ReplaceWorkspaceDocumentAndCheckpoint(context.WorkspaceId,
            saved.ContentRevision, replacement).Success);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        byte[] source = File.ReadAllBytes(Artifact418(context));
        source[^1] ^= 1;
        File.WriteAllBytes(Artifact418(context), source);
        Assert.IsFalse(store.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Campaign518_committed_history_cannot_authorize_Complete_or_a_new_live_review()
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store);
        var live = CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Review);
        var strict = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            prepared.Command.Proposal with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete },
            ruleSources: prepared.RuleSources, campaign: live);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, strict.Outcome);
        Assert.IsTrue(strict.Blockers.Count > 0);
        foreach (var phase in Enum.GetValues<CharacterCreationCampaignPhase>())
        {
            // Merely requesting the Replay phase on a live adapter is not private
            // committed history and must not enter the internal arithmetic replay.
            var notHistorical = CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, phase);
            var rejected = CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
                prepared.Inputs, prepared.Command.Proposal, prepared.Original,
                prepared.RuleSources, notHistorical, default);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome, phase.ToString());
            Assert.IsNull(rejected.Value);
        }
        Finish518(context, store, prepared);
        var committed = Manifest418(context)["CommittedCampaign"]!.Deserialize<CharacterCreationCommittedCampaign>()!;
        var historical = CharacterCreationCampaignExecutionContext.RecoverCommitted(committed);
        Assert.IsFalse(historical.AuthorizesApplyBinding(prepared.Owners.Capture().Owner.Value,
            prepared.Command.Binding, prepared.Binding));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            prepared.Command.Proposal, ruleSources: prepared.RuleSources, campaign: historical).Value);
        Assert.IsNotNull(Success425(CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
            prepared.Inputs, prepared.Command.Proposal, prepared.Original,
            prepared.RuleSources, historical, default)));
        var differentTransition = prepared.Command.Proposal with
        {
            CampaignAuthority = prepared.Binding with { TransitionDigest = Hash518("different-transition") }
        };
        Assert.IsNull(CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
            prepared.Inputs, differentTransition, prepared.Original, prepared.RuleSources, historical, default).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
            prepared.Inputs, prepared.Command.Proposal,
            prepared.Original with { ContentRevision = prepared.Original.ContentRevision + 1 },
            prepared.RuleSources, historical, default).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.ReplayCommittedCampaign(
            prepared.Inputs, prepared.Command.Proposal,
            prepared.Original with { Id = new CharacterWorkspaceId(Guid.NewGuid().ToString("N")) },
            prepared.RuleSources, historical, default).Value);
    }

    // This source-backed lifecycle is an honest V4/None mechanics fixture. It
    // does NOT claim whole-character Complete while other evaluators are absent.
    // The strict test above must stay blocked until those obligations are real.
    private static Prepared518 Prepare518(ReadyContext context, FileWorkspaceStore store,
        CharacterCreationCampaignState state = CharacterCreationCampaignState.AdmittedUnchangedPolicy)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
        var loaded = Success425(reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId)));
        var original = store.Get(context.WorkspaceId).Value!;
        var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
            new(owners.Capture().Owner.Value, original.Id, original.ContentRevision, original.SavedRevision,
                Hash518("retained-original-continuation")[7..], loaded.Inputs.RuleSourceCaptureDigest!,
                loaded.Inputs.Skills.Binding.RawCharacterXmlDigest, original.Document.AuxiliaryStateDigest,
                loaded.Inputs.SnapshotDigest, loaded.Inputs.EffectivePolicy!.PolicyDigest, Hash518("retained-observation")),
            Hash518("authenticated-transition"));
        var authority = new CampaignAuthority518(binding, state);
        var proposal = OwnerProposal425(context, store, owners) with { CampaignAuthority = binding };
        var live = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Review);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var cash = Success425(service.LoadStartingCashWithCampaign(owners.Capture(), context.WorkspaceId, proposal, live));
        var review = Success425(service.ReviewWithCampaign(owners.Capture(), context.WorkspaceId, proposal,
            new(cash.AuthorityDigest, cash.Dice * 3), live));
        return new(owners, original, binding, authority, loaded.Inputs, loaded.Execution, Command(review));
    }

    private static CharacterCreationFinalizationReceipt Finish518(
        ReadyContext context, FileWorkspaceStore store, Prepared518 prepared)
        => Success425(new OwnerBoundCharacterCreationCompletionService(store, prepared.Owners,
            context.Resolver, context.Queries).ApplyWithCampaign(prepared.Owners.Capture(), prepared.Command,
                CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)));

    private static string Hash518(string text) => CharacterCreationFinalizationDigest.ComputeUtf8(text);

    private sealed record Prepared518(CharacterCreationAttributesServiceTests.AllocationOwners Owners,
        WorkspaceStoredDocument Original, CharacterCreationCampaignBinding Binding,
        CampaignAuthority518 Authority, CharacterCreationCompletionReviewInputs Inputs,
        CharacterCreationRuleSourceExecutionContext RuleSources, CharacterCreationCompletionApplyRequest Command);

    private sealed class CampaignAuthority518(CharacterCreationCampaignBinding binding, CharacterCreationCampaignState state)
        : ICharacterCreationCampaignAuthority
    {
        internal CharacterCreationCampaignBinding Binding = binding;
        internal CharacterCreationCampaignState State = state;
        internal CharacterCreationCampaignFailure Failure;
        internal int Reads;
        public CharacterCreationCampaignObservation Observe(CharacterCreationCampaignPhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return new(State, Binding, Failure);
        }
    }

    private sealed class CampaignFault518(FileWorkspaceStoreFaultStage target, Action action) : IFileWorkspaceStoreFaultInjector
    {
        internal bool Called;
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (Called || stage != target || !targetPath.EndsWith(".json", StringComparison.Ordinal)) return;
            Called = true;
            action();
        }
    }
}
