using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationCampaignState.AuthenticatedAbsence, CharacterCreationCompleteCoverage.ProvenInactive)]
    [DataRow(CharacterCreationCampaignState.AdmittedUnchangedPolicy, CharacterCreationCompleteCoverage.Evaluated)]
    public void Campaign512_actual_execution_covers_both_rows_and_reconstructs_cold(
        CharacterCreationCampaignState state, CharacterCreationCompleteCoverage expected)
    {
        using var fixture = ReadyContext.Create(false);
        var inputs = LoadConsumption(fixture);
        var original = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var binding = CampaignBinding512(inputs, original);
        var source = new CampaignAuthority512(binding, state);
        var execution = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Review);
        var request = Request386(inputs) with { CampaignAuthority = binding };
        var legacy = CharacterCreationCompletionReviewService.Review(inputs, Request386(inputs));
        var full = CharacterCreationCompletionReviewService.Review(inputs, request, campaign: execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var assessment = full.Value!.CompleteAssessment!;
        var rows = CampaignRows512(assessment);
        Assert.AreEqual(2, rows.Length);
        Assert.IsTrue(rows.All(row => row.Coverage == expected && row.EvaluationDigest is not null));
        CollectionAssert.AreEqual(legacy.Value!.CompleteAssessment!.Obligations.Select(row => row.Id).ToArray(),
            assessment.Obligations.Select(row => row.Id).ToArray());
        Assert.IsFalse(assessment.SatisfiesComplete); // The other Complete gaps are retained.
        foreach (var row in assessment.Obligations.Except(rows))
        {
            var previous = legacy.Value.CompleteAssessment.Obligations.Single(item => item.Id == row.Id);
            Assert.AreEqual(previous.Evaluability, row.Evaluability, row.Id);
            Assert.AreEqual(previous.Coverage, row.Coverage, row.Id);
        }

        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            fixture.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request, campaign: execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var copiedRequest = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(request))!;
        var withoutAuthority = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, copiedRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, withoutAuthority.Outcome);
        var reconstructed = CharacterCreationCampaignExecutionContext.Capture(
            new CampaignAuthority512(binding, state), CharacterCreationCampaignPhase.Replay);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, copiedRequest, campaign: reconstructed);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(assessment), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));
        Assert.IsTrue(source.Reads >= 4); // Capture, review entry/end and selected reconstruction all read.
    }

    [TestMethod]
    public void Campaign512_prepare_rechecks_and_retains_non_campaign_blockers()
    {
        using var fixture = ReadyContext.Create(false);
        var inputs = LoadConsumption(fixture);
        var binding = CampaignBinding512(inputs, fixture.Store.Get(fixture.WorkspaceId).Value!);
        var source = new CampaignAuthority512(binding, CharacterCreationCampaignState.AuthenticatedAbsence);
        var preparation = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Preparation);
        var request = Request386(inputs) with
        { PolicyRequirement = CharacterCreationPolicyRequirement.Complete, CampaignAuthority = binding };
        var result = CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request, campaign: preparation);
        Assert.IsNotNull(result.Value);
        Assert.IsTrue(CampaignRows512(result.Value).All(row => row.Evaluability == CharacterCreationCompleteEvaluability.Supported));
        Assert.IsFalse(result.Value.MayRequestCompleteCandidate);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request).Value);
        var wrongPhase = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Apply);
        Assert.IsNull(CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request, campaign: wrongPhase).Value);
    }

    [TestMethod]
    [DataRow("denied")]
    [DataRow("unavailable")]
    [DataRow("revoked")]
    [DataRow("aba")]
    [DataRow("retained")]
    [DataRow("owner")]
    public void Campaign512_changed_or_missing_live_authority_cannot_discharge_rows(string mutation)
    {
        using var fixture = ReadyContext.Create(false);
        var inputs = LoadConsumption(fixture);
        var original = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var binding = CampaignBinding512(inputs, original);
        var source = new CampaignAuthority512(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
        var execution = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Review);
        var request = Request386(inputs) with { CampaignAuthority = binding };
        switch (mutation)
        {
            case "denied":
            case "revoked": source.State = CharacterCreationCampaignState.Denied; source.Failure = CharacterCreationCampaignFailure.Denied; break;
            case "unavailable": source.State = CharacterCreationCampaignState.Unavailable; source.Failure = CharacterCreationCampaignFailure.Unavailable; break;
            case "aba": source.Binding = binding with { TransitionDigest = Hash512("A-B-A-new-transition") }; break;
            case "retained": source.Binding = binding with { RetainedIdentity = binding.RetainedIdentity with { InputsDigest = Hash512("new-inputs") } }; break;
            case "owner": source.Binding = binding with { RetainedIdentity = binding.RetainedIdentity with { OwnerId = "other-owner" } }; break;
        }
        var result = CharacterCreationCompletionReviewService.Review(inputs, request, campaign: execution);
        Assert.IsNotNull(result.Value); // Diagnostic None remains diagnostic, never Complete admission.
        Assert.IsTrue(CampaignRows512(result.Value.CompleteAssessment!).All(row =>
            row.Coverage == CharacterCreationCompleteCoverage.Unresolved && row.EvaluationDigest is null));
        if (mutation == "unavailable")
            Assert.IsTrue(CampaignRows512(result.Value.CompleteAssessment!).All(row =>
                row.Evaluability == CharacterCreationCompleteEvaluability.AuthorityUnavailable));
        var replay = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Replay);
        Assert.IsFalse(replay.AuthorizesOriginal(binding.RetainedIdentity.OwnerId, original, binding, CharacterCreationCampaignPhase.Replay));
    }

    [TestMethod]
    public void Campaign512_revocation_during_evaluation_and_cross_owner_apply_fail_closed()
    {
        using var fixture = ReadyContext.Create(false);
        var inputs = LoadConsumption(fixture);
        var original = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var binding = CampaignBinding512(inputs, original);
        var source = new CampaignAuthority512(binding, CharacterCreationCampaignState.AuthenticatedAbsence);
        var review = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Review);
        source.OnRead = count => { if (count >= 3) { source.State = CharacterCreationCampaignState.Denied; source.Failure = CharacterCreationCampaignFailure.Denied; } };
        var result = CharacterCreationCompletionReviewService.Review(inputs,
            Request386(inputs) with { CampaignAuthority = binding }, campaign: review);
        Assert.IsTrue(CampaignRows512(result.Value!.CompleteAssessment!).All(row =>
            row.Coverage == CharacterCreationCompleteCoverage.Unresolved));
        var applySource = new CampaignAuthority512(binding, CharacterCreationCampaignState.AuthenticatedAbsence);
        var apply = CharacterCreationCampaignExecutionContext.Capture(applySource, CharacterCreationCampaignPhase.Apply);
        Assert.IsTrue(apply.AuthorizesOriginal("campaign512-owner", original, binding, CharacterCreationCampaignPhase.Apply));
        Assert.IsFalse(apply.AuthorizesOriginal("foreign-owner", original, binding, CharacterCreationCampaignPhase.Apply));
        applySource.Binding = binding with { TransitionDigest = Hash512("revoke-regrant") };
        Assert.IsFalse(apply.AuthorizesOriginal("campaign512-owner", original, binding, CharacterCreationCampaignPhase.Apply));
    }

    [TestMethod]
    public void Campaign512_forged_receipt_and_deserialized_binding_do_not_create_authority()
    {
        using var fixture = ReadyContext.Create(false);
        var inputs = LoadConsumption(fixture);
        var original = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var binding = CampaignBinding512(inputs, original);
        var request = Request386(inputs) with { CampaignAuthority = binding };
        var copy = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(request))!;
        Assert.AreEqual(binding, copy.CampaignAuthority);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, copy).Value);
        var source = new CampaignAuthority512(binding, CharacterCreationCampaignState.AuthenticatedAbsence);
        var execution = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Review);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            fixture.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request, campaign: execution);
        Assert.IsNotNull(captured.Value);
        var forged = captured.Value with
        {
            CompleteAssessment = captured.Value.CompleteAssessment! with
            {
                Obligations = captured.Value.CompleteAssessment!.Obligations.Select(row =>
                    row.Id is "consumer:Campaign" or "graph:Campaign"
                        ? row with { EvaluationDigest = Hash512("forged-approval") } : row).ToArray()
            }
        };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        var cold = JsonSerializer.Deserialize<CharacterCreationSelectedCompletionWitness>(JsonSerializer.Serialize(forged))!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, cold, copy).Outcome);
        var reconstructed = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Replay);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, cold, copy, campaign: reconstructed).Outcome);
    }

    private static string Hash512(string value) => CharacterCreationFinalizationDigest.ComputeUtf8(value);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Campaign512_actual_store_precommit_revalidates_and_cold_read_uses_committed_history(bool revokeAfterFlush)
    {
        using var fixture = ReadyContext.Create(false);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = LoadConsumption(fixture);
        var original = fixture.Store.Get(fixture.WorkspaceId).Value!;
        var binding = CampaignBinding512(inputs, original);
        binding = binding with { RetainedIdentity = binding.RetainedIdentity with { OwnerId = owners.Capture().Owner.Value } };
        var source = new CampaignAuthority512(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
        var fault = new CampaignWriteFault512(() =>
        {
            if (revokeAfterFlush)
            {
                source.State = CharacterCreationCampaignState.Denied;
                source.Failure = CharacterCreationCampaignFailure.Denied;
            }
        });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(fixture.Directory, fixture.Queries, fault);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, fixture.Resolver, fixture.Queries);
        // Diagnostic V4/None exercises the existing atomic pipeline while the
        // OTHER Complete gaps remain blocked. This is not a positive Finish test.
        var request = Request386(inputs) with { CampaignAuthority = binding };
        var reviewAuthority = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Review);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCashWithCampaign(stamp, fixture.WorkspaceId, request, reviewAuthority);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var choice = new CharacterCreationStartingCashChoice(cash.Value!.AuthorityDigest, cash.Value.Dice * 3);
        var review = service.ReviewWithCampaign(stamp, fixture.WorkspaceId, request, choice, reviewAuthority);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var projection = review.Value!.Projection;
        var command = new CharacterCreationCompletionApplyRequest(projection.Binding, request,
            projection.ReviewDigest, choice, projection.ProjectionDigest, "campaign512-apply", true);
        var applyAuthority = CharacterCreationCampaignExecutionContext.Capture(source, CharacterCreationCampaignPhase.Apply);
        var applied = service.ApplyWithCampaign(stamp, command, applyAuthority);
        Assert.AreEqual(1, fault.Flushed);
        if (revokeAfterFlush)
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, applied.Outcome);
            Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(store.Get(fixture.WorkspaceId).Value));
            return;
        }
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(original.ContentRevision, applied.Value!.PreviousContentRevision);
        Assert.AreEqual(binding, applyAuthority.Binding);
        Assert.IsTrue(store.Get(fixture.WorkspaceId).Success);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(fixture.Directory, fixture.Queries);
        // The account-scoped overload deliberately rejects the reserved local
        // owner, even for the trusted singleton. Do not weaken that boundary.
        Assert.IsFalse(cold.Get(stamp.Owner, fixture.WorkspaceId).Success);
        Assert.IsTrue(cold.Get(fixture.WorkspaceId).Success); // The private history is not live approval.
    }

    private sealed class CampaignWriteFault512(Action afterFlush) : IFileWorkspaceStoreFaultInjector
    {
        internal int Flushed;
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
            Flushed++;
            afterFlush();
        }
    }

    private static CharacterCreationCompleteObligation[] CampaignRows512(CharacterCreationCompleteAssessment value)
        => value.Obligations.Where(row => row.Id is "consumer:Campaign" or "graph:Campaign").ToArray();

    private static CharacterCreationCampaignBinding CampaignBinding512(CharacterCreationCompletionReviewInputs inputs,
        Chummer.Application.Workspaces.WorkspaceStoredDocument original)
        => new(CharacterCreationCampaignBinding.SchemaV1,
            new("campaign512-owner", original.Id, original.ContentRevision, original.SavedRevision,
                Hash512("retained-continuation")[7..], Hash512("retained-sources"),
                inputs.Skills.Binding.RawCharacterXmlDigest, original.Document.AuxiliaryStateDigest,
                inputs.SnapshotDigest, inputs.EffectivePolicy!.PolicyDigest, Hash512("retained-observation")),
            Hash512("durable-transition-one"));

    // Trusted test adapter; synthetic identities here test Core's execution seam.
    // The Hub companion tests exercise the real ObserveCompletion checks.
    private sealed class CampaignAuthority512(CharacterCreationCampaignBinding binding, CharacterCreationCampaignState state)
        : ICharacterCreationCampaignAuthority
    {
        internal CharacterCreationCampaignBinding Binding = binding;
        internal CharacterCreationCampaignState State = state;
        internal CharacterCreationCampaignFailure Failure = CharacterCreationCampaignFailure.None;
        internal Action<int>? OnRead;
        internal int Reads;
        public CharacterCreationCampaignObservation Observe(CharacterCreationCampaignPhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            OnRead?.Invoke(Reads);
            return new(State, Binding, Failure);
        }
    }
}
