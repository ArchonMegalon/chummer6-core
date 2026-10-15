using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationQualitiesService
{
    /// <summary>
    /// Reuse ordinary source/catalog, talent-grant and retained-history admission,
    /// but expose the original inputs before the first saved Attributes draft.
    /// No saved draft or mutation capability is manufactured for this read.
    /// </summary>
    public CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> LoadCompletionInputs(
        CharacterCreationQualitiesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesCompletionInputs>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.RevisionConflict);

        var loaded = BuildState(workspace);
        var attributesResult = _attributes.Load(new(workspace.Id));
        if (loaded.Value is not { PrerequisiteDraft: not null } state)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, loaded.Blockers);
        if (attributesResult.Value is not { } attributes
            || CharacterCreationAttributesService.CreateDraftSession(attributes) is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                attributesResult.Blockers.Count == 0 ? [CharacterCreationAttributesBlockers.AuthorityUnavailable] : attributesResult.Blockers);
        if (!MatchesCompletionAttributes(state.Binding, state.PrerequisiteDraft, attributes))
            return Blocked<CharacterCreationQualitiesCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesCompletionSchemas.SnapshotConflict);

        // A truthful initial binding is the sole new case. An existing invalid
        // quality draft, source drift or required re-review stays blocked.
        bool initialAttributes = attributes.PendingDraft is null
            && state.Binding.AttributesDraftRevision == 0 && state.Binding.AttributesDraftDigest == string.Empty;
        var blockers = Normalize(state.Blockers.Where(blocker =>
            !(initialAttributes && blocker == CharacterCreationQualitiesBlockers.AttributesDraftRequired)));
        if (blockers.Length != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
        if (!CharacterCreationQualitiesReceiptLedgerIntegrity.IsValidLedger(workspace.Id, workspace.ContentRevision,
                workspace.Document.AuxiliaryState.CharacterCreationQualitiesDraft,
                workspace.Document.AuxiliaryState.CharacterCreationQualitiesReceipts))
            return Blocked<CharacterCreationQualitiesCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationQualitiesBlockers.ReceiptLedgerInvalid);

        var inputs = new CharacterCreationQualitiesCompletionInputs(CharacterCreationQualitiesCompletionSchemas.InputsV1,
            state.Binding, attributes, state.Authority, state.PrerequisiteDraft,
            state.PendingDraft?.SelectedOptionIds ?? [], string.Empty)
            { SavedQualitiesState = state.PendingDraft is null ? null : state };
        inputs = inputs with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(inputs) };
        return new(CharacterCreationFoundationOutcomes.Success, DetachCompletion(inputs), []);
    }

    /// <summary>
    /// Pure, detached projection. Attribute costs are obtained from the real
    /// attribute evaluator, never supplied as an unchecked budget. Original
    /// snapshots remain historical observations, not current owner/save authority.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeQualitiesPreview> PreviewWithProposedAttributes(
        CharacterCreationQualitiesCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        IReadOnlyList<string> selectedOptionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(selectedOptionIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (selectedOptionIds.Count > 65_536)
            return Blocked<CharacterCreationAttributeQualitiesPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.InvalidSelection);
        inputs = DetachCompletion(inputs);
        attributeAllocations = attributeAllocations.ToArray();
        selectedOptionIds = selectedOptionIds.ToArray();
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationAttributeQualitiesPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        var session = CharacterCreationAttributesService.CreateDraftSession(inputs.Attributes);
        if (session is null)
            return Blocked<CharacterCreationAttributeQualitiesPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = session.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);
        decimal attributeKarmaUsed = attributes.CreationKarmaBudget.Used;
        if (attributeKarmaUsed < 0 || attributeKarmaUsed > int.MaxValue
            || attributeKarmaUsed != decimal.Truncate(attributeKarmaUsed))
            return Blocked<CharacterCreationAttributeQualitiesPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        var qualities = CharacterCreationQualitiesRules.EvaluateCompletion(
            new(inputs.Binding, inputs.Authority, selectedOptionIds), (int)attributeKarmaUsed);
        var result = new CharacterCreationAttributeQualitiesPreview(CharacterCreationQualitiesCompletionSchemas.PreviewV1,
            inputs.Binding, inputs.Attributes.SnapshotDigest, inputs.SnapshotDigest, attributes, qualities,
            qualities.Blockers, string.Empty);
        result = result with { ProposalDigest = CharacterCreationQualitiesRules.ComputeCompletionPreviewDigest(result) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(qualities.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, DetachCompletion(result), qualities.Blockers.ToArray());
    }

    private static bool IsValidCompletionInputs(CharacterCreationQualitiesCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationQualitiesCompletionSchemas.InputsV1
            || inputs.Binding is null || inputs.Attributes?.Binding is null || inputs.Authority is null
            || inputs.PrerequisiteDraft is null || inputs.SelectedOptionIds is null
            || inputs.SelectedOptionIds.Count > 65_536
            || inputs.Authority.Options is null || inputs.Authority.GrantedQualities is null
            || inputs.Authority.SourceAnchorIds is null || inputs.Authority.Blockers is null
            || inputs.Authority.Options.Any(option => option is null || option.SourceAnchorIds is null)
            || inputs.Authority.GrantedQualities.Any(grant => grant is null || grant.SourceAnchorIds is null)
            || !CharacterCreationQualitiesRules.DigestsEqual(inputs.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(inputs))
            || !MatchesCompletionAttributes(inputs.Binding, inputs.PrerequisiteDraft, inputs.Attributes))
            return false;
        var original = CharacterCreationQualitiesRules.EvaluateCompletion(new(inputs.Binding, inputs.Authority,
            inputs.SelectedOptionIds), inputs.Binding.CreationKarmaUsedBeforeQualities);
        if (original.Blockers.Count != 0)
            return false;
        if (inputs.SavedQualitiesState is not { } saved)
            return inputs.SelectedOptionIds.Count == 0;

        var ordinary = CharacterCreationQualitiesRules.Evaluate(new(inputs.Binding, inputs.Authority, inputs.SelectedOptionIds));
        return saved.Schema == CharacterCreationQualitiesSchemas.StateV1
            && saved.CanEdit && saved.Blockers is { Count: 0 } && saved.PendingDraft is { } draft
            && CharacterCreationQualitiesRules.DigestsEqual(saved.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeStateDigest(saved))
            && saved.Binding == inputs.Binding
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Authority, inputs.Authority)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.PrerequisiteDraft, inputs.PrerequisiteDraft)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.AttributesDraft, inputs.Attributes.PendingDraft)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Preview, ordinary)
            && draft.SelectedOptionIds.SequenceEqual(inputs.SelectedOptionIds, StringComparer.Ordinal)
            && IsValidPending(draft, inputs.Binding, ordinary);
    }

    private static bool MatchesCompletionAttributes(CharacterCreationQualitiesBinding binding,
        CharacterCreationPrerequisiteDraft prerequisite, CharacterCreationAttributesState attributes)
    {
        var original = attributes.Binding;
        return binding.WorkspaceId == original.WorkspaceId
            && binding.ContentRevision > 0 && binding.ContentRevision == binding.SavedRevision
            && binding.ContentRevision == original.ContentRevision && binding.SavedRevision == original.SavedRevision
            && Same(binding.RawCharacterXmlDigest, original.RawCharacterXmlDigest)
            && Same(binding.AuxiliaryStateDigest, original.AuxiliaryStateDigest)
            && binding.PrerequisiteDraftRevision == original.PrerequisiteDraftRevision
            && Same(binding.PrerequisiteDraftDigest, original.PrerequisiteDraftDigest)
            && Same(prerequisite.AuthorityDigest, original.PrerequisiteAuthorityDigest)
            && binding.AttributesDraftRevision == (attributes.PendingDraft?.DraftRevision ?? 0)
            && binding.AttributesDraftDigest == (attributes.PendingDraft?.DraftDigest ?? string.Empty)
            && binding.CreationKarmaUsedBeforeQualities == (attributes.PendingDraft?.CreationKarmaUsed ?? 0)
            && binding.CreationKarmaTotal == prerequisite.CreationKarmaTotal
            && binding.BuildMethod == prerequisite.BuildMethod
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(prerequisite, attributes.PrerequisiteDraft);
    }

    private static T DetachCompletion<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
