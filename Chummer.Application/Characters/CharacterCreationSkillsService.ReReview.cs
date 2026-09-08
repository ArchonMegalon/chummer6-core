using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    // Historical semantics are private and used solely to recognize an exact
    // pre-policy draft. Every new preview and committed draft uses current rules.
    private enum SkillsEvaluationSemantics { CurrentSourceBound, PreTalentAccessHistory }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(
        CharacterCreationSkillsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationSkillsReReviewState>(CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable);
        var loaded = BuildState(workspace, out var catalog);
        if (loaded.Value is not { } state || !TryPrepareReReview(workspace, state, catalog, out var binding))
            return Blocked<CharacterCreationSkillsReReviewState>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsReReviewSchemas.Unavailable);
        var historical = state.PendingDraft!;
        var initial = PreviewReReview(new(binding!, historical.Allocations, historical.GroupAllocations));
        if (initial.Value is null)
            return new(initial.Outcome, null, initial.Blockers);
        var review = new CharacterCreationSkillsReReviewState(CharacterCreationSkillsReReviewSchemas.SnapshotV1,
            binding!, historical, state, initial.Value, string.Empty);
        review = review with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(review) };
        return new(CharacterCreationFoundationOutcomes.Success, review, []);
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(
        CharacterCreationSkillsReReviewPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Allocations is null || request.GroupAllocations is null
            || request.Allocations.Any(item => item is null) || request.GroupAllocations.Any(item => item is null))
            return Blocked<CharacterCreationSkillsReReviewPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.AllocationInvalid);
        if (!HasValidReReviewBinding(request.Binding))
            return Blocked<CharacterCreationSkillsReReviewPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsReReviewSchemas.Stale);
        var evaluated = Evaluate(new(request.Binding.Current, request.Allocations, request.GroupAllocations), request.Binding);
        if (evaluated.Result.Value is not { } preview || evaluated.Workspace is not { } workspace)
            return new(evaluated.Result.Outcome, null, evaluated.Result.Blockers);
        return new(evaluated.Result.Outcome,
            BuildReReviewPreview(request.Binding, workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft!, preview),
            evaluated.Result.Blockers);
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(
        CharacterCreationSkillsReReviewConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyReviewedChanges)
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsReReviewSchemas.ExplicitReviewRequired);
        if (request.Allocations is null || request.GroupAllocations is null
            || request.Allocations.Any(item => item is null) || request.GroupAllocations.Any(item => item is null))
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.AllocationInvalid);
        if (!HasValidReReviewBinding(request.Binding))
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsReReviewSchemas.Stale);
        // ConfirmCore looks up exact historical commands before checking the
        // current editable state. Re-review retry must also work after later saves.
        return ConfirmCore(new(request.Binding.Current, request.Allocations, request.GroupAllocations,
            request.PreviewDigest, request.IdempotencyKey, request.ExplicitlyConfirmed), request.Binding);
    }

    private static bool HasValidReReviewBinding(CharacterCreationSkillsReReviewBinding? binding) =>
        binding is not null && binding.Current is not null
        && binding.Schema == CharacterCreationSkillsReReviewSchemas.BindingV1
        && CharacterCreationSkillsDigest.IsCanonical(binding.HistoricalDraftDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.HistoricalReceiptDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.ContextDigest)
        && CharacterCreationSkillsDigest.EqualsFixedTime(binding.ContextDigest,
            CharacterCreationSkillsDigest.Compute(binding with { ContextDigest = string.Empty }));

    private static bool TryPrepareReReview(WorkspaceStoredDocument workspace, CharacterCreationSkillsState state,
        CharacterCreationSkillsAuthority catalog, out CharacterCreationSkillsReReviewBinding? binding)
    {
        binding = null;
        var historical = state.PendingDraft;
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationSkillsReceipts;
        // This migration recognizes the known pre-TalentAccess catalog/runtime,
        // not arbitrary engine, source, prerequisite or attribute drift.
        if (historical is null || historical.DraftRevision is <= 0 or long.MaxValue
            || historical.Schema != CharacterCreationSkillsSchemas.DraftV1
            || historical.WorkspaceId != workspace.Id
            || historical.BaseContentRevision <= 0 || historical.BaseContentRevision >= workspace.ContentRevision
            || state.PrerequisiteDraft is not { } prerequisite || state.AttributesDraft is not { } attributes
            || catalog.TalentAccess is not null || state.Authority.TalentAccess is null
            || !CharacterCreationSkillsDraftIntegrity.IsValidAuthority(catalog)
            || !CharacterCreationSkillsDraftIntegrity.IsValidAuthority(state.Authority)
            || !state.Blockers.Contains(CharacterCreationSkillsBlockers.DraftInvalid)
            || state.Blockers.Any(item => item is not (CharacterCreationSkillsBlockers.DraftInvalid
                or CharacterCreationSkillsBlockers.TalentAccessRequired))
            || historical.Allocations is null || historical.GroupAllocations is null
            || historical.Skills is null || historical.SkillGroups is null
            || !CharacterCreationSkillsDigest.EqualsFixedTime(historical.DraftDigest,
                CharacterCreationSkillsDraftIntegrity.ComputeDigest(historical))
            || !CharacterCreationSkillsDigest.EqualsFixedTime(historical.SkillsAuthorityDigest, catalog.AuthorityDigest)
            || CharacterCreationSkillsDigest.EqualsFixedTime(catalog.AuthorityDigest, state.Authority.AuthorityDigest)
            || ledger is not { Count: > 0 }
            || !CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(ledger, workspace.Id, workspace.ContentRevision))
            return false;
        var last = ledger[^1];
        if (last.DraftRevision != historical.DraftRevision || last.PreviousContentRevision != historical.BaseContentRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.DraftDigest, historical.DraftDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.SkillsAuthorityDigest, historical.SkillsAuthorityDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.RuntimeDigest, historical.RuntimeDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.CommandDigest, historical.LastCommandDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.IdempotencyKeyDigest, historical.LastIdempotencyKeyDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(last.PreviewDigest, historical.LastPreviewDigest)
            || last.ActivePointsRemaining != historical.ActivePointTotal - historical.ActivePointUsed
            || last.SkillGroupPointsRemaining != historical.SkillGroupPointTotal - historical.SkillGroupPointUsed
            || last.KnowledgePointsRemaining != historical.KnowledgePointTotal - historical.KnowledgePointUsed
            || last.KnowledgePointOverflowToActive != historical.KnowledgePointOverflowToActive)
            return false;
        var historicalBlockers = new List<string>();
        var historicalProjection = EvaluateAllocations(catalog, prerequisite,
            state.SelectedActiveSkillPoints, state.SelectedSkillGroupPoints, state.IntuitionUnaugmented,
            state.LogicUnaugmented, state.MovementCapability, historical.Allocations, historical.GroupAllocations,
            historicalBlockers, SkillsEvaluationSemantics.PreTalentAccessHistory);
        var expected = BuildDraft(workspace, prerequisite, attributes, catalog,
            state.Binding.ContributionInputsDigest, historicalProjection);
        if (historicalBlockers.Count != 0
            || !CharacterCreationSkillsDraftIntegrity.HasSameLogicalPayload(historical, expected))
            return false;
        binding = new(CharacterCreationSkillsReReviewSchemas.BindingV1, state.Binding,
            historical.DraftDigest, last.ReceiptDigest, string.Empty);
        binding = binding with { ContextDigest = CharacterCreationSkillsDigest.Compute(binding) };
        return true;
    }

    private static CharacterCreationSkillsReReviewPreview BuildReReviewPreview(
        CharacterCreationSkillsReReviewBinding binding, CharacterCreationSkillsDraft historical,
        CharacterCreationSkillsPreview current)
    {
        var changes = new List<CharacterCreationSkillsReReviewChange>();
        foreach (var key in historical.Skills.Select(item => (item.Kind, item.SourceSkillId))
                     .Concat(current.Skills.Select(item => (item.Kind, item.SourceSkillId))).Distinct()
                     .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.SourceSkillId, StringComparer.Ordinal))
        {
            var old = historical.Skills.SingleOrDefault(item => item.Kind == key.Kind && item.SourceSkillId == key.SourceSkillId);
            var next = current.Skills.SingleOrDefault(item => item.Kind == key.Kind && item.SourceSkillId == key.SourceSkillId);
            changes.Add(new(key.Kind, key.SourceSkillId, next?.Name ?? old!.Name, old?.Rating, next?.Rating,
                old?.PointCost ?? 0, next?.PointCost ?? 0, old?.IsNativeLanguage ?? false,
                next?.IsNativeLanguage ?? false, next is null, next?.Blockers ?? [])
            {
                HistoricalSpecializationOptionId = old?.SpecializationOptionId,
                CandidateSpecializationOptionId = next?.SpecializationOptionId,
                SourceAnchorIds = CharacterCreationTalentSkillGrants.Anchors(old?.SourceAnchorIds ?? [], next?.SourceAnchorIds)
            });
        }
        foreach (var id in historical.SkillGroups.Select(item => item.GroupId)
                     .Concat(current.SkillGroups.Select(item => item.GroupId)).Distinct(StringComparer.Ordinal)
                     .OrderBy(item => item, StringComparer.Ordinal))
        {
            var old = historical.SkillGroups.SingleOrDefault(item => item.GroupId == id);
            var next = current.SkillGroups.SingleOrDefault(item => item.GroupId == id);
            changes.Add(new("skill-group", id, next?.Name ?? old!.Name, old?.Rating, next?.Rating,
                old?.PointCost ?? 0, next?.PointCost ?? 0, false, false, next is null, next?.Blockers ?? [])
            { SourceAnchorIds = CharacterCreationTalentSkillGrants.Anchors(old?.SourceAnchorIds ?? [], next?.SourceAnchorIds) });
        }
        var review = new CharacterCreationSkillsReReviewPreview(CharacterCreationSkillsReReviewSchemas.PreviewV1,
            binding, current, changes, string.Empty);
        return review with { PreviewDigest = CharacterCreationSkillsDigest.Compute(review) };
    }
}
