using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners550 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void CampaignDraft550_real_nonlocal_owner_bootstrap_review_apply_and_cold_retry()
    {
        using var context = ReputationContext414(preserved: false);
        string directory = Path.Combine(context.Directory, "campaign550-linked");
        var (_, owners, id) = CharacterCreationCompletionApplyTests.CreateLinkedPrerequisite(context, directory);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        var prepared = PrepareDraft550(context, store, owners, workspaceId: id);
        var stamp = owners.Capture();
        Assert.IsFalse(stamp.Owner.IsLocalSingleUser);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(stamp, prepared.Command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)));
        prepared.Authority.State = CharacterCreationCampaignState.Unavailable;
        prepared.Authority.Failure = CharacterCreationCampaignFailure.Unavailable;
        int reads = prepared.Authority.Reads;
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        Assert.IsFalse(cold.Get(id).Success);
        Assert.IsFalse(cold.Get(new OwnerScope("campaign550-foreign"), id).Success);
        Assert.IsTrue(cold.Get(stamp.Owner, id).Success);
        Assert.AreEqual(receipt, Success425(DraftService550(context, cold, owners).ApplyDraft(stamp, prepared.Command)));
        Assert.AreEqual(reads, prepared.Authority.Reads);
        Assert.AreEqual(prepared.Original.Document.Content, cold.Get(stamp.Owner, id).Value!.Document.Content);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(CharacterCreationCampaignState.AuthenticatedAbsence)]
    [DataRow(CharacterCreationCampaignState.AdmittedUnchangedPolicy)]
    public void CampaignDraft550_native_service_commits_only_creation_choices(CharacterCreationCampaignState state)
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new AllocationOwners550();
        var prepared = PrepareDraft550(context, store, owners, state);
        IOwnerBoundCharacterCreationCompletionService service = DraftService550(context, store, owners);
        string xml = prepared.Original.Document.Content;
        byte[] original = File.ReadAllBytes(Record418(context));
        var proposal = prepared.Command.Review.Review.Request;
        Assert.IsNull(service.ReviewDraft(owners.Capture(), context.WorkspaceId, proposal).Value);
        foreach (var phase in new[] { CharacterCreationCampaignPhase.Preparation, CharacterCreationCampaignPhase.Apply,
                     CharacterCreationCampaignPhase.Replay })
            Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId, proposal,
                CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, phase)).Value);
        Assert.IsNull(service.ApplyDraft(owners.Capture(), prepared.Command).Value);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Record418(context)));

        var receipt = Success425(service.ApplyDraftWithCampaign(owners.Capture(), prepared.Command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = cold.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(xml, saved.Document.Content); // No dice, nuyen rewrite, or created=true.
        Assert.AreEqual(prepared.Original.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts);
        Assert.AreEqual(receipt, saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Receipt);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CampaignDraft550_denied_or_revoked_fresh_apply_does_not_replace(bool afterFlush)
    {
        using var context = ReputationContext414(preserved: false);
        CampaignAuthority518? authority = null;
        void Revoke()
        {
            authority!.State = CharacterCreationCampaignState.Denied;
            authority.Failure = CharacterCreationCampaignFailure.Denied;
        }
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, Revoke);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var owners = new AllocationOwners550();
        var prepared = PrepareDraft550(context, store, owners);
        authority = prepared.Authority;
        var apply = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Apply);
        byte[] original = File.ReadAllBytes(Record418(context));
        if (!afterFlush) Revoke();
        var result = DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), prepared.Command, apply);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.AreEqual(afterFlush, fault.Called);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Record418(context)));
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void CampaignDraft550_cold_exact_retry_precedes_unavailable_permission_and_changed_commands_fail()
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new AllocationOwners550();
        var prepared = PrepareDraft550(context, store, owners);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(),
            prepared.Command, CharacterCreationCampaignExecutionContext.Capture(prepared.Authority,
                CharacterCreationCampaignPhase.Apply)));
        prepared.Authority.State = CharacterCreationCampaignState.Unavailable;
        prepared.Authority.Failure = CharacterCreationCampaignFailure.Unavailable;
        var unavailable = CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply);
        int reads = prepared.Authority.Reads;
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        byte[] committed = File.ReadAllBytes(Record418(context));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = DraftService550(context, cold, owners);
        var stamp = owners.Capture();
        Assert.AreEqual(receipt, Success425(service.ApplyDraftWithCampaign(stamp, prepared.Command, unavailable)));
        Assert.AreEqual(receipt, Success425(service.ApplyDraft(stamp, prepared.Command)));
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(prepared.Command);
        Assert.AreEqual(receipt, Success425(service.LookupDraft(stamp, lookup)));
        Assert.AreEqual(reads, prepared.Authority.Reads);

        var changed = prepared.Command.Review with
        {
            Binding = prepared.Command.Review.Binding with
            { ContentRevision = prepared.Command.Review.Binding.ContentRevision + 1,
                SavedRevision = prepared.Command.Review.Binding.SavedRevision + 1 }
        };
        changed = changed with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(changed) };
        var conflict = service.ApplyDraftWithCampaign(stamp, prepared.Command with { Review = changed }, unavailable);
        CollectionAssert.Contains(conflict.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        Assert.IsNull(service.LookupDraft(stamp, lookup with { CommandDigest = Hash518("changed-command") }).Value);
        Assert.AreEqual(reads, prepared.Authority.Reads);
        Assert.IsNull(service.ApplyDraftWithCampaign(stamp,
            prepared.Command with { IdempotencyKey = "campaign550-new-command" }, unavailable).Value);

        owners.Set(new OwnerScope("campaign550-other-owner"));
        owners.Set(stamp.Owner);
        Assert.IsNull(service.ApplyDraftWithCampaign(stamp, prepared.Command, unavailable).Value);
        Assert.IsNull(service.ApplyDraftWithCampaign(owners.Capture(), prepared.Command, unavailable).Value);
        Assert.IsNull(service.LookupDraft(stamp, lookup).Value);
        Assert.AreEqual(receipt, Success425(service.LookupDraft(owners.Capture(), lookup)));
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("receipt")]
    [DataRow("owner")]
    [DataRow("transition")]
    [DataRow("state")]
    [DataRow("source")]
    public void CampaignDraft550_cold_private_record_damage_fails_closed(string damage)
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new AllocationOwners550();
        var prepared = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), prepared.Command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply)));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = DraftService550(context, cold, owners);
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(prepared.Command);
        Success425(service.LookupDraft(owners.Capture(), lookup)); // Warm cache cannot repair missing private authority.
        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        var manifest = record["RetainedRuleSources"]!;
        var history = manifest["CommittedDraftCampaigns"]![0]!;
        if (damage == "missing") manifest.AsObject().Remove("CommittedDraftCampaigns");
        if (damage == "receipt") history["ReceiptDigest"] = Hash518("different-receipt");
        if (damage == "owner") history["Binding"]!["RetainedIdentity"]!["OwnerId"] = "different-owner";
        if (damage == "transition") history["Binding"]!["TransitionDigest"] = Hash518("different-transition");
        if (damage == "state") history["State"] = (int)CharacterCreationCampaignState.Denied;
        if (damage == "source")
        {
            string artifact = Artifact418(context);
            byte[] bytes = File.ReadAllBytes(artifact);
            bytes[^1] ^= 1;
            File.WriteAllBytes(artifact, bytes);
        }
        else File.WriteAllText(Record418(context), record.ToJsonString());
        byte[] broken = File.ReadAllBytes(Record418(context));
        Assert.IsFalse(cold.Get(context.WorkspaceId).Success);
        Assert.IsNull(service.LookupDraft(owners.Capture(), lookup).Value);
        Assert.IsNull(service.ApplyDraft(owners.Capture(), prepared.Command).Value);
        Assert.IsFalse(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
        CollectionAssert.AreEqual(broken, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void CampaignDraft550_successive_drafts_and_Career_keep_private_history_without_granting_live_permission()
    {
        using var context = ReputationContext414(preserved: false);
        var owners = new AllocationOwners550();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var second = PrepareDraft550(context, cold, owners);
        ExerciseFreshHistory588(context, cold, owners);
        // Successful preparation must not leave private-history admission on
        // the call stack. Detached inputs alone cannot replay the saved Draft.
        var detached = Success425(new OwnerBoundCharacterCreationCompletionReader(
            cold, owners, context.Resolver, context.Queries)
            .LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId)));
        Assert.IsNotNull(detached.Inputs.CompositeBaseline);
        Assert.AreEqual(second.Command.Review.Review.Request.InputsSnapshotDigest, detached.Inputs.SnapshotDigest);
        Assert.AreEqual(detached.Inputs.SnapshotDigest,
            CharacterCreationCompletionReviewRules.ComputeInputsDigest(detached.Inputs));
        Assert.IsTrue(CharacterCreationCompletionReviewService.HasCoherentInputs(detached.Inputs));
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(detached.Inputs, out _));
        var detachedReview = CharacterCreationCompletionReviewService.Review(
            detached.Inputs, second.Command.Review.Review.Request, ruleSources: detached.Execution,
            campaign: CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Preparation));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, detachedReview.Outcome);
        Assert.IsNull(detachedReview.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.SnapshotConflict },
            detachedReview.Blockers.ToArray());
        var service = DraftService550(context, cold, owners);
        var history = Manifest418(context)["CommittedDraftCampaigns"]![0]!.Deserialize<CharacterCreationCommittedCampaign>()!;
        string firstHistory = Manifest418(context)["CommittedDraftCampaigns"]![0]!.ToJsonString();
        var historical = CharacterCreationCampaignExecutionContext.RecoverCommitted(history);
        byte[] beforeHistoricalRequests = File.ReadAllBytes(Record418(context));
        Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
            second.Command.Review.Review.Request, historical).Value);
        Assert.IsNull(service.ApplyDraftWithCampaign(owners.Capture(), second.Command, historical).Value);
        CollectionAssert.AreEqual(beforeHistoricalRequests, File.ReadAllBytes(Record418(context)));
        var secondReceipt = Success425(service.ApplyDraftWithCampaign(owners.Capture(), second.Command,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)));
        Assert.AreEqual(2, Manifest418(context)["CommittedDraftCampaigns"]!.AsArray().Count);
        Assert.AreEqual(firstHistory, Manifest418(context)["CommittedDraftCampaigns"]![0]!.ToJsonString());
        string draftHistory = Manifest418(context)["CommittedDraftCampaigns"]!.ToJsonString();

        // Existing Career path must still work after campaign-bound draft history.
        var career = Prepare518(context, cold);
        Finish518(context, cold, career);
        var finalStore = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsTrue(finalStore.Get(context.WorkspaceId).Success);
        Assert.AreEqual(2, Manifest418(context)["CommittedDraftCampaigns"]!.AsArray().Count);
        Assert.IsNotNull(Manifest418(context)["CommittedCampaign"]);
        Assert.AreEqual(draftHistory, Manifest418(context)["CommittedDraftCampaigns"]!.ToJsonString());
        Assert.AreEqual(receipt, Success425(DraftService550(context, finalStore, owners).LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(first.Command))));
        Assert.AreEqual(secondReceipt, Success425(DraftService550(context, finalStore, owners).LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(second.Command))));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void CampaignDraft550_cancelled_acknowledgement_recovers_once()
    {
        using var context = ReputationContext414(preserved: false);
        using var cancellation = new CancellationTokenSource();
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTargetReplaced, () =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var owners = new AllocationOwners550();
        var prepared = PrepareDraft550(context, store, owners);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), prepared.Command,
            CharacterCreationCampaignExecutionContext.Capture(prepared.Authority, CharacterCreationCampaignPhase.Apply), cancellation.Token));
        Assert.IsTrue(fault.Called);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.AreEqual(receipt, Success425(DraftService550(context, cold, owners).ApplyDraft(owners.Capture(), prepared.Command)));
        Assert.AreEqual(prepared.Original.ContentRevision + 1, cold.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    // Private source-bearing history uses the complete private closure carrier.
    // Keep the fresh/detached lifetime checks between the two real Draft commits.
    private static void ExerciseFreshHistory588(ReadyContext context, FileWorkspaceStore store, AllocationOwners550 owners)
    {
        string? data = null;
        for (DirectoryInfo? directory = new(AppDomain.CurrentDomain.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(candidate)) { data = candidate; break; }
        }
        Assert.IsNotNull(data);
        var lifeModules = new Chummer.Infrastructure.Xml.XmlLifeModulesCatalogService(data);
        var stored = store.Get(context.WorkspaceId);
        Assert.IsTrue(stored.Success, $"stage=history588-store-read;outcome={stored.Outcome}");
        byte[] record = File.ReadAllBytes(Record418(context));
        var exported = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        byte[] bytes = exported.ExportPrivateBytes();
        var importedClosure = WorkspacePrivateRuleSourceClosure.Import(bytes, exported.Binding, 4 * 1024 * 1024, context.Queries);
        Assert.AreEqual(stored.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Count,
            importedClosure.Binding.CampaignCount);
        CharacterCreationCompletionReviewInputs? detached = null;
        CharacterCreationRuleSourceExecutionContext? retainedSources = null;
        CharacterCreationCompletionReviewRequest? proposal = null;
        CampaignAuthority518? authority = null;
        int evaluations = 0;
        bool Evaluate(CancellationToken ct = default, Action? inside = null)
            => importedClosure.EvaluateCompletionInputs(owners, owners.Capture(), context.Resolver, context.Queries,
                lifeModules, CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, (fresh, sources, closureBinding) =>
                {
                    Assert.IsTrue(fresh.Success, string.Join(",", fresh.AuthorityBlockers));
                    Assert.IsNotNull(sources);
                    detached = fresh.Inputs!;
                    retainedSources = sources;
                    Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(detached, out _));
                    var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                        fresh.Identity!, Hash518("history588-current"));
                    authority = new CampaignAuthority518(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
                    var request = Request(detached, CharacterCreationPolicyRequirement.None);
                    proposal = request with
                    {
                        Selections = detached.CompositeBaseline!.Selections,
                        CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
                        OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
                        RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
                        RuleSourceCaptureDigest = detached.RuleSourceCaptureDigest,
                        CampaignAuthority = binding
                    };
                    Success425(CharacterCreationCompletionReviewService.Review(detached, proposal, ct, sources,
                        CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation, ct)));
                    evaluations++;
                    inside?.Invoke();
                    return true;
                }, ct);
        void DetachedMustFail()
        {
            Assert.AreEqual(0, owners.ActiveLeases);
            Assert.IsNotNull(detached);
            Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(detached!, out _));
            var imported = JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(JsonSerializer.Serialize(detached))!;
            Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(imported, out _));
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(imported, proposal!, ruleSources: retainedSources,
                campaign: CharacterCreationCampaignExecutionContext.Capture(authority!, CharacterCreationCampaignPhase.Preparation)).Value);
        }
        Assert.IsTrue(Evaluate()); // fresh preparation
        DetachedMustFail();
        Assert.IsTrue(Evaluate()); // independently fresh returned-advice evaluation
        DetachedMustFail();
        Assert.ThrowsExactly<InvalidOperationException>(() => Evaluate(inside: () => throw new InvalidOperationException("history588")));
        DetachedMustFail();
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => Evaluate(cancel.Token, () => cancel.Cancel()));
        DetachedMustFail();
        int before = evaluations;
        Assert.ThrowsExactly<OperationCanceledException>(() => Evaluate(cancel.Token));
        Assert.AreEqual(before, evaluations);
        // The private carrier redacts argument failures to InvalidOperationException.
        bool asyncCalled = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => importedClosure.EvaluateCompletionInputs(
            owners, owners.Capture(), context.Resolver, context.Queries, lifeModules,
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (_, _, _) => { asyncCalled = true; return Task.FromResult(true); }));
        Assert.IsFalse(asyncCalled);
        DetachedMustFail();
        Assert.IsTrue(Evaluate());
        DetachedMustFail();
        Assert.IsTrue(record.AsSpan().SequenceEqual(File.ReadAllBytes(Record418(context))),
            "stage=history588-observation;outcome=record-changed");
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
    }

    private static IOwnerBoundCharacterCreationCompletionService DraftService550(
        ReadyContext context, FileWorkspaceStore store, AllocationOwners550 owners)
        => new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);

    // Uses the existing canonical V4/None fixture. Whole-character Complete
    // remains blocked by its existing unsupported obligations.
    private static PreparedDraft550 PrepareDraft550(ReadyContext context, FileWorkspaceStore store, AllocationOwners550 owners,
        CharacterCreationCampaignState state = CharacterCreationCampaignState.AdmittedUnchangedPolicy,
        CharacterWorkspaceId? workspaceId = null, bool sourcebookSelection = false)
    {
        var id = workspaceId ?? context.WorkspaceId;
        var owner = owners.Capture().Owner;
        WorkspaceStoredDocument original;
        CampaignAuthority518 authority;
        CharacterCreationCompletionReviewRequest proposal;
        // Setup needs fresh store-backed history admission for its direct pure
        // Review. Dispose before invoking the owner-bound handler under test.
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
            var loaded = sourcebookSelection
                ? Success425(reader.LoadCompletionInputsWithSourcebookSelectionExecution(
                    owners.Capture(), new(id), includeLoadout: true))
                : Success425(reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(id)));
            var originalRead = owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id);
            Assert.IsNotNull(originalRead.Value, originalRead.Error);
            original = originalRead.Value!;
            var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                new(owners.Capture().Owner.Value, original.Id, original.ContentRevision, original.SavedRevision,
                    Hash518("draft550-continuation")[7..], loaded.Inputs.RuleSourceCaptureDigest!,
                    loaded.Inputs.Skills.Binding.RawCharacterXmlDigest, original.Document.AuxiliaryStateDigest,
                    loaded.Inputs.SnapshotDigest, loaded.Inputs.EffectivePolicy!.PolicyDigest, Hash518("draft550-observation")),
                Hash518("draft550-transition-" + original.ContentRevision));
            authority = new CampaignAuthority518(binding, state);
            var inputs = loaded.Inputs;
            var request = Request(inputs, CharacterCreationPolicyRequirement.None);
            if (workspaceId is not null)
            {
                // The shared linked-owner fixture stops at prerequisites. Preserve
                // its existing completion helper's native-language seed.
                var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
                request = request with { Selections = request.Selections with
                    { SkillAllocations = [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)] } };
            }
            proposal = request with
            {
                Selections = inputs.CompositeBaseline?.Selections
                    ?? (sourcebookSelection ? request.Selections : SeedOwnerSelections425(inputs, request.Selections)),
                CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
                OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
                RuleSourceExecutionSchema = sourcebookSelection
                    ? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
                    : CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
                RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
                SourcebookSelection = loaded.Inputs.SourcebookSelection,
                CampaignAuthority = binding
            };
            var preparation = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation);
            Success425(CharacterCreationCompletionReviewService.Review(loaded.Inputs, proposal,
                ruleSources: loaded.Execution, campaign: preparation));
        }
        var review = Success425(DraftService550(context, store, owners).ReviewDraftWithCampaign(owners.Capture(),
            id, proposal, CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Review)));
        var afterRead = owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id);
        Assert.IsNotNull(afterRead.Value, afterRead.Error);
        var after = afterRead.Value!;
        Assert.AreEqual(original.ContentRevision, after.ContentRevision);
        Assert.AreEqual(original.Document.Content, after.Document.Content);
        Assert.AreEqual(original.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        return new(original, authority, new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, review,
            "campaign550-draft-" + original.ContentRevision, true));
    }

    private sealed record PreparedDraft550(WorkspaceStoredDocument Original, CampaignAuthority518 Authority,
        CharacterCreationCompletionDraftApplyRequest Command);
}
