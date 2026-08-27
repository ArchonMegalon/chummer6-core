using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationFinalizationService : ICharacterCreationFinalizationService
{
    private readonly IWorkspaceStore _store;
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterCreationPrerequisiteService _prerequisites;
    private readonly ICharacterCreationAttributesService _attributes;
    private readonly ICharacterCreationSkillsService _skills;
    private readonly ICharacterCreationQualitiesService _qualities;
    private readonly ICharacterCreationMagicResonanceService _magicResonance;
    private readonly ICharacterCreationResourcesService _resources;
    private readonly ICharacterCreationGearService _gear;

    public CharacterCreationFinalizationService(
        IWorkspaceStore store,
        ICharacterFileQueries characterFileQueries,
        ICharacterCreationPrerequisiteService prerequisites,
        ICharacterCreationAttributesService attributes,
        ICharacterCreationSkillsService skills,
        ICharacterCreationQualitiesService qualities,
        ICharacterCreationMagicResonanceService magicResonance,
        ICharacterCreationResourcesService resources,
        ICharacterCreationGearService gear)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _characterFileQueries = characterFileQueries ?? throw new ArgumentNullException(nameof(characterFileQueries));
        _prerequisites = prerequisites ?? throw new ArgumentNullException(nameof(prerequisites));
        _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _qualities = qualities ?? throw new ArgumentNullException(nameof(qualities));
        _magicResonance = magicResonance ?? throw new ArgumentNullException(nameof(magicResonance));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _gear = gear ?? throw new ArgumentNullException(nameof(gear));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        CharacterCreationFinalizationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Evaluation evaluation = Evaluate(request.WorkspaceId);
        return evaluation.State is null
            ? Blocked<CharacterCreationFinalizationState>(evaluation.Outcome, evaluation.Blockers)
            : new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
                evaluation.Outcome,
                evaluation.State,
                evaluation.Blockers);
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        CharacterCreationFinalizationReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Evaluation evaluation = Evaluate(request.Binding.WorkspaceId);
        if (evaluation.State is not { } state || evaluation.Workspace is not { } workspace)
            return Blocked<CharacterCreationFinalizationReview>(evaluation.Outcome, evaluation.Blockers);
        if (!BindingEquals(state.Binding, request.Binding))
            return Blocked<CharacterCreationFinalizationReview>(
                CharacterCreationFinalizationOutcomes.Conflict,
                BindingConflict(state.Binding, request.Binding));

        bool projected = CharacterCreationFinalizationProjector.TryProject(
            workspace,
            out string resultXml,
            out CharacterCreationFinalizationDelta[] deltas,
            out string[] sourceAnchors,
            out decimal karmaRemaining,
            out decimal startingNuyen,
            out decimal nuyenRemaining,
            out string[] projectionBlockers);
        string[] blockers = Normalize(evaluation.Blockers.Concat(projectionBlockers));
        CharacterCreationFinalizationPlan? plan = null;
        if (projected && blockers.Length == 0)
        {
            var candidate = new CharacterCreationFinalizationPlan(
                CharacterCreationFinalizationSchemas.PlanV1,
                state.Binding,
                deltas,
                karmaRemaining,
                startingNuyen,
                nuyenRemaining,
                sourceAnchors,
                CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(resultXml),
                string.Empty);
            plan = candidate with
            {
                PlanDigest = CharacterCreationFinalizationDigest.Compute(
                    candidate with { PlanDigest = string.Empty })
            };
        }

        var review = new CharacterCreationFinalizationReview(
            CharacterCreationFinalizationSchemas.ReviewV1,
            state.Binding,
            plan,
            deltas,
            blockers,
            sourceAnchors,
            RequiresExplicitConfirmation: true,
            CanConfirm: plan is not null && blockers.Length == 0,
            PreviewDigest: string.Empty);
        review = review with
        {
            PreviewDigest = CharacterCreationFinalizationDigest.Compute(
                review with { PreviewDigest = string.Empty })
        };
        return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReview>(
            review.CanConfirm
                ? CharacterCreationFinalizationOutcomes.Available
                : CharacterCreationFinalizationOutcomes.Blocked,
            review,
            blockers);
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        CharacterCreationFinalizationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Invalid,
                CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        if (!IsValidIdempotencyKey(request.IdempotencyKey))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Invalid,
                CharacterCreationFinalizationBlockers.IdempotencyKeyInvalid);

        string idempotencyDigest = CharacterCreationFinalizationDigest
            .ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        string commandDigest = ComputeCommandDigest(request);
        WorkspaceStoreReadResult initialRead = _store.Get(request.Binding.WorkspaceId);
        CharacterCreationFinalizationReceiptLedgerEntry? existing = initialRead.Value is { } initial
            ? CharacterCreationFinalizationReceiptLedgerIntegrity.Find(
                initial.Document.AuxiliaryState.CharacterCreationFinalizationReceipts,
                idempotencyDigest)
            : null;
        if (existing is not null)
            return ReplayOrConflict(existing, commandDigest);

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> reviewed = Review(
            new CharacterCreationFinalizationReviewRequest(request.Binding));
        if (reviewed.Value is not { Plan: { } plan } review
            || !review.CanConfirm
            || review.Blockers.Count != 0)
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                reviewed.Outcome,
                null,
                reviewed.Blockers);
        if (!CharacterCreationFinalizationDigest.EqualsFixedTime(
                review.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.PreviewDigestMismatch);
        if (!CharacterCreationFinalizationDigest.EqualsFixedTime(plan.PlanDigest, request.PlanDigest))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.PlanDigestMismatch);

        WorkspaceStoreReadResult read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Unavailable,
                CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.StaleWorkspaceRevision);
        if (!string.Equals(
                workspace.Document.AuxiliaryStateDigest,
                request.Binding.AuxiliaryStateDigest,
                StringComparison.Ordinal))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.StaleAuxiliaryStateDigest);
        string currentRawDigest = CharacterCreationFinalizationProjector
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        if (!CharacterCreationFinalizationDigest.EqualsFixedTime(
                currentRawDigest,
                request.Binding.RawCharacterXmlDigest))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.StaleRawCharacterXmlDigest);
        if (!CharacterCreationFinalizationProjector.TryProject(
                workspace,
                out string resultXml,
                out _, out _, out _, out _, out _, out string[] projectionBlockers))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Blocked,
                projectionBlockers);
        string resultDigest = CharacterCreationFinalizationProjector
            .ComputeRawCharacterXmlDigest(resultXml);
        if (!CharacterCreationFinalizationDigest.EqualsFixedTime(
                resultDigest,
                plan.ExpectedResultRawCharacterXmlDigest))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.PlanDigestMismatch);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Unavailable,
                CharacterCreationFinalizationBlockers.AtomicPersistenceRequired);

        long nextRevision = checked(workspace.ContentRevision + 1);
        var receipt = new CharacterCreationFinalizationReceipt(
            CharacterCreationFinalizationSchemas.ReceiptV1,
            CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationFinalizationSchemas.ReceiptV1,
                workspace.Id,
                idempotencyDigest,
                commandDigest
            }),
            workspace.Id,
            idempotencyDigest,
            commandDigest,
            workspace.ContentRevision,
            nextRevision,
            workspace.SavedRevision,
            nextRevision,
            currentRawDigest,
            resultDigest,
            workspace.Document.AuxiliaryStateDigest,
            request.Binding.AuthorityDigest,
            request.PreviewDigest,
            request.PlanDigest,
            request.Binding.BuildMethod,
            CharacterCreated: true,
            RequiresFreshCareerReopen: true,
            CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt)
        };
        var entry = new CharacterCreationFinalizationReceiptLedgerEntry(
            idempotencyDigest,
            commandDigest,
            receipt);
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                Payload = resultXml,
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationFinalizationReceipts: [entry])
            }
        };
        WorkspaceStoreMutationResult committed = atomic
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                workspace.Id,
                workspace.ContentRevision,
                workspace.Document.AuxiliaryStateDigest,
                replacement);
        if (!committed.Success)
        {
            CharacterCreationFinalizationReceiptLedgerEntry? observed = FindPersisted(
                workspace.Id,
                idempotencyDigest);
            if (observed is not null)
                return ReplayOrConflict(observed, commandDigest);
            return Blocked<CharacterCreationFinalizationReceipt>(
                committed.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFinalizationOutcomes.Conflict
                    : CharacterCreationFinalizationOutcomes.Unavailable,
                committed.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFinalizationBlockers.StaleWorkspaceRevision
                    : CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
        }

        WorkspaceStoreReadResult reopened = _store.Get(workspace.Id);
        CharacterCreationFinalizationReceiptLedgerEntry? persisted = reopened.Value is { } reopenedWorkspace
            ? CharacterCreationFinalizationReceiptLedgerIntegrity.Find(
                reopenedWorkspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts,
                idempotencyDigest)
            : null;
        if (reopened.Value is not { } fresh
            || persisted is null
            || fresh.ContentRevision != receipt.ContentRevision
            || fresh.SavedRevision != receipt.SavedRevision
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(
                    fresh.Document.Content),
                receipt.RawCharacterXmlDigest)
            || !_characterFileQueries.ParseSummary(new CharacterDocument(fresh.Document.Content)).Created)
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Applied,
                receipt,
                [CharacterCreationFinalizationBlockers.PostCommitReopenRequired]);
        }
        return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
            CharacterCreationFinalizationOutcomes.Applied,
            persisted.Receipt,
            []);
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        CharacterCreationFinalizationReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidIdempotencyKey(request.IdempotencyKey))
            return Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Invalid,
                CharacterCreationFinalizationBlockers.IdempotencyKeyInvalid);
        CharacterCreationFinalizationReceiptLedgerEntry? entry = FindPersisted(
            request.WorkspaceId,
            CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey));
        return entry is null
            ? Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.NotFound)
            : new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Replayed,
                entry.Receipt,
                []);
    }

    private Evaluation Evaluate(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = _store.Get(workspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return new Evaluation(
                CharacterCreationFinalizationOutcomes.NotFound,
                null,
                null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);

        CharacterFileSummary summary;
        try
        {
            summary = _characterFileQueries.ParseSummary(new CharacterDocument(workspace.Document.Content));
        }
        catch
        {
            return new Evaluation(
                CharacterCreationFinalizationOutcomes.Corrupt,
                workspace,
                null,
                [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid]);
        }

        string rawDigest = CharacterCreationFinalizationProjector
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        CharacterCreationFinalizationReceipt? lastReceipt = workspace.Document.AuxiliaryState
            .CharacterCreationFinalizationReceipts?.LastOrDefault()?.Receipt;
        if (summary.Created)
        {
            string createdAuthorityDigest = lastReceipt?.AuthorityDigest
                ?? CharacterCreationFinalizationDigest.Compute(new { workspace.Id, rawDigest });
            var createdBinding = new CharacterCreationFinalizationBinding(
                workspace.Id,
                workspace.ContentRevision,
                workspace.SavedRevision,
                rawDigest,
                workspace.Document.AuxiliaryStateDigest,
                summary.BuildMethod,
                createdAuthorityDigest);
            var createdState = new CharacterCreationFinalizationState(
                CharacterCreationFinalizationSchemas.StateV1,
                createdBinding,
                CharacterCreated: true,
                Steps: [],
                [CharacterCreationFinalizationBlockers.CharacterAlreadyCreated],
                CanReview: false,
                lastReceipt,
                SnapshotDigest: string.Empty);
            createdState = createdState with
            {
                SnapshotDigest = CharacterCreationFinalizationDigest.Compute(
                    createdState with { SnapshotDigest = string.Empty })
            };
            return new Evaluation(
                CharacterCreationFinalizationOutcomes.Blocked,
                workspace,
                createdState,
                createdState.Blockers.ToArray());
        }

        var blockers = new List<string>();
        if (!string.Equals(workspace.Document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
            blockers.Add(CharacterCreationFinalizationBlockers.RulesetSr5Required);
        if (summary.BuildMethod is CharacterCreationBuildMethods.SumToTen
            or CharacterCreationBuildMethods.LifeModules)
            blockers.Add(CharacterCreationFinalizationBlockers.BuildMethodNotReady);
        else if (!string.Equals(summary.BuildMethod, CharacterCreationBuildMethods.Priority,
                     StringComparison.Ordinal))
            blockers.Add(CharacterCreationFinalizationBlockers.BuildMethodUnsupported);
        CharacterCreationBootstrapBinding? bootstrap = workspace.Document.AuxiliaryState
            .CharacterCreationBootstrapBinding;
        if (bootstrap is null
            || bootstrap.WorkspaceId != workspace.Id
            || !string.Equals(bootstrap.BuildMethod, summary.BuildMethod, StringComparison.Ordinal))
            blockers.Add(CharacterCreationFinalizationBlockers.BootstrapBindingRequired);

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisites =
            _prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(workspace.Id));
        CharacterCreationFoundationResult<CharacterCreationAttributesState> attributes =
            _attributes.Load(new CharacterCreationAttributesLoadRequest(workspace.Id));
        CharacterCreationFoundationResult<CharacterCreationSkillsState> skills =
            _skills.Load(new CharacterCreationSkillsLoadRequest(workspace.Id));
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> qualities =
            _qualities.Load(new CharacterCreationQualitiesLoadRequest(workspace.Id));
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> magic =
            _magicResonance.Load(new CharacterCreationMagicResonanceLoadRequest(workspace.Id));
        bool magicRequired = !CharacterCreationFinalizationProjector.IsMundaneTalent(
            prerequisites.Value?.PendingDraft);
        CharacterCreationResourcesResult<CharacterCreationResourcesState> resources =
            _resources.Load(new CharacterCreationResourcesLoadRequest(workspace.Id));
        CharacterCreationGearResult<CharacterCreationGearState> gear =
            _gear.Load(new CharacterCreationGearLoadRequest(workspace.Id));

        string[] prerequisiteFinalizationBlockers = prerequisites.Blockers
            .Where(static blocker => !string.Equals(
                blocker,
                CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists,
                StringComparison.Ordinal))
            .ToArray();
        blockers.AddRange(prerequisiteFinalizationBlockers);
        blockers.AddRange(attributes.Blockers);
        blockers.AddRange(skills.Blockers);
        blockers.AddRange(qualities.Blockers);
        if (magicRequired || magic.Value?.PendingDraft is not null)
            blockers.AddRange(magic.Blockers);
        blockers.AddRange(resources.Blockers);
        blockers.AddRange(gear.Blockers);
        var steps = new[]
        {
            Step(CharacterCreationWizardStepIds.Method, prerequisites.Value?.PendingDraft,
                prerequisiteFinalizationBlockers, prerequisites.Value?.PendingDraft?.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.PrerequisiteDraftRequired),
            Step(CharacterCreationWizardStepIds.Attributes, attributes.Value?.PendingDraft,
                attributes.Blockers, attributes.Value?.PendingDraft?.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.AttributesDraftRequired),
            Step(CharacterCreationWizardStepIds.Skills, skills.Value?.PendingDraft,
                skills.Blockers, skills.Value?.PendingDraft?.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.SkillsDraftRequired),
            Step(CharacterCreationWizardStepIds.Qualities, qualities.Value?.PendingDraft,
                qualities.Blockers, qualities.Value?.PendingDraft?.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.QualitiesDraftRequired),
            magicRequired
                ? Step(CharacterCreationWizardStepIds.MagicResonance, magic.Value?.PendingDraft,
                    magic.Blockers, magic.Value?.PendingDraft?.SourceAnchorIds,
                    CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired)
                : OptionalCompleteStep(
                    CharacterCreationWizardStepIds.MagicResonance,
                    prerequisites.Value?.PendingDraft?.TalentSelection?.SourceAnchorIds),
            Step(CharacterCreationWizardStepIds.Resources, resources.Value?.PendingDraft,
                resources.Blockers, resources.Value?.PendingDraft?.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.ResourcesDraftRequired),
            Step("gear", gear.Value?.PendingDraft,
                gear.Blockers, gear.Value?.PendingDraft?.FinalizationContribution.SourceAnchorIds,
                CharacterCreationFinalizationBlockers.GearDraftRequired)
        };
        foreach (CharacterCreationFinalizationStep step in steps)
            blockers.AddRange(step.Blockers);

        _ = CharacterCreationFinalizationProjector.TryProject(
            workspace,
            out _, out _, out _, out _, out _, out _, out string[] projectionBlockers);
        blockers.AddRange(projectionBlockers);
        string[] normalizedBlockers = Normalize(blockers);
        string authorityDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-finalization.authority.v1",
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            RawCharacterXmlDigest = rawDigest,
            AuxiliaryStateDigest = workspace.Document.AuxiliaryStateDigest,
            summary.BuildMethod,
            PrerequisiteSnapshotDigest = prerequisites.Value?.SnapshotDigest,
            AttributesSnapshotDigest = attributes.Value?.SnapshotDigest,
            SkillsSnapshotDigest = skills.Value?.SnapshotDigest,
            QualitiesSnapshotDigest = qualities.Value?.SnapshotDigest,
            MagicSnapshotDigest = magicRequired
                ? magic.Value?.SnapshotDigest
                : "not-applicable:mundane",
            ResourcesSnapshotDigest = resources.Value?.SnapshotDigest,
            GearSnapshotDigest = gear.Value?.SnapshotDigest
        });
        var binding = new CharacterCreationFinalizationBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            summary.BuildMethod,
            authorityDigest);
        var state = new CharacterCreationFinalizationState(
            CharacterCreationFinalizationSchemas.StateV1,
            binding,
            CharacterCreated: false,
            steps,
            normalizedBlockers,
            CanReview: normalizedBlockers.Length == 0,
            lastReceipt,
            SnapshotDigest: string.Empty);
        state = state with
        {
            SnapshotDigest = CharacterCreationFinalizationDigest.Compute(
                state with { SnapshotDigest = string.Empty })
        };
        return new Evaluation(
            state.CanReview
                ? CharacterCreationFinalizationOutcomes.Available
                : CharacterCreationFinalizationOutcomes.Blocked,
            workspace,
            state,
            normalizedBlockers);
    }

    private CharacterCreationFinalizationReceiptLedgerEntry? FindPersisted(
        CharacterWorkspaceId workspaceId,
        string idempotencyDigest)
    {
        WorkspaceStoreReadResult read = _store.Get(workspaceId);
        return read.Value is { } workspace
            ? CharacterCreationFinalizationReceiptLedgerIntegrity.Find(
                workspace.Document.AuxiliaryState.CharacterCreationFinalizationReceipts,
                idempotencyDigest)
            : null;
    }

    private static CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>
        ReplayOrConflict(
            CharacterCreationFinalizationReceiptLedgerEntry existing,
            string commandDigest) => CharacterCreationFinalizationDigest.EqualsFixedTime(
                existing.CommandDigest,
                commandDigest)
            ? new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Replayed,
                existing.Receipt,
                [])
            : Blocked<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                CharacterCreationFinalizationBlockers.IdempotencyConflict);

    private static CharacterCreationFinalizationStep Step<T>(
        string stepId,
        T? draft,
        IReadOnlyList<string> authorityBlockers,
        IReadOnlyList<string>? anchors,
        string missingBlocker)
        where T : class
    {
        string? digest = draft switch
        {
            CharacterCreationPrerequisiteDraft value => value.DraftDigest,
            CharacterCreationAttributesDraft value => value.DraftDigest,
            CharacterCreationSkillsDraft value => value.DraftDigest,
            CharacterCreationQualitiesDraft value => value.DraftDigest,
            CharacterCreationMagicResonanceDraft value => value.DraftDigest,
            CharacterCreationResourcesDraft value => value.DraftDigest,
            CharacterCreationGearDraft value => value.DraftDigest,
            _ => null
        };
        string[] blockers = Normalize(authorityBlockers.Concat(
            draft is null ? [missingBlocker] : Array.Empty<string>()));
        return new CharacterCreationFinalizationStep(
            stepId,
            IsRequired: true,
            IsComplete: draft is not null && blockers.Length == 0,
            digest,
            blockers,
            anchors?.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray()
                ?? []);
    }

    private static CharacterCreationFinalizationStep OptionalCompleteStep(
        string stepId,
        IReadOnlyList<string>? anchors) => new(
        stepId,
        IsRequired: false,
        IsComplete: true,
        DraftDigest: null,
        Blockers: [],
        SourceAnchorIds: anchors?.Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal).ToArray() ?? []);

    private static bool BindingEquals(
        CharacterCreationFinalizationBinding left,
        CharacterCreationFinalizationBinding right) =>
        left.WorkspaceId == right.WorkspaceId
        && left.ContentRevision == right.ContentRevision
        && left.SavedRevision == right.SavedRevision
        && CharacterCreationFinalizationDigest.EqualsFixedTime(
            left.RawCharacterXmlDigest, right.RawCharacterXmlDigest)
        && string.Equals(left.AuxiliaryStateDigest, right.AuxiliaryStateDigest, StringComparison.Ordinal)
        && string.Equals(left.BuildMethod, right.BuildMethod, StringComparison.Ordinal)
        && CharacterCreationFinalizationDigest.EqualsFixedTime(
            left.AuthorityDigest, right.AuthorityDigest);

    private static string BindingConflict(
        CharacterCreationFinalizationBinding current,
        CharacterCreationFinalizationBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationFinalizationBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationFinalizationDigest.EqualsFixedTime(
                current.RawCharacterXmlDigest,
                requested.RawCharacterXmlDigest))
            return CharacterCreationFinalizationBlockers.StaleRawCharacterXmlDigest;
        if (!string.Equals(
                current.AuxiliaryStateDigest,
                requested.AuxiliaryStateDigest,
                StringComparison.Ordinal))
            return CharacterCreationFinalizationBlockers.StaleAuxiliaryStateDigest;
        return CharacterCreationFinalizationBlockers.AuthorityDigestMismatch;
    }

    private static string ComputeCommandDigest(CharacterCreationFinalizationConfirmRequest request) =>
        CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-finalization.command.v1",
            request.Binding,
            request.PreviewDigest,
            request.PlanDigest,
            ExplicitlyConfirmed = true
        });

    private static bool IsValidIdempotencyKey(string? value) => value is not null
        && value.Length is >= 8 and <= 200
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(static character => !char.IsControl(character));

    private static CharacterCreationFinalizationResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, Normalize(blockers));

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static blocker => blocker, StringComparer.Ordinal)
        .ToArray();

    private sealed record Evaluation(
        string Outcome,
        WorkspaceStoredDocument? Workspace,
        CharacterCreationFinalizationState? State,
        string[] Blockers);
}
