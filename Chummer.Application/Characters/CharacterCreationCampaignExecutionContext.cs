using System.Diagnostics;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using E = Chummer.Contracts.Characters.CharacterCreationCompleteEvaluability;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using R = Chummer.Contracts.Characters.CharacterCreationCompleteReason;

namespace Chummer.Application.Characters;

/// <summary>
/// Trusted composition seam, like the owner/source adapters. Implementations must
/// observe authenticated durable authority, never translate client assertions into
/// approval. This interface and its result are in-process services, not endpoints.
/// No interface implementation is selected from request JSON.
/// </summary>
public interface ICharacterCreationCampaignAuthority
{
    CharacterCreationCampaignObservation Observe(
        CharacterCreationCampaignPhase phase, CancellationToken cancellationToken);
}

public sealed record CharacterCreationCampaignObservation(
    CharacterCreationCampaignState State,
    CharacterCreationCampaignBinding? Binding,
    CharacterCreationCampaignFailure Failure)
{
    public override string ToString() => nameof(CharacterCreationCampaignObservation);
}

/// <summary>
/// A live phase or a strictly replay-only committed observation. New operations
/// reconstruct their adapter and Capture again. Private history cannot enter live
/// evaluation or Apply. Neither mode supplies owner/persistence authority.
/// </summary>
public sealed class CharacterCreationCampaignExecutionContext
{
    public const string Blocker = "creation-complete-campaign-authority-unavailable";
    private static readonly ActivitySource Activities = new("Chummer.Application.CharacterCreation.Campaign");
    private readonly ICharacterCreationCampaignAuthority authority;
    private readonly CharacterCreationCampaignObservation captured;
    // A history context can reproduce one committed receipt, never admit a new
    // review, plan or Apply. Only the private store recovery visitor supplies it.
    internal bool IsCommittedReplay { get; private init; }
    public CharacterCreationCampaignPhase Phase { get; }
    public CharacterCreationCampaignBinding? Binding => captured.Binding;

    private CharacterCreationCampaignExecutionContext(ICharacterCreationCampaignAuthority authority,
        CharacterCreationCampaignPhase phase, CharacterCreationCampaignObservation captured)
    {
        this.authority = authority;
        this.captured = captured;
        Phase = phase;
    }

    public static CharacterCreationCampaignExecutionContext Capture(
        ICharacterCreationCampaignAuthority authority, CharacterCreationCampaignPhase phase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        return new(authority, phase, Read(authority, phase, cancellationToken));
    }

    /// <summary>Live contexts reread authority; committed contexts only compare
    /// the receipt-bound observation recovered during this store operation.</summary>
    public CharacterCreationCampaignObservation Revalidate(
        CharacterCreationCampaignBinding? expected, CancellationToken cancellationToken = default)
    {
        using var activity = Activities.StartActivity("campaign.revalidate");
        activity?.SetTag("campaign.phase", Phase.ToString());
        activity?.SetTag("campaign.scope", IsCommittedReplay ? "committed-history" : "live");
        cancellationToken.ThrowIfCancellationRequested();
        var current = IsCommittedReplay ? captured : Read(authority, Phase, cancellationToken);
        if (current.State is CharacterCreationCampaignState.Unavailable or CharacterCreationCampaignState.Denied)
            return Finish(current);
        if (!ValidBinding(expected) || captured.Binding != expected || current.Binding != expected)
            return Finish(new(CharacterCreationCampaignState.Denied, expected, CharacterCreationCampaignFailure.BindingChanged));
        if (current.State != captured.State || current.Failure != captured.Failure)
            return Finish(new(CharacterCreationCampaignState.Denied, expected, CharacterCreationCampaignFailure.BindingChanged));
        return Finish(current);

        CharacterCreationCampaignObservation Finish(CharacterCreationCampaignObservation result)
        {
            activity?.SetTag("campaign.state", result.State.ToString());
            activity?.SetTag("campaign.failure", result.Failure.ToString());
            return result;
        }
    }

    internal static CharacterCreationCampaignExecutionContext RecoverCommitted(
        CharacterCreationCommittedCampaign committed)
    {
        var observation = new CharacterCreationCampaignObservation(
            committed.State, committed.Binding, CharacterCreationCampaignFailure.None);
        return new(new HistoricalObservation(observation), CharacterCreationCampaignPhase.Replay, observation)
            { IsCommittedReplay = true };
    }

    private sealed class HistoricalObservation(CharacterCreationCampaignObservation observation)
        : ICharacterCreationCampaignAuthority
    {
        public CharacterCreationCampaignObservation Observe(
            CharacterCreationCampaignPhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return observation;
        }
    }

    internal Execution? Execute(CharacterCreationCompletionReviewRequest request,
        string inputsDigest, string rawXmlDigest, string policyDigest,
        CancellationToken cancellationToken, bool committedReplay = false)
    {
        if (IsCommittedReplay && !committedReplay) return null;
        var expected = request.CampaignAuthority;
        if (request.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || request.PolicyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1
            || request.PolicyRequirement is not (CharacterCreationPolicyRequirement.None or CharacterCreationPolicyRequirement.Complete)
            || expected is null || !ValidBinding(expected)
            || expected.RetainedIdentity.InputsDigest != inputsDigest
            || expected.RetainedIdentity.RawCharacterXmlDigest != rawXmlDigest
            || expected.RetainedIdentity.PolicyDigest != policyDigest
            || request.InputsSnapshotDigest != inputsDigest || request.EffectivePolicyDigest != policyDigest)
            return null;
        var observation = Revalidate(expected, cancellationToken);
        return new Execution(this, observation, CharacterCreationFinalizationDigest.Compute(request));
    }

    /// <summary>
    /// Live Apply and independent archive reconstruction must compare the actual
    /// owner and ORIGINAL workspace. Never substitute the result workspace or
    /// rewrite a historical observation to the current owner's identity.
    /// Call again inside the store's existing final precommit admission callback.
    /// </summary>
    public bool AuthorizesOriginal(string ownerId, WorkspaceStoredDocument original,
        CharacterCreationCampaignBinding? expected, CharacterCreationCampaignPhase requiredPhase,
        CancellationToken cancellationToken = default)
    {
        if (IsCommittedReplay && requiredPhase != CharacterCreationCampaignPhase.Replay
            || Phase != requiredPhase || expected is null || expected.RetainedIdentity is not { } identity
            || identity.OwnerId != ownerId || identity.WorkspaceId != original.Id
            || identity.ContentRevision != original.ContentRevision || identity.SavedRevision != original.SavedRevision
            || identity.RawCharacterXmlDigest != CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content)
            || identity.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest)
            return false;
        return Admitted(Revalidate(expected, cancellationToken));
    }

    public bool AuthorizesApplyBinding(string ownerId, CharacterCreationFinalizationBinding original,
        CharacterCreationCampaignBinding? expected, CancellationToken cancellationToken = default)
    {
        if (IsCommittedReplay || Phase != CharacterCreationCampaignPhase.Apply || expected is null
            || expected.RetainedIdentity is not { } identity || identity.OwnerId != ownerId
            || identity.WorkspaceId != original.WorkspaceId || identity.ContentRevision != original.ContentRevision
            || identity.SavedRevision != original.SavedRevision || identity.RawCharacterXmlDigest != original.RawCharacterXmlDigest
            || identity.AuxiliaryStateDigest != original.AuxiliaryStateDigest)
            return false;
        return Admitted(Revalidate(expected, cancellationToken));
    }

    // Called only by the atomic store after successful live TryBuild. The result
    // belongs to its private manifest, not the portable receipt/request envelope.
    public CharacterCreationCommittedCampaign CaptureCommitted(
        CharacterCreationFinalizationReceipt receipt, CancellationToken cancellationToken = default)
    {
        var current = Revalidate(Binding, cancellationToken);
        if (IsCommittedReplay || Phase != CharacterCreationCampaignPhase.Apply || !Admitted(current)
            || Binding is not { } binding || receipt.WorkspaceId != binding.RetainedIdentity.WorkspaceId
            || receipt.PreviousContentRevision != binding.RetainedIdentity.ContentRevision
            || receipt.PreviousSavedRevision != binding.RetainedIdentity.SavedRevision
            || receipt.PreviousRawCharacterXmlDigest != binding.RetainedIdentity.RawCharacterXmlDigest
            || receipt.PreviousAuxiliaryStateDigest != binding.RetainedIdentity.AuxiliaryStateDigest
            || receipt.ReceiptDigest != CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt))
            throw new InvalidOperationException(Blocker);
        return new(CharacterCreationCommittedCampaign.SchemaV1, binding, current.State, receipt.ReceiptDigest);
    }

    // Draft commits retain their own exact private observation. This overload
    // does not project cash, finalization, or a Career receipt.
    public CharacterCreationCommittedCampaign CaptureCommitted(
        CharacterCreationCompletionDraftReceipt receipt, CancellationToken cancellationToken = default)
    {
        var current = Revalidate(Binding, cancellationToken);
        if (IsCommittedReplay || Phase != CharacterCreationCampaignPhase.Apply || !Admitted(current)
            || Binding is not { } binding
            || receipt.WorkspaceId != binding.RetainedIdentity.WorkspaceId
            || receipt.PreviousContentRevision != binding.RetainedIdentity.ContentRevision
            || receipt.PreviousSavedRevision != binding.RetainedIdentity.SavedRevision
            || receipt.PreviousRawCharacterXmlDigest != binding.RetainedIdentity.RawCharacterXmlDigest
            || receipt.PreviousAuxiliaryStateDigest != binding.RetainedIdentity.AuxiliaryStateDigest
            || receipt.ReceiptDigest != CharacterCreationCompletionDraftRules.ReceiptDigest(receipt))
            throw new InvalidOperationException(Blocker);
        return new(CharacterCreationCommittedCampaign.SchemaV1, binding, current.State, receipt.ReceiptDigest);
    }

    internal static bool ValidBinding(CharacterCreationCampaignBinding? binding)
        => binding is { Schema: CharacterCreationCampaignBinding.SchemaV1, RetainedIdentity: { } identity }
            && !string.IsNullOrWhiteSpace(identity.OwnerId) && identity.OwnerId.Length <= 512
            && !string.IsNullOrWhiteSpace(identity.WorkspaceId.Value) && identity.WorkspaceId.Value.Length <= 512
            && identity.ContentRevision > 0 && identity.SavedRevision >= 0
            && identity.SavedRevision <= identity.ContentRevision
            && CharacterCreationFinalizationDigest.IsCanonical(identity.InputsDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(identity.PolicyDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(identity.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(identity.SourceDigest)
            // Continuation codec identities are bare SHA-256, unlike the
            // creation digests. Preserve both original encodings verbatim.
            && identity.ContinuationDigest is { Length: 64 }
            && identity.ContinuationDigest.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f')
            && CharacterCreationFinalizationDigest.IsCanonical(identity.ObservationDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.TransitionDigest);

    private static bool Admitted(CharacterCreationCampaignObservation observation)
        => observation.Failure == CharacterCreationCampaignFailure.None
            && observation.State is CharacterCreationCampaignState.AuthenticatedAbsence
                or CharacterCreationCampaignState.AdmittedUnchangedPolicy;

    private static CharacterCreationCampaignObservation Read(ICharacterCreationCampaignAuthority authority,
        CharacterCreationCampaignPhase phase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = Activities.StartActivity("campaign.observe");
        CharacterCreationCampaignObservation result;
        try
        {
            result = authority.Observe(phase, cancellationToken);
            if (result is null || !Enum.IsDefined(result.State) || !Enum.IsDefined(result.Failure)
                || Admitted(result) && !ValidBinding(result.Binding)
                || result.State is CharacterCreationCampaignState.Denied or CharacterCreationCampaignState.Unavailable
                    && result.Failure == CharacterCreationCampaignFailure.None)
                result = new(CharacterCreationCampaignState.Unavailable, null, CharacterCreationCampaignFailure.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // No exception text crosses the seam. Cancellation not requested by
            // this operation is an unavailable adapter, not a successful read.
            result = new(CharacterCreationCampaignState.Unavailable, null, CharacterCreationCampaignFailure.Unavailable);
        }
        cancellationToken.ThrowIfCancellationRequested();
        activity?.SetTag("campaign.phase", phase.ToString());
        activity?.SetTag("campaign.state", result.State.ToString());
        activity?.SetTag("campaign.failure", result.Failure.ToString());
        return result;
    }

    public override string ToString() => nameof(CharacterCreationCampaignExecutionContext);

    // Only Execute creates this capability. No serialized boolean/hash/result can.
    internal sealed class Execution
    {
        private readonly CharacterCreationCampaignExecutionContext context;
        private readonly CharacterCreationCampaignObservation observation;
        private readonly string requestDigest;

        internal Execution(CharacterCreationCampaignExecutionContext context,
            CharacterCreationCampaignObservation observation, string requestDigest)
        { this.context = context; this.observation = observation; this.requestDigest = requestDigest; }

        internal CharacterCreationCompleteAssessment Project(CharacterCreationCompleteAssessment assessment,
            CancellationToken cancellationToken)
        {
            var binding = observation.Binding;
            var current = context.Revalidate(binding, cancellationToken);
            bool admitted = binding is { } pinned && Admitted(current) && current == observation
                && assessment.Schema == CharacterCreationCompleteAssessment.SchemaV4
                && assessment.RequestDigest == requestDigest
                && pinned.RetainedIdentity.InputsDigest == assessment.InputsSnapshotDigest
                && pinned.RetainedIdentity.RawCharacterXmlDigest == assessment.RawCharacterXmlDigest
                && pinned.RetainedIdentity.PolicyDigest == assessment.PolicyDigest;
            // Verify both exact rows exist, without filtering away any obligation.
            if (assessment.Obligations.Count(IsCampaign) != 2
                || assessment.Obligations.Count(row => IsCampaign(row) && row.Kind == K.ConsumerOperation) != 1
                || assessment.Obligations.Count(row => IsCampaign(row) && row.Kind == K.ActiveGraph) != 1)
                throw new InvalidOperationException(Blocker);
            bool coverage = assessment.Phase == CharacterCreationCompletePhase.Coverage;
            bool evaluated = admitted && coverage && assessment.MechanicalSucceeded == true
                && assessment.ConsumptionDigest is not null;
            string digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCampaignBinding.SchemaV1,
                Binding = binding, current.State, current.Failure, requestDigest
            });
            return assessment with
            {
                Obligations = assessment.Obligations.Select(row => !IsCampaign(row) ? row : row with
                {
                    Evaluability = admitted ? E.Supported
                        : current.State == CharacterCreationCampaignState.Denied ? E.Unsupported : E.AuthorityUnavailable,
                    Coverage = !coverage ? C.Pending : !evaluated ? C.Unresolved
                        : current.State == CharacterCreationCampaignState.AuthenticatedAbsence ? C.ProvenInactive : C.Evaluated,
                    Reason = evaluated ? R.CanonicalEvaluation
                        : admitted ? R.CandidateEvaluationRequired : R.CampaignAuthorityUnavailable,
                    AuthorityDigest = digest,
                    EvaluationDigest = evaluated ? CharacterCreationFinalizationDigest.Compute(new
                    {
                        Authority = digest, assessment.InputsSnapshotDigest, assessment.RawCharacterXmlDigest,
                        assessment.SelectedInputsDigest, assessment.SelectionsDigest, assessment.ConsumptionDigest
                    }) : null
                }).ToArray()
            };
        }

        private static bool IsCampaign(CharacterCreationCompleteObligation row)
            => row.Operation == CharacterCreationConsumerPolicyOperation.Campaign
                && (row.Kind == K.ConsumerOperation && row.Id == "consumer:Campaign"
                    || row.Kind == K.ActiveGraph && row.Id == "graph:Campaign");
    }
}
