using Chummer.Application.Workspaces;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationFoundationService
{
    public CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> LoadJourney(
        CharacterCreationFoundationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return ReadFailure<CharacterCreationLifeModuleJourneyState>(read);
        return BuildJourney(workspace, request.EnabledSources, request.EnabledSources is not null);
    }

    public CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> PreviewModule(
        CharacterCreationLifeModulePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluateModule(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationLifeModuleApplyReceipt> ConfirmModule(
        CharacterCreationLifeModuleConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationLifeModuleApplyReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        var evaluation = EvaluateModule(request.Request);
        if (evaluation.Result.Value is not { } preview || evaluation.Workspace is not { } workspace
            || !preview.CanConfirm)
            return new(evaluation.Result.Outcome, null, evaluation.Result.Blockers);
        if (!DigestEquals(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationLifeModuleApplyReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationLifeModuleApplyReceipt>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);

        CharacterCreationFoundationDraftLedger current = workspace.Document.AuxiliaryState
            .CharacterCreationFoundationDraft!;
        var proposed = current with
        {
            DraftRevision = current.DraftRevision + 1,
            BaseContentRevision = workspace.ContentRevision,
            AdditionalModules = [.. current.AdditionalModules ?? [], preview.Entry],
            DraftDigest = string.Empty
        };
        proposed = proposed with
        {
            DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(proposed)
        };
        var originLedger = workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances;
        LifeModuleDecisionAcceptance? originAcceptance = null;
        if (request.OriginDecisionCommand is not null || request.OriginDecisionStep is not null)
        {
            originAcceptance = CharacterCreationFoundationLifeModuleDecisionAuthority.CreateModuleAcceptance(
                this, workspace, proposed, preview, request.OriginDecisionCommand, request.OriginDecisionStep);
            if (originAcceptance is null)
                return Blocked<CharacterCreationLifeModuleApplyReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                    LifeModuleOriginDossierBlockers.DecisionStale);
            originLedger = [.. originLedger ?? [], originAcceptance];
        }
        var replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationFoundationDraft = proposed,
                    LifeModuleDecisionAcceptances = originLedger
                }
            }
        };
        WorkspaceStoreMutationResult result = atomic.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            workspace.Id, workspace.ContentRevision, workspace.Document.AuxiliaryStateDigest, replacement);
        if (!result.Success || result.Entry is not { } entry)
            return Blocked<CharacterCreationLifeModuleApplyReceipt>(
                result.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision);
        return new(CharacterCreationFoundationOutcomes.Success,
            new(preview.Request.Binding, workspace.ContentRevision, entry.ContentRevision, entry.SavedRevision,
                proposed.DraftRevision, proposed.DraftDigest, preview.Entry, CharacterEffectsApplied: false)
            { OriginDecisionAcceptance = originAcceptance }, []);
    }

    internal CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> ProjectJourney(
        WorkspaceStoredDocument workspace) => BuildJourney(workspace, null, false);

    internal CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> ProjectModule(
        WorkspaceStoredDocument workspace, CharacterCreationLifeModulePreviewRequest request,
        CharacterCreationLifeModuleJourneyState state)
        => EvaluateModule(request, workspace, state).Result;

    private CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> BuildJourney(
        WorkspaceStoredDocument workspace, IReadOnlyCollection<string>? sources, bool sourceFilterApplied)
    {
        try
        {
            return BuildJourneyCore(workspace, sources, sourceFilterApplied);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException)
        {
            return Blocked<CharacterCreationLifeModuleJourneyState>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.LifeModuleCatalogAuthorityRequired);
        }
    }

    private CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> BuildJourneyCore(
        WorkspaceStoredDocument workspace, IReadOnlyCollection<string>? sources, bool sourceFilterApplied)
    {
        string catalogDigest = _lifeModulesCatalog.GetAuthority().RawXmlDigest;
        var loaded = BuildState(workspace, sources, sourceFilterApplied);
        if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.AuthorityBlockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, state.AuthorityBlockers);
        if (state.PendingDraft is not { } draft)
            return Blocked<CharacterCreationLifeModuleJourneyState>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.PendingDraftInvalid);
        IReadOnlyList<string> semanticBlockers = ValidateContinuationDraft(workspace, sources, sourceFilterApplied);
        if (semanticBlockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, semanticBlockers);
        int stage = NextModuleStage(draft);
        LifeModuleLegalOptionDto[] options = _lifeModulesCatalog
            .GetOptionProjections(stage: null, state.Binding.EnabledSources)
            .Where(module => module.StageOrder == stage)
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal).ToArray();
        if (!DigestEquals(catalogDigest, _lifeModulesCatalog.GetAuthority().RawXmlDigest))
            return Blocked<CharacterCreationLifeModuleJourneyState>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.SourceDigestConflict);
        return new(CharacterCreationFoundationOutcomes.Success,
            new(state.Binding, draft.DraftRevision, draft.DraftDigest, stage, options,
                state.LifeModuleBudget, draft.AdditionalModules ?? []), []);
    }

    private (CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> Result,
        WorkspaceStoredDocument? Workspace) EvaluateModule(CharacterCreationLifeModulePreviewRequest request,
            WorkspaceStoredDocument? projectionWorkspace = null,
            CharacterCreationLifeModuleJourneyState? projectionState = null)
    {
        WorkspaceStoreReadResult read = projectionWorkspace is null
            ? _workspaceStore.Get(request.Binding.WorkspaceId)
            : new(WorkspaceOperationOutcome.Success, projectionWorkspace);
        if (!read.Success || read.Value is not { } workspace)
            return (ReadFailure<CharacterCreationLifeModulePreview>(read), null);
        var loaded = projectionState is null
            ? BuildJourney(workspace, request.Binding.EnabledSources, request.Binding.SourceFilterApplied)
            : new CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState>(
                CharacterCreationFoundationOutcomes.Success, projectionState, []);
        if (loaded.Value is not { } state)
            return (new(loaded.Outcome, null, loaded.Blockers), null);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(state.Binding, request.Binding)
            || state.DraftRevision != request.DraftRevision || !DigestEquals(state.DraftDigest, request.DraftDigest))
            return (Blocked<CharacterCreationLifeModulePreview>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision), null);
        CharacterCreationFoundationDraftLedger draft = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        var blockers = new List<string>();
        CharacterCreationLifeModuleDraftEntry? entry = ProjectModuleEntry(state.Options,
            request.Selection, request.FollowUpValues, draft.RequestedMetatype, state.CurrentStageOrder, blockers);
        if (entry is null)
            return (new(CharacterCreationFoundationOutcomes.Blocked, null, blockers), null);
        if (draft.DraftRevision == long.MaxValue || workspace.ContentRevision == long.MaxValue
            || state.AdditionalModules.Count >= 128)
            blockers.Add(CharacterCreationFoundationBlockers.PendingDraftConflict);
        if (entry.KarmaCost > decimal.MaxValue - state.Budget.Used)
            return (Blocked<CharacterCreationLifeModulePreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.LifeModuleBudgetExceeded), null);
        decimal used = state.Budget.Used + entry.KarmaCost;
        if (used > state.Budget.Total)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetExceeded);
        string[] normalized = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var after = state.Budget with { Used = used, Remaining = state.Budget.Total - used, Blockers = normalized };
        var normalizedRequest = request with { FollowUpValues = entry.FollowUpValues };
        string digest = Digest(new
        {
            Semantics = "chummer.sr5-life-module-journey-preview/v1",
            Request = normalizedRequest, Entry = entry, Before = state.Budget, After = after
        });
        var preview = new CharacterCreationLifeModulePreview(normalizedRequest, entry,
            state.Budget, after, normalized, normalized.Length == 0, digest);
        return (new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, preview, normalized), workspace);
    }

    private static int NextModuleStage(CharacterCreationFoundationDraftLedger draft)
        => Math.Min(LifeModuleJourneyStageOrders.FormativeYears + (draft.AdditionalModules?.Count ?? 0),
            LifeModuleJourneyStageOrders.RealLife);

    private static bool TryAddContinuationCost(CharacterCreationFoundationDraftLedger draft,
        IReadOnlyList<LifeModuleLegalOptionDto> modules, ref decimal cost, ref bool costIsExact)
    {
        if (draft.AdditionalModules is null)
            return true;
        if (draft.AdditionalModules.Count is 0 or > 128)
            return false;
        for (int i = 0; i < draft.AdditionalModules.Count; i++)
        {
            var entry = draft.AdditionalModules[i];
            var blockers = new List<string>();
            int stage = Math.Min(LifeModuleJourneyStageOrders.FormativeYears + i, LifeModuleJourneyStageOrders.RealLife);
            var projected = ProjectModuleEntry(modules, entry.Selection, entry.FollowUpValues,
                draft.RequestedMetatype, stage, blockers);
            if (projected is null || blockers.Count != 0
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(projected, entry))
            {
                costIsExact = false;
                return false;
            }
            if (projected.KarmaCost > decimal.MaxValue - cost)
                return false;
            cost += projected.KarmaCost;
        }
        return true;
    }

    private static CharacterCreationLifeModuleDraftEntry? ProjectModuleEntry(
        IReadOnlyList<LifeModuleLegalOptionDto> modules, CharacterCreationFoundationSelection selection,
        IReadOnlyDictionary<string, string>? followUps, string metatype, int stage, ICollection<string> blockers)
    {
        LifeModuleLegalOptionDto[] matches = modules.Where(module => module.ModuleId == selection.ModuleId).Take(2).ToArray();
        if (matches.Length != 1)
        {
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleSelectionInvalid);
            return null;
        }
        LifeModuleLegalOptionDto module = matches[0];
        if (module.StageOrder != stage || (stage == LifeModuleJourneyStageOrders.RealLife && !module.CanRepeat))
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleStageInvalid);
        var version = ResolveVersion(module, selection.VersionId, blockers);
        var requirements = module.Requirements.Concat(version?.Requirements ?? [])
            .Select(requirement => EvaluateRequirement(requirement, metatype)).ToArray();
        foreach (var requirement in requirements.Where(requirement => !requirement.IsMet))
            blockers.Add(requirement.DisableReasonKey ?? CharacterCreationFoundationBlockers.LifeModuleRequirementNotMet);
        foreach (string blocker in module.AuthorityBlockers.Concat(version?.AuthorityBlockers ?? [])
                     .Where(blocker => blocker != XmlCatalogCharacterAuthorityBlocker()))
            blockers.Add(blocker);
        var values = NormalizeFollowUpValues(followUps);
        ValidateFollowUps(module.FollowUps.Concat(version?.FollowUps ?? []).ToArray(), values, blockers);
        decimal karma = version?.KarmaCost ?? module.KarmaCost;
        if (!(version?.KarmaIsExact ?? module.KarmaIsExact) || karma < 0)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        string[] anchors = module.SourceAnchorIds.Concat(version?.SourceAnchorIds ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (anchors.Length == 0)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleCatalogAuthorityRequired);
        return new(module.StageOrder, module.StageId, selection, karma, requirements,
            [.. version?.Effects ?? [], .. module.Effects], values, anchors,
            string.IsNullOrWhiteSpace(version?.StoryTemplate) ? module.StoryTemplate : version.StoryTemplate);
    }
}
