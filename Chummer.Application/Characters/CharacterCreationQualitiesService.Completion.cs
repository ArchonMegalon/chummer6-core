using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationQualitiesService
{
    private CharacterCreationFoundationResult<CharacterCreationQualitiesState> BuildCompositeState(
        WorkspaceStoredDocument workspace)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _resolver, _completionQueries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            return new(loaded.Outcome, null, loaded.Blockers);
        var qualities = inputs.Lifestyles.Qualities;
        var preview = CharacterCreationQualitiesRules.Evaluate(new(
            qualities.Binding, qualities.Authority, baseline.Selections.QualityOptionIds));
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(
            workspace, inputs, baseline.Selections, CharacterCreationCompositeEditStage.None);
        var blockers = Normalize(preview.Blockers.Concat(reviewed.Blockers));
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
            || _store is not ICharacterCreationCompositeStageAtomicCommitCapability { SupportsCompositeStageAtomicCommit: true })
            blockers = Normalize(blockers.Append(CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired));
        var state = new CharacterCreationQualitiesState(CharacterCreationQualitiesSchemas.StateV1,
            qualities.Binding, qualities.Authority, qualities.PrerequisiteDraft, qualities.Attributes.PendingDraft,
            workspace.Document.AuxiliaryState.CharacterCreationQualitiesDraft, preview, blockers,
            blockers.Length == 0, string.Empty);
        state = state with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeStateDigest(state) };
        return new(CharacterCreationFoundationOutcomes.Success, state, blockers);
    }

    private CharacterCreationQualitiesPreview AdmitCompositePreview(
        WorkspaceStoredDocument workspace, CharacterCreationQualitiesPreview preview)
    {
        if (workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts is null) return preview;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _resolver, _completionQueries);
        CharacterCreationCompletionReview? shared = null;
        var blockers = preview.Blockers.ToList();
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            blockers.AddRange(loaded.Blockers);
        else
        {
            var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
                baseline.Selections with { QualityOptionIds = preview.Selections.Select(row => row.OptionId).ToArray() },
                CharacterCreationCompositeEditStage.Qualities);
            blockers.AddRange(reviewed.Blockers);
            shared = reviewed.Value;
        }
        var normalized = Normalize(blockers);
        preview = preview with
        {
            Blockers = normalized, CanConfirm = preview.CanConfirm && normalized.Length == 0,
            CompositeReviewDigest = shared?.ReviewDigest
        };
        return preview with { PreviewDigest = CharacterCreationQualitiesRules.ComputePreviewDigest(preview) };
    }

    /// <summary>
    /// Reuse ordinary source/catalog, talent-grant and retained-history admission,
    /// but expose the original inputs before the first saved Attributes draft.
    /// No saved draft or mutation capability is manufactured for this read.
    /// </summary>
    public CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> LoadCompletionInputs(
        CharacterCreationQualitiesLoadRequest request) => LoadCompletionInputs(request, false);

    internal CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> LoadCompletionInputs(
        CharacterCreationQualitiesLoadRequest request, bool knowledgeModifiers)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesCompletionInputs>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.RevisionConflict);

        if (workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts is not null
            && workspace.Document.AuxiliaryState.CharacterCreationQualitiesDraft is { } pending)
        {
            var effective = _attributes.Load(new(workspace.Id)).Value?.PendingDraft;
            // Preserve existing replay observation shapes unless the effective
            // Attributes identity actually differs from the retained input.
            if (effective is not null
                && (pending.AttributesDraftRevision != effective.DraftRevision
                    || pending.AttributesDraftDigest != effective.DraftDigest)
                && CharacterCreationCompositeBaselineReader.TryReadRetainedQualities(workspace, out var original))
                return LoadRetainedCompletionInputs(workspace, original!, knowledgeModifiers);
        }

        var loaded = BuildState(workspace, forCompletion: true);
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
            !(initialAttributes && blocker == CharacterCreationQualitiesBlockers.AttributesDraftRequired)
            && !(!state.CanEdit && blocker == CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired)));
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
        if (knowledgeModifiers)
        {
            var authority = KnowledgeCompletionAuthority(inputs.Authority);
            inputs = inputs with { Authority = authority, Binding = inputs.Binding with { AuthorityDigest = authority.AuthorityDigest } };
        }
        inputs = inputs with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(inputs) };
        return new(CharacterCreationFoundationOutcomes.Success, DetachCompletion(inputs), []);
    }

    private CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> LoadRetainedCompletionInputs(
        WorkspaceStoredDocument workspace, WorkspaceStoredDocument original, bool knowledgeModifiers)
    {
        if (_completionQueries is null)
            return Blocked<CharacterCreationQualitiesCompletionInputs>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        var view = new WorkspaceContinuationReadView(original);
        var historical = new CharacterCreationQualitiesService(view, _resolver,
            new CharacterCreationPrerequisiteService(view, _completionQueries, _resolver),
            new CharacterCreationAttributesService(view, _resolver), _completionQueries)
            .LoadCompletionInputs(new(workspace.Id), knowledgeModifiers);
        if (historical.Value is not { SavedQualitiesState: not null } admitted)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, historical.Blockers.Count == 0
                ? [CharacterCreationQualitiesBlockers.DraftInvalid] : historical.Blockers);
        var loaded = _attributes.Load(new(workspace.Id));
        if (loaded.Value is not { } attributes || CharacterCreationAttributesService.CreateDraftSession(attributes) is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, loaded.Blockers.Count == 0
                ? [CharacterCreationAttributesBlockers.AuthorityUnavailable] : loaded.Blockers);
        // Only current inputs are rebound. SavedQualitiesState remains the exact
        // historical observation, validated against its original Attributes.
        var binding = admitted.Binding with
        {
            ContentRevision = workspace.ContentRevision, SavedRevision = workspace.SavedRevision,
            AuxiliaryStateDigest = workspace.Document.AuxiliaryStateDigest,
            AttributesDraftRevision = attributes.PendingDraft?.DraftRevision ?? 0,
            AttributesDraftDigest = attributes.PendingDraft?.DraftDigest ?? string.Empty,
            CreationKarmaUsedBeforeQualities = attributes.PendingDraft?.CreationKarmaUsed ?? 0
        };
        var inputs = admitted with { Binding = binding, Attributes = attributes };
        inputs = inputs with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(inputs) };
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationQualitiesCompletionInputs>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesCompletionSchemas.SnapshotConflict);
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

    private static CharacterCreationQualitiesAuthority KnowledgeCompletionAuthority(CharacterCreationQualitiesAuthority original)
    {
        var authority = original with { Options = original.Options.Select(option =>
        {
            // Resolver reason ordering is a prerequisite: EffectsNotProjectable is
            // last, after source, requirements, prompts, cost and rating gates.
            if (option.IsSelectable || option.DisableReasonKey != CharacterCreationQualitiesBlockers.EffectsNotProjectable
                || !CharacterCreationLegacySourceProjector.IsSkillKarmaCompletionSource(option.SourceNodeXml, option.ResolvedArmCount))
                return option;
            var enabled = option with { IsSelectable = true, EligibilityIsExact = true, DisableReasonKey = null };
            return enabled with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(enabled) };
        }).ToArray() };
        return authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
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
        if (inputs.SavedQualitiesState is not { } saved)
            return inputs.SelectedOptionIds.Count == 0
                && CharacterCreationQualitiesRules.EvaluateCompletion(new(inputs.Binding, inputs.Authority,
                    inputs.SelectedOptionIds), inputs.Binding.CreationKarmaUsedBeforeQualities).Blockers.Count == 0;

        if (inputs.Authority.AuthorityDigest != saved.Authority.AuthorityDigest)
        {
            // Validate the unchanged saved authority/history first, then the exact
            // deterministic extension of its disabled catalog. Never rewrite a draft.
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    inputs.Authority, KnowledgeCompletionAuthority(saved.Authority))) return false;
            var historical = inputs with { Authority = saved.Authority,
                Binding = inputs.Binding with { AuthorityDigest = saved.Authority.AuthorityDigest } };
            historical = historical with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(historical) };
            return IsValidCompletionInputs(historical);
        }
        var ordinary = CharacterCreationQualitiesRules.Evaluate(new(saved.Binding, inputs.Authority, inputs.SelectedOptionIds));
        return saved.Schema == CharacterCreationQualitiesSchemas.StateV1
            && CharacterCreationCompletionReadiness.RulesReady(saved.CanEdit, saved.Blockers,
                CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired) && saved.PendingDraft is { } draft
            && CharacterCreationQualitiesRules.DigestsEqual(saved.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeStateDigest(saved))
            && saved.Binding.ContentRevision > 0 && saved.Binding.ContentRevision <= inputs.Binding.ContentRevision
            && saved.Binding.SavedRevision == saved.Binding.ContentRevision
            && (saved.Binding with
            {
                ContentRevision = inputs.Binding.ContentRevision, SavedRevision = inputs.Binding.SavedRevision,
                AuxiliaryStateDigest = inputs.Binding.AuxiliaryStateDigest,
                AttributesDraftRevision = inputs.Binding.AttributesDraftRevision,
                AttributesDraftDigest = inputs.Binding.AttributesDraftDigest,
                CreationKarmaUsedBeforeQualities = inputs.Binding.CreationKarmaUsedBeforeQualities
            }) == inputs.Binding
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Authority, inputs.Authority)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.PrerequisiteDraft, inputs.PrerequisiteDraft)
            && saved.AttributesDraft is { } attributes
            && saved.Binding.AttributesDraftRevision == attributes.DraftRevision
            && saved.Binding.AttributesDraftDigest == attributes.DraftDigest
            && saved.Binding.CreationKarmaUsedBeforeQualities == attributes.CreationKarmaUsed
            && CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(attributes,
                saved.Binding.WorkspaceId, saved.Binding.ContentRevision, saved.Binding.RawCharacterXmlDigest,
                inputs.PrerequisiteDraft)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Preview, ordinary)
            && draft.SelectedOptionIds.SequenceEqual(inputs.SelectedOptionIds, StringComparer.Ordinal)
            && ordinary.Blockers.Count == 0 && IsValidPending(draft, saved.Binding, ordinary);
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
