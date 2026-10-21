using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Synchronous, stack-scoped permission for the unchanged full/selected evaluators.
// No public constructor, serialization hook, global cache or historical live admission.
// The two live call sites are exclusively inside the owner-leased rebase workflow.
internal static class CharacterCreationAttributesReplacementExecution
{
    [ThreadStatic] private static Scope? current;
    // Internal synchronous test observation only. It cannot supply authorization.
    [ThreadStatic] private static Action<WorkspaceStoredDocument, CharacterCreationReservation>? reservationComparisonObserved;
    internal static Action<WorkspaceStoredDocument, CharacterCreationReservation>? ReservationComparisonObserved
    {
        get => reservationComparisonObserved;
        set => reservationComparisonObserved = value;
    }

    internal static IDisposable EnterLive(IOwnerContextLease lease, OwnerContextStamp owner,
        WorkspaceStoredDocument original, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request)
    {
        if (lease.Stamp != owner || request.AttributesReplacement is not { } intent
            || !MatchesOriginal(intent, original)
            || !RetainsRecordedChoices(request, original)
            || intent.OwnerScopeDigest != CharacterCreationFinalizationDigest.Compute(owner.Owner)
            || intent.OwnerStampDigest != CharacterCreationFinalizationDigest.Compute(owner)
            || !Validate(inputs.Skills, inputs.SnapshotDigest, request))
            throw new InvalidOperationException(CharacterCreationAttributesReplacementIntent.Invalid);
        return new Scope(request, true, lease, original.LocalHistory?.CreationReservation);
    }

    // Called only inside the canonical Draft ledger replay, after receipt/digest,
    // prefix/original-state, archive and historical source/campaign checks.
    // This validates recorded intent; it never produces live permission.
    internal static bool TryEnterReplay(WorkspaceStoredDocument original,
        CharacterCreationCompletionDraftReview review, CharacterCreationSelectedCompletionWitness witness,
        out IDisposable? scope)
    {
        scope = null;
        var request = review.Review.Request;
        if (request.AttributesReplacement is { } intent
            && (!CharacterCreationSelectedCompletionEvaluator.IsValidEnvelope(witness, request)
                || !MatchesOriginal(intent, original)
                || !RetainsRecordedChoices(request, original)
                || intent.OwnerScopeDigest != review.OwnerScopeDigest
                || intent.OwnerStampDigest != review.OwnerStampDigest
                || !Validate(witness.Skills, witness.ProvenanceInputsSnapshotDigest, request))) return false;
        // Mask even an absent intent so nested old history cannot borrow a live scope.
        scope = new Scope(request, false, null);
        return true;
    }

    internal static bool AuthorizesReview(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, bool committedReplay)
        => request.AttributesReplacement is null || current is { } execution
            && execution.Live != committedReplay && execution.Matches(request)
            && Validate(inputs.Skills, inputs.SnapshotDigest, request);

    internal static bool AuthorizesCommit(OwnerScope owner, WorkspaceStoredDocument original,
        CharacterCreationCompletionDraftApplyRequest command)
    {
        var request = command.Review.Review.Request;
        if (request.AttributesReplacement is null) return true;
        if (current is not { Live: true, Lease: { } lease } execution
            || !execution.Matches(request) || request.AttributesReplacement is not { } intent
            || lease.Stamp.Owner != owner
            || intent.OwnerScopeDigest != CharacterCreationFinalizationDigest.Compute(owner)
            || intent.OwnerStampDigest != CharacterCreationFinalizationDigest.Compute(lease.Stamp)
            || intent.OwnerScopeDigest != command.Review.OwnerScopeDigest
            || intent.OwnerStampDigest != command.Review.OwnerStampDigest
            || !MatchesOriginal(intent, original)) return false;
        if (execution.CreationReservation is not { } reservation) return true;
        ReservationComparisonObserved?.Invoke(original, reservation);
        return original.LocalHistory?.CreationReservation == reservation
            && original.LocalHistory.IncarnationId == reservation.IncarnationId;
    }

    internal static bool AuthorizesPreservation(CharacterCreationSkillsCompletionInputs skills,
        CharacterCreationCompletionSelections proposed)
        => current is { Request.AttributesReplacement: not null } execution
            && Same(execution.Request.Selections, proposed)
            && Validate(skills, execution.Request.InputsSnapshotDigest, execution.Request);

    private static bool Validate(CharacterCreationSkillsCompletionInputs skills, string snapshot,
        CharacterCreationCompletionReviewRequest request)
    {
        if (request.AttributesReplacement is not { Schema: CharacterCreationAttributesReplacementIntent.SchemaV1 } intent
            || request.RuleSourceExecutionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            || request.OriginalCharacterExecutionSchema != CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
            || request.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || request.PolicyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1
            || request.CampaignAuthority is null
            || !CharacterCreationFinalizationDigest.IsCanonical(intent.OwnerScopeDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(intent.OwnerStampDigest)
            || skills?.CompositeBaseline?.Selections is not { } original
            || skills.Attributes?.Attributes is not { } rows || request.Selections is not { } proposed
            || !Same(intent.Original, skills.Attributes.Binding)
            || intent.InputsSnapshotDigest != snapshot || request.InputsSnapshotDigest != snapshot
            || intent.OriginalSelectionsDigest != CharacterCreationFinalizationDigest.Compute(original)
            || intent.TargetSelectionsDigest != CharacterCreationFinalizationDigest.Compute(proposed)
            || intent.Targets is null || intent.Targets.Count != rows.Count
            || intent.Targets.Any(row => row is null || row.PriorityPoints < 0 || row.KarmaLevels < 0)
            || !intent.Targets.Select(row => row.AttributeId).SequenceEqual(
                rows.Select(row => row.AttributeId).OrderBy(id => id, StringComparer.Ordinal))
            || intent.Targets.Select(row => row.AttributeId).Distinct(StringComparer.Ordinal).Count() != rows.Count
            || !Same(intent.Targets, proposed.AttributeAllocations)) return false;

        // The current full graph decides prices and legal ranks. This only permits
        // the exact requested replacement and proves all other choices are unchanged.
        if (!Same(original, proposed with
            {
                AttributeAllocations = original.AttributeAllocations,
                PlayerChosenAttributeIds = original.PlayerChosenAttributeIds
            })) return false;
        var previous = original.PlayerChosenAttributeIds ?? skills.Attributes.PendingDraft?.PlayerChosenAttributeIds;
        var chosen = proposed.PlayerChosenAttributeIds;
        if (chosen is not null
            && (chosen.Any(id => !rows.Any(row => row.AttributeId == id && row.IsEnabled))
                || chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Count
                || !chosen.SequenceEqual(chosen.OrderBy(id => id, StringComparer.Ordinal)))) return false;
        return previous is null || chosen is not null && previous.All(chosen.Contains);
    }

    private static bool RetainsRecordedChoices(CharacterCreationCompletionReviewRequest request, WorkspaceStoredDocument original)
        => original.Document.AuxiliaryState.CharacterCreationAttributesDraft?.PlayerChosenAttributeIds is not { } chosen
            || request.Selections.PlayerChosenAttributeIds is { } target && chosen.All(target.Contains);

    private static bool MatchesOriginal(CharacterCreationAttributesReplacementIntent intent, WorkspaceStoredDocument original)
        => intent.Original is { } binding && binding.WorkspaceId == original.Id
            && binding.ContentRevision == original.ContentRevision && binding.SavedRevision == original.SavedRevision
            && binding.RawCharacterXmlDigest == CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content)
            && binding.AuxiliaryStateDigest == original.Document.AuxiliaryStateDigest;

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private sealed class Scope : IDisposable
    {
        private readonly Scope? previous;
        private readonly string requestDigest;
        private bool disposed;
        internal CharacterCreationCompletionReviewRequest Request { get; }
        internal bool Live { get; }
        internal IOwnerContextLease? Lease { get; }
        internal CharacterCreationReservation? CreationReservation { get; }

        internal Scope(CharacterCreationCompletionReviewRequest request, bool live, IOwnerContextLease? lease, CharacterCreationReservation? creationReservation = null)
        {
            Request = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(request))!;
            requestDigest = CharacterCreationFinalizationDigest.Compute(Request);
            Live = live;
            Lease = lease;
            CreationReservation = creationReservation;
            previous = current;
            current = this;
        }

        internal bool Matches(CharacterCreationCompletionReviewRequest request)
            => !disposed && requestDigest == CharacterCreationFinalizationDigest.Compute(request);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current = previous;
        }
    }
}
