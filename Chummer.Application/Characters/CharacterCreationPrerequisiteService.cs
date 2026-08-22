using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Authoritative, draft-only prerequisite lane for Priority and Sum-to-Ten.
/// The only persisted mutation is auxiliary wizard state; canonical character
/// XML and legacy priority fields remain untouched until full finalization.
/// </summary>
public sealed class CharacterCreationPrerequisiteService :
    ICharacterCreationPrerequisiteService
{
    private static readonly string[] s_AttributeIds =
        ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "RES", "ESS", "DEP"];

    private static readonly string[] s_LegacyPriorityElements =
    [
        "prioritymetatype",
        "priorityattributes",
        "priorityspecial",
        "priorityskills",
        "priorityresources",
        "prioritytalent"
    ];

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSourceDataResolver _sourceDataResolver;

    public CharacterCreationPrerequisiteService(
        IWorkspaceStore workspaceStore,
        ICharacterFileQueries characterFileQueries,
        ICharacterSourceDataResolver sourceDataResolver)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _characterFileQueries = characterFileQueries
                                ?? throw new ArgumentNullException(nameof(characterFileQueries));
        _sourceDataResolver = sourceDataResolver
                              ?? throw new ArgumentNullException(nameof(sourceDataResolver));
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(
        CharacterCreationPrerequisiteLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        return read.Success && read.Value is WorkspaceStoredDocument workspace
            ? BuildState(workspace)
            : ReadFailure<CharacterCreationPrerequisiteState>(read);
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(
        CharacterCreationPrerequisitePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(
        CharacterCreationPrerequisiteConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
        {
            return Blocked<CharacterCreationPrerequisiteReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationPrerequisiteBlockers.ExplicitConfirmationRequired);
        }

        PreviewEvaluation evaluation = EvaluatePreview(
            new CharacterCreationPrerequisitePreviewRequest(
                request.Binding,
                request.PriorityAssignments)
            {
                HeritageSelectionId = request.HeritageSelectionId,
                TalentSelectionId = request.TalentSelectionId,
                TalentActiveSkillSelectionIds = request.TalentActiveSkillSelectionIds,
                TalentSkillGroupSelectionIds = request.TalentSkillGroupSelectionIds
            });
        if (evaluation.Result.Value is not CharacterCreationPrerequisitePreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace
            || evaluation.Draft is not CharacterCreationPrerequisiteDraft draft)
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                preview.PreviewDigest,
                request.PreviewDigest))
        {
            return Blocked<CharacterCreationPrerequisiteReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationPrerequisiteBlockers.PreviewDigestMismatch);
        }
        if (!preview.CanConfirm || preview.Blockers.Count > 0)
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                preview.Blockers);
        }
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore)
        {
            return Blocked<CharacterCreationPrerequisiteReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired);
        }

        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationPrerequisiteDraft = draft
                }
            }
        };
        WorkspaceStoreMutationResult mutation =
            atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                workspace.Id,
                workspace.ContentRevision,
                workspace.Document.AuxiliaryStateDigest,
                replacement);
        if (!mutation.Success || mutation.Entry is not WorkspaceStoreEntry entry)
            return MutationFailure(mutation);

        int baseNormalAttributePoints = draft.Assignments.Single(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Attributes,
                StringComparison.Ordinal))
            .BaseNormalAttributePoints!.Value;
        return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt>(
            CharacterCreationFoundationOutcomes.Success,
            new CharacterCreationPrerequisiteReceipt(
                workspace.Id,
                workspace.ContentRevision,
                entry.ContentRevision,
                entry.SavedRevision,
                draft.BaseRawCharacterXmlDigest,
                draft.AuthorityDigest,
                draft.DraftRevision,
                draft.DraftDigest,
                checked(draft.CreationKarmaTotal - draft.CreationKarmaUsed),
                baseNormalAttributePoints,
                CharacterDocumentChanged: false)
            {
                EffectiveNormalAttributePoints = draft.EffectiveNormalAttributePoints,
                TotalSpecialAttributePoints = draft.TotalSpecialAttributePoints
            },
            []);
    }

    private PreviewEvaluation EvaluatePreview(
        CharacterCreationPrerequisitePreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            return new PreviewEvaluation(
                ReadFailure<CharacterCreationPrerequisitePreview>(read),
                null,
                null);
        }
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationPrerequisitePreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision),
                null,
                null);
        }

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> stateResult =
            BuildState(workspace);
        if (stateResult.Value is not CharacterCreationPrerequisiteState state)
        {
            return new PreviewEvaluation(
                new CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>(
                    stateResult.Outcome,
                    null,
                    stateResult.Blockers),
                null,
                null);
        }
        string? bindingBlocker = CompareBinding(state.Binding, request.Binding);
        if (bindingBlocker is not null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationPrerequisitePreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    bindingBlocker),
                null,
                null);
        }

        var blockers = new List<string>(state.Blockers);
        CharacterCreationPriorityAssignment[] assignments = ResolveAssignments(
            state.Authority,
            request.PriorityAssignments,
            blockers);
        int sumToTenUsed = 0;
        try
        {
            sumToTenUsed = checked(assignments.Sum(assignment => assignment.SumToTenValue));
        }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
        }

        ValidateSelectedRanks(state.Authority, assignments, sumToTenUsed, blockers);
        CharacterCreationPriorityHeritageSelection? heritageSelection = ResolveHeritageSelection(
            state.Authority,
            assignments,
            request.HeritageSelectionId,
            blockers);
        CharacterCreationPriorityTalentSelection? talentSelection = ResolveTalentSelection(
            state.Authority,
            assignments,
            request.TalentSelectionId,
            request.TalentActiveSkillSelectionIds,
            request.TalentSkillGroupSelectionIds,
            blockers);
        CharacterCreationPriorityAssignment? attributeAssignment = assignments
            .SingleOrDefault(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Attributes,
                StringComparison.Ordinal));
        int baseNormalAttributePoints = attributeAssignment?.BaseNormalAttributePoints ?? -1;
        if (baseNormalAttributePoints < 0)
            blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
        int effectiveNormalAttributePoints = 0;
        int totalSpecialAttributePoints = 0;
        if (heritageSelection is not null && talentSelection is not null && baseNormalAttributePoints >= 0)
        {
            effectiveNormalAttributePoints = heritageSelection.HalvesNormalAttributePoints
                ? baseNormalAttributePoints / 2
                : baseNormalAttributePoints;
            try
            {
                totalSpecialAttributePoints = checked(
                    heritageSelection.SpecialAttributePoints
                    + talentSelection.SpecialAttributePoints);
            }
            catch (OverflowException)
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
            }
        }

        CharacterCreationPrerequisiteDraft? draft = null;
        if (assignments.Length == CharacterCreationPriorityCategoryIds.Ordered.Count
            && state.Authority.CreationKarmaTotal is int creationKarmaTotal
            && creationKarmaTotal >= 0
            && baseNormalAttributePoints >= 0
            && heritageSelection is not null
            && talentSelection is not null)
        {
            draft = BuildProposedDraft(
                workspace,
                state.Authority,
                assignments,
                creationKarmaTotal,
                heritageSelection,
                talentSelection,
                effectiveNormalAttributePoints,
                totalSpecialAttributePoints);
            CharacterCreationPrerequisiteDraft? current = state.PendingDraft;
            if (current is not null)
            {
                if (current.DraftRevision == long.MaxValue)
                    blockers.Add(CharacterCreationPrerequisiteBlockers.DraftConflict);
                else if (CharacterCreationPrerequisiteDraftIntegrity.HasSameLogicalPayload(
                             current,
                             draft))
                    blockers.Add(CharacterCreationPrerequisiteBlockers.DraftDuplicate);
            }
        }

        CharacterCreationBudgetState budget = BuildBudget(
            state.Authority,
            draft ?? state.PendingDraft,
            blockers.Contains(
                CharacterCreationPrerequisiteBlockers.CreationKarmaAuthorityRequired,
                StringComparer.Ordinal));
        blockers.AddRange(budget.Blockers);
        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        var preview = new CharacterCreationPrerequisitePreview(
            CharacterCreationPrerequisiteSchemas.PreviewV1,
            state.Binding,
            assignments,
            budget,
            sumToTenUsed,
            state.Authority.SumToTenTarget,
            Math.Max(0, baseNormalAttributePoints),
            RequiresMetatypeAttributeAdjustment: heritageSelection is null || talentSelection is null,
            normalizedBlockers,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalizedBlockers.Length == 0 && draft is not null,
            PreviewDigest: string.Empty)
        {
            HeritageSelection = heritageSelection,
            TalentSelection = talentSelection,
            EffectiveNormalAttributePoints = effectiveNormalAttributePoints,
            TotalSpecialAttributePoints = totalSpecialAttributePoints
        };
        preview = preview with
        {
            PreviewDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(preview with { PreviewDigest = string.Empty })
        };
        return new PreviewEvaluation(
            new CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>(
                normalizedBlockers.Length == 0
                    ? CharacterCreationFoundationOutcomes.Success
                    : CharacterCreationFoundationOutcomes.Blocked,
                preview,
                normalizedBlockers),
            workspace,
            draft);
    }

    private CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> BuildState(
        WorkspaceStoredDocument workspace)
    {
        CharacterDocument characterDocument = new(workspace.Document.Content);
        CharacterValidationResult validation;
        CharacterFileSummary summary;
        try
        {
            validation = _characterFileQueries.Validate(characterDocument);
            summary = _characterFileQueries.ParseSummary(characterDocument);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or FormatException
                                          or InvalidDataException
                                          or InvalidOperationException
                                          or XmlException)
        {
            return Blocked<CharacterCreationPrerequisiteState>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
        }
        if (!validation.IsValid)
        {
            return Blocked<CharacterCreationPrerequisiteState>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
        }

        var blockers = new List<string>();
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            })
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired);
        }

        CharacterCreationPrerequisiteAuthority authority =
            CharacterCreationPrerequisiteAuthority.Unavailable;
        ICharacterSourceDataContext? sourceContext =
            _sourceDataResolver.TryCreateContext(workspace.Document.Content);
        bool hasAuthority = sourceContext is not null
                            && sourceContext.TryResolveCreationPrerequisiteAuthority(out authority)
                            && IsValidAuthority(authority);
        if (!hasAuthority)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            blockers.AddRange(authority.Blockers ?? []);
        }

        if (!string.Equals(
                RulesetDefaults.NormalizeOptional(workspace.Document.RulesetId),
                RulesetDefaults.Sr5,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationPrerequisiteBlockers.RulesetSr5Required);
        if (summary.Created)
            blockers.Add(CharacterCreationPrerequisiteBlockers.CharacterAlreadyCreated);
        if (summary.BuildMethod is not (CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen))
            blockers.Add(CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);
        else if (hasAuthority
                 && !string.Equals(summary.BuildMethod, authority.BuildMethod, StringComparison.Ordinal))
            blockers.Add(CharacterCreationPrerequisiteBlockers.BuildMethodMismatch);

        string rawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        var binding = new CharacterCreationPrerequisiteBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawCharacterXmlDigest,
            workspace.Document.AuxiliaryStateDigest,
            authority.AuthorityDigest);

        CharacterCreationPrerequisiteDraft? pendingDraft = null;
        CharacterCreationPrerequisiteDraft? persistedDraft = workspace.Document
            .AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (persistedDraft is not null)
        {
            if (hasAuthority
                && CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                    persistedDraft,
                    workspace.Id,
                    workspace.ContentRevision,
                    rawCharacterXmlDigest,
                    authority))
            {
                pendingDraft = persistedDraft;
            }
            else
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.DraftInvalid);
            }
        }
        else if (HasLegacyPriorityState(workspace.Document.Content))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.LegacyPriorityStateRequiresImport);
        }
        if (workspace.Document.AuxiliaryState.CharacterCreationAttributesDraft is not null)
            blockers.Add(CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists);

        CharacterCreationBudgetState budget = BuildBudget(
            authority,
            pendingDraft,
            !hasAuthority || authority.CreationKarmaTotal is null);
        blockers.AddRange(budget.Blockers);
        int? baseNormalAttributePoints = pendingDraft?.Assignments.SingleOrDefault(assignment =>
                string.Equals(
                    assignment.CategoryId,
                    CharacterCreationPriorityCategoryIds.Attributes,
                    StringComparison.Ordinal))
            ?.BaseNormalAttributePoints;
        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        var state = new CharacterCreationPrerequisiteState(
            CharacterCreationPrerequisiteSchemas.SnapshotV1,
            binding,
            workspace.Document.RulesetId,
            summary.BuildMethod,
            summary.Created,
            authority,
            budget,
            pendingDraft,
            baseNormalAttributePoints,
            RequiresMetatypeAttributeAdjustment: pendingDraft is null,
            CanEnterAttributes: pendingDraft is not null
                                && !normalizedBlockers.Contains(
                                    CharacterCreationPrerequisiteBlockers.DraftInvalid,
                                    StringComparer.Ordinal),
            Blockers: normalizedBlockers,
            SnapshotDigest: string.Empty)
        {
            EffectiveNormalAttributePoints = pendingDraft?.EffectiveNormalAttributePoints,
            TotalSpecialAttributePoints = pendingDraft?.TotalSpecialAttributePoints
        };
        state = state with
        {
            SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(state with { SnapshotDigest = string.Empty })
        };
        return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>(
            CharacterCreationFoundationOutcomes.Success,
            state,
            normalizedBlockers);
    }

    private static CharacterCreationPriorityAssignment[] ResolveAssignments(
        CharacterCreationPrerequisiteAuthority authority,
        IReadOnlyDictionary<string, string>? requested,
        ICollection<string> blockers)
    {
        if (requested is null
            || requested.Count != CharacterCreationPriorityCategoryIds.Ordered.Count
            || CharacterCreationPriorityCategoryIds.Ordered.Any(category =>
                !requested.ContainsKey(category)))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionIncomplete);
            return [];
        }
        if (requested.Keys.Any(key =>
                !CharacterCreationPriorityCategoryIds.Ordered.Contains(key, StringComparer.Ordinal)))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
            return [];
        }

        var assignments = new List<CharacterCreationPriorityAssignment>();
        for (int order = 0; order < CharacterCreationPriorityCategoryIds.Ordered.Count; order++)
        {
            string category = CharacterCreationPriorityCategoryIds.Ordered[order];
            string? rank = requested[category];
            if (string.IsNullOrWhiteSpace(rank)
                || !string.Equals(rank, rank.Trim(), StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
                continue;
            }
            CharacterCreationPriorityOptionProjection[] matches = authority.Options
                .Where(option => string.Equals(option.CategoryId, category, StringComparison.Ordinal)
                                 && string.Equals(option.Rank, rank, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
                continue;
            }
            CharacterCreationPriorityOptionProjection option = matches[0];
            assignments.Add(new CharacterCreationPriorityAssignment(
                order,
                category,
                option.Rank,
                option.SourceId,
                option.SourceNodeDigest,
                option.SumToTenValue,
                option.BaseNormalAttributePoints,
                option.SourceAnchorIds.ToArray()));
        }
        return assignments.ToArray();
    }

    private static CharacterCreationPriorityHeritageSelection? ResolveHeritageSelection(
        CharacterCreationPrerequisiteAuthority authority,
        IReadOnlyList<CharacterCreationPriorityAssignment> assignments,
        string? selectionId,
        ICollection<string> blockers)
    {
        CharacterCreationPriorityAssignment? assignment = assignments.SingleOrDefault(item =>
            string.Equals(item.CategoryId, CharacterCreationPriorityCategoryIds.Heritage, StringComparison.Ordinal));
        if (assignment is null || string.IsNullOrWhiteSpace(selectionId))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionIncomplete);
            return null;
        }
        CharacterCreationPriorityOptionProjection? option = authority.Options.SingleOrDefault(item =>
            string.Equals(item.SourceId, assignment.SourceId, StringComparison.Ordinal));
        CharacterCreationPriorityHeritageOptionProjection[] matches = option?.HeritageOptions
            .Where(item => string.Equals(item.SelectionId, selectionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray() ?? [];
        if (matches.Length != 1)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionInvalid);
            return null;
        }
        CharacterCreationPriorityHeritageOptionProjection selected = matches[0];
        if (!selected.IsEnabled || selected.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
            foreach (string blocker in selected.Blockers)
                blockers.Add(blocker);
            return null;
        }
        return new CharacterCreationPriorityHeritageSelection(
            selected.SelectionId,
            selected.Kind,
            assignment.SourceId,
            selected.MetatypeSourceId,
            selected.MetavariantSourceId,
            selected.MetatypeName,
            selected.MetavariantName,
            selected.SpecialAttributePoints,
            selected.KarmaCost,
            selected.HalvesNormalAttributePoints,
            selected.Attributes.ToArray(),
            selected.PriorityChildNodeDigest,
            selected.MetatypeSourceNodeDigest,
            selected.SourceAnchorIds.ToArray());
    }

    private static CharacterCreationPriorityTalentSelection? ResolveTalentSelection(
        CharacterCreationPrerequisiteAuthority authority,
        IReadOnlyList<CharacterCreationPriorityAssignment> assignments,
        string? selectionId,
        IReadOnlyList<string>? activeSkillSelectionIds,
        IReadOnlyList<string>? skillGroupSelectionIds,
        ICollection<string> blockers)
    {
        CharacterCreationPriorityAssignment? assignment = assignments.SingleOrDefault(item =>
            string.Equals(item.CategoryId, CharacterCreationPriorityCategoryIds.Talent, StringComparison.Ordinal));
        if (assignment is null || string.IsNullOrWhiteSpace(selectionId))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSelectionIncomplete);
            return null;
        }
        CharacterCreationPriorityOptionProjection? option = authority.Options.SingleOrDefault(item =>
            string.Equals(item.SourceId, assignment.SourceId, StringComparison.Ordinal));
        CharacterCreationPriorityTalentOptionProjection[] matches = option?.TalentOptions
            .Where(item => string.Equals(item.SelectionId, selectionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray() ?? [];
        if (matches.Length != 1)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSelectionInvalid);
            return null;
        }
        CharacterCreationPriorityTalentOptionProjection selected = matches[0];
        CharacterCreationTalentGrantPlanContribution? grantPlan = ResolveTalentGrantPlan(
            selected,
            activeSkillSelectionIds,
            skillGroupSelectionIds,
            blockers);
        if (!selected.IsEnabled || selected.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSelectionUnsupported);
            foreach (string blocker in selected.Blockers)
                blockers.Add(blocker);
        }
        return new CharacterCreationPriorityTalentSelection(
            selected.SelectionId,
            assignment.SourceId,
            selected.Name,
            selected.Value,
            selected.SpecialAttributePoints,
            selected.Magic,
            selected.Resonance,
            selected.Depth,
            selected.GrantedQualities.ToArray(),
            selected.PriorityChildNodeDigest,
            selected.SourceAnchorIds
                .Concat(grantPlan?.SourceAnchorIds ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray())
        {
            GrantPlan = grantPlan
        };
    }

    private static CharacterCreationTalentGrantPlanContribution? ResolveTalentGrantPlan(
        CharacterCreationPriorityTalentOptionProjection talent,
        IReadOnlyList<string>? activeSkillSelectionIds,
        IReadOnlyList<string>? skillGroupSelectionIds,
        ICollection<string> blockers)
    {
        string[] requestedSkills = activeSkillSelectionIds?.ToArray() ?? [];
        string[] requestedGroups = skillGroupSelectionIds?.ToArray() ?? [];
        var activeEntries = new List<CharacterCreationTalentActiveSkillGrantPlanEntry>();
        var groupEntries = new List<CharacterCreationTalentSkillGroupGrantPlanEntry>();

        if (talent.ActiveSkillGrant is not CharacterCreationTalentActiveSkillGrantProjection activeGrant)
        {
            if (requestedSkills.Length != 0)
                blockers.Add(CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionInvalid);
        }
        else if (!activeGrant.IsSupported || activeGrant.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
            foreach (string blocker in activeGrant.Blockers)
                blockers.Add(blocker);
        }
        else if (requestedSkills.Length < activeGrant.Quantity)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionIncomplete);
        }
        else if (requestedSkills.Length != activeGrant.Quantity
                 || requestedSkills.Distinct(StringComparer.Ordinal).Count()
                 != requestedSkills.Length)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionInvalid);
        }
        else
        {
            foreach (string selectionId in requestedSkills.OrderBy(id => id, StringComparer.Ordinal))
            {
                CharacterCreationTalentActiveSkillChoiceProjection[] matches = activeGrant.Options
                    .Where(option => string.Equals(
                        option.SelectionId,
                        selectionId,
                        StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (matches.Length != 1)
                {
                    blockers.Add(CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionInvalid);
                    continue;
                }
                CharacterCreationTalentActiveSkillChoiceProjection option = matches[0];
                activeEntries.Add(new CharacterCreationTalentActiveSkillGrantPlanEntry(
                    option.SelectionId,
                    "active-skill",
                    option.SourceId,
                    option.CanonicalName,
                    option.Category,
                    option.SkillGroup,
                    activeGrant.BaseRating,
                    option.SourceNodeDigest,
                    option.SkillsSourceDigest,
                    option.SourceAnchorIds.ToArray()));
            }
        }

        if (talent.SkillGroupGrant is not CharacterCreationTalentSkillGroupGrantProjection groupGrant)
        {
            if (requestedGroups.Length != 0)
                blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGroupSelectionInvalid);
        }
        else if (!groupGrant.IsSupported || groupGrant.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
            foreach (string blocker in groupGrant.Blockers)
                blockers.Add(blocker);
        }
        else if (requestedGroups.Length < groupGrant.Quantity)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGroupSelectionIncomplete);
        }
        else if (requestedGroups.Length != groupGrant.Quantity
                 || requestedGroups.Distinct(StringComparer.Ordinal).Count()
                 != requestedGroups.Length)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGroupSelectionInvalid);
        }
        else
        {
            foreach (string selectionId in requestedGroups.OrderBy(id => id, StringComparer.Ordinal))
            {
                CharacterCreationTalentSkillGroupChoiceProjection[] matches = groupGrant.Options
                    .Where(option => string.Equals(
                        option.SelectionId,
                        selectionId,
                        StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (matches.Length != 1)
                {
                    blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGroupSelectionInvalid);
                    continue;
                }
                CharacterCreationTalentSkillGroupChoiceProjection option = matches[0];
                groupEntries.Add(new CharacterCreationTalentSkillGroupGrantPlanEntry(
                    option.SelectionId,
                    "skill-group",
                    option.CanonicalName,
                    option.MemberSkillSourceIds.ToArray(),
                    groupGrant.BaseRating,
                    option.GroupDigest,
                    option.SkillsSourceDigest,
                    option.SourceAnchorIds.ToArray()));
            }
        }

        if (talent.ActiveSkillGrant is null && talent.SkillGroupGrant is null)
            return null;
        string[] anchors = activeEntries.SelectMany(entry => entry.SourceAnchorIds)
            .Concat(groupEntries.SelectMany(entry => entry.SourceAnchorIds))
            .Concat(talent.ActiveSkillGrant?.SourceAnchorIds ?? [])
            .Concat(talent.SkillGroupGrant?.SourceAnchorIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        var plan = new CharacterCreationTalentGrantPlanContribution(
            CharacterCreationPrerequisiteSchemas.TalentGrantPlanV1,
            activeEntries.ToArray(),
            groupEntries.ToArray(),
            anchors,
            PlanDigest: string.Empty);
        return plan with
        {
            PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                plan with { PlanDigest = string.Empty })
        };
    }

    private static void ValidateSelectedRanks(
        CharacterCreationPrerequisiteAuthority authority,
        IReadOnlyList<CharacterCreationPriorityAssignment> assignments,
        int sumToTenUsed,
        ICollection<string> blockers)
    {
        if (assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count)
            return;
        if (string.Equals(
                authority.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            if (!assignments.Select(assignment => assignment.Rank)
                    .OrderBy(rank => rank, StringComparer.Ordinal)
                    .SequenceEqual(
                        authority.PriorityArray.OrderBy(rank => rank, StringComparer.Ordinal),
                        StringComparer.Ordinal))
                blockers.Add(CharacterCreationPrerequisiteBlockers.SelectionInvalid);
            return;
        }
        if (!string.Equals(
                authority.BuildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);
            return;
        }
        if (authority.SumToTenTarget is not int target)
            blockers.Add(CharacterCreationPrerequisiteBlockers.SumToTenTargetInvalid);
        else if (sumToTenUsed != target)
            blockers.Add(CharacterCreationPrerequisiteBlockers.SumToTenMismatch);
    }

    private static CharacterCreationPrerequisiteDraft BuildProposedDraft(
        WorkspaceStoredDocument workspace,
        CharacterCreationPrerequisiteAuthority authority,
        IReadOnlyList<CharacterCreationPriorityAssignment> assignments,
        int creationKarmaTotal,
        CharacterCreationPriorityHeritageSelection heritageSelection,
        CharacterCreationPriorityTalentSelection talentSelection,
        int effectiveNormalAttributePoints,
        int totalSpecialAttributePoints)
    {
        CharacterCreationPrerequisiteDraft? current = workspace.Document.AuxiliaryState
            .CharacterCreationPrerequisiteDraft;
        long nextDraftRevision = current is null
            ? 1
            : current.DraftRevision == long.MaxValue
                ? long.MaxValue
                : current.DraftRevision + 1;
        string[] anchors = authority.SourceAnchorIds
            .Concat(assignments.SelectMany(assignment => assignment.SourceAnchorIds))
            .Concat(heritageSelection.SourceAnchorIds)
            .Concat(talentSelection.SourceAnchorIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var draft = new CharacterCreationPrerequisiteDraft(
            CharacterCreationPrerequisiteSchemas.DraftV1,
            workspace.Id,
            nextDraftRevision,
            workspace.ContentRevision,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                workspace.Document.Content),
            authority.AuthorityDigest,
            authority.BuildMethod,
            authority.SettingsProfileId,
            authority.PriorityTable,
            authority.PriorityArray.ToArray(),
            authority.SumToTenTarget,
            assignments.ToArray(),
            creationKarmaTotal,
            CreationKarmaUsed: heritageSelection.KarmaCost,
            SourceAnchorIds: anchors,
            DraftDigest: string.Empty)
        {
            HeritageSelection = heritageSelection,
            TalentSelection = talentSelection,
            EffectiveNormalAttributePoints = effectiveNormalAttributePoints,
            TotalSpecialAttributePoints = totalSpecialAttributePoints
        };
        return draft with
        {
            DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(draft)
        };
    }

    private static CharacterCreationBudgetState BuildBudget(
        CharacterCreationPrerequisiteAuthority authority,
        CharacterCreationPrerequisiteDraft? pendingDraft,
        bool unavailable)
    {
        int total = authority.CreationKarmaTotal.GetValueOrDefault();
        int used = pendingDraft?.CreationKarmaUsed ?? 0;
        var blockers = new List<string>();
        if (unavailable || authority.CreationKarmaTotal is null)
            blockers.Add(CharacterCreationPrerequisiteBlockers.CreationKarmaAuthorityRequired);
        if (used < 0 || used > total)
            blockers.Add(CharacterCreationPrerequisiteBlockers.CreationKarmaExceeded);
        int remaining = used is >= 0 && used <= total ? total - used : 0;
        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        return new CharacterCreationBudgetState(
            CharacterCreationBudgetIds.Karma,
            "Creation Karma",
            total,
            used,
            remaining,
            IsExact: normalized.Length == 0,
            Blockers: normalized,
            Unit: "karma");
    }

    private static bool IsValidAuthority(CharacterCreationPrerequisiteAuthority authority)
    {
        if (!string.Equals(
                authority.Schema,
                CharacterCreationPrerequisiteSchemas.AuthorityV1,
                StringComparison.Ordinal)
            || !authority.IsAuthoritative
            || authority.Blockers is null
            || authority.Blockers.Count != 0
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || authority.BuildMethod is not (CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
            || authority.CreationKarmaTotal is not int creationKarmaTotal
            || creationKarmaTotal < 0
            || authority.PriorityArray is null
            || authority.PriorityArray.Count != CharacterCreationPriorityCategoryIds.Ordered.Count
            || authority.PriorityArray.Any(rank => string.IsNullOrWhiteSpace(rank)
                                                   || rank.Length != 1
                                                   || rank[0] is < 'A' or > 'Z')
            || string.IsNullOrWhiteSpace(authority.PriorityTable)
            || !string.Equals(
                authority.PriorityTable,
                authority.PriorityTable.Trim(),
                StringComparison.Ordinal)
            || authority.SumToTenTarget is not int sumToTenTarget
            || sumToTenTarget < 0
            || authority.RankWeights is null
            || authority.Options is null
            || authority.SourceAnchorIds is null
            || authority.SourceAnchorIds.Count == 0
            || authority.SourceAnchorIds.Any(anchor => string.IsNullOrWhiteSpace(anchor)
                                                       || !string.Equals(
                                                           anchor,
                                                           anchor.Trim(),
                                                           StringComparison.Ordinal))
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.RawProfileInputsDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.RawPrioritiesXmlDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.EffectivePrioritiesInputsDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.SelectedPriorityCustomDataInputsDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.SelectedCustomDataInputsDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.RawMetatypesXmlDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.EffectiveMetatypesInputsDigest)
            || authority.MaxNumberMaxAttributesCreate is not int maxAtMaximum
            || maxAtMaximum < 0
            || authority.KarmaAttribute is not int karmaAttribute
            || karmaAttribute <= 0
            || authority.AlternateMetatypeAttributeKarma is null
            || authority.ReverseAttributePriorityOrder is null
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                authority.AuthorityDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                authority.AuthorityDigest,
                CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)))
        {
            return false;
        }

        string[] allowedRanks = authority.PriorityArray
            .Distinct(StringComparer.Ordinal)
            .OrderBy(rank => rank, StringComparer.Ordinal)
            .ToArray();
        if (authority.RankWeights.Any(weight => weight is null)
            || authority.RankWeights.GroupBy(weight => weight.Rank, StringComparer.Ordinal)
                .Any(group => group.Count() != 1)
            || allowedRanks.Any(rank => authority.RankWeights.Count(weight =>
                string.Equals(weight.Rank, rank, StringComparison.Ordinal)) != 1)
            || authority.RankWeights.Any(weight => string.IsNullOrWhiteSpace(weight.Rank)
                                                   || weight.Rank.Length != 1
                                                   || weight.Value < 0
                                                   || weight.SourceAnchorIds is null
                                                   || weight.SourceAnchorIds.Count == 0
                                                   || weight.SourceAnchorIds.Any(anchor =>
                                                       string.IsNullOrWhiteSpace(anchor)
                                                       || !string.Equals(
                                                           anchor,
                                                           anchor.Trim(),
                                                           StringComparison.Ordinal))))
        {
            return false;
        }

        int expectedOptions = checked(
            CharacterCreationPriorityCategoryIds.Ordered.Count * allowedRanks.Length);
        if (authority.Options.Count != expectedOptions)
            return false;
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CharacterCreationPriorityOptionProjection? option in authority.Options)
        {
            if (option is null
                || !CharacterCreationPriorityCategoryIds.Ordered.Contains(
                    option.CategoryId,
                    StringComparer.Ordinal)
                || !allowedRanks.Contains(option.Rank, StringComparer.Ordinal)
                || !Guid.TryParseExact(option.SourceId, "D", out Guid id)
                || id == Guid.Empty
                || !string.Equals(option.SourceId, id.ToString("D"), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(option.Label)
                || !string.Equals(option.Label, option.Label.Trim(), StringComparison.Ordinal)
                || option.SumToTenValue < 0
                || (string.Equals(
                        option.CategoryId,
                        CharacterCreationPriorityCategoryIds.Attributes,
                        StringComparison.Ordinal)
                        ? option.BaseNormalAttributePoints is null or < 0
                    : option.BaseNormalAttributePoints is not null)
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                    option.SourceNodeDigest)
                || option.SourceAnchorIds is null
                || option.SourceAnchorIds.Count == 0
                || option.SourceAnchorIds.Any(anchor => string.IsNullOrWhiteSpace(anchor)
                                                        || !string.Equals(
                                                            anchor,
                                                            anchor.Trim(),
                                                            StringComparison.Ordinal))
                || !pairs.Add($"{option.CategoryId}\0{option.Rank}")
                || !ids.Add(option.SourceId)
                || option.HeritageOptions is null
                || option.TalentOptions is null
                || (string.Equals(
                        option.CategoryId,
                        CharacterCreationPriorityCategoryIds.Heritage,
                        StringComparison.Ordinal)
                        ? option.HeritageOptions.Count == 0 || option.TalentOptions.Count != 0
                    : option.HeritageOptions.Count != 0)
                || (string.Equals(
                        option.CategoryId,
                        CharacterCreationPriorityCategoryIds.Talent,
                        StringComparison.Ordinal)
                        ? option.TalentOptions.Count == 0 || option.HeritageOptions.Count != 0
                    : option.TalentOptions.Count != 0)
                || option.HeritageOptions.Any(child => !IsValidHeritageOption(child))
                || option.TalentOptions.Any(child => !IsValidTalentOption(
                    child,
                    authority.EffectiveSkillsInputsDigest)))
            {
                return false;
            }
            CharacterCreationPriorityRankWeight weight = authority.RankWeights.Single(item =>
                string.Equals(item.Rank, option.Rank, StringComparison.Ordinal));
            if (weight.Value != option.SumToTenValue)
                return false;
        }
        return true;
    }

    private static bool IsValidHeritageOption(
        CharacterCreationPriorityHeritageOptionProjection? option)
    {
        return option is not null
               && !string.IsNullOrWhiteSpace(option.SelectionId)
               && option.Kind is CharacterCreationPriorityChildKinds.Metatype
                   or CharacterCreationPriorityChildKinds.Metavariant
               && option.SpecialAttributePoints >= 0
               && option.KarmaCost >= 0
               && option.Attributes is not null
               && option.Blockers is not null
               && option.IsEnabled == (option.Blockers.Count == 0)
               && option.SourceAnchorIds is { Count: > 0 }
               && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                   option.PriorityChildNodeDigest)
               && (!option.IsEnabled
                   || option.Blockers.Count == 0
                   && Guid.TryParseExact(option.MetatypeSourceId, "D", out Guid metatypeId)
                   && metatypeId != Guid.Empty
                   && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                       option.MetatypeSourceNodeDigest)
                   && option.Attributes.Count == s_AttributeIds.Length
                   && option.Attributes.Select(item => item.AttributeId)
                       .SequenceEqual(s_AttributeIds, StringComparer.Ordinal)
                   && option.Attributes.All(item => item.Minimum >= 0
                                                    && item.Minimum <= item.Maximum
                                                    && item.Maximum <= item.AugmentedMaximum));
    }

    private static bool IsValidTalentOption(
        CharacterCreationPriorityTalentOptionProjection? option,
        string effectiveSkillsInputsDigest)
    {
        return option is not null
               && !string.IsNullOrWhiteSpace(option.SelectionId)
               && !string.IsNullOrWhiteSpace(option.Name)
               && !string.IsNullOrWhiteSpace(option.Value)
               && option.SpecialAttributePoints >= 0
               && option.GrantedQualities is not null
               && option.Blockers is not null
               && option.IsEnabled == (option.Blockers.Count == 0)
               && option.SourceAnchorIds is { Count: > 0 }
               && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                   option.PriorityChildNodeDigest)
               && IsValidActiveSkillGrant(option.ActiveSkillGrant, effectiveSkillsInputsDigest)
               && IsValidSkillGroupGrant(option.SkillGroupGrant, effectiveSkillsInputsDigest)
               && (!option.IsEnabled
                   || option.Blockers.Count == 0
                   && option.ActiveSkillGrant is null
                   && option.SkillGroupGrant is null);
    }

    private static bool IsValidActiveSkillGrant(
        CharacterCreationTalentActiveSkillGrantProjection? grant,
        string effectiveSkillsInputsDigest)
    {
        if (grant is null)
            return true;
        return CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(effectiveSkillsInputsDigest)
               && grant.Quantity > 0
               && grant.BaseRating >= 0
               && grant.SkillType is CharacterCreationTalentSkillGrantTypes.Active
                   or CharacterCreationTalentSkillGrantTypes.Magic
                   or CharacterCreationTalentSkillGrantTypes.Resonance
               && grant.Options is not null
               && grant.Blockers is not null
               && grant.SourceAnchorIds is { Count: > 0 }
               && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(grant.GrantDigest)
               && grant.IsSupported == (grant.Blockers.Count == 0)
               && (!grant.IsSupported || grant.Options.Count >= grant.Quantity)
               && grant.Options.Select(option => option.SelectionId)
                   .Distinct(StringComparer.Ordinal).Count() == grant.Options.Count
               && grant.Options.All(option => Guid.TryParseExact(
                                                  option.SelectionId,
                                                  "D",
                                                  out Guid selectionId)
                                              && selectionId != Guid.Empty
                                              && string.Equals(
                                                  option.SelectionId,
                                                  option.SourceId,
                                                  StringComparison.Ordinal)
                                              && !string.IsNullOrWhiteSpace(option.CanonicalName)
                                              && !string.IsNullOrWhiteSpace(option.Category)
                                              && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                                  option.SourceNodeDigest)
                                              && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                                                  option.SkillsSourceDigest,
                                                  effectiveSkillsInputsDigest)
                                              && option.SourceAnchorIds is { Count: > 0 });
    }

    private static bool IsValidSkillGroupGrant(
        CharacterCreationTalentSkillGroupGrantProjection? grant,
        string effectiveSkillsInputsDigest)
    {
        if (grant is null)
            return true;
        return CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(effectiveSkillsInputsDigest)
               && grant.Quantity > 0
               && grant.BaseRating >= 0
               && string.Equals(
                   grant.SkillGroupType,
                   CharacterCreationTalentSkillGrantTypes.Choices,
                   StringComparison.Ordinal)
               && grant.Options is not null
               && grant.Blockers is not null
               && grant.SourceAnchorIds is { Count: > 0 }
               && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(grant.GrantDigest)
               && grant.IsSupported == (grant.Blockers.Count == 0)
               && (!grant.IsSupported || grant.Options.Count >= grant.Quantity)
               && grant.Options.Select(option => option.SelectionId)
                   .Distinct(StringComparer.Ordinal).Count() == grant.Options.Count
               && grant.Options.All(option => !string.IsNullOrWhiteSpace(option.SelectionId)
                                              && !string.IsNullOrWhiteSpace(option.CanonicalName)
                                              && option.MemberSkillSourceIds is { Count: > 0 }
                                              && option.MemberSkillSourceIds.All(sourceId =>
                                                  Guid.TryParseExact(sourceId, "D", out Guid parsed)
                                                  && parsed != Guid.Empty)
                                              && option.MemberSkillSourceIds
                                                  .Distinct(StringComparer.Ordinal).Count()
                                                  == option.MemberSkillSourceIds.Count
                                              && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                                  option.GroupDigest)
                                              && CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                                                  option.SkillsSourceDigest,
                                                  effectiveSkillsInputsDigest)
                                              && option.SourceAnchorIds is { Count: > 0 });
    }

    private static string? CompareBinding(
        CharacterCreationPrerequisiteBinding current,
        CharacterCreationPrerequisiteBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.RawCharacterXmlDigest,
                requested.RawCharacterXmlDigest))
            return CharacterCreationPrerequisiteBlockers.StaleRawCharacterXmlDigest;
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.AuxiliaryStateDigest,
                requested.AuxiliaryStateDigest))
            return CharacterCreationPrerequisiteBlockers.DraftConflict;
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.AuthorityDigest,
                requested.AuthorityDigest))
            return CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift;
        return null;
    }

    private static bool HasLegacyPriorityState(string xml)
    {
        try
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.None);
            XElement? root = document.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
                return true;
            return s_LegacyPriorityElements.Any(name => root.Elements(name).Any(element =>
                element.HasElements || !string.IsNullOrWhiteSpace(element.Value)));
        }
        catch (XmlException)
        {
            return true;
        }
    }

    private static CharacterCreationFoundationResult<T> ReadFailure<T>(
        WorkspaceStoreReadResult read)
        where T : class
    {
        string outcome = read.Outcome == WorkspaceOperationOutcome.Missing
            ? CharacterCreationFoundationOutcomes.Missing
            : CharacterCreationFoundationOutcomes.Invalid;
        return Blocked<T>(outcome, CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable);
    }

    private static CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt>
        MutationFailure(WorkspaceStoreMutationResult mutation)
    {
        string outcome = mutation.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCreationFoundationOutcomes.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCreationFoundationOutcomes.Conflict,
            _ => CharacterCreationFoundationOutcomes.Invalid
        };
        string blocker = mutation.Outcome == WorkspaceOperationOutcome.Conflict
            ? CharacterCreationPrerequisiteBlockers.DraftConflict
            : CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired;
        return Blocked<CharacterCreationPrerequisiteReceipt>(outcome, blocker);
    }

    private static CharacterCreationFoundationResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class =>
        new(outcome, null, blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Result,
        WorkspaceStoredDocument? Workspace,
        CharacterCreationPrerequisiteDraft? Draft);
}
