using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationAttributesService : ICharacterCreationAttributesService
{
    private static readonly string[] s_NormalAttributeIds =
        ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL"];

    private static readonly string[] s_SpecialAttributeIds =
        ["EDG", "MAG", "RES", "ESS", "DEP"];

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterSourceDataResolver _sourceDataResolver;

    public CharacterCreationAttributesService(
        IWorkspaceStore workspaceStore,
        ICharacterSourceDataResolver sourceDataResolver)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _sourceDataResolver = sourceDataResolver ?? throw new ArgumentNullException(nameof(sourceDataResolver));
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesState> Load(
        CharacterCreationAttributesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        return read.Success && read.Value is WorkspaceStoredDocument workspace
            ? BuildState(workspace)
            : Blocked<CharacterCreationAttributesState>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing
                    : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributesBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
        CharacterCreationAttributesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> Confirm(
        CharacterCreationAttributesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
        {
            return Blocked<CharacterCreationAttributesReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributesBlockers.ExplicitConfirmationRequired);
        }

        PreviewEvaluation evaluation = EvaluatePreview(
            new CharacterCreationAttributesPreviewRequest(request.Binding, request.Allocations));
        if (evaluation.Result.Value is not CharacterCreationAttributesPreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace
            || evaluation.Draft is not CharacterCreationAttributesDraft draft)
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                preview.PreviewDigest,
                request.PreviewDigest))
        {
            return Blocked<CharacterCreationAttributesReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributesBlockers.PreviewDigestMismatch);
        }
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                preview.Blockers);
        }
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore)
        {
            return Blocked<CharacterCreationAttributesReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
        }

        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationAttributesDraft = draft
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
        {
            return Blocked<CharacterCreationAttributesReceipt>(
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict
                    : CharacterCreationFoundationOutcomes.Invalid,
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationAttributesBlockers.DraftConflict
                    : CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
        }

        return new CharacterCreationFoundationResult<CharacterCreationAttributesReceipt>(
            CharacterCreationFoundationOutcomes.Success,
            new CharacterCreationAttributesReceipt(
                workspace.Id,
                workspace.ContentRevision,
                entry.ContentRevision,
                entry.SavedRevision,
                draft.DraftRevision,
                draft.DraftDigest,
                draft.NormalPointTotal - draft.NormalPointUsed,
                draft.SpecialPointTotal - draft.SpecialPointUsed,
                draft.CreationKarmaTotal - draft.CreationKarmaUsed,
                CharacterDocumentChanged: false),
            []);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationAttributesPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationAttributesPreview>(
                    read.Outcome == WorkspaceOperationOutcome.Missing
                        ? CharacterCreationFoundationOutcomes.Missing
                        : CharacterCreationFoundationOutcomes.Invalid,
                    CharacterCreationAttributesBlockers.WorkspaceUnavailable),
                null,
                null);
        }
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationAttributesPreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationAttributesBlockers.StaleWorkspaceRevision),
                null,
                null);
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesState> stateResult =
            BuildState(workspace);
        if (stateResult.Value is not CharacterCreationAttributesState state
            || state.PrerequisiteDraft is not CharacterCreationPrerequisiteDraft prerequisite)
        {
            return new PreviewEvaluation(
                new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
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
                Blocked<CharacterCreationAttributesPreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    bindingBlocker),
                null,
                null);
        }

        var blockers = new List<string>(state.Blockers);
        AttributeEvaluation projected = EvaluateAllocations(
            prerequisite,
            state.MaxNumberMaxAttributesCreate,
            state.KarmaAttribute,
            request.Allocations,
            blockers);
        CharacterCreationAttributesDraft? draft = null;
        if (blockers.Count == 0)
        {
            draft = BuildDraft(workspace, prerequisite, projected);
            if (state.PendingDraft is CharacterCreationAttributesDraft current)
            {
                if (current.DraftRevision == long.MaxValue)
                    blockers.Add(CharacterCreationAttributesBlockers.DraftConflict);
                else if (CharacterCreationAttributesDraftIntegrity.HasSameLogicalPayload(current, draft))
                    blockers.Add(CharacterCreationAttributesBlockers.DraftDuplicate);
            }
        }

        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var preview = new CharacterCreationAttributesPreview(
            CharacterCreationAttributesSchemas.PreviewV1,
            state.Binding,
            projected.Attributes,
            projected.NormalBudget,
            projected.SpecialBudget,
            projected.KarmaBudget,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0 && draft is not null,
            PreviewDigest: string.Empty);
        preview = preview with
        {
            PreviewDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                preview with { PreviewDigest = string.Empty })
        };
        return new PreviewEvaluation(
            new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                normalized.Length == 0
                    ? CharacterCreationFoundationOutcomes.Success
                    : CharacterCreationFoundationOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            draft);
    }

    private CharacterCreationFoundationResult<CharacterCreationAttributesState> BuildState(
        WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            })
        {
            blockers.Add(CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
        }

        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteAuthority.Unavailable;
        ICharacterSourceDataContext? sourceContext = _sourceDataResolver.TryCreateContext(workspace.Document.Content);
        bool hasAuthority = sourceContext is not null
                            && sourceContext.TryResolveCreationPrerequisiteAuthority(out authority);
        CharacterCreationPrerequisiteDraft? prerequisite = workspace.Document.AuxiliaryState
            .CharacterCreationPrerequisiteDraft;
        if (!hasAuthority || prerequisite is null)
        {
            blockers.Add(prerequisite is null
                ? CharacterCreationAttributesBlockers.PrerequisiteDraftRequired
                : CharacterCreationAttributesBlockers.AuthorityUnavailable);
        }
        else if (!CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                     prerequisite,
                     workspace.Id,
                     workspace.ContentRevision,
                     rawDigest,
                     authority))
        {
            blockers.Add(CharacterCreationAttributesBlockers.PrerequisiteSourceDrift);
            prerequisite = null;
        }

        int maxAtMaximum = authority.MaxNumberMaxAttributesCreate.GetValueOrDefault();
        if (prerequisite is not null)
        {
            ValidatePrerequisiteAttributeAuthority(prerequisite, authority, blockers);
            if (HasLegacyAttributeState(workspace.Document.Content))
            {
                blockers.Add(CharacterCreationAttributesBlockers.LegacyAttributeStateRequiresImport);
                blockers.Add(CharacterCreationAttributesBlockers.ExceptionalAttributeAuthorityRequired);
            }
        }

        CharacterCreationAttributesDraft? pending = null;
        CharacterCreationAttributesDraft? persisted = workspace.Document.AuxiliaryState
            .CharacterCreationAttributesDraft;
        if (persisted is not null)
        {
            if (prerequisite is null
                || !CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(
                    persisted,
                    workspace.Id,
                    workspace.ContentRevision,
                    rawDigest,
                    prerequisite))
            {
                blockers.Add(CharacterCreationAttributesBlockers.DraftInvalid);
            }
            else
            {
                var persistedBlockers = new List<string>();
                AttributeEvaluation reevaluated = EvaluateAllocations(
                    prerequisite,
                    maxAtMaximum,
                    authority.KarmaAttribute.GetValueOrDefault(),
                    persisted.Allocations,
                    persistedBlockers);
                if (persistedBlockers.Count != 0
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        persisted.Attributes,
                        reevaluated.Attributes)
                    || persisted.NormalPointUsed != (int)reevaluated.NormalBudget.Used
                    || persisted.SpecialPointUsed != (int)reevaluated.SpecialBudget.Used
                    || persisted.CreationKarmaUsed != (int)reevaluated.KarmaBudget.Used)
                {
                    blockers.Add(CharacterCreationAttributesBlockers.DraftInvalid);
                }
                else
                {
                    pending = persisted;
                }
            }
        }

        var binding = new CharacterCreationAttributesBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            prerequisite?.DraftRevision ?? 0,
            prerequisite?.DraftDigest ?? string.Empty,
            prerequisite?.AuthorityDigest ?? string.Empty);
        AttributeEvaluation currentProjection = prerequisite is null
            ? AttributeEvaluation.Empty
            : EvaluateAllocations(
                prerequisite,
                maxAtMaximum,
                authority.KarmaAttribute.GetValueOrDefault(),
                pending?.Allocations ?? [],
                new List<string>());
        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var state = new CharacterCreationAttributesState(
            CharacterCreationAttributesSchemas.SnapshotV1,
            binding,
            prerequisite,
            pending,
            currentProjection.Attributes,
            currentProjection.NormalBudget,
            currentProjection.SpecialBudget,
            currentProjection.KarmaBudget,
            maxAtMaximum,
            normalized,
            CanEdit: prerequisite is not null && normalized.Length == 0,
            SnapshotDigest: string.Empty)
        {
            KarmaAttribute = authority.KarmaAttribute.GetValueOrDefault()
        };
        state = state with
        {
            SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                state with { SnapshotDigest = string.Empty })
        };
        return new CharacterCreationFoundationResult<CharacterCreationAttributesState>(
            CharacterCreationFoundationOutcomes.Success,
            state,
            normalized);
    }

    private static void ValidatePrerequisiteAttributeAuthority(
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationPrerequisiteAuthority authority,
        ICollection<string> blockers)
    {
        CharacterCreationPriorityHeritageSelection? heritage = prerequisite.HeritageSelection;
        CharacterCreationPriorityTalentSelection? talent = prerequisite.TalentSelection;
        if (heritage is null
            || heritage.Kind != CharacterCreationPriorityChildKinds.Metatype
            || heritage.MetavariantSourceId is not null
            || !string.Equals(heritage.MetatypeName, "Human", StringComparison.Ordinal)
            || heritage.Attributes.Count != 13
            || !heritage.Attributes.Select(item => item.AttributeId)
                .SequenceEqual(s_NormalAttributeIds.Concat(s_SpecialAttributeIds), StringComparer.Ordinal)
            || heritage.Attributes.Any(item => item.Minimum < 0
                                                || item.Minimum > item.Maximum
                                                || item.Maximum > item.AugmentedMaximum)
            || !Guid.TryParseExact(heritage.MetatypeSourceId, "D", out Guid sourceId)
            || sourceId == Guid.Empty
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(heritage.MetatypeSourceNodeDigest))
        {
            blockers.Add(CharacterCreationAttributesBlockers.MetatypeAuthorityIncomplete);
        }
        if (talent is null
            || !string.Equals(talent.Value, "Mundane", StringComparison.Ordinal)
            || talent.Magic is not null
            || talent.Resonance is not null
            || talent.Depth is not null
            || talent.GrantedQualities.Count != 0)
        {
            blockers.Add(CharacterCreationAttributesBlockers.SpecialAttributeAuthorityIncomplete);
        }
        if (authority.MaxNumberMaxAttributesCreate is null
            || authority.KarmaAttribute is null)
        {
            blockers.Add(CharacterCreationAttributesBlockers.AuthorityUnavailable);
        }
        if (authority.AlternateMetatypeAttributeKarma != false
            || authority.ReverseAttributePriorityOrder != false)
        {
            blockers.Add(CharacterCreationAttributesBlockers.HouseRuleUnsupported);
        }
    }

    private static AttributeEvaluation EvaluateAllocations(
        CharacterCreationPrerequisiteDraft prerequisite,
        int maxAtMaximum,
        int karmaAttribute,
        IReadOnlyList<CharacterCreationAttributeAllocation>? requested,
        ICollection<string> blockers)
    {
        requested ??= [];
        var allocationMap = new Dictionary<string, CharacterCreationAttributeAllocation>(StringComparer.Ordinal);
        foreach (CharacterCreationAttributeAllocation? allocation in requested)
        {
            if (allocation is null
                || !s_NormalAttributeIds.Concat(s_SpecialAttributeIds).Contains(
                    allocation.AttributeId,
                    StringComparer.Ordinal)
                || allocation.PriorityPoints < 0
                || allocation.KarmaLevels < 0)
            {
                blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                continue;
            }
            if (!allocationMap.TryAdd(allocation.AttributeId, allocation))
                blockers.Add(CharacterCreationAttributesBlockers.AllocationDuplicate);
        }

        CharacterCreationPriorityHeritageSelection heritage = prerequisite.HeritageSelection!;
        var projections = new List<CharacterCreationAttributeProjection>();
        int normalUsed = 0;
        int specialUsed = 0;
        int karmaUsed = prerequisite.CreationKarmaUsed;
        foreach (CharacterCreationMetatypeAttributeProjection range in heritage.Attributes)
        {
            bool normal = s_NormalAttributeIds.Contains(range.AttributeId, StringComparer.Ordinal);
            bool edge = string.Equals(range.AttributeId, "EDG", StringComparison.Ordinal);
            bool essence = string.Equals(range.AttributeId, "ESS", StringComparison.Ordinal);
            bool enabled = normal || edge;
            string category = normal
                ? CharacterCreationAttributeCategories.Normal
                : CharacterCreationAttributeCategories.Special;
            CharacterCreationAttributeAllocation allocation = allocationMap.TryGetValue(
                range.AttributeId,
                out CharacterCreationAttributeAllocation? selected)
                ? selected
                : new CharacterCreationAttributeAllocation(range.AttributeId, 0, 0);
            if (!enabled && (allocation.PriorityPoints != 0 || allocation.KarmaLevels != 0))
                blockers.Add(CharacterCreationAttributesBlockers.AttributeDisabled);

            int minimum = enabled || essence ? range.Minimum : 0;
            int maximum = enabled || essence ? range.Maximum : 0;
            int augmentedMaximum = enabled || essence ? range.AugmentedMaximum : 0;
            int current = essence ? maximum : minimum;
            int karmaCost = 0;
            if (enabled)
            {
                try
                {
                    current = checked(minimum + allocation.PriorityPoints + allocation.KarmaLevels);
                    int totalBase = checked(minimum + allocation.PriorityPoints);
                    karmaCost = checked(
                        (2 * totalBase + allocation.KarmaLevels + 1)
                        * allocation.KarmaLevels / 2
                        * karmaAttribute);
                }
                catch (OverflowException)
                {
                    blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                }
            }
            if (current > maximum)
                blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
            try
            {
                if (normal)
                    normalUsed = checked(normalUsed + allocation.PriorityPoints);
                else if (edge)
                    specialUsed = checked(specialUsed + allocation.PriorityPoints);
                karmaUsed = checked(karmaUsed + karmaCost);
            }
            catch (OverflowException)
            {
                blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
            }

            string[] disableReasons = enabled
                ? []
                : string.Equals(range.AttributeId, "ESS", StringComparison.Ordinal)
                    ? [CharacterCreationAttributesBlockers.EssenceNotSpendable]
                    : [CharacterCreationAttributesBlockers.SpecialAttributeNotEnabled];
            projections.Add(new CharacterCreationAttributeProjection(
                range.AttributeId,
                category,
                minimum,
                maximum,
                augmentedMaximum,
                Math.Max(0, current),
                allocation.PriorityPoints,
                allocation.KarmaLevels,
                allocation.PriorityPoints,
                Math.Max(0, karmaCost),
                enabled,
                disableReasons,
                heritage.SourceAnchorIds
                    .Concat([$"metatypes.xml#metatype:{heritage.MetatypeSourceId}:attribute:{range.AttributeId}"])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
        }

        if (normalUsed > prerequisite.EffectiveNormalAttributePoints)
            blockers.Add(CharacterCreationAttributesBlockers.NormalPointsExceeded);
        if (specialUsed > prerequisite.TotalSpecialAttributePoints)
            blockers.Add(CharacterCreationAttributesBlockers.SpecialPointsExceeded);
        if (karmaUsed > prerequisite.CreationKarmaTotal)
            blockers.Add(CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
        int atMaximum = projections.Count(item =>
            item.Category == CharacterCreationAttributeCategories.Normal
            && item.Maximum > 0
            && item.Current == item.Maximum);
        if (atMaximum > maxAtMaximum)
            blockers.Add(CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);

        return new AttributeEvaluation(
            projections.ToArray(),
            Budget("normal-attribute-points", "Normal Attribute Points", prerequisite.EffectiveNormalAttributePoints, normalUsed, "points", CharacterCreationAttributesBlockers.NormalPointsExceeded),
            Budget("special-attribute-points", "Special Attribute Points", prerequisite.TotalSpecialAttributePoints, specialUsed, "points", CharacterCreationAttributesBlockers.SpecialPointsExceeded),
            Budget(CharacterCreationBudgetIds.Karma, "Creation Karma", prerequisite.CreationKarmaTotal, karmaUsed, "karma", CharacterCreationAttributesBlockers.GlobalKarmaExceeded));
    }

    private static CharacterCreationBudgetState Budget(
        string id,
        string label,
        int total,
        int used,
        string unit,
        string overageBlocker)
    {
        bool valid = total >= 0 && used >= 0 && used <= total;
        return new CharacterCreationBudgetState(
            id,
            label,
            Math.Max(0, total),
            Math.Max(0, used),
            valid ? total - used : 0,
            valid,
            valid ? [] : [overageBlocker],
            unit);
    }

    private static CharacterCreationAttributesDraft BuildDraft(
        WorkspaceStoredDocument workspace,
        CharacterCreationPrerequisiteDraft prerequisite,
        AttributeEvaluation evaluation)
    {
        CharacterCreationAttributesDraft? current = workspace.Document.AuxiliaryState
            .CharacterCreationAttributesDraft;
        long nextRevision = current is null
            ? 1
            : current.DraftRevision == long.MaxValue
                ? long.MaxValue
                : current.DraftRevision + 1;
        CharacterCreationPriorityHeritageSelection heritage = prerequisite.HeritageSelection!;
        var draft = new CharacterCreationAttributesDraft(
            CharacterCreationAttributesSchemas.DraftV1,
            workspace.Id,
            nextRevision,
            workspace.ContentRevision,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                workspace.Document.Content),
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest,
            heritage.MetatypeSourceId,
            heritage.MetatypeSourceNodeDigest,
            heritage.HalvesNormalAttributePoints,
            (int)evaluation.NormalBudget.Total,
            (int)evaluation.NormalBudget.Used,
            (int)evaluation.SpecialBudget.Total,
            (int)evaluation.SpecialBudget.Used,
            (int)evaluation.KarmaBudget.Total,
            (int)evaluation.KarmaBudget.Used,
            evaluation.Attributes.Select(item => new CharacterCreationAttributeAllocation(
                item.AttributeId,
                item.PriorityPointsSpent,
                item.KarmaLevels)).ToArray(),
            evaluation.Attributes,
            prerequisite.SourceAnchorIds
                .Concat(heritage.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            CharacterEffectsApplied: false,
            DraftDigest: string.Empty);
        return draft with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(draft) };
    }

    private static string? CompareBinding(
        CharacterCreationAttributesBinding current,
        CharacterCreationAttributesBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationAttributesBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.RawCharacterXmlDigest,
                requested.RawCharacterXmlDigest))
            return CharacterCreationAttributesBlockers.StaleRawCharacterXmlDigest;
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.AuxiliaryStateDigest,
                requested.AuxiliaryStateDigest))
            return CharacterCreationAttributesBlockers.DraftConflict;
        if (current.PrerequisiteDraftRevision != requested.PrerequisiteDraftRevision
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.PrerequisiteDraftDigest,
                requested.PrerequisiteDraftDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.PrerequisiteAuthorityDigest,
                requested.PrerequisiteAuthorityDigest))
            return CharacterCreationAttributesBlockers.PrerequisiteSourceDrift;
        return null;
    }

    private static bool HasLegacyAttributeState(string xml)
    {
        try
        {
            XElement? root = XDocument.Parse(xml, LoadOptions.None).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
                return true;
            return root.Elements("attributes").Any(element => element.HasElements)
                   || root.Elements("qualities").Any(element => element.HasElements)
                   || root.Elements("improvements").Any(element => element.HasElements);
        }
        catch (XmlException)
        {
            return true;
        }
    }

    private static CharacterCreationFoundationResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Result,
        WorkspaceStoredDocument? Workspace,
        CharacterCreationAttributesDraft? Draft);

    private sealed record AttributeEvaluation(
        IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
        CharacterCreationBudgetState NormalBudget,
        CharacterCreationBudgetState SpecialBudget,
        CharacterCreationBudgetState KarmaBudget)
    {
        public static AttributeEvaluation Empty { get; } = new(
            [],
            Budget("normal-attribute-points", "Normal Attribute Points", 0, 0, "points", CharacterCreationAttributesBlockers.NormalPointsExceeded),
            Budget("special-attribute-points", "Special Attribute Points", 0, 0, "points", CharacterCreationAttributesBlockers.SpecialPointsExceeded),
            Budget(CharacterCreationBudgetIds.Karma, "Creation Karma", 0, 0, "karma", CharacterCreationAttributesBlockers.GlobalKarmaExceeded));
    }
}
