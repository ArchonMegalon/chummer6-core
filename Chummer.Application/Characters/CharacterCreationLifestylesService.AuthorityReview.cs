using System.Xml;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationLifestylesService
{
    private PreviewEvaluation EvaluateAuthorityReview(CharacterCreationLifestylePreviewRequest request,
        WorkspaceStoredDocument workspace, AuthorityContext context)
    {
        var blockers = context.Blockers.Where(item => item != CharacterCreationLifestylesBlockers.AuthorityReReviewRequired)
            .Concat(context.Budget.Blockers).ToList();
        if (_workspaceStore is not ICharacterCreationLifestyleAuthorityReviewCommitCapability
                { SupportsLifestyleAuthorityReviewCommit: true })
            blockers.Add(CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired);
        var mutation = request.Mutation;
        if (context.AuthorityReview is null || mutation.Configuration is not null || mutation.LifestyleId != Guid.Empty
            || mutation.AuthorityReview is not { } requested)
            return BlockedPreview(context, workspace, mutation, null, null,
                blockers.Append(CharacterCreationLifestylesBlockers.InvalidMutation));
        try
        {
            if (!CharacterCreationLifestylesDraftRules.TryPrepareAuthorityReview(workspace, _sourceData, requested,
                    out var next, out var transition, out var before, out var after)
                || next is null || transition is null || before is null || after is null)
                return BlockedPreview(context, workspace, mutation, null, null,
                    blockers.Append(CharacterCreationLifestylesBlockers.AuthorityUnavailable));
            string empty = EmptyDigest();
            // A review is an auxiliary-only transition of the entire pending list.
            // Its evidence retains both authorities; no row is represented as an ordinary edit.
            var plan = new CharacterCreationLifestyleAtomicWritePlan(CharacterCreationLifestylesSchemas.DraftWritePlanV1,
                CharacterCreationWizardStepIds.ContactsLifestyles, mutation.MutationKind, Guid.Empty, null, null,
                [new(1, mutation.MutationKind, Guid.Empty, empty, empty, CharacterCreationLifestyleSourceAnchors.All)],
                context.Binding.ContentDigest, context.Binding.ContentDigest, empty, empty, empty, empty, true, true, string.Empty)
            { PendingDraft = next, AuthorityTransition = transition };
            plan = plan with { PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(plan) };
            string[] normalized = Normalize(blockers);
            var preview = new CharacterCreationLifestylePreview(CharacterCreationLifestylesSchemas.PreviewV1,
                CharacterCreationWizardStepIds.ContactsLifestyles, context.Binding, mutation.MutationKind,
                null, null, context.Budget, after, plan, normalized, true, normalized.Length == 0, string.Empty);
            preview = preview with { PreviewDigest = CharacterCreationLifestylesRules.ComputePreviewDigest(preview) };
            return new(new(normalized.Length == 0 ? CharacterCreationLifestyleOutcomes.Available
                : CharacterCreationLifestyleOutcomes.Blocked, preview, normalized), workspace, workspace.Document.Content);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException)
        {
            return BlockedPreview(context, workspace, mutation, null, null,
                blockers.Append(CharacterCreationLifestylesBlockers.AuthorityUnavailable));
        }
    }

    // Called again under the owning store's lease and immediately before replace.
    // Public copies/digests are compared to fresh execution, never treated as capabilities.
    public bool AdmitsAuthorityReviewCommit(WorkspaceStoredDocument workspace,
        CharacterCreationLifestylePreview preview, WorkspaceDocument replacement)
    {
        if (preview.WritePlan is not { AuthorityTransition: { } transition, PendingDraft: { } draft }
            || preview.MutationKind != CharacterCreationLifestyleMutationKinds.ReReviewAuthority
            || replacement.AuxiliaryState.CharacterCreationLifestyleReceipts is not { Count: > 0 } ledger
            || !CharacterCreationLifestylesDraftRules.EqualReview(ledger[^1].Receipt.WritePlan, preview.WritePlan)
            || !CharacterCreationLifestylesDraftRules.EqualReview(replacement.AuxiliaryState.CharacterCreationLifestylesDraft, draft))
            return false;
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.ReReviewAuthority,
            Guid.Empty, null) { AuthorityReview = new(transition.HistoricalReceiptDigest, draft.Lifestyles) };
        var evaluated = EvaluatePreview(new(preview.Binding, mutation), workspace);
        return evaluated.Result.Value is { CanConfirm: true, Blockers.Count: 0 } current
            && evaluated.ReplacementContent == replacement.Content
            && CharacterCreationLifestylesDraftRules.EqualReview(current, preview);
    }
}
