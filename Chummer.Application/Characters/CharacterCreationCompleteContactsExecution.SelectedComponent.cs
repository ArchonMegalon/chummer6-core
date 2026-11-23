using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteContactsExecution
{
    // A component assessment is not a replay/commit of a Complete proposal. Own
    // the real None replay and the separate component operation in one private
    // lifetime; callers cannot attach copied public results to this execution.
    internal sealed class SelectedComponentReplay
    {
        private readonly WorkspaceStoredDocument original;
        private readonly CharacterCreationCompletionBootstrapSnapshot bootstrap;
        private readonly CharacterCreationSelectedCompletionWitness witness;
        private readonly CharacterCreationCompletionReviewRequest replayRequest;
        private readonly CharacterCreationCompletionReviewRequest componentRequest;
        private readonly CharacterCreationCompleteContactsExecution? contacts;
        private readonly string binding;

        internal CharacterCreationSelectedCompletionEvaluation Replay { get; }
        // This is a charged component result, never a new replay or witness.
        internal CharacterCreationSelectedCompletionEvaluation Result { get; }
        internal CharacterCreationCompletionOriginalSelections Baseline { get; }
        internal IReadOnlyList<CharacterContactRuleEffect> Effects { get; }
        internal CharacterCreationCompleteAssessment Authority { get; }
        internal CharacterCreationCompleteRuleSourceExecution SourceExecution { get; }
        internal CharacterCreationOriginalCharacterSource OriginalSource { get; }

        private SelectedComponentReplay(WorkspaceStoredDocument original,
            CharacterCreationCompletionBootstrapSnapshot bootstrap,
            CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest replayRequest,
            CharacterCreationCompletionReviewRequest componentRequest,
            CharacterCreationSelectedCompletionEvaluation replay,
            CharacterCreationSelectedCompletionEvaluation result,
            CharacterCreationCompletionOriginalSelections baseline,
            IReadOnlyList<CharacterContactRuleEffect> effects,
            CharacterCreationCompleteAssessment authority,
            CharacterCreationCompleteRuleSourceExecution sourceExecution,
            CharacterCreationOriginalCharacterSource originalSource,
            CharacterCreationCompleteContactsExecution? contacts)
        {
            this.original = original;
            this.bootstrap = bootstrap;
            this.witness = witness;
            this.replayRequest = replayRequest;
            this.componentRequest = componentRequest;
            Replay = replay;
            Result = result;
            Baseline = baseline;
            Effects = effects;
            Authority = authority;
            SourceExecution = sourceExecution;
            OriginalSource = originalSource;
            this.contacts = contacts;
            binding = Binding();
        }

        internal static SelectedComponentReplay? Create(WorkspaceStoredDocument original,
            CharacterCreationCompletionBootstrapSnapshot bootstrap,
            CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest replayRequest,
            CharacterCreationCompletionReviewRequest componentRequest,
            CharacterCreationRuleSourceExecutionContext sources, out string blocker,
            CancellationToken cancellationToken = default)
        {
            blocker = CharacterCreationSelectedCompletionEvaluator.Invalid;
            cancellationToken.ThrowIfCancellationRequested();
            // Only the policy-coverage demand may differ. No schema, selection,
            // history, source capture, or Essence identity is substituted.
            if (witness.PolicyRequirement != CharacterCreationPolicyRequirement.None
                || replayRequest.PolicyRequirement != CharacterCreationPolicyRequirement.None
                || componentRequest.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
                || !Same(replayRequest, componentRequest with { PolicyRequirement = CharacterCreationPolicyRequirement.None })
                || !CharacterCreationSelectedCompletionEvaluator.IsValidEnvelope(witness, replayRequest)) return null;

            string OriginBinding() => CharacterCreationFinalizationDigest.Compute(new
            {
                Original = original, Bootstrap = bootstrap, Witness = witness,
                ReplayRequest = replayRequest, ComponentRequest = componentRequest
            });
            string originBinding = OriginBinding();

            var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness,
                replayRequest, cancellationToken, sources);
            if (replay.Outcome != CharacterCreationFoundationOutcomes.Success || replay.Value is null
                || replay.Blockers.Count != 0)
            {
                blocker = string.Join(",", replay.Blockers);
                return null;
            }
            if (originBinding != OriginBinding() || replay.Value.WitnessDigest != witness.WitnessDigest) return null;
            if (!CharacterCreationSelectedCompletionEvaluator.TryReadOriginal(original, bootstrap, witness,
                    out var baseline, out var effects)
                || baseline is null || effects is null
                || !CharacterCreationOriginalCharacterSource.TryFromSelected(original, bootstrap, witness,
                    replayRequest, out var originalSource, cancellationToken)
                || originalSource is null) return null;
            var execution = sources.Resolve(original, componentRequest, cancellationToken);
            if (execution is null || execution.Inputs.SnapshotDigest != witness.ProvenanceInputsSnapshotDigest
                || execution.Inputs.RuleSourceCaptureDigest != replayRequest.RuleSourceCaptureDigest) return null;
            var authority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness,
                componentRequest, baseline, effects, execution);
            if (!execution.ApplyRetainedMagicSelected1221(witness, replay.Value, authority,
                    original.Document.AuxiliaryState.CharacterCreationLoadoutDraft?.Instances,
                    originalSource, cancellationToken, out var priced, out blocker)) return null;

            // Reexecute selected contacts under this exact component authority.
            // The ordinary FromSelected proposal/witness guard remains intact.
            // There is no full-route contact proof or supplied result to borrow.
            var contacts = authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
                ? PriceSelected(witness, componentRequest, priced, baseline, effects, authority, cancellationToken)
                : null;
            if (authority.Schema == CharacterCreationCompleteAssessment.SchemaV4 && contacts is null)
            {
                blocker = CharacterCreationSelectedCompletionEvaluator.Invalid;
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (originBinding != OriginBinding()) return null;
            blocker = string.Empty;
            return new(original, bootstrap, witness, replayRequest, componentRequest,
                replay.Value, priced, baseline, effects, authority, execution, originalSource, contacts);
        }

        internal CharacterCreationCompleteContactsExecution? ContactsFor(
            CharacterCreationSelectedCompletionWitness candidateWitness,
            CharacterCreationCompletionReviewRequest request,
            CharacterCreationSelectedCompletionEvaluation result,
            CharacterCreationCompletionOriginalSelections baseline,
            IReadOnlyList<CharacterContactRuleEffect> effects,
            CharacterCreationCompleteAssessment authority,
            CharacterCreationCompleteRuleSourceExecution? execution)
            => ReferenceEquals(candidateWitness, witness) && ReferenceEquals(request, componentRequest)
                && ReferenceEquals(result, Result) && ReferenceEquals(baseline, Baseline)
                && ReferenceEquals(effects, Effects) && ReferenceEquals(authority, Authority)
                && ReferenceEquals(execution, SourceExecution) && binding == Binding()
                    ? contacts : null;

        private string Binding() => CharacterCreationFinalizationDigest.Compute(new
        {
            Original = original, Bootstrap = bootstrap, Witness = witness,
            ReplayRequest = replayRequest, ComponentRequest = componentRequest,
            Replay, Result, Baseline, Effects, Authority, SourceExecution.Inputs
        });
    }
}
