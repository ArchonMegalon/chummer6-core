using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class OwnerBoundCharacterCreationAttributesService
{
    public CharacterCreationFoundationResult<CharacterCreationAttributesRebasePreview> PreviewRebase(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (rebaseServices is not { Queries: not null, Campaigns: not null } services)
            return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCampaignExecutionContext.Blocker);
        // Detach before callbacks. Missing rows preserve current allocations; explicit
        // zero is a requested replacement and remains subject to Finish preservation.
        try { request = JsonSerializer.Deserialize<CharacterCreationAttributesPreviewRequest>(JsonSerializer.Serialize(request))!; }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.RequestInvalid); }
        if (request?.Binding is null || request.Allocations is null)
            return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.RequestInvalid);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            try
            {
                using var scope = (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                ICharacterSourceDataResolver resolver = scope ?? sourceResolver;
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner,
                    request.Binding.WorkspaceId, reuseReadObservation: true);
                var read = view.Get(request.Binding.WorkspaceId);
                if (!read.Success || read.Value is not { } original
                    || original.Document.AuxiliaryState.CharacterCreationCompletionDrafts is not { Count: > 0 } ledger)
                    return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
                var loaded = LoadRebaseInputs(view, resolver, services.Queries, original, cancellationToken);
                if (loaded.Value is not { CompositeBaseline: { } baseline } inputs || loaded.Blockers.Count != 0)
                    return new(loaded.Outcome, null, loaded.Blockers);
                if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(request.Binding, inputs.Skills.Attributes.Binding))
                    return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
                var rows = inputs.Skills.Attributes.Attributes;
                if (request.Allocations.Count > rows.Count || request.Allocations.Any(row => row is null
                        || row.PriorityPoints < 0 || row.KarmaLevels < 0
                        || !rows.Any(current => current.AttributeId == row.AttributeId))
                    || request.Allocations.Select(row => row.AttributeId).Distinct(StringComparer.Ordinal).Count() != request.Allocations.Count)
                    return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.RequestInvalid);
                var allocations = rows.Select(row => request.Allocations.SingleOrDefault(value => value.AttributeId == row.AttributeId)
                    ?? new CharacterCreationAttributeAllocation(row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels))
                    .OrderBy(row => row.AttributeId, StringComparer.Ordinal).ToArray();
                var changed = allocations.Where(row => rows.Any(old => old.AttributeId == row.AttributeId
                    && (old.PriorityPointsSpent != row.PriorityPoints || old.KarmaLevels != row.KarmaLevels)))
                    .Select(row => row.AttributeId).ToArray();
                var previousChoices = baseline.Selections.PlayerChosenAttributeIds
                    ?? inputs.Skills.Attributes.PendingDraft?.PlayerChosenAttributeIds
                    ?? original.Document.AuxiliaryState.CharacterCreationAttributesDraft?.PlayerChosenAttributeIds;
                if (request.PlayerChosenAttributeIds is { } explicitChoices
                    && (explicitChoices.Any(id => !rows.Any(row => row.AttributeId == id && row.IsEnabled))
                        || explicitChoices.Distinct(StringComparer.Ordinal).Count() != explicitChoices.Count))
                    return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.RequestInvalid);
                var chosen = request.PlayerChosenAttributeIds is null && previousChoices is null ? null
                    : (previousChoices ?? []).Concat(request.PlayerChosenAttributeIds ?? []).Concat(changed)
                        .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (changed.Length == 0 && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        chosen, baseline.Selections.PlayerChosenAttributeIds))
                    return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationAttributesBlockers.DraftDuplicate);
                var authority = services.Campaigns.Observe(RebaseObservation(expectedOwner, original, inputs), cancellationToken);
                var campaign = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Review, cancellationToken);
                // Every other choice, permission, purchase and reserve stays exact.
                var selections = baseline.Selections with
                {
                    AttributeAllocations = allocations,
                    PlayerChosenAttributeIds = chosen
                };
                var intent = new CharacterCreationAttributesReplacementIntent(
                    CharacterCreationAttributesReplacementIntent.SchemaV1, inputs.Skills.Attributes.Binding,
                    CharacterCreationFinalizationDigest.Compute(expectedOwner.Owner),
                    CharacterCreationFinalizationDigest.Compute(expectedOwner), inputs.SnapshotDigest,
                    CharacterCreationFinalizationDigest.Compute(baseline.Selections),
                    CharacterCreationFinalizationDigest.Compute(selections), allocations);
                var proposal = ledger[^1].Baseline.Review.Review.Request with
                {
                    InputsSnapshotDigest = inputs.SnapshotDigest, Selections = selections, AttributesReplacement = intent,
                    EffectivePolicyDigest = inputs.EffectivePolicy!.PolicyDigest,
                    PolicyConsumptionSchema = CharacterCreationPolicyConsumption.SchemaV1,
                    CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
                    OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
                    RuleSourceExecutionSchema = inputs.RuleSourceExecutionSchema,
                    RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
                    SourcebookSelection = inputs.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                        ? inputs.SourcebookSelection : ledger[^1].Baseline.Review.Review.Request.SourcebookSelection, CampaignAuthority = campaign.Binding
                };
                // Same reviewer as Finish, under the same real lease. It recaptures
                // current sources and observes the workspace again before minting.
                using var replacement = CharacterCreationAttributesReplacementExecution.EnterLive(
                    lease, expectedOwner, original, inputs, proposal);
                var reviewed = CharacterCreationCompletionDraftReviewer.Review(store, lease, expectedOwner,
                    original.Id, sourceResolver, services.Queries, proposal, cancellationToken, campaign);
                return reviewed.Value is { } review && reviewed.Blockers.Count == 0
                    ? new(CharacterCreationFoundationOutcomes.Success, new(expectedOwner, review), [])
                    : new(reviewed.Outcome, null, reviewed.Blockers);
            }
            catch (Exception error) when (RebaseFailure(error))
            { return RebaseRejected<CharacterCreationAttributesRebasePreview>(CharacterCreationCompletionReviewSchemas.SnapshotConflict); }
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ConfirmRebase(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesRebasePreview preview,
        string idempotencyKey, bool explicitlyConfirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        if (rebaseServices is not { Queries: not null, Campaigns: not null } services)
            return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCampaignExecutionContext.Blocker);
        var command = preview.Command(idempotencyKey, explicitlyConfirmed);
        if (preview.Owner != expectedOwner || !CharacterCreationCompletionDraftTransaction.IsConfirmed(command)
            || command.Review.OwnerStampDigest != CharacterCreationFinalizationDigest.Compute(expectedOwner)
            || command.Review.OwnerScopeDigest != CharacterCreationFinalizationDigest.Compute(expectedOwner.Owner))
            return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            try
            {
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, command.Review.Binding.WorkspaceId);
                // Exact committed reconciliation precedes unavailable current sources
                // or campaign permission. Never reinterpret retry as a fresh edit.
                var replay = view.LookupCompletionDraftReceipt(expectedOwner.Owner,
                    CharacterCreationCompletionDraftTransaction.LookupRequest(command), cancellationToken);
                if (replay.Value is not null || replay.Blockers.Contains(CharacterCreationFinalizationBlockers.IdempotencyConflict))
                    return replay;
                var read = view.Get(command.Review.Binding.WorkspaceId);
                if (!read.Success || read.Value is not { } original
                    || original.ContentRevision != command.Review.Binding.ContentRevision
                    || original.SavedRevision != command.Review.Binding.SavedRevision
                    || original.Document.AuxiliaryStateDigest != command.Review.Binding.AuxiliaryStateDigest
                    || CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content) != command.Review.Binding.RawCharacterXmlDigest)
                    return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
                // Keep one admitted grammar and poisoned source scope through both
                // atomic commit fences, including when the host resolver starts historical.
                var admitted = sourceResolver.ForCreationSourceExecution(command.Review.Review.Request.RuleSourceExecutionSchema);
                if (admitted is null)
                    return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
                using var scope = (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                ICharacterSourceDataResolver resolver = scope ?? admitted;
                var loaded = LoadRebaseInputs(view, resolver, services.Queries, original, cancellationToken);
                if (loaded.Value is not { } inputs || loaded.Blockers.Count != 0
                    || inputs.SnapshotDigest != command.Review.Review.Request.InputsSnapshotDigest)
                    return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
                var authority = services.Campaigns.Observe(RebaseObservation(expectedOwner, original, inputs), cancellationToken);
                var campaign = CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Apply, cancellationToken);
                using var replacement = CharacterCreationAttributesReplacementExecution.EnterLive(
                    lease, expectedOwner, original, inputs, command.Review.Review.Request);
                // TryBuild recomputes the entire selected review, validates exact
                // bindings, captures the complete original state, and appends one
                // Draft entry. File store repeats it at both atomic source fences.
                return ((ICharacterCreationCompletionDraftAtomicCommitCapability)view).CommitCompletionDraft(
                    expectedOwner.Owner, command, resolver, services.Queries, campaign, cancellationToken);
            }
            catch (Exception error) when (RebaseFailure(error))
            { return RebaseRejected<CharacterCreationCompletionDraftReceipt>(CharacterCreationCompletionReviewSchemas.SnapshotConflict); }
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadRebaseInputs(
        IWorkspaceStore view, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        WorkspaceStoredDocument original, CancellationToken cancellationToken)
        => OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(view, resolver, queries,
            new(original.Id) { IncludeKarmaPurchasePolicy = true }, cancellationToken,
            includeLoadout: original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
                .Baseline.Review.Review.Request.Selections.Loadout is not null,
            includeEffectivePolicy: true, includeConsumerPolicy: true, knowledgeModifiers: true,
            policyConsumption: true, includeOriginalCharacter: true, includeRuleSourceExecution: true,
            ruleSourceExecutionSchema: original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
                .Baseline.Review.Review.Request.RuleSourceExecutionSchema switch
                {
                    CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4 => CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
                    CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 => CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3,
                    _ => CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
                },
            includeSourcebooks: original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1]
                .Baseline.Review.Review.Request is { RuleSourceExecutionSchema: CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
                    SourcebookSelection: not null });

    private static CharacterCreationAttributesRebaseObservation RebaseObservation(OwnerContextStamp owner,
        WorkspaceStoredDocument original, CharacterCreationCompletionReviewInputs inputs)
        => new(owner, original.Id, original.ContentRevision, original.SavedRevision,
            CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content), original.Document.AuxiliaryStateDigest,
            inputs.SnapshotDigest, inputs.RuleSourceCaptureDigest!, inputs.EffectivePolicy!.PolicyDigest);

    private static bool RebaseFailure(Exception error) => error is JsonException or IOException
        or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or OverflowException or FormatException or System.Xml.XmlException;

    private static CharacterCreationFoundationResult<T> RebaseRejected<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
