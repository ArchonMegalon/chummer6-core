using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationMagicResonanceService
{
    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> LoadReReview(
        CharacterCreationMagicResonanceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationMagicResonanceReReviewState>(CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable);
        var loaded = BuildState(workspace);
        if (loaded.Value is not { } state || !TryPrepareReReview(workspace, state, out var binding))
            return Blocked<CharacterCreationMagicResonanceReReviewState>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationMagicResonanceReReviewSchemas.Unavailable);
        var historical = state.PendingDraft!;
        var initial = PreviewReReview(new(binding!, historical.Selections));
        if (initial.Value is null) return new(initial.Outcome, null, initial.Blockers);
        var review = new CharacterCreationMagicResonanceReReviewState(
            CharacterCreationMagicResonanceReReviewSchemas.SnapshotV1,
            binding!, historical, state, initial.Value, string.Empty);
        review = review with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(review) };
        return new(CharacterCreationFoundationOutcomes.Success, review, []);
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewPreview> PreviewReReview(
        CharacterCreationMagicResonanceReReviewPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HasValidReReviewBinding(request.Binding) || !HasReReviewSelections(request.Selections))
            return Blocked<CharacterCreationMagicResonanceReReviewPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceReReviewSchemas.Stale);
        var evaluated = Evaluate(new(request.Binding.Current, request.Selections), request.Binding);
        return evaluated.Result.Value is { } preview
            ? new(evaluated.Result.Outcome, BuildReReviewPreview(request.Binding, preview), evaluated.Result.Blockers)
            : new(evaluated.Result.Outcome, null, evaluated.Result.Blockers);
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> ConfirmReReview(
        CharacterCreationMagicResonanceReReviewConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyReviewedChanges)
            return Blocked<CharacterCreationMagicResonanceReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceReReviewSchemas.ExplicitReviewRequired);
        if (!HasValidReReviewBinding(request.Binding) || !HasReReviewSelections(request.Selections))
            return Blocked<CharacterCreationMagicResonanceReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceReReviewSchemas.Stale);
        // Exact historical command recovery remains available after later saves;
        // a new command still passes fresh owner, binding, rules and atomic CAS.
        return ConfirmCore(new(request.Binding.Current, request.Selections, request.PreviewDigest,
            request.IdempotencyKey, request.ExplicitlyConfirmed), request.Binding);
    }

    private static bool HasReReviewSelections(CharacterCreationMagicResonanceSelections? selections) =>
        selections is not null && selections.AdeptPowers is not null && selections.Spells is not null
        && selections.ComplexForms is not null && selections.AdeptPowers.All(item => item is not null)
        && selections.Spells.All(item => item is not null) && selections.ComplexForms.All(item => item is not null);

    private static bool HasValidReReviewBinding(CharacterCreationMagicResonanceReReviewBinding? binding) =>
        binding is not null && binding.Current is not null
        && binding.Schema == CharacterCreationMagicResonanceReReviewSchemas.BindingV1
        && CharacterCreationMagicResonanceDigest.IsCanonical(binding.HistoricalDraftDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(binding.HistoricalReceiptDigest)
        && CharacterCreationMagicResonanceDigest.IsCanonical(binding.ContextDigest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(binding.ContextDigest,
            CharacterCreationMagicResonanceDigest.Compute(binding with { ContextDigest = string.Empty }));

    private static bool TryPrepareReReview(WorkspaceStoredDocument workspace,
        CharacterCreationMagicResonanceState state, out CharacterCreationMagicResonanceReReviewBinding? binding)
    {
        binding = null;
        var historical = state.PendingDraft;
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts;
        if (historical is null || historical.DraftRevision is <= 0 or long.MaxValue
            || historical.AdeptPowerPointBudget is null || historical.SpellBudget is null
            || historical.ComplexFormBudget is null
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(historical.DraftDigest,
                CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(historical))
            || historical.FinalizationContribution is not { } contribution
            || contribution.AttributesDraftRevision != historical.AttributesDraftRevision
            || contribution.AttributesDraftDigest != historical.AttributesDraftDigest
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(contribution.ContributionDigest,
                CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(contribution))
            || state.PrerequisiteDraft is not { } prerequisite || state.AttributesDraft is not { } attributes
            || state.SelectedTalent is not { } talent
            || !state.Blockers.Contains(CharacterCreationMagicResonanceBlockers.DraftInvalid)
            || state.Blockers.Any(item => item != CharacterCreationMagicResonanceBlockers.DraftInvalid)
            || !CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(state.Authority)
            || historical.AttributesDraftRevision <= 0 || historical.AttributesDraftRevision >= attributes.DraftRevision
            || !CharacterCreationMagicResonanceDigest.IsCanonical(historical.AttributesDraftDigest)
            || CharacterCreationMagicResonanceDigest.EqualsFixedTime(historical.AttributesDraftDigest, attributes.DraftDigest)
            || ledger is not { Count: > 0 }
            || !CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(ledger, workspace.Id, workspace.ContentRevision))
            return false;
        var last = ledger[^1];
        if (last.DraftRevision != historical.DraftRevision
            || last.PreviousContentRevision != historical.BaseContentRevision
            || last.ContentRevision > attributes.BaseContentRevision
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.DraftDigest, historical.DraftDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.AuthorityDigest, historical.AuthorityDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.SourceInputsDigest, historical.SourceInputsDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.CustomDataInputsDigest, historical.CustomDataInputsDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.GmPolicyDigest, historical.GmPolicyDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.RuntimeDigest, historical.RuntimeDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.CommandDigest, historical.LastCommandDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.IdempotencyKeyDigest, historical.LastIdempotencyKeyDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(last.PreviewDigest, historical.LastPreviewDigest)
            || last.TalentKind != historical.TalentKind
            || last.AdeptPowerPointsRemaining != historical.AdeptPowerPointBudget.Remaining
            || last.SpellsRemaining != historical.SpellBudget.Remaining
            || last.ComplexFormsRemaining != historical.ComplexFormBudget.Remaining)
            return false;

        // Comparison only, after authenticating the original draft/receipt and
        // contribution. Project only their old Attributes references to current
        // tokens, then require all stored semantics to equal a fresh evaluation.
        // The real Attributes draft is never retagged: its self-digest, effective
        // values and current source rules remain mandatory. Changed effective
        // Magic/Resonance/power budgets or other contribution fields are rejected.
        // Neither comparison object is returned or persisted as a new decision.
        var comparisonContribution = contribution with
        {
            AttributesDraftRevision = attributes.DraftRevision,
            AttributesDraftDigest = attributes.DraftDigest,
            ContributionDigest = string.Empty
        };
        comparisonContribution = comparisonContribution with
        {
            ContributionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(comparisonContribution)
        };
        var comparison = historical with
        {
            AttributesDraftRevision = attributes.DraftRevision,
            AttributesDraftDigest = attributes.DraftDigest,
            FinalizationContribution = comparisonContribution,
            DraftDigest = string.Empty
        };
        comparison = comparison with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(comparison) };
        if (!CharacterCreationMagicResonanceDraftIntegrity.IsStructurallyValidPending(comparison,
                workspace.Id, workspace.ContentRevision, state.Binding.RawCharacterXmlDigest,
                prerequisite, attributes, state.Authority))
            return false;
        var historicalBlockers = new List<string>();
        var projected = EvaluateSelections(state.Authority, talent, attributes,
            historical.Selections, historicalBlockers);
        var expected = BuildDraft(workspace, prerequisite, attributes, state.Authority,
            talent, projected, historicalBlockers);
        if (expected is null || historicalBlockers.Count != 0
            || !CharacterCreationMagicResonanceDraftIntegrity.HasSameLogicalPayload(comparison, expected))
            return false;
        binding = new(CharacterCreationMagicResonanceReReviewSchemas.BindingV1, state.Binding,
            historical.DraftDigest, last.ReceiptDigest, string.Empty);
        binding = binding with { ContextDigest = CharacterCreationMagicResonanceDigest.Compute(binding) };
        return true;
    }

    private static CharacterCreationMagicResonanceReReviewPreview BuildReReviewPreview(
        CharacterCreationMagicResonanceReReviewBinding binding, CharacterCreationMagicResonancePreview current)
    {
        var review = new CharacterCreationMagicResonanceReReviewPreview(
            CharacterCreationMagicResonanceReReviewSchemas.PreviewV1, binding, current, string.Empty);
        return review with { PreviewDigest = CharacterCreationMagicResonanceDigest.Compute(review) };
    }
}
